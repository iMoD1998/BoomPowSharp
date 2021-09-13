using System;
using System.Collections.Generic;
using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Receiving;
using MQTTnet.Client.Options;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using MQTTnet.Protocol;
using System.Text.Json.Serialization;
using System.Text.Json;
using MQTTnet.Extensions.ManagedClient;
using System.Diagnostics;

namespace BoomPowSharp
{
    class BoomPow : IDisposable
    {
        private static readonly MqttFactory MqttFactory = new MqttFactory();

        //
        // MQTT related stuff
        //
        IManagedMqttClientOptions _BrokerMQTTOptions;
        IManagedMqttClient        _MQTTClient;
        DateTime                  _LastServerHeartbeat;

        public enum BoomPowWorkType
        {
            OnDemand,
            Precache,
            Any
        };

        //
        // BoomPow/client related infomation
        //
        NanoClientRPC   _WorkServer;
        string          _PayoutAddress;
        BoomPowWorkType _WorkType;

        public BoomPow(MqttClientOptionsBuilder BrokerMQTTOptions, Uri WorkUri, string PayoutAddress = "ban_1ncpdt1tbusi9n4c7pg6tqycgn4oxrnz5stug1iqyurorhwbc9gptrsmxkop", BoomPowWorkType WorkType = BoomPowWorkType.Any)
        {

            BrokerMQTTOptions.WithKeepAlivePeriod(TimeSpan.FromMilliseconds(120));

            _BrokerMQTTOptions = new ManagedMqttClientOptionsBuilder().WithClientOptions(BrokerMQTTOptions)
                                                                      .WithAutoReconnectDelay(TimeSpan.FromSeconds(10))
                                                                      .Build();


            _MQTTClient = MqttFactory.CreateManagedMqttClient();
            _MQTTClient.ConnectedHandler = new MqttClientConnectedHandlerDelegate(BrokerOnConnected);
            _MQTTClient.DisconnectedHandler = new MqttClientDisconnectedHandlerDelegate(BrokerOnDisconnected);
            _MQTTClient.ApplicationMessageReceivedHandler = new MqttApplicationMessageReceivedHandlerDelegate(BrokerOnMessageRecieved);

            _WorkServer = new NanoClientRPC(WorkUri);

            _PayoutAddress = PayoutAddress;
            _WorkType = WorkType;

        }

        public async void Dispose()
        {
            _MQTTClient.Dispose();
        }

        public async Task<bool> Run()
        {
            try
            {
                await _MQTTClient.StartAsync(_BrokerMQTTOptions);

                //
                // TODO: make this nicer.
                //
                var DesiredWorkTopicName   = _WorkType == BoomPowWorkType.Any      ? "work/#" :
                                             _WorkType == BoomPowWorkType.OnDemand ? "work/ondemand/#" : "work/precache/#";

                var DesiredCancelTopicName = _WorkType == BoomPowWorkType.Any      ? "cancel/#" :
                                             _WorkType == BoomPowWorkType.OnDemand ? "cancel/ondemand" : "cancel/precache";

                var SubscriberTopics = new List<MqttTopicFilter>() {
                    new MqttTopicFilter{ Topic = "heartbeat",                           QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce },
                    new MqttTopicFilter{ Topic = DesiredWorkTopicName,                  QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce },
                    new MqttTopicFilter{ Topic = DesiredCancelTopicName,                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce },
                    new MqttTopicFilter{ Topic = $"client/{_PayoutAddress}",            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce },
                    new MqttTopicFilter{ Topic = $"priority_response/{_PayoutAddress}", QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce },
                };

                SubscriberTopics.ForEach(X => Console.WriteLine($"Subscribing to {X.Topic} at QOS {((int)X.QualityOfServiceLevel)}"));

                await _MQTTClient.SubscribeAsync(SubscriberTopics.ToArray());
            }
            catch (Exception e)
            {
                await Console.Out.WriteLineAsync($"Failed to connect to broker: {e.Message}");

                return false;
            }

            return true;
        }

        public async void BrokerOnConnected(MqttClientConnectedEventArgs Args)
        {
            await Console.Out.WriteLineAsync("Connected to broker.");
        }

        public async void BrokerOnDisconnected(MqttClientDisconnectedEventArgs Args)
        {
            await Console.Out.WriteLineAsync("Disconnected from broker.");
        }

        public async void BrokerOnMessageRecieved(MqttApplicationMessageReceivedEventArgs Args)
        {
            string[] Topics  = Args.ApplicationMessage.Topic.Split("/");
            string   Message = Args.ApplicationMessage.Payload == null ? "" : Encoding.UTF8.GetString(Args.ApplicationMessage.Payload);

            switch (Topics[0])
            {
                case "work":
                    HandleWork(Topics[1], Message);
                    break;
                case "cancel":
                    HandleCancel(Message);
                    break;
                case "heartbeat":
                    HandleHeartbeat();
                    break;
                case "client":
                    HandleClientBlockAccepted(Topics[1], Message);
                    break;
            }
        }

        //
        // work/precache, work/ondemand
        // These topics are used by the server to publish new work for clients. Clients can choose to subscribe to precache work, on-demand work, or both.
        //
        // Message format:
        //
        // block_hash,difficulty
        //
        public async Task HandleWork(string WorkType, string Message)
        {
            var Timer = new Stopwatch();

            Timer.Start();

            string[] WorkData   = Message.Split(',');
            string   BlockHash  = WorkData[0];
            string   Difficulty = WorkData[1];

            var Response = await _WorkServer.WorkGenerate(BlockHash, Difficulty);

            if(Response.Error == null)
            {
                string ResultPayload = string.Join(',', new string[] {
                    BlockHash,
                    Response.WorkResult,
                    _PayoutAddress
                });

                await _MQTTClient.InternalClient.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic($"result/{WorkType}")
                    .WithPayload(Encoding.UTF8.GetBytes(ResultPayload))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    .Build()
                 );

                Timer.Stop();

                await Console.Out.WriteLineAsync($"Solved block {BlockHash}:{Response.WorkResult}:{Response.Difficulty} - {Timer.Elapsed.TotalSeconds}s");
            }
        }

        public async Task HandleCancel(string Message)
        {
            await _WorkServer.WorkCancel(Message);
        }

        public class ClientBlockAcceptedMessage
        {
            [JsonPropertyName("precache")]
            public string _NumPrecacheAccepted { get; set; }
            [JsonPropertyName("ondemand")]
            public string _NumOnDemandAccepted { get; set; }
            [JsonPropertyName("total_credited")]
            public string _NumWorkPaid { get; set; }
            [JsonPropertyName("total_paid")]
            public string _TotalPaid { get; set; }
            [JsonPropertyName("payment_factor")]
            public float PaymentFactor { get; set; }
            [JsonPropertyName("percent_of_total")]
            public float PercentageTotal { get; set; }
            [JsonPropertyName("block_rewarded")]
            public string BlockRewarded { get; set; }

            public int NumPrecacheAccepted { get { return int.Parse(_NumPrecacheAccepted ?? "0"); } }
            public int NumOnDemandAccepted { get { return int.Parse(_NumOnDemandAccepted ?? "0"); } }
            public int NumWorkPaid { get { return int.Parse(_NumWorkPaid ?? "0"); } }
            public float TotalPaid { get { return float.Parse(_TotalPaid ?? "0"); } }
        };

        public async Task HandleClientBlockAccepted(string ClientAddress, string Message)
        {
            var StatsMessage = JsonSerializer.Deserialize<ClientBlockAcceptedMessage>(Message);

            await Console.Out.WriteLineAsync($"Block accepted {StatsMessage.BlockRewarded} work units solved: {StatsMessage.NumOnDemandAccepted + StatsMessage.NumPrecacheAccepted} paid for: {StatsMessage.NumWorkPaid}");

            Console.Title = $"BoomPowSharp - Earnings: { StatsMessage.TotalPaid } OnDemand: { StatsMessage.NumOnDemandAccepted } Precache: { StatsMessage.NumPrecacheAccepted} Total: { StatsMessage.NumOnDemandAccepted + StatsMessage.NumPrecacheAccepted }";
        }

        public async Task HandleHeartbeat()
        {
            _LastServerHeartbeat = DateTime.Now;
        }
    }
}

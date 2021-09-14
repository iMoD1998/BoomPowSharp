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
using System.Collections.Concurrent;

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
        bool            _Verbose;

        ConcurrentDictionary<string, DateTime> _GeneratedBlocks = new ConcurrentDictionary<string, DateTime>();

        public NanoClientRPC WorkServer { get { return _WorkServer; } }

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

        public BoomPow(MqttClientOptionsBuilder BrokerMQTTOptions, Uri WorkUri, string PayoutAddress = "ban_1ncpdt1tbusi9n4c7pg6tqycgn4oxrnz5stug1iqyurorhwbc9gptrsmxkop", BoomPowWorkType WorkType = BoomPowWorkType.Any, bool Verbose = false)
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
            _Verbose = Verbose;
        }

        public void Dispose()
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

                if (_Verbose)
                {
                    SubscriberTopics.ForEach(X => Console.WriteLine($"[{DateTime.Now}][?] Subscribing to {X.Topic} at QOS {((int)X.QualityOfServiceLevel)}"));
                }

                await _MQTTClient.SubscribeAsync(SubscriberTopics.ToArray());
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{DateTime.Now}][!] Error during startup {e.Message}");

                return false;
            }

            return true;
        }

        public async void BrokerOnConnected(MqttClientConnectedEventArgs Args)
        {
            Console.WriteLine($"[{DateTime.Now}][+] Connected to broker");
        }

        public async void BrokerOnDisconnected(MqttClientDisconnectedEventArgs Args)
        {
            Console.WriteLine($"[{DateTime.Now}][!] Disconnected from broker");
        }

        public async void BrokerOnMessageRecieved(MqttApplicationMessageReceivedEventArgs Args)
        {
            string[] Topics = Args.ApplicationMessage.Topic.Split("/");
            string Message = Args.ApplicationMessage.Payload == null ? "" : Encoding.UTF8.GetString(Args.ApplicationMessage.Payload);

            //
            // Dispatch the handlers without await to ensure they run parallell (assumption).
            //
            switch (Topics[0])
            {
                case "work":
                    HandleWork(Topics[1], Message).ContinueWith(HandleException, TaskContinuationOptions.OnlyOnFaulted);
                    break;
                case "cancel":
                    HandleCancel(Message).ContinueWith(HandleException, TaskContinuationOptions.OnlyOnFaulted);
                    break;
                case "heartbeat":
                    HandleHeartbeat();
                    break;
                case "client":
                    HandleClientBlockAccepted(Topics[1], Message).ContinueWith(HandleException, TaskContinuationOptions.OnlyOnFaulted);
                    break;
            }
        }

        public void HandleException(Task HandlerTask)
        {
            Console.WriteLine($"[{DateTime.Now}][!] Exception in handler {HandlerTask.Exception.Message}");
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
            string BlockHash  = Message.Substring(0, 64);
            string Difficulty = Message.Substring(65, 16);

            var Response = await _WorkServer.WorkGenerate(BlockHash, Difficulty);

            if (Response.Error == null)
            {
                string ResultPayload = string.Join(',', BlockHash, Response.WorkResult, _PayoutAddress);

                await _MQTTClient.InternalClient.PublishAsync($"result/{WorkType}", ResultPayload, MqttQualityOfServiceLevel.AtMostOnce).ConfigureAwait(false);

                //
                // Add generated block to hashmap after sending result to ensure we dont add the latency of the lock etc.
                //
                _GeneratedBlocks.TryAdd(BlockHash, DateTime.Now);

                if (_Verbose)
                {
                    Console.WriteLine($"[{DateTime.Now}][?] Solved block {BlockHash}:{Response.WorkResult}:{Response.Difficulty}");
                }
            }
            else
            {
                if (_Verbose)
                {
                    Console.WriteLine($"[{DateTime.Now}][!] Error for block: {Response.Error}");
                }
            }
        }

        public async Task HandleCancel(string BlockHash)
        {
            //
            // Dont cancel jobs we have already done.
            // 
            if(!_GeneratedBlocks.ContainsKey(BlockHash))
            {
                await _WorkServer.WorkCancel(BlockHash);
            }
            else
            {
                //
                // Handled cancel request so remove entry from hashmap.
                //
                DateTime Old;
                _GeneratedBlocks.TryRemove(BlockHash, out Old);
            }
        }

        public async Task HandleClientBlockAccepted(string ClientAddress, string Message)
        {
            var StatsMessage = JsonSerializer.Deserialize<ClientBlockAcceptedMessage>(Message);

            Console.WriteLine($"[{DateTime.Now}][+] Block accepted {StatsMessage.BlockRewarded.Substring(0, 32)}... {StatsMessage.NumWorkPaid}/{StatsMessage.NumOnDemandAccepted + StatsMessage.NumPrecacheAccepted} paid {StatsMessage.PercentageTotal*100.0f}% of next payout");

            Console.Title = $"BoomPowSharp - Earnings: { StatsMessage.TotalPaid } OnDemand: { StatsMessage.NumOnDemandAccepted } Precache: { StatsMessage.NumPrecacheAccepted} Total: { StatsMessage.NumOnDemandAccepted + StatsMessage.NumPrecacheAccepted }";
        }

        public async Task HandleHeartbeat()
        {
            _LastServerHeartbeat = DateTime.Now;
        }
    }
}

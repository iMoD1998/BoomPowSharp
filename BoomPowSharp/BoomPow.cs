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
    class BoomPow
    {
        public class MQTTClientBlockRewardedMessage
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

            //
            // Conversions
            //
            [JsonIgnore]
            public int NumPrecacheAccepted { get { return int.Parse(_NumPrecacheAccepted ?? "0"); } }
            [JsonIgnore]
            public int NumOnDemandAccepted { get { return int.Parse(_NumOnDemandAccepted ?? "0"); } }
            [JsonIgnore]
            public int NumWorkPaid { get { return int.Parse(_NumWorkPaid ?? "0"); } }
            [JsonIgnore]
            public float TotalPaid { get { return float.Parse(_TotalPaid ?? "0"); } }
        };

        public enum BoomPowWorkType
        {
            OnDemand,
            Precache,
            Any
        };

        private static readonly MqttFactory MqttFactory = new MqttFactory();

        //
        // MQTT related stuff
        //
        private IManagedMqttClientOptions _BrokerMQTTOptions;
        private IManagedMqttClient        _MQTTClient;
        private DateTime                  _LastServerHeartbeat;

        //
        // BoomPow/client related infomation
        //
        private NanoClientRPC   _WorkServer;
        private string          _PayoutAddress;
        private BoomPowWorkType _WorkType;
        private bool            _Verbose;
        private ulong           _MinDifficulty;

        ConcurrentDictionary<string, DateTime> _GeneratedBlocks = new ConcurrentDictionary<string, DateTime>();

        public NanoClientRPC WorkServer { get { return _WorkServer; } }


        public BoomPow(MqttClientOptionsBuilder BrokerMQTTOptions, Uri WorkUri, string PayoutAddress, BoomPowWorkType WorkType = BoomPowWorkType.Any, ulong MinDifficulty = 0, bool Verbose = false)
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
            _MinDifficulty = MinDifficulty;
            _Verbose = Verbose;
        }

        public async Task Run()
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

                //
                // Get priority queue
                //
                await _MQTTClient.PublishAsync($"get_priority/{_WorkType.ToString().ToLower()}", _PayoutAddress, MqttQualityOfServiceLevel.AtMostOnce);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{DateTime.Now}][!] Error during startup {e.Message}");
            }

            while (true)
            {
                try
                {
                    var Stats = await _WorkServer.GetStatus();
                }
                catch
                {
                    Console.WriteLine("Error: Work server not responding.");
                }

                await Task.Delay(5000);
            }
        }

        public void BrokerOnConnected(MqttClientConnectedEventArgs Args)
        {
            Console.WriteLine($"[{DateTime.Now}][+] Connected to broker");
        }

        public void BrokerOnDisconnected(MqttClientDisconnectedEventArgs Args)
        {
            Console.WriteLine($"[{DateTime.Now}][!] Disconnected from broker");
        }

        public void BrokerOnMessageRecieved(MqttApplicationMessageReceivedEventArgs Args)
        {
            string[] Topics  = Args.ApplicationMessage.Topic.Split("/");
            string   Message = Args.ApplicationMessage.Payload == null ? "" : Encoding.UTF8.GetString(Args.ApplicationMessage.Payload);

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
                    HandleClientBlockRewarded(Topics[1], Message);
                    break;
            }

            //Console.WriteLine($"{DateTime.Now}][?] TOPIC:{Args.ApplicationMessage.Topic} MSG:{Message}");
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

            if (_Verbose)
            {
                Console.WriteLine($"[{DateTime.Now}][?] Got block {BlockHash}:{Difficulty}");
            }

            if(Convert.ToUInt64(Difficulty, 16) < _MinDifficulty)
            {
                if (_Verbose)
                {
                    Console.WriteLine($"[{DateTime.Now}][?] Ignoring block {BlockHash}:{Difficulty}");
                }
                
                _GeneratedBlocks.TryAdd(BlockHash, DateTime.Now);

                return;
            }

            var StopWatch = Stopwatch.StartNew();

            var Response = await _WorkServer.GenerateWork(BlockHash, Difficulty);

            StopWatch.Stop();

            if (Response.Error == null)
            {
                await _MQTTClient.InternalClient.PublishAsync($"result/{WorkType}", string.Join(',', BlockHash, Response.WorkResult, _PayoutAddress), MqttQualityOfServiceLevel.AtMostOnce).ConfigureAwait(false);

                //
                // Add generated block to hashmap after sending result to ensure we dont add the latency of the lock etc.
                //
                _GeneratedBlocks.TryAdd(BlockHash, DateTime.Now);

                if (_Verbose)
                {
                    Console.WriteLine($"[{DateTime.Now}][?] Solved block {BlockHash}:{Response.WorkResult}:{Response.Difficulty} in {StopWatch.ElapsedMilliseconds}ms");
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
            DateTime Old;

            //
            // Dont cancel jobs we have already done.
            // 
            if (!_GeneratedBlocks.TryRemove(BlockHash, out Old))
            {
                await _WorkServer.CancelWork(BlockHash);
            }
        }

        public void HandleClientBlockRewarded(string ClientAddress, string Message)
        {
            var StatsMessage = JsonSerializer.Deserialize<MQTTClientBlockRewardedMessage>(Message);

            Console.WriteLine($"[{DateTime.Now}][+] Block rewarded {StatsMessage.BlockRewarded}... {StatsMessage.NumWorkPaid}/{StatsMessage.NumOnDemandAccepted + StatsMessage.NumPrecacheAccepted} paid {(StatsMessage.PercentageTotal*100.0f).ToString("0.00")}% of next payout");
        }

        public void HandleHeartbeat()
        {
            _LastServerHeartbeat = DateTime.Now;

            if(_Verbose)
            {
                Console.WriteLine($"[{DateTime.Now}][?] Heartbeat");
            }
        }
    }
}

using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Connecting;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Receiving;
using MQTTnet.Client.Options;
using MQTTnet.Protocol;
using System.Text.Json.Serialization;
using System.Text.Json;
using MQTTnet.Extensions.ManagedClient;
using System.Diagnostics;
using System.Collections.Concurrent;
using static BoomPowSharp.NanoClientRPC;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;

namespace BoomPowSharp
{
    class BoomPow : IDisposable
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

        public class MQTTClientPriorityMessage
        {
            [JsonPropertyName("precache")]
            public string PrecachePriorityIndex { get; set; }
            [JsonPropertyName("ondemand")]
            public string OnDemandPriorityIndex { get; set; }
        }

        public enum BoomPowWorkType
        {
            OnDemand,
            Precache,
            Any
        };

        private static readonly MqttFactory MqttFactory = new MqttFactory();

        public const string DeveloperAddress = "ban_1mod19984d47idnf5cepfcsrdhey56dhtgn6omba4sk4dfs97xacyrppb9pi";

        //
        // MQTT related stuff
        //
        private IManagedMqttClientOptions _BrokerMQTTOptions;
        private IManagedMqttClient _MQTTClient;
        private DateTime _LastServerHeartbeat;

        //
        // BoomPow/client related infomation
        //
        private NanoClientRPC _WorkServer;
        private string _PayoutAddress;
        private BoomPowWorkType _WorkType;
        private bool _Verbose;
        private ulong _MinDifficulty;

        private static uint SolvedBlockNumber = 0;

        Dictionary<string, Func<string[], string, Task>> MessageDispatchHandlers = new Dictionary<string, Func<string[], string, Task>>();

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

            MessageDispatchHandlers.Add("work", HandleWork);
            MessageDispatchHandlers.Add("cancel", HandleCancel);
            MessageDispatchHandlers.Add("heartbeat", HandleHeartbeat);
            MessageDispatchHandlers.Add("client", HandleClientBlockRewarded);
            MessageDispatchHandlers.Add("priority_response", HandlePriority);
        }

        public void Dispose()
        {
            //_MQTTClient.PublishAsync()
        }

        public async Task Run()
        {
            try
            {
                await _MQTTClient.StartAsync(_BrokerMQTTOptions);

                //
                // TODO: make this nicer.
                //
                var DesiredWorkTopicName =   _WorkType == BoomPowWorkType.Any ? "work/#" :
                                             _WorkType == BoomPowWorkType.OnDemand ? "work/ondemand/#" : "work/precache/#";

                var DesiredCancelTopicName = _WorkType == BoomPowWorkType.Any ? "cancel/#" :
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
            }

            while (true)
            {
                try
                {
                    if (_MQTTClient.IsConnected)
                    {
                        await _WorkServer.GetStatus();
                        await RequestPriority();
                        //await _MQTTClient.PingAsync(CancellationToken.None);
                    }

                    Console.Title = ($"Current Threads {ThreadPool.ThreadCount}");
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

        public async void BrokerOnMessageRecieved(MqttApplicationMessageReceivedEventArgs Args)
        {
            string[] TopicPath = Args.ApplicationMessage.Topic.Split("/");
            string Message     = Args.ApplicationMessage.Payload == null ? "" : Encoding.UTF8.GetString(Args.ApplicationMessage.Payload);

            MessageDispatchHandlers[TopicPath[0]](TopicPath, Message).ContinueWith((HandlerTask) => {
                Console.WriteLine($"[{DateTime.Now}][!] Exception in handler {TopicPath[0]} {HandlerTask.Exception.Message}");
            }, TaskContinuationOptions.OnlyOnFaulted);

            //Console.WriteLine($"{DateTime.Now}][?] TOPIC:{Args.ApplicationMessage.Topic} MSG:{Message}");
        }

        async Task RequestPriority()
        {
            await _MQTTClient.PublishAsync($"get_priority/{_WorkType.ToString().ToLower()}", _PayoutAddress, MqttQualityOfServiceLevel.AtMostOnce);
        }

        async Task SubmitWork(string WorkType, string BlockHash, string WorkResult)
        {
            //
            // For every 100 solves, send 1 for developer.
            //
            bool SubmitForDev = Interlocked.Increment(ref SolvedBlockNumber) % 100 == 0;

            var WalletAddress = SubmitForDev ? DeveloperAddress : _PayoutAddress;

            //
            // Send result to broker.
            //
            await _MQTTClient.InternalClient.PublishAsync($"result/{WorkType}", string.Join(',', BlockHash, WorkResult, WalletAddress), MqttQualityOfServiceLevel.AtMostOnce).ConfigureAwait(false);

            if (_Verbose)
            {
                Console.WriteLine($"[{DateTime.Now}][?] Sent block {BlockHash}:{WorkResult} dev {SubmitForDev}");
            }
        }

        public async Task HandleWork(string[] TopicPath, string Message)
        {
            int    QueueIndex = int.Parse(TopicPath[2]);
            string WorkType   = TopicPath[1];
            string BlockHash  = Message.Substring(0, 64);
            string Difficulty = Message.Substring(65, 16);

            if (_Verbose)
            {
                Console.WriteLine($"[{DateTime.Now}][?] Got block {WorkType}/{QueueIndex} {BlockHash}:{Difficulty}");
            }

            if (ulong.Parse(Difficulty, System.Globalization.NumberStyles.HexNumber) < _MinDifficulty)
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
                await SubmitWork(WorkType, BlockHash, Response.WorkResult);

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

        public async Task HandlePriority(string[] TopicPath, string Message)
        {
            var Queues = JsonSerializer.Deserialize<MQTTClientPriorityMessage>(Message);

            if (_Verbose)
            {
                Console.WriteLine($"[{DateTime.Now}][?] Priority response: {Message}");
            }
        }

        public async Task HandleCancel(string[] TopicPath, string Message)
        {
            DateTime Old;

            //
            // Dont cancel jobs we have already done.
            // 
            if (!_GeneratedBlocks.TryRemove(Message, out Old))
            {
                await _WorkServer.CancelWork(Message);
            }
        }

        public async Task HandleClientBlockRewarded(string[] TopicPath, string Message)
        {
            string ClientWalletAddress = TopicPath[1];

            var StatsMessage = JsonSerializer.Deserialize<MQTTClientBlockRewardedMessage>(Message);

            Console.WriteLine($"[{DateTime.Now}][+] Block rewarded {StatsMessage.BlockRewarded} {StatsMessage.NumWorkPaid}/{StatsMessage.NumOnDemandAccepted + StatsMessage.NumPrecacheAccepted} paid {(StatsMessage.PercentageTotal * 100.0f).ToString("0.00")}% of next payout");
        }

        public async Task HandleHeartbeat(string[] TopicPath, string Message)
        {
            _LastServerHeartbeat = DateTime.Now;

            if (_Verbose)
            {
                Console.WriteLine($"[{DateTime.Now}][?] Heartbeat");
            }
        }
    }
}

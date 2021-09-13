using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MQTTnet.Client.Options;

namespace BoomPowSharp
{
    class Program
    {
        public class WorkGenerateRequest
        {
            [JsonPropertyName("action")]
            public string Action { get; set; }

            [JsonPropertyName("hash")]
            public string BlockHash { get; set; }

            [JsonPropertyName("difficulty")]
            public string Difficulty { get; set; }
        }

        static async Task Main(string[] args)
        {
            //var Rand = new Random();

            //var JsonText = await File.ReadAllTextAsync(@"C:\Users\iMoD1998\Desktop\NanoRequests.json");

            //var Requests = JsonSerializer.Deserialize<List<WorkGenerateRequest>>(JsonText);

            //NanoClientRPC NanoClientRPC = new NanoClientRPC(new Uri("http://127.0.0.1:20000"));

            //foreach (var Req in Requests.OrderBy(X => Rand.Next()).Take(1000))
            //{
            //    List<Task<HttpResponseMessage>> Tasks = new List<Task<HttpResponseMessage>>()
            //    {
            //        NanoClientRPC.WorkGenerate(Req.BlockHash, Req.Difficulty),
            //        NanoClientRPC.WorkCancel(Req.BlockHash)
            //    };

            //    while (Tasks.Any())
            //    {
            //        var FinishedTask = await Task.WhenAny(Tasks);
            //        Tasks.Remove(FinishedTask);
            //        await FinishedTask;
            //    }

            //    //Console.WriteLine($"Got Response in { stopWatch.Elapsed.TotalMilliseconds } ms { await Response.Content.ReadAsStringAsync() }");
            //}

            var Banner = @"
                 ____                        ____                ____  _
                | __ )  ___   ___  _ __ ___ |  _ \ _____      __/ ___|| |__   __ _ _ __ _ __
                |  _ \ / _ \ / _ \| '_ ` _ \| |_) / _ \ \ /\ / /\___ \| '_ \ / _` | '__| '_ \
                | |_) | (_) | (_) | | | | | |  __/ (_) \ V  V /  ___) | | | | (_| | |  | |_) |
                |____/ \___/ \___/|_| |_| |_|_|   \___/ \_/\_/  |____/|_| |_|\__,_|_|  | .__/
                                                                                       |_|";

            Console.WriteLine(Banner);

            var BrokerOptions = new MqttClientOptionsBuilder().WithCredentials("client", "client")
                                                              .WithWebSocketServer("bpow.banano.cc:443/mqtt")
                                                              .WithTls()
                                                              .WithCleanSession(false);

            var BoomPow = new BoomPow(BrokerOptions, new Uri("http://127.0.0.1:20000"));

            await BoomPow.Init();

            Console.ReadKey();
        }
    }
}

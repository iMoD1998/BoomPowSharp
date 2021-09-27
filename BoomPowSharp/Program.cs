using System;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MQTTnet.Client.Options;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace BoomPowSharp
{
    class Program
    {
        static string Banner = @"
 ____                        ____                ____  _
| __ )  ___   ___  _ __ ___ |  _ \ _____      __/ ___|| |__   __ _ _ __ _ __
|  _ \ / _ \ / _ \| '_ ` _ \| |_) / _ \ \ /\ / /\___ \| '_ \ / _` | '__| '_ \
| |_) | (_) | (_) | | | | | |  __/ (_) \ V  V /  ___) | | | | (_| | |  | |_) |
|____/ \___/ \___/|_| |_| |_|_|   \___/ \_/\_/  |____/|_| |_|\__,_|_|  | .__/
                                                                       |_|";

        private static readonly Regex BanAddressRegex = new Regex("^(ban)_[13]{1}[13456789abcdefghijkmnopqrstuwxyz]{59}$");

        static int Main(string[] args)
        {
            RootCommand RootCommand = new RootCommand {
                Description = ""
            };

            RootCommand.Add(new Option<Uri>(
                aliases: new string[] { "--worker-url", "-u" },
                description: "URL of the nano work server.",
                getDefaultValue: () => new Uri("http://127.0.0.1:20000")
           ) { ArgumentHelpName = "URL" });

            RootCommand.Add(new Option<Uri>(
                aliases: new string[] { "--server", "-s" },
                description: "URL BoomPow MQTT server.",
                getDefaultValue: () => new Uri("wss://client:client@bpow.banano.cc/mqtt")
            ) { ArgumentHelpName = "URL" });

            RootCommand.Add(new Option<string>(
                aliases: new string[] { "--payout", "-p" },
                description: "URL BoomPow MQTT server.",
                getDefaultValue: () => "ban_1ncpdt1tbusi9n4c7pg6tqycgn4oxrnz5stug1iqyurorhwbc9gptrsmxkop"
            ) { ArgumentHelpName = "Address" });

            RootCommand.Add(new Option<BoomPow.BoomPowWorkType>(
                aliases: new string[] { "--work", "-w" },
                description: "Desired work type. Options: any (default), ondemand, precache.",
                getDefaultValue: () => BoomPow.BoomPowWorkType.Any
            ) { ArgumentHelpName = "WORKTYPE" });

            RootCommand.Add(new Option(
                aliases: new string[] { "--verbose", "-v" },
                description: "Desired work type. Options: any (default), ondemand, precache."
            ));

            RootCommand.Add(new Option<string>(
               aliases: new string[] { "--min-difficulty", "-d" },
               description: "Desired work type. Options: any (default), ondemand, precache.",
               getDefaultValue: () => "0"
           ));

            RootCommand.Handler = CommandHandler.Create<Uri, Uri, string, BoomPow.BoomPowWorkType, bool, string>(async (workerUrl, server, payout, work, verbose, minDifficulty) => { 
                Console.WriteLine(Banner);

                if(workerUrl.Scheme != Uri.UriSchemeHttp && workerUrl.Scheme != Uri.UriSchemeHttps)
                {
                    Console.WriteLine("Error: Worker only supports HTTP/HTTPS.");
                    return;
                }

                if (server.Scheme != "wss" && server.Scheme != "ws")
                {
                    Console.WriteLine("Error: Worker only supports websocket.");
                    return;
                }

                if (!BanAddressRegex.IsMatch(payout))
                {
                    Console.WriteLine("Error: Invalid wallet address.");
                    return;
                }

                var Credentials = server.UserInfo == "" ? null : server.UserInfo.Split(":");
                var Username = Credentials == null ? "" : Credentials[0];
                var Password = Credentials == null ? "" : Credentials[1];

                var BrokerOptions = new MqttClientOptionsBuilder().WithCredentials(Username, Password)
                                                                  .WithWebSocketServer($"{server.Host}:{server.Port}{server.LocalPath}")
                                                                  .WithCleanSession(false);

                var BoomPow = new BoomPow(server.Scheme == "wss" ? BrokerOptions.WithTls() : BrokerOptions, workerUrl, payout, work, Convert.ToUInt64(minDifficulty, 16), verbose);

                Console.WriteLine("=======Config========");
                Console.WriteLine($"Worker: {workerUrl}");
                Console.WriteLine($"Server: {server}");
                Console.WriteLine($"Payout Address: {payout}");
                Console.WriteLine($"Desired Work: {work}");
                Console.WriteLine($"Min Difficulty: {minDifficulty}");
                Console.WriteLine("=====================");

                await BoomPow.Run();
            });


            return RootCommand.Invoke(args);
        }
    }
}

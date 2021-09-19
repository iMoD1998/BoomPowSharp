using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BoomPowSharp
{
    public class NanoClientRPC
    {
        private static SocketsHttpHandler _SocketHTTPHandler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 10,
        };

        private static HttpClient            _WebClient       = new HttpClient(_SocketHTTPHandler);
        private static JsonSerializerOptions SerializeOptions = new JsonSerializerOptions { WriteIndented = false, IgnoreNullValues = true };

        public class RPCRequest
        {
            [JsonPropertyName("action")]
            public string Action { get; set; }
        }

        public class RPCGenerateWorkRequest : RPCRequest
        {
            [JsonPropertyName("hash")]
            public string BlockHash { get; set; }
            [JsonPropertyName("difficulty")]
            public string Difficulty { get; set; }
        };

        public class RPCGenerateWorkResponse
        {
            [JsonPropertyName("error")]
            public string Error { get; set; }
            [JsonPropertyName("work")]
            public string WorkResult { get; set; }
            [JsonPropertyName("difficulty")]
            public string Difficulty { get; set; }
            [JsonPropertyName("multiplier")]
            public string Multiplier { get; set; }
        };

        public class RPCCancelWorkRequest : RPCRequest
        {
            [JsonPropertyName("hash")]
            public string BlockHash { get; set; }
        };

        public class RPCValidateWorkRequest : RPCRequest
        {
            [JsonPropertyName("hash")]
            public string BlockHash { get; set; }
            [JsonPropertyName("work")]
            public string WorkResult { get; set; }
            [JsonPropertyName("difficulty")]
            public string Difficulty { get; set; }
        };

        public class RPCValidateWorkResponse
        {
            [JsonPropertyName("valid_all")]
            public string _AllValid { get; set; }
            [JsonPropertyName("valid_receive")]
            public string _NumReceived { get; set; }
            [JsonPropertyName("difficulty")]
            public string Difficulty { get; set; }
            [JsonPropertyName("multiplier")]
            public string _Multiplier { get; set; }

            //
            // Conversions
            //
            [JsonIgnore]
            public int AllValid { get { return int.Parse(_AllValid ?? "0"); } }
            [JsonIgnore]
            public int NumReceived { get { return int.Parse(_NumReceived ?? "0"); } }
            [JsonIgnore]
            public float Multiplier { get { return float.Parse(_Multiplier ?? "0"); } }
        };

        public class RPCBenchmarkRequest : RPCRequest
        {
            [JsonPropertyName("count")]
            public string Count { get; set; }
        };

        public class RPCBenchmarkResponse
        {
            [JsonPropertyName("average")]
            public string _AverageTime { get; set; }
            [JsonPropertyName("count")]
            public string _Count { get; set; }
            [JsonPropertyName("difficulty")]
            public string Difficulty { get; set; }
            [JsonPropertyName("duration")]
            public string _Duration { get; set; }
            [JsonPropertyName("hint")]
            public string Hint { get; set; }
            [JsonPropertyName("multiplier")]
            public string _Multiplier { get; set; }

            //
            // Conversions
            //
            [JsonIgnore]
            public int AverageTime { get { return int.Parse(_AverageTime ?? "0"); } }
            [JsonIgnore]
            public int Count { get { return int.Parse(_Count ?? "0"); } }
            [JsonIgnore]
            public int Duration { get { return int.Parse(_Duration ?? "0"); } }
            [JsonIgnore]
            public float Multiplier { get { return float.Parse(_Multiplier ?? "0"); } }
        };

        public class RPCStatusRequest : RPCRequest
        {

        }

        public class RPCStatusResponse
        {
            [JsonPropertyName("generating")]
            public string _NumGenerating { get; set; }
            [JsonPropertyName("queue_size")]
            public string _NumQueue { get; set; }

            //
            // Conversions
            //
            [JsonIgnore]
            public int NumGenerating { get { return int.Parse(_NumGenerating ?? "0"); } }
            [JsonIgnore]
            public int NumQueue { get { return int.Parse(_NumQueue ?? "0"); } }
        };

        static NanoClientRPC()
        {
            _WebClient.DefaultRequestHeaders.Clear();
            _WebClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private Uri _WorkerUri;
        public Uri URL { get { return _WorkerUri; } }

        public NanoClientRPC(Uri WorkerUri)
        {
            _WorkerUri = WorkerUri;
        }

        public async Task<RPCGenerateWorkResponse> GenerateWork(string BlockHash, string Difficulty)
        {
            var WorkGeneratePayload = new RPCGenerateWorkRequest
            {
                Action = "work_generate",
                BlockHash = BlockHash,
                Difficulty = Difficulty
            };

            var HttpResult = await _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions))).ConfigureAwait(false);

            return await JsonSerializer.DeserializeAsync<RPCGenerateWorkResponse>(await HttpResult.Content.ReadAsStreamAsync());
        }

        public Task<HttpResponseMessage> CancelWork( string BlockHash )
        {
            var WorkGeneratePayload = new RPCCancelWorkRequest {
                Action = "work_cancel",
                BlockHash = BlockHash,
            };

            return _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions)));
        }

        public async Task<RPCValidateWorkResponse> ValidateWork( string BlockHash, string WorkResult, string Difficulty)
        {
            var WorkGeneratePayload = new RPCValidateWorkRequest {
                Action = "work_validate",
                BlockHash = BlockHash,
                WorkResult = WorkResult,
                Difficulty = Difficulty
            };

            var Response = await _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions))).ConfigureAwait(false);

            return await JsonSerializer.DeserializeAsync<RPCValidateWorkResponse>(await Response.Content.ReadAsStreamAsync(), SerializeOptions);
        }

        public async Task<RPCBenchmarkResponse> Benchmark(int Count)
        {
            var WorkGeneratePayload = new RPCBenchmarkRequest
            {
                Action = "status",
                Count = Count.ToString()
            };

            var Response = await _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions))).ConfigureAwait(false);

            return await JsonSerializer.DeserializeAsync<RPCBenchmarkResponse>(await Response.Content.ReadAsStreamAsync(), SerializeOptions);
        }

        public async Task<RPCStatusResponse> GetStatus( )
        {
            var WorkGeneratePayload = new RPCStatusRequest {
                Action = "status"
            };

            var Response = await _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions))).ConfigureAwait(false);

            return await JsonSerializer.DeserializeAsync<RPCStatusResponse>(await Response.Content.ReadAsStreamAsync(), SerializeOptions);
        }
    }
}

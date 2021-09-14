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
        private SocketsHttpHandler _SocketHTTPHandler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer     = 10
        };

        private HttpClient _WebClient;
        private Uri _WorkerUri;

        static JsonSerializerOptions SerializeOptions = new JsonSerializerOptions { WriteIndented = false, IgnoreNullValues = true };

        public class RPCRequest
        {
            [JsonPropertyName("action")]
            public string Action { get; set; }
        }

        public class WorkGenerateRequest : RPCRequest
        {
            [JsonPropertyName("hash")]
            public string BlockHash { get; set; }
            [JsonPropertyName("difficulty")]
            public string Difficulty { get; set; }
        };

        public class WorkGenerateResponse
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

        public class WorkCancelRequest : RPCRequest
        {
            [JsonPropertyName("hash")]
            public string BlockHash { get; set; }
        };

        public class WorkValidateRequest : RPCRequest
        {
            [JsonPropertyName("hash")]
            public string BlockHash { get; set; }
            [JsonPropertyName("work")]
            public string WorkResult { get; set; }
            [JsonPropertyName("difficulty")]
            public string Difficulty { get; set; }
        };

        public class StatusResponse
        {
            [JsonPropertyName("generating")]
            public string NumGenerating { get; set; }
            [JsonPropertyName("queue_size")]
            public string NumQueue { get; set; }
        };

        public NanoClientRPC(Uri WorkerUri)
        {
            _WorkerUri = WorkerUri;
            _WebClient = new HttpClient(_SocketHTTPHandler);
            _WebClient.DefaultRequestHeaders.Clear();
            _WebClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<WorkGenerateResponse> WorkGenerate(string BlockHash, string Difficulty)
        {
            var WorkGeneratePayload = new WorkGenerateRequest
            {
                Action = "work_generate",
                BlockHash = BlockHash,
                Difficulty = Difficulty
            };

            var HttpResult = await _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions)));

            return await JsonSerializer.DeserializeAsync<WorkGenerateResponse>(await HttpResult.Content.ReadAsStreamAsync());
        }

        public async Task WorkCancel( string BlockHash )
        {
            var WorkGeneratePayload = new WorkCancelRequest {
                Action = "work_cancel",
                BlockHash = BlockHash,
            };

            await _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions)));
        }

        public async Task<HttpResponseMessage> WorkValidate( string BlockHash, string WorkResult, string Difficulty)
        {
            var WorkGeneratePayload = new WorkValidateRequest {
                Action = "work_validate",
                BlockHash = BlockHash,
                WorkResult = WorkResult,
                Difficulty = Difficulty
            };

            return await _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions)));
        }

        public async Task<StatusResponse> Status( )
        {
            var WorkGeneratePayload = new RPCRequest {
                Action = "status"
            };

            var HttpResult = await _WebClient.PostAsync(_WorkerUri, new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(WorkGeneratePayload, SerializeOptions)));

            return await JsonSerializer.DeserializeAsync<StatusResponse>(await HttpResult.Content.ReadAsStreamAsync());
        }
    }
}

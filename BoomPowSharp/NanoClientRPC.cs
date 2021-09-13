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
            PooledConnectionLifetime    = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer     = 40
        };

        private HttpClient _WebClient;
        private Uri _WorkerUri;

        static JsonSerializerOptions SerializeOptions = new JsonSerializerOptions { WriteIndented = false };

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

        public NanoClientRPC(Uri WorkerUri)
        {
            _WorkerUri = WorkerUri;
            _WebClient = new HttpClient(_SocketHTTPHandler);
            _WebClient.DefaultRequestHeaders.Clear();
            _WebClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<WorkGenerateResponse> WorkGenerate(string BlockHash, string Difficulty)
        {
            var WorkGeneratePayload = new { 
                action = "work_generate",
                hash = BlockHash,
                difficulty = Difficulty
            };

            var HttpResult = await _WebClient.PostAsync(_WorkerUri, new StringContent(JsonSerializer.Serialize(WorkGeneratePayload, SerializeOptions), Encoding.UTF8, "application/json"));

            return await JsonSerializer.DeserializeAsync<WorkGenerateResponse>(await HttpResult.Content.ReadAsStreamAsync());
        }

        public async Task<HttpResponseMessage> WorkCancel( string BlockHash )
        {
            var WorkGeneratePayload = new {
                action = "work_cancel",
                hash = BlockHash,
            };

            return await _WebClient.PostAsync(_WorkerUri, new StringContent(JsonSerializer.Serialize(WorkGeneratePayload, SerializeOptions), Encoding.UTF8, "application/json"));
        }

        public Task<HttpResponseMessage> WorkValidate( string BlockHash )
        {
            var WorkGeneratePayload = new {
                action = "work_validate",
                hash = BlockHash,
            };

            return _WebClient.PostAsync(_WorkerUri, new StringContent(JsonSerializer.Serialize(WorkGeneratePayload, SerializeOptions), Encoding.UTF8, "application/json"));
        }

        public Task<HttpResponseMessage> Status( )
        {
            var WorkGeneratePayload = new {
                action = "status"
            };

            return _WebClient.PostAsync(_WorkerUri, new StringContent(JsonSerializer.Serialize(WorkGeneratePayload, SerializeOptions), Encoding.UTF8, "application/json"));
        }
    }
}

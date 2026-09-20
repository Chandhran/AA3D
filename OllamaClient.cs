using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AA3D
{
    /// <summary>
    /// Thin async wrapper around the Ollama /api/generate endpoint.
    /// Uses non-streaming mode so we get a single JSON response.
    /// </summary>
    public static class OllamaClient
    {
        // ── Configuration ────────────────────────────────────────────────
        public const string DEFAULT_ENDPOINT = "http://localhost:11434/api/generate";
        public const string DEFAULT_MODEL    = "qwen2.5-coder:14b";

        // Single shared HttpClient (HttpClient is thread-safe and designed to be reused).
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(180)   // 3 min — large model, cold start
        };

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Send a prompt to Ollama and return the raw text response.
        /// Throws on network error or HTTP failure.
        /// </summary>
        public static async Task<string> GenerateAsync(
            string prompt,
            string model    = DEFAULT_MODEL,
            string endpoint = DEFAULT_ENDPOINT,
            CancellationToken ct = default)
        {
            var requestBody = new
            {
                model  = model,
                prompt = prompt,
                stream = false,           // single JSON blob back — easier to parse
                options = new
                {
                    temperature      = 0.2,   // low: deterministic JSON output
                    top_p            = 0.9,
                    num_predict      = 4096   // enough for a full architectural model
                }
            };

            string json  = JsonConvert.SerializeObject(requestBody);
            var content  = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage resp = await _http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            string body  = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var   jObj   = JObject.Parse(body);

            // Ollama non-streaming response: { "response": "...", "done": true, ... }
            return jObj["response"]?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Check if Ollama is reachable at the given endpoint.
        /// Returns true if the /api/tags endpoint responds with HTTP 200.
        /// </summary>
        public static async Task<bool> IsAliveAsync(
            string endpoint = DEFAULT_ENDPOINT,
            CancellationToken ct = default)
        {
            try
            {
                string tagsUrl = endpoint.Replace("/api/generate", "/api/tags");
                var resp = await _http.GetAsync(tagsUrl, ct).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}

using DirectLineMiddleware.Interfaces;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace DirectLineMiddleware.Services
{
    public class OmnichannelService : IOmnichannelService
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OmnichannelService> _logger;

        private readonly string _orgUrl;
        private readonly string _tenantId;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _channelId;
        private readonly string _defaultLanguage;

        public OmnichannelService(
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            ILogger<OmnichannelService> logger)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
            _logger = logger;

            _orgUrl = _config["Omnichannel:OrgUrl"]
                      ?? throw new ArgumentNullException("Omnichannel:OrgUrl");
            _tenantId = _config["Omnichannel:TenantId"]
                        ?? throw new ArgumentNullException("Omnichannel:TenantId");
            _clientId = _config["Omnichannel:ClientId"]
                        ?? throw new ArgumentNullException("Omnichannel:ClientId");
            _clientSecret = _config["Omnichannel:ClientSecret"]
                            ?? throw new ArgumentNullException("Omnichannel:ClientSecret");
            _channelId = _config["Omnichannel:ChannelId"] ?? "external-santex";
            _defaultLanguage = _config["Omnichannel:DefaultLanguage"] ?? "es-AR";
        }

        // ================================
        // Auth contra Azure AD (client credentials)
        // ================================
        private async Task<string> GetAccessTokenAsync()
        {
            var http = _httpClientFactory.CreateClient();

            var tokenEndpoint = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";

            var body = new Dictionary<string, string>
            {
                { "client_id", _clientId },
                { "client_secret", _clientSecret },
                { "grant_type", "client_credentials" },
                { "scope", $"{_orgUrl}/.default" }
            };

            var content = new FormUrlEncodedContent(body);
            var response = await http.PostAsync(tokenEndpoint, content);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error getting token AAD: {StatusCode} {Body}", response.StatusCode, json);
                throw new Exception($"Error getting token AAD: {response.StatusCode}");
            }

            var tokenObj = JsonConvert.DeserializeObject<JObject>(json);
            return tokenObj["access_token"]?.ToString()
                   ?? throw new Exception("No access_token in token response");
        }

        private async Task<HttpClient> CreateOcClientAsync()
        {
            var token = await GetAccessTokenAsync();

            var http = _httpClientFactory.CreateClient();
            http.BaseAddress = new Uri(_orgUrl.TrimEnd('/') + "/");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return http;
        }

        // ================================
        // 1) Crear sesión de Omnichannel
        // ================================
        public async Task<string> CreateSessionAsync(IActivity sourceActivity)
        {
            var http = await CreateOcClientAsync();

            var customerId = sourceActivity.From?.Id ?? "anonymous";
            var customerName = sourceActivity.From?.Name ?? "Cliente";

            var payload = new
            {
                channelId = _channelId,
                language = _defaultLanguage,
                customer = new
                {
                    id = customerId,
                    displayName = customerName
                },
                // acá podés mandar más contexto si querés
                context = new
                {
                    externalConversationId = sourceActivity.Conversation?.Id,
                    externalChannel = "santex-directline"
                }
            };

            var url = "oc/api/v1.0/registration";
            var jsonContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await http.PostAsync(url, jsonContent);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error creating OC session: {StatusCode} {Body}", response.StatusCode, json);
                throw new Exception($"Error creating OC session: {response.StatusCode}");
            }

            var obj = JsonConvert.DeserializeObject<JObject>(json);
            var sessionId = obj["sessionId"]?.ToString();

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                _logger.LogError("OC session response without sessionId: {Body}", json);
                throw new Exception("No sessionId returned from OC");
            }

            _logger.LogInformation("OC Session created: {SessionId}", sessionId);

            return sessionId;
        }

        // ================================
        // 2) Enviar transcript como mensajes
        // ================================
        public async Task SendTranscriptAsync(string sessionId, List<Activity> activities)
        {
            if (activities == null || activities.Count == 0)
            {
                _logger.LogWarning("Transcript vacío para session {SessionId}", sessionId);
                return;
            }

            var http = await CreateOcClientAsync();

            foreach (var act in activities)
            {
                if (string.IsNullOrWhiteSpace(act.Text) && (act.Attachments == null || act.Attachments.Count == 0))
                    continue;

                var isFromBot = act.From?.Id?.StartsWith("bot", StringComparison.OrdinalIgnoreCase) == true
                                || act.From?.Role == "bot";

                var source = isFromBot ? "bot" : "customer";

                var payload = new
                {
                    sessionId = sessionId,
                    type = "input",
                    text = act.Text,
                    source = source,
                    timestamp = (act.Timestamp ?? DateTime.UtcNow).ToUniversalTime().ToString("o")
                };

                var url = "oc/api/v1.0/messages";
                var jsonContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

                var response = await http.PostAsync(url, jsonContent);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Error sending message to OC. Session {SessionId}. Status {Status} Body {Body}",
                        sessionId, response.StatusCode, json);
                    // si querés seguir intentando con el resto, no tirés excepción acá
                }
            }

            _logger.LogInformation("Transcript enviado a OC para Session {SessionId} con {Count} actividades", sessionId, activities.Count);
        }

        // ================================
        // 3) Disparar handoff al agente
        // ================================
        public async Task TriggerHandoffAsync(string sessionId, string reason)
        {
            var http = await CreateOcClientAsync();

            var payload = new
            {
                sessionId = sessionId,
                reason = reason ?? "escalado desde bot externo"
            };

            var url = "oc/api/v1.0/handoff";
            var jsonContent = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            var response = await http.PostAsync(url, jsonContent);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error triggering handoff in OC. Session {SessionId}. Status {Status} Body {Body}",
                    sessionId, response.StatusCode, json);
                throw new Exception($"Error triggering handoff: {response.StatusCode}");
            }

            _logger.LogInformation("Handoff disparado en OC para Session {SessionId}", sessionId);
        }
    }
}

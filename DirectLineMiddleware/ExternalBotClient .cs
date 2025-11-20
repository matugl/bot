using DirectLineMiddleware.Interfaces;

public class ExternalBotClient : IExternalBotClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public ExternalBotClient(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<string> SendAsync(string userMessage, string userId, string conversationId)
    {
        var url = _config["ExternalBot:BaseUrl"]; // ej: https://bot-externo.com/api/messages

        var payload = new
        {
            message = userMessage,
            userId = userId,
            conversationId = conversationId
        };

        var response = await _httpClient.PostAsJsonAsync(url, payload);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<ExternalBotResponse>();

        return json?.Reply ?? "(sin respuesta del bot externo)";
    }
}

public class ExternalBotResponse
{
    public string Reply { get; set; }
}

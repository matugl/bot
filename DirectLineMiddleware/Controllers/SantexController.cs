using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

[ApiController]
[Route("api/santex")]
public class SantexController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http;

    public SantexController(IConfiguration config)
    {
        _config = config;
        _http = new HttpClient();
    }

    [HttpPost("start-conversation")]
    public async Task<IActionResult> StartConversation([FromBody] SantexStartRequest request)
    {
        var secret = _config["DirectLine:Secret"];

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", secret);
        var body = new
        {
            user = new
            {
                id = request.UserId ?? Guid.NewGuid().ToString(),
                name = request.Name ?? "Invitado",
                locale = request.Locale ?? "es-AR"
            }
        };
        // 1. Pedimos token y conversacion a Direct Line

        var response = await _http.PostAsJsonAsync(
            "https://directline.botframework.com/v3/directline/conversations",
            body
        );

        var json = await response.Content.ReadAsStringAsync();

        // 2. Podés almacenar la relación userId ↔ conversationId si querés

        return Content(json, "application/json");
    }
}

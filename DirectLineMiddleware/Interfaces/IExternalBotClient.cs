namespace DirectLineMiddleware.Interfaces
{
    public interface IExternalBotClient
    {
        Task<string> SendAsync(string userMessage, string userId, string conversationId);
        Task SendAgentMessageAsync(string conversationId, string userId, string text);

    }

}

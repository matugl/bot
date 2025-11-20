using Microsoft.Bot.Schema;

namespace DirectLineMiddleware.Interfaces
{
    public interface IOmnichannelService
    {
        Task<string> CreateSessionAsync(IActivity sourceActivity);
        Task SendTranscriptAsync(string sessionId, List<Activity> activities);
        Task TriggerHandoffAsync(string sessionId, string reason);
    }
}

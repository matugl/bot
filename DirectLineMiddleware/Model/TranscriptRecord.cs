using Microsoft.Bot.Schema;

namespace DirectLineMiddleware.Controllers
{
    public class TranscriptRecord
    {
        public string ConversationId { get; set; }
        public List<Activity> Activities { get; set; } = new();
        public DateTime Created { get; set; } = DateTime.UtcNow;
    }

}

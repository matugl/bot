using Microsoft.Bot.Schema;
using System;
using System.Collections.Generic;

namespace DirectLineMiddleware.Transcript
{
    public class TranscriptRecord
    {
        public string ConversationId { get; set; }
        public List<Activity> Activities { get; set; } = new();
        public DateTime Created { get; set; } = DateTime.UtcNow;
    }
}

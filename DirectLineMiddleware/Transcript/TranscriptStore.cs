using System.Collections.Generic;

namespace DirectLineMiddleware.Transcript
{
    public static class TranscriptStore
    {
        public static Dictionary<string, TranscriptRecord> Store { get; } = new();
    }
}

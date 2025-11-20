using DirectLineMiddleware.Controllers;

namespace DirectLineMiddleware.Model
{
    public static class TranscriptStore
    {
        public static Dictionary<string, TranscriptRecord> Store { get; } = new();
    }

}

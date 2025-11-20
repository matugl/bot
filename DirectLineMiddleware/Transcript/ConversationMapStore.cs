using System.Collections.Concurrent;

public static class ConversationMapStore
{
    private static readonly ConcurrentDictionary<string, ConversationMap> _maps =
        new ConcurrentDictionary<string, ConversationMap>();

    public static void Save(ConversationMap map)
    {
        _maps[map.OcConversationId] = map;
    }

    public static bool TryGet(string ocConvId, out ConversationMap map)
    {
        return _maps.TryGetValue(ocConvId, out map);
    }
}

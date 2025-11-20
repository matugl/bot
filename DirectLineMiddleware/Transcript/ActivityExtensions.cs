using Microsoft.Bot.Schema;
using Newtonsoft.Json;

namespace DirectLineMiddleware.Transcript
{
    public static class ActivityExtensions
    {
        public static Activity CloneActivity(this IActivity activity)
        {
            return JsonConvert.DeserializeObject<Activity>(
                JsonConvert.SerializeObject(activity)
            );
        }
    }
}

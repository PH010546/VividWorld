#nullable enable
using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VividWorld.Core.Persistence;

namespace VividWorld.Core.Events
{
    public static class PublicEventSanitizer
    {
        public static string SanitizeJson(WorldEvent evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            var jo = JObject.Parse(VividJson.Write(evt));

            RemovePropertyIgnoreCase(jo, "fabricated");
            RemovePropertyIgnoreCase(jo, "originatorHeroId");
            RemovePropertyIgnoreCase(jo, "isFabricated");

            if (jo["facts"] is JArray factsArr)
            {
                foreach (var fToken in factsArr)
                {
                    if (fToken is JObject fObj)
                    {
                        RemovePropertyIgnoreCase(fObj, "isFabricated");
                    }
                }
            }

            if (MadeUpTalk.IsHearsayOnly(evt))
            {
                RemovePropertyIgnoreCase(jo, "situationId");
            }

            return jo.ToString(Formatting.Indented);
        }

        private static void RemovePropertyIgnoreCase(JObject jo, string propertyName)
        {
            var prop = jo.Properties()
                .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            prop?.Remove();
        }
    }
}

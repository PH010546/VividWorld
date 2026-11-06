using System;
using System.Collections.Generic;
using VividWorld.Core.Catalog;
using VividWorld.Core.Events;

namespace VividWorld.Core.Secrets
{
    public static class SecretHarmEvaluation
    {
        public static HashSet<string> EvaluateHarmedHeroIds(
            WorldEvent evt,
            EventTemplate? template,
            ISecretRelationshipLookup? lookup)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (evt == null) return result;

            if (evt.SecretHarmedHeroIds != null)
            {
                foreach (var id in evt.SecretHarmedHeroIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        result.Add(id);
                    }
                }
            }

            if (template?.Harmed == null) return result;

            if (template.Harmed.Roles != null && evt.Participants != null)
            {
                foreach (var role in template.Harmed.Roles)
                {
                    if (!string.IsNullOrEmpty(role) &&
                        evt.Participants.TryGetValue(role, out var hId) &&
                        !string.IsNullOrEmpty(hId))
                    {
                        result.Add(hId);
                    }
                }
            }

            if (template.Harmed.KinOf != null && evt.Participants != null && lookup != null)
            {
                foreach (var role in template.Harmed.KinOf)
                {
                    if (!string.IsNullOrEmpty(role) &&
                        evt.Participants.TryGetValue(role, out var hId) &&
                        !string.IsNullOrEmpty(hId))
                    {
                        var kin = lookup.GetKin(hId);
                        if (kin != null)
                        {
                            foreach (var k in kin)
                            {
                                if (!string.IsNullOrEmpty(k))
                                {
                                    result.Add(k);
                                }
                            }
                        }
                    }
                }
            }

            if (template.Harmed.FriendsOf != null && evt.Participants != null && lookup != null)
            {
                foreach (var role in template.Harmed.FriendsOf)
                {
                    if (!string.IsNullOrEmpty(role) &&
                        evt.Participants.TryGetValue(role, out var hId) &&
                        !string.IsNullOrEmpty(hId))
                    {
                        var friends = lookup.GetFriends(hId);
                        if (friends != null)
                        {
                            foreach (var f in friends)
                            {
                                if (!string.IsNullOrEmpty(f))
                                {
                                    result.Add(f);
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}

#nullable enable
using System;
using VividWorld.Core.Events;
using VividWorld.Core.Rumors;

namespace VividWorld.Core.Catalog
{
    public readonly struct SelfTellResult
    {
        public bool CanTell { get; }
        public string? Role { get; }
        public bool IsNever { get; }
        public string? Trait { get; }
        public int ActualValue { get; }
        public int MinValue { get; }
        public string? ReasonText { get; }

        public SelfTellResult(bool canTell, string? role, bool isNever, string? trait, int actualValue, int minValue, string? reasonText)
        {
            CanTell = canTell;
            Role = role;
            IsNever = isNever;
            Trait = trait;
            ActualValue = actualValue;
            MinValue = minValue;
            ReasonText = reasonText;
        }

        public static SelfTellResult Allowed => new(true, null, false, null, 0, 0, null);

        public static SelfTellResult Never(string role) =>
            new(false, role, true, null, 0, 0, $"won't tell own (role {role})");

        public static SelfTellResult TraitTooLow(string role, string trait, int actual, int min) =>
            new(false, role, false, trait, actual, min, $"won't tell own (role {role}, {trait} {actual} < {min})");
    }

    /// <summary>
    /// 當事人自述資格判定（純函式）。
    /// 當事人為事件 participants 之一時，依模板的 selfTell 規則決定是否能講述該事件。
    /// 秘密類事件（Origin == Secret）絕不套用此限制。
    /// </summary>
    public static class SelfTellEvaluator
    {
        public static SelfTellResult Evaluate(
            WorldEvent? evt,
            string? speakerHeroId,
            Func<string, EventTemplate?>? getTemplate,
            IHeroTraitLookup? traits)
        {
            // 性格只在規則真的要看時才查：講述者輪每小時對每個話題都會問一次
            return Evaluate(evt, speakerHeroId, getTemplate,
                () => traits != null ? traits.Of(speakerHeroId!) : null);
        }

        public static SelfTellResult Evaluate(
            WorldEvent? evt,
            string? speakerHeroId,
            Func<string, EventTemplate?>? getTemplate,
            TraitProfile? profile)
        {
            return Evaluate(evt, speakerHeroId, getTemplate, () => profile);
        }

        private static SelfTellResult Evaluate(
            WorldEvent? evt,
            string? speakerHeroId,
            Func<string, EventTemplate?>? getTemplate,
            Func<TraitProfile?> profileOf)
        {
            if (evt == null || string.IsNullOrEmpty(speakerHeroId))
            {
                return SelfTellResult.Allowed;
            }

            // 編的話的參與者沒做過那件事，不會用「我」把它講出去；他們聽到之後怎麼回應另外處理
            if (MadeUpTalk.IsHearsayOnly(evt))
            {
                string? participantRole = evt.RoleOf(speakerHeroId!);
                if (participantRole != null)
                {
                    return SelfTellResult.Never(participantRole);
                }
            }

            // 秘密類事件不套「不講」：秘密外流就是靠知情的當事人說溜嘴
            if (evt.Origin == EventOrigin.Secret)
            {
                return SelfTellResult.Allowed;
            }

            string? role = evt.RoleOf(speakerHeroId!);
            if (role == null)
            {
                return SelfTellResult.Allowed;
            }

            if (getTemplate == null)
            {
                return SelfTellResult.Allowed;
            }

            var template = getTemplate(evt.Type);
            if (template?.SelfTell == null || !template.SelfTell.TryGetValue(role, out var rule) || rule == null)
            {
                return SelfTellResult.Allowed;
            }

            if (rule.IsNever)
            {
                return SelfTellResult.Never(role);
            }

            string traitName = rule.Trait ?? string.Empty;
            var profile = profileOf();
            int traitVal = profile != null ? GetTraitValue(profile, traitName) : 0;

            if (traitVal >= rule.Min)
            {
                return SelfTellResult.Allowed;
            }

            return SelfTellResult.TraitTooLow(role, traitName, traitVal, rule.Min);
        }

        private static int GetTraitValue(TraitProfile profile, string trait)
        {
            return trait.ToLowerInvariant() switch
            {
                "honor" => profile.Honor,
                "mercy" => profile.Mercy,
                "valor" => profile.Valor,
                "calculating" => profile.Calculating,
                "generosity" => profile.Generosity,
                _ => 0
            };
        }
    }
}

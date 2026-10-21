using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Localization;
using VividWorld.Core.Situations;
using VividWorld.Presentation;

namespace VividWorld.Campaign
{
    /// <summary>
    /// 玩家入局視窗上的字：場面、做法、代價方向。這裡只放英文後備，中文由字串表提供。
    /// 角色名一律以大寫角色名當變數（<c>{FAVORED}</c>、<c>{AGGRIEVED}</c>），值是綁到的那位英雄的名字。
    /// </summary>
    internal static class PlayerPresenceText
    {
        private static readonly Dictionary<string, string> SceneFallbacks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seat_dispute_Bystander"] = "At the feast, {SLIGHTED} has been seated below {FAVORED}. {FAVORED} is only one member of a great house, while {SLIGHTED} is the head of a house and by rights should sit higher. Around the table, people keep glancing at {SLIGHTED}.",
            ["seat_dispute_Decider"] = "At the feast, you have been seated below {FAVORED}. {FAVORED} is only one member of a great house, while you are the head of yours and by rights should sit higher. The table falls quiet, waiting for you.",
            ["advice_sought_Bystander"] = "{STUDENT} comes over to {VETERAN}, cup in hand, and asks politely how best to lead men in the field.",
            ["advice_sought_Decider"] = "{STUDENT} comes over to you, cup in hand, and asks politely how best to lead men in the field. The words have clearly been rehearsed.",
            ["tavern_words_Bystander"] = "In the tavern, {SPEAKER} and {LISTENER} share a table. A few cups in, the talk grows looser.",
            ["tavern_words_Decider"] = "In the tavern, you share a table with {LISTENER}. A few cups in, someone starts making fun of {LISTENER}, and the whole table turns to see what you will say.",
            ["market_brawl_Bystander"] = "In the market, men of {PATRON} and men of {AGGRIEVED} have come to blows. It takes some doing to pull the two sides apart, and {AGGRIEVED} goes straight to {PATRON} for an answer.",
            ["market_brawl_Decider"] = "In the market, some of your men have come to blows with men of {AGGRIEVED}. It takes some doing to pull the two sides apart, and {AGGRIEVED} comes straight to you demanding an answer.",
            ["victory_credit_Bystander"] = "In the hall, well into the evening, someone raises a cup to the last victory. {CLAIMANT} and {RIVAL} exchange a look: both of them fought in that battle.",
            ["hunt_wager_Bystander"] = "In the hall, {CHALLENGER} slaps the table and challenges {CHALLENGED} to a wager on who brings back more game from the next hunt.",
            ["spoke_against_ruler_Listener"] = "{SPEAKER} draws you aside and, voice lowered, starts talking about {RULER}."
        };

        private static readonly Dictionary<string, string> OptionFallbacks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seat_dispute_demand_seat"] = "Tell {FAVORED}, in front of everyone, to give up the seat",
            ["seat_dispute_endure"] = "Say nothing and swallow it",
            ["seat_dispute_yield"] = "Let {FAVORED} keep the seat and raise a cup to {FAVORED:him|her|them}",
            ["seat_dispute_walk_out"] = "Get up and walk out of {HOST}'s feast",
            ["advice_sought_teach_freely"] = "Tell {STUDENT} everything, your own mistakes included",
            ["advice_sought_brush_off"] = "Offer a few things everyone knows and change the subject",
            ["advice_sought_mock"] = "Make a joke of the question in front of everyone",
            ["tavern_words_good_word"] = "Stand up for {LISTENER}",
            ["tavern_words_boast"] = "Turn the talk to your own deeds in battle",
            ["tavern_words_sour_words"] = "Join in with a cutting remark about {LISTENER}",
            ["market_brawl_hand_over"] = "Hand over the men who started it and apologize to {AGGRIEVED}",
            ["market_brawl_shield_own"] = "Your men, your business: hand no one over",
            ["market_brawl_counter_accuse"] = "Say it was the men of {AGGRIEVED} who struck first",
            ["market_brawl_hush_it_up"] = "Agree with {AGGRIEVED} that neither house will speak of it again"
        };

        private static readonly Dictionary<string, string> OutcomeFallbacks = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["seat_dispute_demand_seat"] = "{SLIGHTED} sets down the cup and, in front of everyone, tells {FAVORED} to give up the higher seat. The whole hall falls silent.",
            ["seat_dispute_yield"] = "{SLIGHTED} lets it pass, gives the higher seat to {FAVORED}, and raises a cup to {FAVORED:him|her|them} as well.",
            ["seat_dispute_walk_out"] = "{SLIGHTED} gets up without a word and walks out of {HOST}'s hall.",
            ["advice_sought_teach_freely"] = "{VETERAN} tells the whole story of how that battle was won, holding nothing back, not even the mistakes.",
            ["advice_sought_brush_off"] = "{VETERAN} offers a few things everyone already knows and, before the cup is empty, changes the subject.",
            ["advice_sought_mock"] = "{VETERAN} makes a joke of the question in front of the whole hall, saying some people should first learn to sit still. As {STUDENT} leaves the hall, people are still laughing.",
            ["tavern_words_good_word"] = "Someone at the table makes fun of {LISTENER}, and {SPEAKER} speaks up for {LISTENER}, adding that it is not the wine talking.",
            ["tavern_words_boast"] = "{SPEAKER} starts telling of past battles, and the tale grows taller with every cup; the table only smiles.",
            ["tavern_words_sour_words"] = "With the wine loosening the tongue, {SPEAKER} says something to {LISTENER}, and {LISTENER}'s face changes at once.",
            ["market_brawl_hand_over"] = "In front of the whole street, {PATRON} hands the man who struck first over to {AGGRIEVED}, and apologizes as well.",
            ["market_brawl_shield_own"] = "{PATRON} refuses to hand anyone over: a house deals with its own men, and it is no one else's business.",
            ["market_brawl_counter_accuse"] = "{PATRON} not only refuses to hand anyone over, but says before everyone that the men of {AGGRIEVED} struck first, and that {AGGRIEVED} owes an answer. The two houses stand there, neither giving way.",
            ["victory_credit_claim_aloud"] = "{CLAIMANT} is first on {CLAIMANT:his|her|their} feet, claims the credit for that battle, and says {RIVAL} only arrived once the enemy line had already broken.",
            ["victory_credit_defer"] = "The toast was meant for {CLAIMANT}, but {CLAIMANT} gives the credit to {RIVAL}.",
            ["victory_credit_let_the_host_judge"] = "Neither will give way, so in the end they ask {HOST} to decide. {HOST} gives the credit to {RIVAL}.",
            ["hunt_wager_take_the_wager"] = "{CHALLENGED} accepts on the spot, and half the hall puts money on it too.",
            ["hunt_wager_refuse_and_jest"] = "{CHALLENGED} turns it down and makes a joke of it; the hall laughs for a good while."
        };

        private static readonly Dictionary<string, string> TraitNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["honor"] = "Honor",
            ["mercy"] = "Mercy",
            ["valor"] = "Valor",
            ["generosity"] = "Generosity",
            ["calculating"] = "Calculating"
        };

        internal static string RoleToken(PlayerPresenceRole role) => role.ToString();

        private static string TextWithEnglishFallback(string key, string programmaticFallback)
        {
            string en = EnglishStringTableStore.Instance.Get(key) ?? programmaticFallback;
            return new TextObject("{=" + key + "}" + en).ToString();
        }

        internal static string Title(string? settlementId)
        {
            const string key = "VividWorld_Presence_Title";
            string en = EnglishStringTableStore.Instance.Get(key) ?? "In {SETTLEMENT}";
            var t = new TextObject("{=" + key + "}" + en);
            t.SetTextVariable("SETTLEMENT", FallbackTextRenderer.ResolveVar("settlement:" + (settlementId ?? string.Empty), false));
            return t.ToString();
        }

        internal static string Scene(string situationId, PlayerPresenceRole role, IReadOnlyDictionary<string, string?> boundHeroIds)
        {
            string keySuffix = $"{situationId}_{RoleToken(role)}";
            string fullKey = "VividWorld_Presence_Scene_" + keySuffix;
            SceneFallbacks.TryGetValue(keySuffix, out var fallback);
            return FallbackTextRenderer.RenderRoleTemplate(fullKey, fallback ?? keySuffix, boundHeroIds);
        }

        internal static string Option(string situationId, string branchId, IReadOnlyDictionary<string, string?> boundHeroIds)
        {
            string keySuffix = $"{situationId}_{branchId}";
            string fullKey = "VividWorld_Presence_Option_" + keySuffix;
            OptionFallbacks.TryGetValue(keySuffix, out var fallback);
            return FallbackTextRenderer.RenderRoleTemplate(fullKey, fallback ?? branchId, boundHeroIds);
        }

        internal static string? Outcome(string situationId, string branchId, IReadOnlyDictionary<string, string?> boundHeroIds)
        {
            string keySuffix = $"{situationId}_{branchId}";
            if (!OutcomeFallbacks.TryGetValue(keySuffix, out var fallback))
            {
                return null;
            }

            string fullKey = "VividWorld_Presence_Outcome_" + keySuffix;
            return FallbackTextRenderer.RenderRoleTemplate(fullKey, fallback, boundHeroIds);
        }

        internal static string Ack() => TextWithEnglishFallback("VividWorld_Presence_Ack", "I see.");

        internal static string Confirm() => TextWithEnglishFallback("VividWorld_Presence_Confirm", "Done");

        /// <summary>代價方向組成一句：「卡拉多格不滿、聲望↑、影響力↓、勇猛↑」。</summary>
        internal static string Directions(IReadOnlyList<PlayerChoiceDirection> directions)
        {
            if (directions == null || directions.Count == 0)
            {
                return TextWithEnglishFallback("VividWorld_Presence_Dir_None", "No lasting effect");
            }

            string separator = TextWithEnglishFallback("VividWorld_Presence_Dir_Separator", ", ");
            return string.Join(separator, directions.Select(Direction));
        }

        private static string Direction(PlayerChoiceDirection d)
        {
            string upDown = d.Up ? "Up" : "Down";
            string arrow = d.Up ? "↑" : "↓";
            switch (d.Kind)
            {
                case PlayerChoiceDirectionKind.Affection:
                {
                    string key = "VividWorld_Presence_Dir_Affection_" + upDown;
                    string programmatic = d.Up ? "Relation with {NAME} ↑" : "Relation with {NAME} ↓";
                    string en = EnglishStringTableStore.Instance.Get(key) ?? programmatic;
                    var t = new TextObject("{=" + key + "}" + en);
                    t.SetTextVariable("NAME", FallbackTextRenderer.ResolveVar("hero:" + (d.HeroId ?? string.Empty), false));
                    return t.ToString();
                }
                case PlayerChoiceDirectionKind.Renown:
                    return TextWithEnglishFallback("VividWorld_Presence_Dir_Renown_" + upDown, "Renown " + arrow);
                case PlayerChoiceDirectionKind.Influence:
                    return TextWithEnglishFallback("VividWorld_Presence_Dir_Influence_" + upDown, "Influence " + arrow);
                case PlayerChoiceDirectionKind.Morale:
                    return TextWithEnglishFallback("VividWorld_Presence_Dir_Morale_" + upDown, "Morale " + arrow);
                case PlayerChoiceDirectionKind.Trait:
                {
                    string trait = d.Trait ?? string.Empty;
                    TraitNames.TryGetValue(trait, out var name);
                    string keyTrait = name ?? trait;
                    return TextWithEnglishFallback("VividWorld_Presence_Dir_" + keyTrait + "_" + upDown, keyTrait + " " + arrow);
                }
                default:
                    return d.ToString();
            }
        }
    }
}

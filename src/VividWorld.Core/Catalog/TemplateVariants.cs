#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using VividWorld.Core.Events;

namespace VividWorld.Core.Catalog
{
    /// <summary>逃脫的三套碎片：看守的人照記、不知道誰關著他、關在城鎮或城堡的牢裡。</summary>
    public enum EscapeShape
    {
        WithCaptor,
        WithoutCaptor,
        FromDungeon
    }

    /// <summary>一次逃脫用哪一套碎片，以及一行說明為什麼的日誌。</summary>
    public sealed class EscapeShapeChoice
    {
        public EscapeShape Shape { get; }
        public string Log { get; }

        public EscapeShapeChoice(EscapeShape shape, string log)
        {
            Shape = shape;
            Log = log;
        }
    }

    /// <summary>
    /// 三種消息會依當下的情況換一組碎片：逃脫時不知道誰關著他或關在城裡的牢、獲救時沒有救人的人、被領主釋放依原因換。
    /// 模組送出事件時與開發者工具列句型時都走這裡，兩邊不會各寫一份而漸漸對不上。
    /// 每個變體只換碎片與角色，其餘（來源、戲劇性、同地者算聽來的、當事人不講的角色）照原模板。
    /// </summary>
    public static class TemplateVariants
    {
        private const string FactPrefix = "VividWorld_Fact_";

        public static EventTemplate EscapeWithoutCaptor(EventTemplate baseTemplate)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var facts = baseTemplate.Facts.Select(f =>
                string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase)
                    ? MakeFact(f, "VividWorld_Fact_HeroEscaped_WhoNoCaptor", "{PRISONER} slipped out of captivity",
                        new Dictionary<string, string> { ["PRISONER"] = "hero:{PRISONER}" })
                    : CopyFact(f)).ToList();

            return Derive(baseTemplate, PrisonerOnlyRoles(), facts, PrisonerOnlySelfTell(baseTemplate));
        }

        /// <summary>
        /// 人關在城鎮或城堡的牢裡而逃脫：押著他的是聚落，不是哪一位領主的隊伍，
        /// 所以這則消息沒有「看守的人」這個當事人（城主多半人在外面，不會把它當成「從我手中逃掉」來講）。
        /// 城名併進「從〈城〉的牢裡」，一定有地點；傳到記不得地點時，剩下的就是「不知道誰關著他」那一句。
        /// </summary>
        public static EventTemplate EscapeFromDungeon(EventTemplate baseTemplate)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var facts = baseTemplate.Facts.Select(f =>
            {
                if (string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase))
                {
                    return MakeFact(f, "VividWorld_Fact_HeroEscaped_WhoNoCaptor", "{PRISONER} slipped out of captivity",
                        new Dictionary<string, string> { ["PRISONER"] = "hero:{PRISONER}" });
                }
                if (string.Equals(f.Id, "where", StringComparison.OrdinalIgnoreCase))
                {
                    var where = MakeFact(f, "VividWorld_Fact_HeroEscaped_WhereDungeon", "from the dungeon at {SETTLEMENT}",
                        new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:{SETTLEMENT}" });
                    where.Optional = false;
                    return where;
                }
                return CopyFact(f);
            }).ToList();

            return Derive(baseTemplate, PrisonerOnlyRoles(), facts, PrisonerOnlySelfTell(baseTemplate));
        }

        /// <summary>
        /// 逃脫當下押著俘虜的是哪一種，決定用哪一套碎片。純函式，模組端把遊戲狀態讀成三個值傳進來。
        /// 押著他的是聚落 ⇒ 不記看守的人；聚落的代號讀得到就用「從〈城〉的牢裡」那一套，讀不到就用「不知道誰關著他」那一套。
        /// 押著他的是隊伍 ⇒ 隊伍有隊長或主人就記成看守的人，沒有就用「不知道誰關著他」那一套。
        /// </summary>
        public static EscapeShapeChoice ChooseEscapeShape(bool heldBySettlement, string? heldSettlementId, bool captorKnown)
        {
            if (heldBySettlement)
            {
                if (!string.IsNullOrWhiteSpace(heldSettlementId))
                {
                    return new EscapeShapeChoice(EscapeShape.FromDungeon,
                        $"escape template: dungeon (held by settlement {heldSettlementId}, not by a lord's party => no captor role, " +
                        $"the settlement's owner is not a participant; place bound to {heldSettlementId})");
                }
                return new EscapeShapeChoice(EscapeShape.WithoutCaptor,
                    "escape template: no captor (held by a settlement whose id could not be read => no captor role, " +
                    "the settlement's owner is not a participant; place falls back to where the prisoner is)");
            }

            if (!captorKnown)
            {
                return new EscapeShapeChoice(EscapeShape.WithoutCaptor,
                    "escape template: no captor (held by a party with no leader or owner => no captor role)");
            }

            return new EscapeShapeChoice(EscapeShape.WithCaptor,
                "escape template: captor (held by a lord's party, not by a settlement => its leader or owner is the captor)");
        }

        /// <summary>依 <see cref="ChooseEscapeShape"/> 的結果取碎片；看守的人照記時回傳 null（用原模板）。</summary>
        public static EventTemplate? EscapeFor(EventTemplate baseTemplate, EscapeShape shape)
        {
            switch (shape)
            {
                case EscapeShape.FromDungeon: return EscapeFromDungeon(baseTemplate);
                case EscapeShape.WithoutCaptor: return EscapeWithoutCaptor(baseTemplate);
                default: return null;
            }
        }

        public static EventTemplate RescueWithoutRescuer(EventTemplate baseTemplate)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var facts = baseTemplate.Facts.Select(f =>
            {
                if (string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase))
                {
                    return MakeFact(f, "VividWorld_Fact_HeroRescuedFromBandits_WhoNoRescuer",
                        "a band of {BANDITS} was routed, and {PRISONER} got away in the confusion",
                        new Dictionary<string, string>
                        {
                            ["BANDITS"] = "{BANDITS}",
                            ["PRISONER"] = "hero:{PRISONER}"
                        });
                }
                if (string.Equals(f.Id, "outcome", StringComparison.OrdinalIgnoreCase))
                {
                    return MakeFact(f, "VividWorld_Fact_HeroRescuedFromBandits_OutcomeNoRescuer",
                        "and later made it back to {PRISONER.his} own people safely",
                        new Dictionary<string, string>());
                }
                return CopyFact(f);
            }).ToList();

            return Derive(baseTemplate, PrisonerOnlyRoles(), facts, PrisonerOnlySelfTell(baseTemplate));
        }

        /// <summary>
        /// 依放人的原因組出「被領主釋放」的碎片。
        /// 抓人的人查不到時（<paramref name="captorKnown"/> 為 false）改用「不知道誰關著他」的講法且不分原因；
        /// 例外是「關人的城鎮城堡換了主人」：遊戲給的抓人者是城主，分不出新舊主人，所以一律不提抓人的人，原因照講。
        /// </summary>
        public static EventTemplate Released(EventTemplate baseTemplate, string? reason, bool captorKnown)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            bool ownerChanged = string.Equals(reason, ReleaseReasons.SettlementOwnerChanged, StringComparison.Ordinal);
            bool noCaptor = !captorKnown || ownerChanged;
            string? reasonSegment = ReleaseReasons.SegmentOf(reason);
            bool includeReason = reasonSegment != null && (captorKnown || ownerChanged);

            var facts = new List<TemplateFact>
            {
                WhoFact(reason, noCaptor),
                WhereFact(baseTemplate, optional: !ownerChanged)
            };
            if (includeReason)
            {
                facts.Add(ReasonFact(reasonSegment!));
            }

            var roles = noCaptor
                ? PrisonerOnlyRoles()
                : new Dictionary<string, string>(StringComparer.Ordinal) { ["captor"] = "{CAPTOR}", ["prisoner"] = "{PRISONER}" };

            Dictionary<string, SelfTellRule>? selfTell = null;
            if (!noCaptor && string.Equals(reason, ReleaseReasons.PlayerChoice, StringComparison.Ordinal))
            {
                // 你本人放人：玩家不是講述者，沒有「放人的人自己講」
                selfTell = new Dictionary<string, SelfTellRule>(StringComparer.Ordinal) { ["captor"] = SelfTellRule.Never };
            }

            return Derive(baseTemplate, roles, facts, selfTell);
        }

        /// <summary>
        /// 舊戰役裡已經記下、沒記原因的放人消息的碎片形狀（誰、地點、經過、結果四塊）。
        /// 只給開發者工具列舉用：舊事件的碎片照存檔原樣讀回，不會由這裡產生。
        /// </summary>
        public static EventTemplate ReleasedLegacy(EventTemplate baseTemplate, bool captorKnown)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var facts = new List<TemplateFact>
            {
                WhoFact(null, !captorKnown),
                WhereFact(baseTemplate, optional: true),
                new TemplateFact
                {
                    Id = "what",
                    Category = FactCategory.What,
                    TextId = FactPrefix + "HeroReleased_What",
                    Text = "the terms were settled and the guards stood aside",
                    Fragility = 3
                },
                new TemplateFact
                {
                    Id = "outcome",
                    Category = FactCategory.Outcome,
                    TextId = FactPrefix + "HeroReleased_Outcome",
                    Text = "and so {PRISONER.he} walked free",
                    Fragility = 4
                }
            };

            var roles = captorKnown
                ? new Dictionary<string, string>(StringComparer.Ordinal) { ["captor"] = "{CAPTOR}", ["prisoner"] = "{PRISONER}" }
                : PrisonerOnlyRoles();
            return Derive(baseTemplate, roles, facts, null);
        }

        public static EventTemplate MadeUpSpokeAgainstRuler(EventTemplate baseTemplate)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var roles = baseTemplate.Roles
                .Where(r => !string.Equals(r.Key, "listener", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);

            var facts = baseTemplate.Facts
                .Where(f => !string.Equals(f.Id, "context", StringComparison.OrdinalIgnoreCase))
                .Select(CopyFact)
                .ToList();

            return DeriveMadeUp(baseTemplate, roles, facts);
        }

        public static EventTemplate MadeUpPoisonedOldAge(EventTemplate baseTemplate)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var roles = new Dictionary<string, string>(baseTemplate.Roles, StringComparer.Ordinal);
            var facts = baseTemplate.Facts.Select(CopyFact).ToList();

            return DeriveMadeUp(baseTemplate, roles, facts, linkedTemplateType: "hero_died_of_old_age");
        }

        public static EventTemplate MadeUpPoisonedAnyDeath(EventTemplate baseTemplate)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var roles = new Dictionary<string, string>(baseTemplate.Roles, StringComparer.Ordinal);
            var facts = baseTemplate.Facts.Select(f =>
            {
                if (string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase))
                {
                    return MakeFact(f, "VividWorld_Fact_ConductPoisoned_WhoAnyDeath", "{VICTIM} was poisoned by {POISONER}",
                        f.Vars != null ? new Dictionary<string, string>(f.Vars) : new Dictionary<string, string>());
                }
                return CopyFact(f);
            }).ToList();

            return DeriveMadeUp(baseTemplate, roles, facts, linkedTemplateType: "hero_died_naturally");
        }

        public static EventTemplate MadeUpCommon(EventTemplate baseTemplate)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var roles = new Dictionary<string, string>(baseTemplate.Roles, StringComparer.Ordinal);
            var facts = baseTemplate.Facts.Select(CopyFact).ToList();

            return DeriveMadeUp(baseTemplate, roles, facts);
        }

        public static EventTemplate GetMadeUpVariant(EventTemplate baseTemplate, string? linkedType = null)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));
            if (string.Equals(baseTemplate.Type, "conduct_spoke_against_ruler", StringComparison.OrdinalIgnoreCase))
            {
                return MadeUpSpokeAgainstRuler(baseTemplate);
            }
            if (string.Equals(baseTemplate.Type, "conduct_poisoned", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(linkedType, "hero_died_naturally", StringComparison.OrdinalIgnoreCase))
                {
                    return MadeUpPoisonedAnyDeath(baseTemplate);
                }
                return MadeUpPoisonedOldAge(baseTemplate);
            }
            return MadeUpCommon(baseTemplate);
        }

        /// <summary>開發者工具要列出的所有碎片形狀（模板本身加上它的變體）。</summary>
        public static IReadOnlyList<(string Label, EventTemplate Template)> AllShapes(EventTemplate template)
        {
            var list = new List<(string, EventTemplate)>();
            if (template == null) return list;

            switch (template.Type)
            {
                case "hero_escaped_captivity":
                    list.Add((template.Type, template));
                    list.Add(("hero_escaped_captivity (WhoNoCaptor)", EscapeWithoutCaptor(template)));
                    list.Add(("hero_escaped_captivity (WhereDungeon)", EscapeFromDungeon(template)));
                    break;
                case "hero_rescued_from_bandits":
                    list.Add((template.Type, template));
                    list.Add(("hero_rescued_from_bandits (WhoNoRescuer)", RescueWithoutRescuer(template)));
                    break;
                case "hero_released":
                    foreach (var reason in new[]
                    {
                        ReleaseReasons.Ransom, ReleaseReasons.Peace, ReleaseReasons.Defeated,
                        ReleaseReasons.SettlementOwnerChanged, ReleaseReasons.PlayerChoice, ReleaseReasons.Disbanded
                    })
                    {
                        list.Add(($"hero_released ({reason})", Released(template, reason, captorKnown: true)));
                    }
                    list.Add(("hero_released (no captor, any reason)", Released(template, ReleaseReasons.Ransom, captorKnown: false)));
                    list.Add(("hero_released (old event, no reason)", ReleasedLegacy(template, captorKnown: true)));
                    list.Add(("hero_released (old event, no reason, no captor)", ReleasedLegacy(template, captorKnown: false)));
                    break;
                case "conduct_spoke_against_ruler":
                    list.Add((template.Type, template));
                    list.Add(("conduct_spoke_against_ruler (made up)", MadeUpSpokeAgainstRuler(template)));
                    break;
                case "conduct_poisoned":
                    list.Add((template.Type, template));
                    list.Add(("conduct_poisoned (made up)", MadeUpPoisonedOldAge(template)));
                    list.Add(("conduct_poisoned (made up, any death)", MadeUpPoisonedAnyDeath(template)));
                    break;
                case "conduct_refused_aid":
                case "conduct_rash_capture":
                case "conduct_mistreated_prisoner":
                case "victory_credit_deferred":
                case "advice_given_freely":
                case "brawl_man_handed_over":
                case "seat_dispute_yielded":
                case "tavern_good_word":
                    list.Add((template.Type, template));
                    list.Add(($"{template.Type} (made up)", MadeUpCommon(template)));
                    break;
                case "talk_denied_spoke_against_ruler":
                    list.Add((template.Type, template));
                    list.Add(("talk_denied_spoke_against_ruler (named)", DeniedNamed(template, "TalkDeniedSpokeAgainstRuler")));
                    break;
                case "talk_denied_mistreated_prisoner":
                    list.Add((template.Type, template));
                    list.Add(("talk_denied_mistreated_prisoner (named)", DeniedNamed(template, "TalkDeniedMistreatedPrisoner")));
                    break;
                case "talk_denied_refused_aid":
                    list.Add((template.Type, template));
                    list.Add(("talk_denied_refused_aid (named)", DeniedNamed(template, "TalkDeniedRefusedAid")));
                    break;
                case "talk_denied_rash_capture":
                    list.Add((template.Type, template));
                    list.Add(("talk_denied_rash_capture (named)", DeniedNamed(template, "TalkDeniedRashCapture")));
                    break;
                case "talk_denied_poisoned":
                    list.Add((template.Type, template));
                    list.Add(("talk_denied_poisoned (named)", DeniedNamed(template, "TalkDeniedPoisoned")));
                    break;
                default:
                    list.Add((template.Type, template));
                    break;
            }
            return list;
        }

        private static TemplateFact WhoFact(string? reason, bool noCaptor)
        {
            if (noCaptor)
            {
                return new TemplateFact
                {
                    Id = "who",
                    Category = FactCategory.Who,
                    TextId = FactPrefix + "HeroReleased_WhoNoCaptor",
                    Text = "{PRISONER} got out of captivity",
                    Vars = new Dictionary<string, string> { ["PRISONER"] = "hero:{PRISONER}" },
                    Fragility = 1
                };
            }

            string textId;
            string text;
            switch (reason)
            {
                case ReleaseReasons.Defeated:
                    textId = "HeroReleased_WhoDefeated";
                    text = "{PRISONER} was released from {CAPTOR}'s hands";
                    break;
                case ReleaseReasons.PlayerChoice:
                    textId = "HeroReleased_WhoPlayerChoice";
                    text = "{CAPTOR} let {PRISONER} go free";
                    break;
                case ReleaseReasons.Disbanded:
                    textId = "HeroReleased_WhoDisbanded";
                    text = "{CAPTOR}'s party disbanded and {PRISONER}, held with it, was released";
                    break;
                default:
                    textId = "HeroReleased_Who";
                    text = "{CAPTOR} set {PRISONER} free";
                    break;
            }

            return new TemplateFact
            {
                Id = "who",
                Category = FactCategory.Who,
                TextId = FactPrefix + textId,
                Text = text,
                Vars = new Dictionary<string, string>
                {
                    ["CAPTOR"] = "hero:{CAPTOR}",
                    ["PRISONER"] = "hero:{PRISONER}"
                },
                Fragility = 1
            };
        }

        private static TemplateFact WhereFact(EventTemplate baseTemplate, bool optional)
        {
            var where = baseTemplate.Facts.FirstOrDefault(f => string.Equals(f.Id, "where", StringComparison.OrdinalIgnoreCase));
            if (where == null)
            {
                return new TemplateFact
                {
                    Id = "where",
                    Category = FactCategory.Where,
                    TextId = FactPrefix + "HeroReleased_Where",
                    Text = "near {SETTLEMENT}",
                    Vars = new Dictionary<string, string> { ["SETTLEMENT"] = "settlement:{SETTLEMENT}" },
                    Fragility = 2,
                    Optional = optional
                };
            }

            var copy = CopyFact(where);
            copy.Optional = optional;
            return copy;
        }

        private static TemplateFact ReasonFact(string segment)
        {
            string text;
            var vars = new Dictionary<string, string>();
            switch (segment)
            {
                case "Peace":
                    text = "after the two sides stopped fighting";
                    break;
                case "Defeated":
                    text = "after {CAPTOR}'s party was defeated in battle";
                    vars["CAPTOR"] = "hero:{CAPTOR}";
                    break;
                case "SettlementOwnerChanged":
                    text = "after {SETTLEMENT} changed hands";
                    vars["SETTLEMENT"] = "settlement:{SETTLEMENT}";
                    break;
                case "PlayerChoice":
                    text = "the man {CAPTOR} had defeated, released without a ransom";
                    vars["CAPTOR"] = "hero:{CAPTOR}";
                    break;
                case "Disbanded":
                    text = "after {CAPTOR}'s party was disbanded";
                    vars["CAPTOR"] = "hero:{CAPTOR}";
                    break;
                default:
                    text = "after ransom was paid";
                    break;
            }

            return new TemplateFact
            {
                Id = "reason",
                Category = FactCategory.Why,
                TextId = FactPrefix + "HeroReleased_" + segment,
                Text = text,
                Vars = vars,
                Fragility = 2
            };
        }

        private static Dictionary<string, string> PrisonerOnlyRoles()
            => new Dictionary<string, string>(StringComparer.Ordinal) { ["prisoner"] = "{PRISONER}" };

        private static Dictionary<string, SelfTellRule>? PrisonerOnlySelfTell(EventTemplate baseTemplate)
        {
            if (baseTemplate.SelfTell == null) return null;
            var copy = baseTemplate.SelfTell
                .Where(kv => string.Equals(kv.Key, "prisoner", StringComparison.Ordinal))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            return copy.Count > 0 ? copy : null;
        }

        public static EventTemplate DeniedNamed(EventTemplate baseTemplate, string stem)
        {
            if (baseTemplate == null) throw new ArgumentNullException(nameof(baseTemplate));

            var roles = new Dictionary<string, string>(baseTemplate.Roles, StringComparer.Ordinal);
            roles["originator"] = "{ORIGINATOR}";

            var selfTell = baseTemplate.SelfTell != null
                ? new Dictionary<string, SelfTellRule>(baseTemplate.SelfTell, StringComparer.Ordinal)
                : new Dictionary<string, SelfTellRule>(StringComparer.Ordinal);
            selfTell["originator"] = SelfTellRule.Never;

            var facts = baseTemplate.Facts.Select(f =>
            {
                if (string.Equals(f.Id, "who", StringComparison.OrdinalIgnoreCase))
                {
                    var copy = CopyFact(f);
                    copy.TextId = FactPrefix + stem + "_WhoNamed";
                    // 整句句型只拿碎片帶的變數，點名的那個人要掛在這一塊，句子裡的名字才填得上
                    copy.Vars["ORIGINATOR"] = "hero:{ORIGINATOR}";
                    return copy;
                }
                return CopyFact(f);
            }).ToList();

            var derived = Derive(baseTemplate, roles, facts, selfTell);
            derived.Response = baseTemplate.Response;
            return derived;
        }

        private static EventTemplate Derive(
            EventTemplate baseTemplate,
            Dictionary<string, string> roles,
            List<TemplateFact> facts,
            Dictionary<string, SelfTellRule>? selfTell)
        {
            return new EventTemplate
            {
                Type = baseTemplate.Type,
                Origin = baseTemplate.Origin,
                DramaWeight = baseTemplate.DramaWeight,
                DramaScale = baseTemplate.DramaScale,
                LinkedTemplateType = baseTemplate.LinkedTemplateType,
                Roles = roles,
                KnowingRoles = new HashSet<string>(baseTemplate.KnowingRoles ?? new HashSet<string>()),
                Facts = facts,
                Headline = baseTemplate.Headline,
                SelfTell = selfTell,
                ColocatedWitnessAsHearsay = baseTemplate.ColocatedWitnessAsHearsay,
                Response = baseTemplate.Response,
                Feelings = baseTemplate.Feelings != null ? new Dictionary<string, string>(baseTemplate.Feelings, StringComparer.OrdinalIgnoreCase) : null,
                FeelingOverrides = baseTemplate.FeelingOverrides != null ? new List<FeelingOverride>(baseTemplate.FeelingOverrides) : null,
                SelfFeelingVariants = baseTemplate.SelfFeelingVariants
            };
        }

        private static EventTemplate DeriveMadeUp(
            EventTemplate baseTemplate,
            Dictionary<string, string> roles,
            List<TemplateFact> facts,
            string? linkedTemplateType = null)
        {
            var selfTell = new Dictionary<string, SelfTellRule>(StringComparer.Ordinal);
            foreach (var r in roles.Keys)
            {
                selfTell[r] = SelfTellRule.Never;
            }

            return new EventTemplate
            {
                Type = baseTemplate.Type,
                Origin = EventOrigin.Public,
                DramaWeight = baseTemplate.DramaWeight,
                DramaScale = baseTemplate.DramaScale,
                LinkedTemplateType = linkedTemplateType ?? baseTemplate.LinkedTemplateType,
                Roles = roles,
                KnowingRoles = new HashSet<string>(StringComparer.Ordinal),
                Facts = facts,
                Opinions = baseTemplate.Opinions != null ? new List<OpinionDef>(baseTemplate.Opinions) : null,
                Headline = baseTemplate.Headline,
                SelfTell = selfTell,
                Retired = baseTemplate.Retired,
                ColocatedWitnessAsHearsay = baseTemplate.ColocatedWitnessAsHearsay,
                WitnessSource = "none",
                MadeUpHearsay = true,
                Feelings = baseTemplate.Feelings != null ? new Dictionary<string, string>(baseTemplate.Feelings, StringComparer.OrdinalIgnoreCase) : null,
                FeelingOverrides = baseTemplate.FeelingOverrides != null ? new List<FeelingOverride>(baseTemplate.FeelingOverrides) : null,
                SelfFeelingVariants = baseTemplate.SelfFeelingVariants
            };
        }

        private static TemplateFact CopyFact(TemplateFact f)
        {
            return new TemplateFact
            {
                Id = f.Id,
                Category = f.Category,
                TextId = f.TextId,
                Text = f.Text,
                Vars = f.Vars != null ? new Dictionary<string, string>(f.Vars) : new Dictionary<string, string>(),
                Fragility = f.Fragility,
                RefersToTemplateType = f.RefersToTemplateType,
                Role = f.Role,
                Optional = f.Optional
            };
        }

        private static TemplateFact MakeFact(TemplateFact original, string textId, string text, Dictionary<string, string> vars)
        {
            return new TemplateFact
            {
                Id = original.Id,
                Category = original.Category,
                TextId = textId,
                Text = text,
                Vars = vars,
                Fragility = original.Fragility,
                Optional = original.Optional
            };
        }
    }
}

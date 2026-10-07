#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VividWorld.Core.Catalog;
using VividWorld.Core.Config;
using VividWorld.Core.Dialogue;
using VividWorld.Core.Events;
using VividWorld.Core.Feelings;
using VividWorld.Core.Memory;
using VividWorld.Core.Persistence;
using VividWorld.Core.Presentation;
using VividWorld.Core.Util;

namespace VividWorld.Core.Rumors
{
    public enum ProbeResultKind
    {
        AccusedDeny,
        AccusedPraise,
        AccusedRefuse,
        OriginatorInsist,
        OriginatorSlip,
        TruthFace,
        TruthTell,
        TwoVersions,
        Tell,
        NotHeard,
        RefuseToTell,
        AlreadyTold
    }

    /// <summary>打探的回答計畫：只描述「要怎麼答」，不改任何資料。</summary>
    public sealed class ProbePlan
    {
        public ProbeResultKind ResultKind { get; set; }
        public string ResultKindLabel { get; set; } = string.Empty;

        /// <summary>回答講的是哪一則事件（記進紀事時加在這一則）。</summary>
        public string? EventId { get; set; }

        public string? SentenceKey { get; set; }

        /// <summary>句子裡的代換值：變數名（大寫）→ 英雄代號。</summary>
        public Dictionary<string, string> Vars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string? AddressKey { get; set; }
        public string? AddressHeroId { get; set; }

        /// <summary>問到被說的人本人、而他還沒有那句話的知情記錄：回答之前要先讓他知道。</summary>
        public bool ShouldDeliverToAccused { get; set; }
        public string? DeliverEventId { get; set; }
        public int DeliverHop { get; set; }
        public List<string>? DeliverKnownFactIds { get; set; }
        public bool DeliverKnowsOriginator { get; set; }

        /// <summary>回答用的「知不知道是誰起頭的」（先讓他知道的話，取那一步之後的值）。</summary>
        public bool KnowsOriginator { get; set; }

        /// <summary>稱呼用的是中性預設（沒有人情世界可查）。</summary>
        public bool NeutralAddress { get; set; }

        public string LogReason { get; set; } = string.Empty;

        /// <summary>「講一則」的那一種（第 6、8 列）帶著要講的整份內容。</summary>
        public RumorOffer? Offer { get; set; }
    }

    /// <summary>
    /// 打探時對方怎麼答（純函式，不改任何資料）。判定順序由上而下，第一個成立的就是答案。
    /// </summary>
    public static class ProbeClassifier
    {
        // ───────────── 公開的小工具（對話、開發者工具、測試共用）─────────────

        /// <summary>E：玩家紀錄裡屬於這個區塊的每一筆（<c>RootEventId</c>，沒有就用自己的 <c>EventId</c>）。</summary>
        public static List<PlayerHeardEntry> CollectBlockEntries(string rootEventId, IEnumerable<PlayerHeardEntry>? entries)
        {
            var result = new List<PlayerHeardEntry>();
            if (string.IsNullOrEmpty(rootEventId) || entries == null) return result;
            foreach (var e in entries)
            {
                if (e == null) continue;
                string root = !string.IsNullOrEmpty(e.RootEventId) ? e.RootEventId! : e.EventId;
                if (string.Equals(root, rootEventId, StringComparison.Ordinal)) result.Add(e);
            }
            return result;
        }

        /// <summary>
        /// 他有沒有親口跟玩家講過這則事件：玩家紀錄裡那一筆的來源有他、而且不是打探時的回答。
        /// 有就回傳那一份來源（取最早的一份，穩定），沒有回 null。只看事件層，不比碎片。
        /// </summary>
        public static PlayerHeardSource? ToldByHim(string heroId, string eventId, IEnumerable<PlayerHeardEntry>? entries)
        {
            if (string.IsNullOrEmpty(heroId) || string.IsNullOrEmpty(eventId) || entries == null) return null;
            PlayerHeardSource? found = null;
            foreach (var entry in entries)
            {
                if (entry == null || !string.Equals(entry.EventId, eventId, StringComparison.Ordinal)) continue;
                foreach (var source in entry.EffectiveSources())
                {
                    if (source == null || source.HasProbeAnswer) continue;
                    if (!string.Equals(source.HeroId, heroId, StringComparison.Ordinal)) continue;
                    if (found == null || source.Day < found.Day) found = source;
                }
            }
            return found;
        }

        private static string DayText(double day)
            => day.ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>
        /// C：源頭加上索引裡 <c>LinkedEventId</c> 一層一層連回源頭的每一則（不管玩家聽過沒有）。
        /// 先從索引建「誰連到誰」的對照，再由源頭往外走；不載入任何事件。
        /// </summary>
        public static List<string> CollectChain(string rootEventId, IEnumerable<RumorIndexEntry>? index)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(rootEventId)) return result;

            var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            if (index != null)
            {
                foreach (var entry in index)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.EventId) || string.IsNullOrEmpty(entry.LinkedEventId)) continue;
                    if (!children.TryGetValue(entry.LinkedEventId!, out var list))
                    {
                        list = new List<string>();
                        children[entry.LinkedEventId!] = list;
                    }
                    list.Add(entry.EventId);
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal) { rootEventId };
            var queue = new Queue<string>();
            queue.Enqueue(rootEventId);
            result.Add(rootEventId);
            while (queue.Count > 0)
            {
                string cur = queue.Dequeue();
                if (!children.TryGetValue(cur, out var kids)) continue;
                foreach (var kid in kids)
                {
                    if (seen.Add(kid))
                    {
                        result.Add(kid);
                        queue.Enqueue(kid);
                    }
                }
            }
            return result;
        }

        /// <summary>起頭的人說溜嘴的機率（百分點，夾在 0～100）：基礎；理性 ≤ −1 加魯莽加成；理性 ≥ +1 加謀略加成。</summary>
        public static double SlipChancePercent(SlipConfig? slip, int calculating)
        {
            slip ??= new SlipConfig();
            double p = slip.BaseChance;
            if (calculating <= -1) p += slip.RashBonus;
            else if (calculating >= 1) p += slip.CalculatingBonus;
            return Math.Max(0.0, Math.Min(100.0, p));
        }

        /// <summary>說溜嘴的擲點（0 以上、100 以下）：同一個人對同一則永遠擲出同一個值，不存檔。</summary>
        public static double SlipRoll(long campaignSeed, string eventId, string heroId)
        {
            long s = RumorSeed.Of(campaignSeed, eventId, heroId, "probeSlip");
            long m = ((s % 10000L) + 10000L) % 10000L;
            return m / 100.0;
        }

        /// <summary>兩句擇一的輪替：同一天、同一區塊、同一個人固定同一句。回傳 0 或 1。</summary>
        public static int RotationIndex(long campaignSeed, string blockRootEventId, string heroId, double today)
        {
            long s = RumorSeed.Of(campaignSeed, blockRootEventId, heroId, "probeLine", (int)today);
            return (int)(((s % 2L) + 2L) % 2L);
        }

        /// <summary>
        /// 問到被說的人本人時，先讓他知道的那一筆知情記錄：手數＝玩家那筆的手數 + 1、來源＝玩家、
        /// 段落＝玩家手上的那幾段、知不知道是誰起頭的照玩家的版本。
        /// </summary>
        public static KnownByEntry BuildAccusedKnowledge(ProbePlan plan, string heroId, string playerHeroId, double today)
        {
            return new KnownByEntry
            {
                HeroId = heroId,
                Hop = plan.DeliverHop,
                SourceHeroId = playerHeroId,
                LearnedDay = today,
                KnownFactIds = plan.DeliverKnownFactIds != null ? new List<string>(plan.DeliverKnownFactIds) : new List<string>(),
                OriginatorKnownOverride = plan.DeliverKnowsOriginator
            };
        }

        // ───────────── 判定 ─────────────

        public static ProbePlan Classify(
            string heHeroId,
            string playerHeroId,
            ChronicleEntry? block,
            IReadOnlyList<PlayerHeardEntry> blockEntriesE,
            IReadOnlyList<WorldEvent> chainEventsC,
            double today,
            long campaignSeed,
            VividWorldConfig config,
            Func<string, EventTemplate?> getTemplate,
            Func<string, CandidateRejection> canTellPredicate,
            Func<string, RumorOffer> buildOfferFunc,
            IFeelingWorld? feelingWorld,
            IHeroTraitLookup? traitLookup)
        {
            var plan = new ProbePlan();
            string rootEventId = !string.IsNullOrEmpty(block?.EventId)
                ? block!.EventId
                : (chainEventsC.Count > 0 ? chainEventsC[0].EventId : string.Empty);

            // 前面各列沒成立的短語；每一種答案的日誌都帶著
            var notes = new List<string>();

            var cById = new Dictionary<string, WorldEvent>(StringComparer.Ordinal);
            foreach (var e in chainEventsC)
            {
                if (e != null && !string.IsNullOrEmpty(e.EventId)) cById[e.EventId] = e;
            }

            var eEvents = new List<(PlayerHeardEntry Heard, WorldEvent Evt)>();
            int eMissing = 0;
            foreach (var heard in blockEntriesE)
            {
                if (cById.TryGetValue(heard.EventId, out var evt)) eEvents.Add((heard, evt));
                else eMissing++;
            }

            IOrderedEnumerable<(PlayerHeardEntry Heard, WorldEvent Evt)> SortE(IEnumerable<(PlayerHeardEntry Heard, WorldEvent Evt)> seq)
                => seq.OrderByDescending(x => x.Heard.UpdatedDay).ThenBy(x => x.Evt.EventId, StringComparer.Ordinal);

            int Rotation() => RotationIndex(campaignSeed, rootEventId, heHeroId, today);
            string Pair(string a, string b) => Rotation() == 0 ? a : b;

            // 稱呼：沒有人情世界可查時用中性預設（地位相當、好感一般），並記下來
            void SetAddress(string? accusedHeroId)
            {
                if (string.IsNullOrEmpty(accusedHeroId)) return;
                plan.AddressHeroId = accusedHeroId;
                plan.AddressKey = FeelingResolver.ResolveAddressKey(feelingWorld, heHeroId, accusedHeroId!, config.Presentation?.Feelings);
                plan.NeutralAddress = feelingWorld == null;
            }

            bool OpinionSign(WorldEvent evt, int sign)
            {
                var opinions = getTemplate(evt.Type)?.Opinions;
                if (opinions == null || opinions.Count == 0) return false;
                return sign < 0 ? opinions[0].Amount < 0 : opinions[0].Amount > 0;
            }

            bool IsForgotten(WorldEvent evt, string heroId)
            {
                var entry = evt.EntryFor(heroId);
                return entry == null || Forgetting.IsForgotten(evt, entry, today, playerHeroId, config.Memory);
            }

            bool IsKnownAndRemembered(WorldEvent evt) => evt.IsKnownBy(heHeroId) && !IsForgotten(evt, heHeroId);

            ProbePlan Done(string reason)
            {
                var sb = new System.Text.StringBuilder(reason);
                if (plan.NeutralAddress) sb.Append("; no feeling world, neutral address used");
                if (notes.Count > 0) sb.Append("; earlier rows: ").Append(string.Join(", ", notes));
                plan.LogReason = sb.ToString();
                return plan;
            }

            // 讓被說的人先知道：知情記錄照玩家的版本。回傳「回答用的知不知道是誰起頭的」。
            bool PrepareAccused(PlayerHeardEntry heard, WorldEvent xEvt)
            {
                var existing = xEvt.EntryFor(heHeroId);
                if (existing != null)
                {
                    plan.ShouldDeliverToAccused = false;
                    return MadeUpTalk.KnowsOriginator(xEvt, existing);
                }

                bool knows = MadeUpTalk.KnowsOriginator(xEvt, playerHeroId, heard.PlayerHop);
                var playerEntry = xEvt.EntryFor(playerHeroId);
                plan.ShouldDeliverToAccused = true;
                plan.DeliverEventId = xEvt.EventId;
                plan.DeliverHop = heard.PlayerHop + 1;
                plan.DeliverKnownFactIds = playerEntry?.KnownFactIds != null && playerEntry.KnownFactIds.Count > 0
                    ? new List<string>(playerEntry.KnownFactIds)
                    : (heard.Facts ?? new List<Fact>()).Select(f => f.Id).ToList();
                plan.DeliverKnowsOriginator = knows;
                return knows;
            }

            // 1. 被說的人：編的話、壞話
            var accusedBad = SortE(eEvents.Where(x =>
                MadeUpTalk.IsHearsayOnly(x.Evt) &&
                string.Equals(MadeUpTalk.GetAccusedHeroId(x.Evt), heHeroId, StringComparison.Ordinal) &&
                OpinionSign(x.Evt, -1))).ToList();
            if (accusedBad.Count > 0)
            {
                var (heard, xEvt) = accusedBad[0];
                bool knows = PrepareAccused(heard, xEvt);
                plan.KnowsOriginator = knows;
                plan.ResultKind = ProbeResultKind.AccusedDeny;
                plan.ResultKindLabel = "accused-deny";
                plan.EventId = xEvt.EventId;
                bool named = knows && !string.IsNullOrEmpty(xEvt.OriginatorHeroId);
                plan.SentenceKey = named
                    ? "VividWorld_Probe_Accused_DenyNamed"
                    : "VividWorld_Probe_Accused_DenyUnnamed";
                if (named) plan.Vars["ORIGINATOR"] = xEvt.OriginatorHeroId!;
                return Done($"accused of {xEvt.EventId} (bad talk), knowsOriginator={knows}, {DeliveryText(plan)}");
            }
            notes.Add("1 accused-deny: no bad made-up talk about him in E");

            // 2. 被說的人：編的話、好話
            var accusedGood = SortE(eEvents.Where(x =>
                MadeUpTalk.IsHearsayOnly(x.Evt) &&
                string.Equals(MadeUpTalk.GetAccusedHeroId(x.Evt), heHeroId, StringComparison.Ordinal) &&
                OpinionSign(x.Evt, +1))).ToList();
            if (accusedGood.Count > 0)
            {
                var (heard, xEvt) = accusedGood[0];
                bool knows = PrepareAccused(heard, xEvt);
                plan.KnowsOriginator = knows;
                plan.ResultKind = ProbeResultKind.AccusedPraise;
                plan.ResultKindLabel = "accused-praise";
                plan.EventId = xEvt.EventId;
                plan.SentenceKey = "VividWorld_Probe_Accused_PraiseSubject";
                return Done($"accused of {xEvt.EventId} (good talk), {DeliveryText(plan)}");
            }
            notes.Add("2 accused-praise: no good made-up talk about him in E");

            // 3. 發生過的壞事問到做的人本人：只有模板裡他那個角色 selfTell 是 never 的才不肯談
            var realBad = chainEventsC
                .Where(evt => !evt.Fabricated
                    && string.Equals(MadeUpTalk.GetAccusedHeroId(evt), heHeroId, StringComparison.Ordinal)
                    && OpinionSign(evt, -1))
                .ToList();
            var refuseSelf = new List<WorldEvent>();
            foreach (var evt in realBad)
            {
                var st = SelfTellEvaluator.Evaluate(evt, heHeroId, getTemplate, traitLookup);
                if (st.IsNever) refuseSelf.Add(evt);
                else notes.Add($"3 accused-refuse: {evt.EventId} is his bad deed but selfTell is not never ({(st.CanTell ? "allowed" : st.ReasonText)}), later rows decide");
            }
            if (refuseSelf.Count > 0)
            {
                var cEvt = refuseSelf
                    .OrderByDescending(evt => blockEntriesE.FirstOrDefault(e => e.EventId == evt.EventId)?.UpdatedDay ?? (evt.Day - 100000.0))
                    .ThenBy(evt => evt.EventId, StringComparer.Ordinal)
                    .First();
                plan.ResultKind = ProbeResultKind.AccusedRefuse;
                plan.ResultKindLabel = "accused-refuse";
                plan.EventId = cEvt.EventId;
                plan.SentenceKey = Pair("VividWorld_Probe_Refuse_1", "VividWorld_Probe_Refuse_2");
                return Done($"accused of real event {cEvt.EventId} (bad deed, selfTell never for role {cEvt.RoleOf(heHeroId)})");
            }
            notes.Add("3 accused-refuse: no real bad deed of his with selfTell never in C");

            // 4. 起頭的人：堅持或說溜嘴
            var originators = SortE(eEvents.Where(x =>
                x.Evt.Fabricated && string.Equals(x.Evt.OriginatorHeroId, heHeroId, StringComparison.Ordinal))).ToList();
            if (originators.Count > 0)
            {
                var xEvt = originators[0].Evt;
                bool isBoast = string.Equals(xEvt.Type, "tavern_boast_told", StringComparison.Ordinal);
                string? situation = xEvt.SituationId;
                bool known = isBoast
                    || string.Equals(situation, "madeup_slander", StringComparison.Ordinal)
                    || string.Equals(situation, "madeup_rivalry", StringComparison.Ordinal)
                    || string.Equals(situation, "madeup_praise", StringComparison.Ordinal);

                plan.EventId = xEvt.EventId;
                if (!known)
                {
                    plan.ResultKind = ProbeResultKind.OriginatorInsist;
                    plan.ResultKindLabel = "originator-insist";
                    plan.SentenceKey = Pair("VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2");
                    return Done($"originator of {xEvt.EventId} (unknown situation {situation ?? "none"}), no slip roll, insist");
                }

                int calc = traitLookup?.Of(heHeroId)?.Calculating ?? 0;
                double p = SlipChancePercent(config.FalseRumors?.Slip, calc);
                double roll = SlipRoll(campaignSeed, xEvt.EventId, heHeroId);
                bool slipped = roll < p;
                string rollText = $"slip p={p.ToString("0.##", CultureInfo.InvariantCulture)} (calculating {calc}) rolled {roll.ToString("0.##", CultureInfo.InvariantCulture)} -> {(slipped ? "slip" : "insist")}";

                if (slipped && !isBoast)
                {
                    SetAddress(MadeUpTalk.GetAccusedHeroId(xEvt));
                }

                string what;
                if (isBoast)
                {
                    plan.SentenceKey = slipped ? "VividWorld_Probe_Slip_Boast" : "VividWorld_Probe_Insist_Boast";
                    what = "boast";
                }
                else if (string.Equals(situation, "madeup_slander", StringComparison.Ordinal))
                {
                    plan.SentenceKey = slipped
                        ? Pair("VividWorld_Probe_Slip_Grudge_1", "VividWorld_Probe_Slip_Grudge_2")
                        : Pair("VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2");
                    what = "grudge slander";
                }
                else if (string.Equals(situation, "madeup_rivalry", StringComparison.Ordinal))
                {
                    plan.SentenceKey = slipped
                        ? "VividWorld_Probe_Slip_Rivalry"
                        : Pair("VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2");
                    what = "rivalry";
                }
                else
                {
                    plan.SentenceKey = slipped
                        ? "VividWorld_Probe_Slip_Praise"
                        : Pair("VividWorld_Probe_Insist_1", "VividWorld_Probe_Insist_2");
                    what = "praise of own kin";
                }

                plan.ResultKind = slipped ? ProbeResultKind.OriginatorSlip : ProbeResultKind.OriginatorInsist;
                plan.ResultKindLabel = slipped ? "originator-slip" : "originator-insist";
                return Done($"originator of {xEvt.EventId} ({what}), {rollText}");
            }
            notes.Add("4 originator: he started none of the made-up talk in E");

            // 5、6. 知道實情的人（編的話沒發生過，他在真相知情者名單裡）
            var hearsayE = SortE(eEvents.Where(x => MadeUpTalk.IsHearsayOnly(x.Evt))).ToList();
            (PlayerHeardEntry Heard, WorldEvent Evt, WorldEvent? L, MadeUpTalk.TruthKnower Knower)? face = null;
            (PlayerHeardEntry Heard, WorldEvent Evt, WorldEvent? L, MadeUpTalk.TruthKnower Knower)? tell = null;
            foreach (var item in hearsayE)
            {
                WorldEvent? lEvt = null;
                if (!string.IsNullOrEmpty(item.Evt.LinkedEventId)) cById.TryGetValue(item.Evt.LinkedEventId!, out lEvt);

                var match = MadeUpTalk.GetTruthKnowers(item.Evt, lEvt, playerHeroId)
                    .FirstOrDefault(k => string.Equals(k.HeroId, heHeroId, StringComparison.Ordinal) && !k.IsAccused && !k.IsPlayer);
                if (match == null) continue;

                if (TruthFaceKey(match.ResponseType) != null)
                {
                    face = (item.Heard, item.Evt, lEvt, match);
                    break;
                }
                tell ??= (item.Heard, item.Evt, lEvt, match);
            }

            if (face != null)
            {
                var cand = face.Value;
                plan.ResultKind = ProbeResultKind.TruthFace;
                plan.ResultKindLabel = "truth-face";
                plan.EventId = cand.Evt.EventId;
                plan.SentenceKey = TruthFaceKey(cand.Knower.ResponseType);
                CopyRoles(plan.Vars, cand.Evt, cand.L);
                return Done($"truth knower of {cand.Evt.EventId} ({cand.Knower.Reason}), face-to-face answer for {cand.Knower.ResponseType}");
            }
            notes.Add("5 truth-face: not a truth knower with a face-to-face answer");

            if (tell != null)
            {
                var cand = tell.Value;
                var toldL = cand.L != null ? ToldByHim(heHeroId, cand.L.EventId, blockEntriesE) : null;
                if (cand.L != null && toldL != null)
                {
                    plan.ResultKind = ProbeResultKind.AlreadyTold;
                    plan.ResultKindLabel = "already-told";
                    plan.EventId = cand.L.EventId;
                    plan.SentenceKey = Pair("VividWorld_Probe_AlreadyTold_1", "VividWorld_Probe_AlreadyTold_2");
                    return Done($"truth knower of {cand.Evt.EventId} ({cand.Knower.Reason}), but {cand.L.EventId} is what he already told the player on day {DayText(toldL.Day)}");
                }
                if (cand.L != null)
                {
                    var rej = IsKnownAndRemembered(cand.L) ? canTellPredicate(cand.L.EventId) : CandidateRejection.TellerDoesNotKnow;
                    if (rej == CandidateRejection.None)
                    {
                        plan.ResultKind = ProbeResultKind.TruthTell;
                        plan.ResultKindLabel = "truth-tell";
                        plan.EventId = cand.L.EventId;
                        plan.Offer = buildOfferFunc(cand.L.EventId);
                        return Done($"truth knower of {cand.Evt.EventId} ({cand.Knower.Reason}), tells what he saw: {cand.L.EventId}");
                    }
                    if (IsReluctant(rej))
                    {
                        plan.ResultKind = ProbeResultKind.RefuseToTell;
                        plan.ResultKindLabel = "refuse-to-tell";
                        plan.EventId = cand.L.EventId;
                        plan.SentenceKey = Pair("VividWorld_Probe_Refuse_1", "VividWorld_Probe_Refuse_2");
                        return Done($"truth knower of {cand.Evt.EventId} ({cand.Knower.Reason}), but would rather not tell {cand.L.EventId}: {rej}");
                    }
                    notes.Add($"6 truth-tell: {cand.L.EventId} skipped ({rej})");
                }
                else
                {
                    notes.Add($"6 truth-tell: {cand.Evt.EventId} has no linked real event in C");
                }
            }
            else
            {
                notes.Add("6 truth-tell: not a truth knower without a face-to-face answer");
            }

            // 7. 兩種說法都聽過
            foreach (var item in hearsayE)
            {
                var xEvt = item.Evt;
                if (!IsKnownAndRemembered(xEvt)) continue;

                string? secondId = null;
                foreach (var resp in chainEventsC)
                {
                    if (!string.Equals(resp.LinkedEventId, xEvt.EventId, StringComparison.Ordinal)) continue;
                    if (string.IsNullOrEmpty(getTemplate(resp.Type)?.Response)) continue;
                    if (!IsKnownAndRemembered(resp)) continue;
                    secondId = resp.EventId;
                    break;
                }

                if (secondId == null && string.Equals(xEvt.Type, "conduct_poisoned", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(xEvt.LinkedEventId)
                    && cById.TryGetValue(xEvt.LinkedEventId!, out var lDeath) && IsKnownAndRemembered(lDeath))
                {
                    secondId = lDeath.EventId;
                }

                if (secondId == null) continue;

                var xEntry = xEvt.EntryFor(heHeroId);
                bool believes = xEntry?.Believes ?? true;
                string reason = xEntry?.BeliefReason ?? string.Empty;
                string? sourceId = xEntry?.SourceHeroId;

                SetAddress(MadeUpTalk.GetAccusedHeroId(xEvt));
                if (!string.IsNullOrEmpty(sourceId)) plan.Vars["SOURCE"] = sourceId!;

                plan.ResultKind = ProbeResultKind.TwoVersions;
                plan.ResultKindLabel = "two-versions";
                plan.EventId = xEvt.EventId;
                plan.SentenceKey = MapTwoVersionsSentenceKey(believes, reason, sourceId, playerHeroId);
                return Done($"knows {xEvt.EventId} and {secondId}, belief {(believes ? "true" : "false")}{(xEntry?.Believes == null ? " (never judged, counted as believing)" : "")}, reason {(reason.Length == 0 ? "none" : reason)}");
            }
            notes.Add("7 two-versions: he does not know both a made-up talk and its response");

            // 8. 只聽過一種說法：他知道 C 裡的某一則
            var knownC = chainEventsC.Where(IsKnownAndRemembered).ToList();
            var skipped = new List<string>();
            if (knownC.Count > 0)
            {
                var hearsayIds = new HashSet<string>(hearsayE.Select(h => h.Evt.EventId), StringComparer.Ordinal);
                var ordered = knownC
                    .OrderByDescending(evt => evt.RoleOf(heHeroId) != null || evt.EntryFor(heHeroId)?.Hop == 0)
                    .ThenByDescending(evt => hearsayIds.Contains(evt.EventId))
                    .ThenByDescending(evt => evt.Day)
                    .ThenBy(evt => evt.EventId, StringComparer.Ordinal)
                    .ToList();

                string? firstReluctantId = null;
                CandidateRejection firstReluctant = CandidateRejection.None;
                var toldIds = new List<string>();
                foreach (var cand in ordered)
                {
                    var told = ToldByHim(heHeroId, cand.EventId, blockEntriesE);
                    if (told != null)
                    {
                        toldIds.Add(cand.EventId);
                        skipped.Add($"{cand.EventId}: already told the player on day {DayText(told.Day)}");
                        continue;
                    }

                    var rej = canTellPredicate(cand.EventId);
                    if (rej == CandidateRejection.None)
                    {
                        bool firsthand = cand.RoleOf(heHeroId) != null || cand.EntryFor(heHeroId)?.Hop == 0;
                        plan.ResultKind = ProbeResultKind.Tell;
                        plan.ResultKindLabel = "tell";
                        plan.EventId = cand.EventId;
                        plan.Offer = buildOfferFunc(cand.EventId);
                        return Done($"knows {knownC.Count} of C, tells {cand.EventId}{(firsthand ? " (he was there)" : string.Empty)}{SkippedText(skipped)}");
                    }

                    skipped.Add($"{cand.EventId}: {rej}");
                    if (IsReluctant(rej) && firstReluctantId == null)
                    {
                        firstReluctantId = cand.EventId;
                        firstReluctant = rej;
                    }
                }

                // 講過的反問比說「不想談」自然，也不洩漏他另外還知道什麼
                if (toldIds.Count > 0)
                {
                    plan.ResultKind = ProbeResultKind.AlreadyTold;
                    plan.ResultKindLabel = "already-told";
                    plan.EventId = toldIds[0];
                    plan.SentenceKey = Pair("VividWorld_Probe_AlreadyTold_1", "VividWorld_Probe_AlreadyTold_2");
                    return Done($"knows {knownC.Count} of C, already told the player {string.Join(", ", toldIds)}{(firstReluctantId != null ? $"; also would rather not tell {firstReluctantId}: {firstReluctant}" : string.Empty)}{SkippedText(skipped)}");
                }

                if (firstReluctantId != null)
                {
                    plan.ResultKind = ProbeResultKind.RefuseToTell;
                    plan.ResultKindLabel = "refuse-to-tell";
                    plan.EventId = firstReluctantId;
                    plan.SentenceKey = Pair("VividWorld_Probe_Refuse_1", "VividWorld_Probe_Refuse_2");
                    return Done($"would rather not tell {firstReluctantId}: {firstReluctant}{SkippedText(skipped)}");
                }
                notes.Add($"8 tell: knows {knownC.Count} of C but none can be told{SkippedText(skipped)}");
            }
            else
            {
                notes.Add("8 tell: he knows none of C");
            }

            // 9. 沒聽說
            plan.ResultKind = ProbeResultKind.NotHeard;
            plan.ResultKindLabel = "not-heard";
            plan.EventId = rootEventId;
            plan.SentenceKey = Pair("VividWorld_Probe_NotHeard_1", "VividWorld_Probe_NotHeard_2");
            if (eMissing > 0) notes.Add($"{eMissing} heard entries not found in C");
            return Done("has not heard of any of it");
        }

        private static string SkippedText(List<string> skipped)
            => skipped.Count == 0 ? string.Empty : $"; skipped {string.Join(", ", skipped)}";

        private static string DeliveryText(ProbePlan plan)
        {
            if (!plan.ShouldDeliverToAccused) return "already knew it, nothing delivered";
            return $"will learn it now (hop {plan.DeliverHop}, {plan.DeliverKnownFactIds?.Count ?? 0} facts, knows originator {(plan.DeliverKnowsOriginator ? "yes" : "no")})";
        }

        private static void CopyRoles(Dictionary<string, string> vars, WorldEvent x, WorldEvent? l)
        {
            foreach (var evt in new[] { x, l })
            {
                if (evt?.Participants == null) continue;
                foreach (var kv in evt.Participants)
                {
                    string key = kv.Key.ToUpperInvariant();
                    if (!vars.ContainsKey(key)) vars[key] = kv.Value;
                }
            }
        }

        private static bool IsReluctant(CandidateRejection rej)
        {
            return rej == CandidateRejection.WontTellOwn
                || rej == CandidateRejection.SecretHolderNotWilling
                || rej == CandidateRejection.LeakedSecretHonorable
                || rej == CandidateRejection.LeakedSecretCautiousStranger
                || rej == CandidateRejection.HeldBackShameful;
        }

        /// <summary>當面回答的句子鍵；這個回應型別沒有當面那一句（例：同軍團的人）時回 null。</summary>
        public static string? TruthFaceKey(string? responseType)
        {
            return responseType switch
            {
                "talk_corrected_rash_capture_by_captor" => "VividWorld_Probe_Truth_RashCaptureByCaptor",
                "talk_corrected_rash_capture_by_bystander" => "VividWorld_Probe_Truth_RashCaptureByBystander",
                "talk_corrected_mistreated_by_prisoner" => "VividWorld_Probe_Truth_MistreatedByPrisoner",
                "talk_corrected_refused_aid_by_asker" => "VividWorld_Probe_Truth_RefusedAidByAsker",
                "talk_not_so_victory_credit_deferred" => "VividWorld_Probe_Truth_NotSo_VictoryCreditDeferred",
                "talk_not_so_advice_given_freely" => "VividWorld_Probe_Truth_NotSo_AdviceGivenFreely",
                "talk_not_so_brawl_man_handed_over" => "VividWorld_Probe_Truth_NotSo_BrawlManHandedOver",
                "talk_not_so_seat_dispute_yielded" => "VividWorld_Probe_Truth_NotSo_SeatDisputeYielded",
                "talk_not_so_tavern_good_word" => "VividWorld_Probe_Truth_NotSo_TavernGoodWord",
                _ => null
            };
        }

        /// <summary>兩種說法都聽過：理由對句子。來源是玩家或沒有來源時，「說的人」那一種改用不提來源的句子。</summary>
        public static string MapTwoVersionsSentenceKey(bool believes, string? beliefReason, string? sourceHeroId, string? playerHeroId)
        {
            bool sourceUsable = !string.IsNullOrEmpty(sourceHeroId)
                && !(!string.IsNullOrEmpty(playerHeroId) && string.Equals(sourceHeroId, playerHeroId, StringComparison.Ordinal));
            string b = believes ? "Believe" : "Disbelieve";

            switch (beliefReason)
            {
                case "SubjectRelation":
                    return "VividWorld_Probe_Reason_SubjectRelation_" + b;
                case "TellerRelation":
                    return sourceUsable
                        ? "VividWorld_Probe_Reason_TellerRelation_" + b
                        : "VividWorld_Probe_Reason_None_" + b;
                case "TraitFit":
                    return "VividWorld_Probe_Reason_TraitFit_" + b;
                case "ListenerNature":
                    return "VividWorld_Probe_Reason_ListenerNature_" + b;
                case "Distance":
                    // 相信的那一格照理不會出現
                    return believes ? "VividWorld_Probe_Reason_None_Believe" : "VividWorld_Probe_Reason_Distance_Disbelieve";
                default:
                    return "VividWorld_Probe_Reason_None_" + b;
            }
        }
    }
}

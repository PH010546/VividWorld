using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;

namespace VividWorld.Campaign
{
    /// <summary>
    /// 職位換回去的處理：每天檢查四個職位，被拔掉的人對玩家好感下降、士氣下降，
    /// 並排一場抱怨；抱怨每小時在大地圖上以原版同伴抱怨的方式開出來，開不成就改成訊息列一行。
    /// </summary>
    internal sealed partial class WandererEventBehavior
    {
        /// <summary>抱怨排了之後，這麼多天內開不成就改成訊息列。</summary>
        private const double RoleComplaintOpenDays = 3.0;

        /// <summary>NPC 那句的優先權。2300 在 start 上沒有別的模組在用，見 DialogArgs 的掃描結果。</summary>
        private const int RoleComplaintLinePriority = 2300;

        private const int RoleComplaintReplyPriority = 100;
        private const string RoleComplaintStartToken = "start";
        private const string RoleComplaintReplyToken = "vividworld_role_complaint_reply";
        private const string RoleComplaintLineId = "vividworld_role_complaint_line";
        private const string RoleComplaintReplyId = "vividworld_role_complaint_reply_line";

        private static readonly PartyRole[] TrackedRoles =
        {
            PartyRole.Scout,
            PartyRole.Quartermaster,
            PartyRole.Engineer,
            PartyRole.Surgeon
        };

        /// <summary>目前這場 VW 開的抱怨對話是哪一位英雄。NPC 那句的條件比對它，只有這一場才會講。</summary>
        private string? _complaintConversationHeroId;

        /// <summary>這場抱怨對話裡 NPC 那句真的講出口了（沒講出口就結束，表示開得不成功）。</summary>
        private bool _complaintLineSpoken;

        /// <summary>上一次開的抱怨對話沒講到我們那句就結束了。這種情況不重試，等期限到了改成訊息列。</summary>
        private bool _complaintAttemptFailed;

        /// <summary>每小時檢查沒開的原因。只在原因改變時印，避免每小時洗版。</summary>
        private string _complaintBlockReason = string.Empty;

        /// <summary>開發者工具按下時，對話還沒結束：等對話結束的第一個 tick 再開。</summary>
        private bool _devComplaintWaiting;

        /// <summary>每天一次：讀四個職位與誰還在玩家隊伍裡，交給紀錄去判斷，被換回去的就扣好感、士氣並排抱怨。</summary>
        private void CheckRoleRevertsToday()
        {
            var cfg = _config.Situations?.WandererEvents;
            if (cfg == null || !cfg.Enabled)
            {
                ModLog.Info("WandererEvents: role revert check skipped (disabled by config).");
                return;
            }

            var party = MobileParty.MainParty;
            var player = Hero.MainHero;
            if (party == null || player == null || _roleRecords == null)
            {
                ModLog.Warn("WandererEvents: role revert check skipped (main party, player or role records missing).");
                return;
            }

            double day = CampaignTime.Now.ToDays;
            var todayRoles = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var role in TrackedRoles)
            {
                todayRoles[RoleKey(role)] = party.GetRoleHolder(role)?.StringId;
            }

            var records = _roleRecords.Records;
            ModLog.Info($"WandererEvents: role check day {day.ToString("0.00", CultureInfo.InvariantCulture)} - today {FormatRoleMap(todayRoles)}; yesterday {FormatRoleMap(records.Yesterday)}.");

            var result = records.CheckDay(todayRoles, day, cfg.RoleRevertDays, IsHeroInPlayerParty);

            foreach (var ev in result.Evaluations)
            {
                ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                    "WandererEvents: swap record {0} {1} -> {2} (day {3:0.00}, {4}): {5} ({6}).",
                    ev.Swap.Role, ev.Swap.From, ev.Swap.To, ev.Swap.Day, ev.Swap.Source, ev.Status, ev.Reason ?? "-"));
            }

            foreach (var revert in result.Reverts)
            {
                ApplyRoleRevert(revert, day);
            }

            // 每天的昨天職位都會變，所以每天都標髒；實際寫檔照既有做法在存檔時沖寫。
            _roleRecordsDirty = true;
            ModLog.Info($"WandererEvents: role check done - {result.Evaluations.Count} record(s) checked, {result.Reverts.Count} reverted.");
        }

        /// <summary>換回去了：被拔掉的人對玩家好感 −8、玩家士氣 −1，排一場抱怨（同一時間只留一場，新的蓋掉舊的）。</summary>
        private void ApplyRoleRevert(WandererRevertAction revert, double day)
        {
            var replaced = LookupHero(revert.ReplacedHeroId);
            var player = Hero.MainHero;
            var party = MobileParty.MainParty;
            if (replaced == null || player == null || party == null || _roleRecords == null)
            {
                ModLog.Warn($"WandererEvents: {revert.Role} reverted, but {revert.ReplacedHeroId} or the player is missing - no relation change and no complaint.");
                return;
            }

            // 寫法與恩怨施加相同：先讀原本的個人值，寫入後再讀一次印出來。
            int before = replaced.GetBaseHeroRelation(player);
            replaced.SetPersonalRelation(player, before + revert.RelationChange);
            int after = replaced.GetBaseHeroRelation(player);
            ModLog.Info(string.Format(CultureInfo.InvariantCulture,
                "WandererEvents: {0} reverted to {1}; {2} relation to player {3} -> {4} ({5:+0;-0}).",
                revert.Role, revert.OriginalHeroId, revert.ReplacedHeroId, before, after, revert.RelationChange));

            float moraleBefore = party.RecentEventsMorale;
            party.RecentEventsMorale = moraleBefore + (float)revert.MoraleChange;
            ModLog.Info(PlayerPresenceLogFormatter.FormatValueChange("morale", revert.MoraleChange, moraleBefore, party.RecentEventsMorale));

            var earlier = _roleRecords.Records.PendingComplaint;
            if (earlier != null)
            {
                ModLog.Info($"WandererEvents: an earlier complaint from {earlier.HeroId} ({earlier.Role}) is replaced by this one (only one can wait).");
            }

            _roleRecords.Records.PendingComplaint = new WandererPendingComplaint
            {
                HeroId = revert.ReplacedHeroId,
                Role = revert.Role,
                Day = day,
                RelationBefore = before,
                RelationAfter = after
            };
            _roleRecordsDirty = true;
            ModLog.Info($"WandererEvents: complaint queued from {revert.ReplacedHeroId} ({revert.Role}), relation {before} -> {after}, day {day.ToString("0.00", CultureInfo.InvariantCulture)}; it opens on the map, or becomes a message after {RoleComplaintOpenDays.ToString("0", CultureInfo.InvariantCulture)} days.");
        }

        /// <summary>每小時檢查排著的抱怨：開得了就開；開不成在期限內只在原因改變時印；期限到了改成訊息列。</summary>
        private void TryOpenRoleComplaint()
        {
            if (_roleRecords == null) return;

            var pending = _roleRecords.Records.PendingComplaint;
            if (pending == null)
            {
                ResetComplaintState();
                return;
            }

            var cfg = _config.Situations?.WandererEvents;
            if (cfg == null || !cfg.Enabled)
            {
                // 開關關著：不開、不改訊息列，抱怨留著，打開後接著處理。
                NoteComplaintBlocked(pending.HeroId, "wandererEvents is switched off (complaint kept; not opened, message list unchanged)");
                return;
            }

            string? reason = MapBlockReason(pending);
            if (reason == null && ConversationInProgress())
            {
                reason = "another conversation is in progress";
            }
            if (reason == null && _complaintAttemptFailed)
            {
                reason = "the last conversation ended before the complaint line; not retried";
            }

            if (reason == null)
            {
                _complaintBlockReason = string.Empty;
                OpenRoleComplaint(pending);
                return;
            }

            NoteComplaintBlocked(pending.HeroId, reason);

            double day = CampaignTime.Now.ToDays;
            if (day - pending.Day >= RoleComplaintOpenDays)
            {
                ShowComplaintAsMessage(pending, reason);
            }
        }

        /// <summary>
        /// 大地圖上開抱怨對話之前的條件（不含對話是否進行中）：
        /// 在大地圖、隊伍沒有遭遇或圍城、不在海上、不在聚落裡、玩家沒被俘、那位還在玩家隊伍裡。
        /// 回傳 null 表示可以開；否則回傳原因。
        /// </summary>
        private string? MapBlockReason(WandererPendingComplaint pending)
        {
            if (_windowOpen) return "a decision window is open";

            var party = MobileParty.MainParty;
            var player = Hero.MainHero;
            if (party == null || player == null) return "main party is not available";

            if (!(Game.Current?.GameStateManager?.ActiveState is MapState))
            {
                return "not on the map (game state " + CurrentGameStateName() + ")";
            }

            if (party.MapEvent != null) return "main party is in a map event";
            if (party.SiegeEvent != null) return "main party is in a siege";
            if (PlayerEncounter.Current != null) return "a player encounter is open";
            if (party.IsCurrentlyAtSea) return "main party is at sea";
            if (party.CurrentSettlement != null) return "main party is in a settlement";
            if (player.IsPrisoner) return "player is a prisoner";
            if (!IsHeroInPlayerParty(pending.HeroId)) return pending.HeroId + " is not in the player party";
            return null;
        }

        private static bool ConversationInProgress()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.IsConversationInProgress == true;
        }

        /// <summary>開一場原版同伴抱怨那種大地圖對話（兩個 ConversationCharacterData，都是玩家隊伍裡的人）。</summary>
        private void OpenRoleComplaint(WandererPendingComplaint pending)
        {
            var hero = LookupHero(pending.HeroId);
            var player = Hero.MainHero;
            if (hero == null || player == null)
            {
                ShowComplaintAsMessage(pending, "hero or player not found when opening");
                return;
            }

            // 旗標要在開對話之前設好：NPC 那句的條件可能在 OpenConversation 裡就被評估。
            _complaintConversationHeroId = hero.StringId;
            _complaintLineSpoken = false;
            try
            {
                var playerData = new ConversationCharacterData(player.CharacterObject, PartyBase.MainParty, false, false, false, false, false, false);
                var partnerData = new ConversationCharacterData(hero.CharacterObject, PartyBase.MainParty, false, false, false, false, false, false);
                CampaignMapConversation.OpenConversation(playerData, partnerData);
            }
            catch (Exception ex)
            {
                ClearComplaintConversation();
                ModLog.Error($"WandererEvents: complaint conversation with {hero.StringId} threw while opening", ex);
                ShowComplaintAsMessage(pending, "OpenConversation threw: " + ex.Message);
                return;
            }

            if (!ConversationInProgress())
            {
                // OpenConversation 回來了卻沒有對話在進行：旗標不能留著，否則之後跟這位的一般對話會講到抱怨。
                ClearComplaintConversation();
                _complaintAttemptFailed = true;
                ModLog.Warn($"WandererEvents: complaint conversation with {hero.StringId} did not start; not retried, message list after the expiry.");
                return;
            }

            ModLog.Info($"WandererEvents: complaint conversation opened with {hero.StringId} ({pending.Role}), relation {pending.RelationBefore} -> {pending.RelationAfter}.");
        }

        /// <summary>開不成（期限到了、或開對話丟例外）：訊息列一行（誰、哪個職位、好感變多少），清掉排著的抱怨。</summary>
        private void ShowComplaintAsMessage(WandererPendingComplaint pending, string reason)
        {
            var hero = LookupHero(pending.HeroId);
            string heroName = hero?.Name?.ToString() ?? pending.HeroId;
            string roleName = RoleDisplayName(pending.Role);

            var text = new TextObject("{=VividWorld_RoleComplaint_Message}{HERO} is aggrieved over being removed as {ROLE} (Relation: {BEFORE} -> {AFTER})");
            text.SetTextVariable("HERO", heroName);
            text.SetTextVariable("ROLE", roleName);
            text.SetTextVariable("BEFORE", pending.RelationBefore.ToString(CultureInfo.InvariantCulture));
            text.SetTextVariable("AFTER", pending.RelationAfter.ToString(CultureInfo.InvariantCulture));
            InformationManager.DisplayMessage(new InformationMessage(text.ToString()));

            ModLog.Info($"WandererEvents: complaint from {heroName} ({pending.Role}) not opened ({reason}); shown in the message list, relation {pending.RelationBefore} -> {pending.RelationAfter}.");
            ClearPendingComplaint();
            ResetComplaintState();
        }

        /// <summary>NPC 那句：只在「這一場是我們為這位開的」時為真。條件會被呼叫很多次，只做比對與設變數。</summary>
        private bool RoleComplaintLineCondition()
        {
            try
            {
                var hero = Hero.OneToOneConversationHero;
                if (hero == null || _complaintConversationHeroId == null) return false;
                if (!string.Equals(hero.StringId, _complaintConversationHeroId, StringComparison.Ordinal)) return false;

                var pending = _roleRecords?.Records.PendingComplaint;
                if (pending == null || !string.Equals(pending.HeroId, hero.StringId, StringComparison.Ordinal)) return false;

                MBTextManager.SetTextVariable("ROLE", RoleDisplayName(pending.Role), false);
                MBTextManager.SetTextVariable("HERO", hero.Name?.ToString() ?? hero.StringId, false);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("WandererEvents: complaint line condition threw", ex);
                return false;
            }
        }

        private void OnRoleComplaintLineSpoken()
        {
            _complaintLineSpoken = true;
            ModLog.Info($"WandererEvents: complaint line spoken by {_complaintConversationHeroId ?? "none"}.");
        }

        /// <summary>玩家唯一的一句回話：清掉排著的抱怨，不動任何數字。</summary>
        private void OnRoleComplaintReplied()
        {
            var pending = _roleRecords?.Records.PendingComplaint;
            if (pending != null)
            {
                ClearPendingComplaint();
            }

            ResetComplaintState();
            ModLog.Info($"WandererEvents: player replied to the complaint from {pending?.HeroId ?? "none"}; complaint cleared, no numbers changed.");
        }

        /// <summary>對話結束：沒講到我們那句就記成「開不成功」，不再重試。</summary>
        private void OnComplaintConversationEnded(IEnumerable<CharacterObject> characters)
        {
            if (_complaintConversationHeroId == null) return;

            if (!_complaintLineSpoken)
            {
                _complaintAttemptFailed = true;
                ModLog.Info($"WandererEvents: complaint conversation with {_complaintConversationHeroId} ended before our line was spoken; not retried until the expiry.");
            }

            ClearComplaintConversation();
        }

        /// <summary>把抱怨的 NPC 那句與玩家回話掛上 start（優先權 2300）與玩家的回話（優先權 100，輸出 close_window）。</summary>
        internal void RegisterComplaintDialogues(CampaignGameStarter starter)
        {
            if (starter == null) return;

            starter.AddDialogLine(
                RoleComplaintLineId,
                RoleComplaintStartToken,
                RoleComplaintReplyToken,
                "{=VividWorld_RoleComplaint_Line}You put me in as {ROLE} only to take it back days later. Are you toying with me?",
                RoleComplaintLineCondition,
                OnRoleComplaintLineSpoken,
                RoleComplaintLinePriority,
                null);

            starter.AddPlayerLine(
                RoleComplaintReplyId,
                RoleComplaintReplyToken,
                "close_window",
                "{=VividWorld_RoleComplaint_Reply}Company assignments change with our needs. Keep your mind on your duties.",
                null,
                OnRoleComplaintReplied,
                RoleComplaintReplyPriority,
                null);

            ModLog.Info($"WandererEvents: complaint dialogue registered on {RoleComplaintStartToken} (priority {RoleComplaintLinePriority}), reply priority {RoleComplaintReplyPriority}.");
        }

        /// <summary>開發者工具：有排著的就立刻開（只看地圖條件，不看開關與期限）。對話進行中就等它結束再開。</summary>
        internal string ForceOpenComplaint()
        {
            if (_roleRecords == null) return "(dev) no role records are loaded.";

            var pending = _roleRecords.Records.PendingComplaint;
            if (pending == null) return "(dev) no complaint is waiting.";

            string? reason = MapBlockReason(pending);
            if (reason != null) return "(dev) cannot open now: " + reason;

            if (ConversationInProgress())
            {
                // 開發者選單本身就是一場對話；原版不能在對話裡再開一場，等它結束後的第一個 tick 再開。
                _devComplaintWaiting = true;
                TaleWorlds.CampaignSystem.CampaignEvents.TickEvent.ClearListeners(this);
                TaleWorlds.CampaignSystem.CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTickDevComplaintWait);
                ModLog.Info($"WandererEvents: dev complaint waits for the current conversation to close ({pending.HeroId}).");
                return "(dev) the complaint opens when this conversation closes.";
            }

            OpenRoleComplaint(pending);
            return "(dev) complaint conversation opened - see log.txt";
        }

        private void OnTickDevComplaintWait(float dt)
        {
            if (!_devComplaintWaiting)
            {
                TaleWorlds.CampaignSystem.CampaignEvents.TickEvent.ClearListeners(this);
                return;
            }

            if (ConversationInProgress()) return;

            _devComplaintWaiting = false;
            TaleWorlds.CampaignSystem.CampaignEvents.TickEvent.ClearListeners(this);

            var pending = _roleRecords?.Records.PendingComplaint;
            if (pending == null)
            {
                ModLog.Info("WandererEvents: dev complaint not opened - nothing is waiting any more.");
                return;
            }

            string? reason = MapBlockReason(pending);
            if (reason != null)
            {
                ModLog.Info($"WandererEvents: dev complaint not opened after the conversation: {reason}.");
                return;
            }

            OpenRoleComplaint(pending);
        }

        /// <summary>開發者狀態行：排著的抱怨、上一次沒開的原因、開不成的旗標、目前這場是誰。</summary>
        internal string ComplaintStatusLine()
        {
            return "  Complaint: last block reason: " + (string.IsNullOrEmpty(_complaintBlockReason) ? "none" : _complaintBlockReason)
                + "; last attempt failed: " + _complaintAttemptFailed
                + "; conversation with: " + (_complaintConversationHeroId ?? "none")
                + "; line spoken: " + _complaintLineSpoken
                + "; dev waiting: " + _devComplaintWaiting + ".";
        }

        private void NoteComplaintBlocked(string heroId, string reason)
        {
            if (reason == _complaintBlockReason) return;
            _complaintBlockReason = reason;
            ModLog.Info($"WandererEvents: complaint from {heroId} not opened this hour: {reason}.");
        }

        private void ClearComplaintConversation()
        {
            _complaintConversationHeroId = null;
            _complaintLineSpoken = false;
        }

        private void ClearPendingComplaint()
        {
            if (_roleRecords == null) return;
            _roleRecords.Records.PendingComplaint = null;
            _roleRecordsDirty = true;
        }

        private void ResetComplaintState()
        {
            _complaintBlockReason = string.Empty;
            _complaintAttemptFailed = false;
        }

        private bool IsHeroInPlayerParty(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return false;
            var hero = LookupHero(heroId);
            var party = MobileParty.MainParty;
            return hero != null && hero.IsAlive && party != null && hero.PartyBelongedTo == party;
        }

        private static string RoleDisplayName(string roleKey)
        {
            switch (roleKey)
            {
                case "scout": return new TextObject("{=92M0Pb5T}Scout").ToString();
                case "quartermaster": return new TextObject("{=redwEIlW}Quartermaster").ToString();
                case "engineer": return new TextObject("{=7h6cXdW7}Engineer").ToString();
                case "surgeon": return new TextObject("{=QBPrRdQJ}Surgeon").ToString();
                default: return roleKey;
            }
        }

        private static string FormatRoleMap<TValue>(IEnumerable<KeyValuePair<string, TValue>> roles)
        {
            var parts = roles
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key + "=" + (kv.Value == null ? "none" : kv.Value.ToString() ?? "none"))
                .ToList();
            return parts.Count == 0 ? "empty" : string.Join(", ", parts);
        }
    }
}

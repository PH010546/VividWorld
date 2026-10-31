using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using VividWorld.Core.Grudges;
using VividWorld.Core.Situations;
using VividWorld.Core.Util;

namespace VividWorld.Campaign
{
    /// <summary>
    /// 流浪者事件做法的效果：比一比、士氣、個性、技能經驗、改職位。
    /// 執行順序：先定比一比的結果（恩怨要用到 @winner／@loser，所以在 SituationRunner 執行之前），
    /// 執行之後再套士氣、個性、技能經驗，最後才寫職位。
    /// </summary>
    internal sealed partial class WandererEventBehavior
    {
        /// <summary>職位資料裡現任者的角色名（與 partyRoleHolder 條件一致）。</summary>
        private const string IncumbentRole = "incumbent";

        /// <summary>執行前算好的做法：綁定（含 @winner／@loser）、已換過 grudge 角色名的做法、要寫的職位。</summary>
        internal sealed class WandererOutcome
        {
            public SituationBranchDef Branch = null!;
            public Dictionary<string, string> Bindings = new(StringComparer.Ordinal);
            public PendingRoleSwap? Swap;
        }

        /// <summary>要寫入的職位換人：哪個職位、從誰換成誰、來源（swap＝裁決換人，contest＝比一比）。</summary>
        internal sealed class PendingRoleSwap
        {
            public PartyRole Role;
            public string RoleName = string.Empty;
            public string? FromId;
            public string? ToId;
            public string Source = string.Empty;
        }

        /// <summary>現任擔任的職位（擔任好幾個時用種子挑一個）與那個職位的技能。</summary>
        internal sealed class PickedPartyRole
        {
            public PartyRole Role;
            public string RoleName = string.Empty;
            public SkillObject Skill = null!;
            public int MatchCount;
        }

        /// <summary>
        /// 把做法在執行前準備好：比一比（有的話）定出勝負、綁 @winner／@loser、grudge 換成真正的角色名、
        /// 換人（裁決換人或比一比換人）要寫的內容。
        /// </summary>
        private WandererOutcome PrepareOutcome(
            SituationTemplate template,
            SituationBranchDef branch,
            SituationEvaluation evaluation,
            double day,
            Dictionary<string, string> customBindings,
            bool judgeChosen,
            string playerId)
        {
            var outcome = new WandererOutcome
            {
                Branch = branch,
                Bindings = new Dictionary<string, string>(customBindings, StringComparer.Ordinal)
            };

            ModLog.Info($"WandererEvents: running {template.Id} branch {branch.Id} (judged {judgeChosen}, stand-aside {branch.StandAside}).");

            if (branch.Contest != null)
            {
                PrepareContest(template, branch, evaluation, day, playerId, outcome);
            }
            else if (branch.PartyRoleChange != null)
            {
                // 裁決換人：新的持有者就是 partyRoleChange.to；文字裡的 @winner 也以他為準
                string? toId = HeroIdFor(template, evaluation, branch.PartyRoleChange.To, playerId);
                if (!string.IsNullOrEmpty(toId)) outcome.Bindings[WandererOutcomeRules.WinnerKey] = toId!;
                outcome.Swap = PlanRoleSwap(template, day, HeroIdFor(template, evaluation, IncumbentRole, playerId), toId, "swap");
            }

            return outcome;
        }

        private void PrepareContest(
            SituationTemplate template,
            SituationBranchDef branch,
            SituationEvaluation evaluation,
            double day,
            string playerId,
            WandererOutcome outcome)
        {
            var contest = branch.Contest!;
            // 資料慣例：contest.a 是現任（持有者），contest.b 是挑戰者；平手持有者留著
            string holderRole = contest.A;
            string challengerRole = contest.B;
            string? holderId = HeroIdFor(template, evaluation, holderRole, playerId);
            string? challengerId = HeroIdFor(template, evaluation, challengerRole, playerId);
            var holder = LookupHero(holderId);
            var challenger = LookupHero(challengerId);
            string? skillKey = WandererOutcomeRules.ResolveSkillKey(contest.Skill, outcome.Bindings);
            var skill = FindSkill(skillKey);

            if (holder == null || challenger == null || skill == null)
            {
                // 比不成：現任留著、不換人；恩怨照套，@winner 是現任
                ModLog.Warn($"WandererEvents: contest in {template.Id}/{branch.Id} could not run (holder {holderId ?? "none"}, challenger {challengerId ?? "none"}, skill {skillKey ?? contest.Skill}); the holder keeps the role.");
                outcome.Branch = WandererOutcomeRules.WithContestRoles(branch, holderRole, challengerRole);
                if (!string.IsNullOrEmpty(holderId)) outcome.Bindings[WandererOutcomeRules.WinnerKey] = holderId!;
                if (!string.IsNullOrEmpty(challengerId)) outcome.Bindings[WandererOutcomeRules.LoserKey] = challengerId!;
                return;
            }

            long seed = RumorSeed.Of(RumorSeed.Of(0, playerId), "wanderer_contest", template.Id, holderId!, challengerId!, RumorSeed.DayBucket(day));
            int holderSkill = holder.GetSkillValue(skill);
            int challengerSkill = challenger.GetSkillValue(skill);
            var result = WandererContestRules.Decide(holderSkill, challengerSkill, contest.RandomMax, seed);

            string winnerRole = result.HolderWon ? holderRole : challengerRole;
            string loserRole = result.HolderWon ? challengerRole : holderRole;
            string winnerId = result.HolderWon ? holderId! : challengerId!;
            string loserId = result.HolderWon ? challengerId! : holderId!;

            outcome.Branch = WandererOutcomeRules.WithContestRoles(branch, winnerRole, loserRole);
            outcome.Bindings[WandererOutcomeRules.WinnerKey] = winnerId;
            outcome.Bindings[WandererOutcomeRules.LoserKey] = loserId;

            ModLog.Info($"  contest on {skill.StringId}: holder {holderId} skill {holderSkill} + roll {result.HolderRoll} = {result.HolderScore}; challenger {challengerId} skill {challengerSkill} + roll {result.ChallengerRoll} = {result.ChallengerScore} (roll range 0..{result.RandomMax}); winner {winnerId}{(result.HolderWon ? " (holder; a tie also keeps the holder)" : " (challenger)")}.");

            if (contest.WinnerTakesRole && !result.HolderWon)
            {
                outcome.Swap = PlanRoleSwap(template, day, holderId, challengerId, "contest");
            }
        }

        /// <summary>
        /// 計畫換職位：照現任的種子挑他擔任的職位。現任已經不擔任任何職位時不換，印一行。
        /// </summary>
        private static PendingRoleSwap? PlanRoleSwap(SituationTemplate template, double day, string? fromId, string? toId, string source)
        {
            var party = MobileParty.MainParty;
            if (party == null || string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
            {
                ModLog.Info($"  role swap ({source}) in {template.Id} skipped: from {fromId ?? "none"}, to {toId ?? "none"}.");
                return null;
            }

            var picked = PickIncumbentRole(party, fromId!, day);
            if (picked == null)
            {
                ModLog.Info($"  role swap ({source}) in {template.Id} skipped: {fromId} holds no party role now.");
                return null;
            }

            return new PendingRoleSwap
            {
                Role = picked.Role,
                RoleName = picked.RoleName,
                FromId = fromId,
                ToId = toId,
                Source = source
            };
        }

        /// <summary>
        /// 現任擔任的職位（斥候、軍需官、工程師、外科醫生）；擔任好幾個時用種子挑一個。沒有就回傳 null。
        /// 綁定與換人共用這一支，同一個現任、同一天挑出的職位一定相同。
        /// </summary>
        private static PickedPartyRole? PickIncumbentRole(MobileParty party, string incumbentId, double day)
        {
            var matchingRoles = new List<(PartyRole Role, string RoleName, SkillObject Skill)>();
            var scout = party.GetRoleHolder(PartyRole.Scout);
            if (scout?.StringId == incumbentId)
                matchingRoles.Add((PartyRole.Scout, new TextObject("{=92M0Pb5T}Scout").ToString(), DefaultSkills.Scouting));
            var quartermaster = party.GetRoleHolder(PartyRole.Quartermaster);
            if (quartermaster?.StringId == incumbentId)
                matchingRoles.Add((PartyRole.Quartermaster, new TextObject("{=redwEIlW}Quartermaster").ToString(), DefaultSkills.Steward));
            var engineer = party.GetRoleHolder(PartyRole.Engineer);
            if (engineer?.StringId == incumbentId)
                matchingRoles.Add((PartyRole.Engineer, new TextObject("{=7h6cXdW7}Engineer").ToString(), DefaultSkills.Engineering));
            var surgeon = party.GetRoleHolder(PartyRole.Surgeon);
            if (surgeon?.StringId == incumbentId)
                matchingRoles.Add((PartyRole.Surgeon, new TextObject("{=QBPrRdQJ}Surgeon").ToString(), DefaultSkills.Medicine));

            if (matchingRoles.Count == 0) return null;

            ulong pickSeed = unchecked((ulong)RumorSeed.Of(0, "wanderer_role_pick", incumbentId, RumorSeed.DayBucket(day)));
            var pickedRole = matchingRoles[(int)(pickSeed % (ulong)matchingRoles.Count)];
            return new PickedPartyRole
            {
                Role = pickedRole.Role,
                RoleName = pickedRole.RoleName,
                Skill = pickedRole.Skill,
                MatchCount = matchingRoles.Count
            };
        }

        /// <summary>
        /// 做法執行之後套用的效果。個性只在裁決的做法（不含不插手）；士氣、技能經驗、換人任何做法都可能有。
        /// </summary>
        private void ApplyOutcomeEffects(SituationTemplate template, WandererOutcome outcome, SituationEvaluation evaluation, double day, bool judgeChosen, string playerId, bool seen)
        {
            var branch = outcome.Branch;

            ApplyOutcomeMorale(branch);

            if (judgeChosen && !branch.StandAside)
            {
                ApplyOutcomeTraits(template, branch);
            }
            else
            {
                ModLog.Info($"  traits not changed: {branch.Id} is not a judged choice.");
            }

            ApplyOutcomeSkillXp(template, branch, evaluation, outcome.Bindings, playerId, seen);

            if (outcome.Swap != null)
            {
                ExecuteRoleSwap(outcome.Swap, day);
            }
        }

        private static void ApplyOutcomeMorale(SituationBranchDef branch)
        {
            double morale = branch.PlayerEffects?.Morale ?? 0.0;
            if (morale == 0.0) return;

            var party = MobileParty.MainParty;
            if (party == null)
            {
                ModLog.Info(PlayerPresenceLogFormatter.FormatValueSkipped("morale", "no main party"));
                return;
            }

            // 跟玩家入局一樣：加在主角隊伍的近期事件士氣上（原版改士氣的做法）
            float before = party.RecentEventsMorale;
            party.RecentEventsMorale = before + (float)morale;
            ModLog.Info(PlayerPresenceLogFormatter.FormatValueChange("morale", morale, before, party.RecentEventsMorale));
        }

        private static void ApplyOutcomeTraits(SituationTemplate template, SituationBranchDef branch)
        {
            var plan = PlayerEffectsResolver.Resolve(template, branch, false, false);
            var main = Hero.MainHero;
            if (main == null) return;

            foreach (var t in plan.Traits)
            {
                var trait = PlayerPresenceBehavior.TraitOf(t.Key);
                if (trait == null)
                {
                    ModLog.Info(PlayerPresenceLogFormatter.FormatValueSkipped("trait " + t.Key, "unknown trait"));
                    continue;
                }

                // 同玩家入局：經驗值跟原版同一處讀，等級換算交給原版的事件用方法
                var developer = TaleWorlds.CampaignSystem.Campaign.Current?.PlayerTraitDeveloper;
                int? xpBefore = developer?.GetPropertyValue(trait);
                int levelBefore = main.GetTraitLevel(trait);
                TraitLevelingHelper.OnIncidentResolved(trait, t.Value);
                int levelAfter = main.GetTraitLevel(trait);
                int? xpAfter = developer?.GetPropertyValue(trait);
                ModLog.Info(PlayerPresenceLogFormatter.FormatTraitChange(t.Key, t.Value, xpBefore, xpAfter, levelBefore, levelAfter));
            }
        }

        private void ApplyOutcomeSkillXp(SituationTemplate template, SituationBranchDef branch, SituationEvaluation evaluation, Dictionary<string, string> bindings, string playerId, bool seen)
        {
            if (branch.SkillXp == null) return;

            foreach (var sx in branch.SkillXp)
            {
                string? heroId = HeroIdFor(template, evaluation, sx.Role, playerId);
                var hero = LookupHero(heroId);
                string? skillKey = WandererOutcomeRules.ResolveSkillKey(sx.Skill, bindings);
                var skill = FindSkill(skillKey);
                if (hero == null || skill == null)
                {
                    ModLog.Info($"  skill experience not given: role {sx.Role} ({heroId ?? "none"}), skill {skillKey ?? sx.Skill}.");
                    continue;
                }

                // 經驗值與等級前後都印出來：實際加了多少由前後差看得出
                int levelBefore = hero.GetSkillValue(skill);
                float xpBefore = hero.HeroDeveloper.GetSkillXp(skill);
                hero.HeroDeveloper.AddSkillXp(skill, sx.Amount, true, true);
                int levelAfter = hero.GetSkillValue(skill);
                float xpAfter = hero.HeroDeveloper.GetSkillXp(skill);
                ModLog.Info($"  skill experience {skill.StringId} for {hero.StringId} (role {sx.Role}): requested {sx.Amount}; skill {levelBefore} -> {levelAfter}; xp {xpBefore:0.##} -> {xpAfter:0.##} (added {xpAfter - xpBefore:0.##}).");

                float xpDelta = xpAfter - xpBefore;
                int xpAdded = (int)Math.Round(xpDelta);
                if (!seen)
                {
                    ModLog.Info("  skill xp message skipped: event occurred unseen.");
                }
                else if (xpAdded <= 0)
                {
                    ModLog.Info($"  skill xp message skipped: no experience added (delta {xpDelta:0.##}).");
                }
                else
                {
                    string? teacherId = HeroIdFor(template, evaluation, "teacher", playerId);
                    var teacherHero = LookupHero(teacherId);
                    var teacherName = teacherHero?.Name ?? new TextObject(teacherId ?? string.Empty);
                    bool isPlayer = hero == Hero.MainHero || string.Equals(heroId, playerId, StringComparison.OrdinalIgnoreCase);

                    TextObject msgText;
                    if (isPlayer)
                    {
                        msgText = new TextObject("{=VividWorld_WandererSkillXp_Player}You learned {SKILL_NAME} from {TEACHER}: +{XP} experience");
                        msgText.SetTextVariable("TEACHER", teacherName);
                        msgText.SetTextVariable("SKILL_NAME", skill.Name);
                        msgText.SetTextVariable("XP", xpAdded);
                    }
                    else
                    {
                        msgText = new TextObject("{=VividWorld_WandererSkillXp_Companion}{LEARNER} learned {SKILL_NAME} from {TEACHER}: +{XP} experience");
                        msgText.SetTextVariable("LEARNER", hero.Name);
                        msgText.SetTextVariable("TEACHER", teacherName);
                        msgText.SetTextVariable("SKILL_NAME", skill.Name);
                        msgText.SetTextVariable("XP", xpAdded);
                    }

                    InformationManager.DisplayMessage(new InformationMessage(msgText.ToString()));
                    ModLog.Info($"  skill xp message displayed: {msgText.ToString()}");
                }
            }
        }

        private void ExecuteRoleSwap(PendingRoleSwap swap, double day)
        {
            var party = MobileParty.MainParty;
            var toHero = LookupHero(swap.ToId);
            if (party == null || toHero == null)
            {
                ModLog.Warn($"WandererEvents: role swap ({swap.Source}) not written: party {(party != null ? "found" : "missing")}, new holder {swap.ToId ?? "none"} {(toHero != null ? "found" : "not found")}.");
                return;
            }

            LogPartyRoles($"before swap ({swap.Source})", party);
            switch (swap.Role)
            {
                case PartyRole.Scout: party.SetPartyScout(toHero); break;
                case PartyRole.Quartermaster: party.SetPartyQuartermaster(toHero); break;
                case PartyRole.Engineer: party.SetPartyEngineer(toHero); break;
                case PartyRole.Surgeon: party.SetPartySurgeon(toHero); break;
                default:
                    ModLog.Warn($"WandererEvents: role {swap.Role} has no setter; swap ({swap.Source}) not written.");
                    return;
            }
            LogPartyRoles($"after swap ({swap.Source})", party);

            string roleKey = RoleKey(swap.Role);
            if (_roleRecords == null)
            {
                ModLog.Warn($"WandererEvents: {roleKey} swapped ({swap.Source}) but no record store is loaded; not recorded.");
                return;
            }

            _roleRecords.Records.RecordSwap(roleKey, swap.FromId ?? string.Empty, swap.ToId ?? string.Empty, day, swap.Source);
            _roleRecordsDirty = true;
            ModLog.Info($"WandererEvents: {roleKey} now held by {swap.ToId} (was {swap.FromId}); swap record written (source {swap.Source}).");
        }

        /// <summary>寫入職位前後各讀一次四個職位，印出來（寫入方法會不會把同一人從別的職位拿掉，要看日誌）。</summary>
        private static void LogPartyRoles(string label, MobileParty party)
        {
            ModLog.Info($"  party roles {label}: scout={RoleHolderText(party, PartyRole.Scout)}, quartermaster={RoleHolderText(party, PartyRole.Quartermaster)}, engineer={RoleHolderText(party, PartyRole.Engineer)}, surgeon={RoleHolderText(party, PartyRole.Surgeon)}.");
        }

        private static string RoleHolderText(MobileParty party, PartyRole role)
        {
            return party.GetRoleHolder(role)?.StringId ?? "none";
        }

        private static string RoleKey(PartyRole role)
        {
            switch (role)
            {
                case PartyRole.Scout: return "scout";
                case PartyRole.Quartermaster: return "quartermaster";
                case PartyRole.Engineer: return "engineer";
                case PartyRole.Surgeon: return "surgeon";
                default: return role.ToString().ToLowerInvariant();
            }
        }

        /// <summary>有換人紀錄沒寫進檔案時，存檔（Flush）時沖寫到 wanderer_roles.json。</summary>
        internal void FlushRoleRecords()
        {
            if (_roleRecords == null || !_roleRecordsDirty) return;

            if (_roleRecords.Save())
            {
                _roleRecordsDirty = false;
                ModLog.Info($"WandererEvents: role records written ({_roleRecords.Data.Swaps.Count} swap record(s)).");
            }
            else
            {
                ModLog.Warn("WandererEvents: role records could not be written; the next flush tries again.");
            }
        }

        /// <summary>角色在做法裡綁到的英雄；沒綁到而且是裁決者（玩家）就是玩家自己。</summary>
        private static string? HeroIdFor(SituationTemplate template, SituationEvaluation evaluation, string role, string playerId)
        {
            if (evaluation.BoundHeroIds.TryGetValue(role, out var id) && !string.IsNullOrEmpty(id)) return id;
            if (string.Equals(role, template.Player?.JudgeRole, StringComparison.OrdinalIgnoreCase)) return playerId;
            return null;
        }

        private Hero? LookupHero(string? heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            return _heroLookup?.Get(heroId!) ?? Hero.Find(heroId!);
        }

        private static SkillObject? FindSkill(string? skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            return Skills.All.FirstOrDefault(s => s != null && string.Equals(s.StringId, skillId, StringComparison.OrdinalIgnoreCase));
        }
    }
}

#nullable enable
using System.Collections.Generic;

namespace VividWorld.Core.Dialogue
{
    /// <summary>傳聞模式那一段該用哪一組字。</summary>
    public enum ModeSectionKind
    {
        /// <summary>自動偵測到會擋住平民的模組，改用寫實。</summary>
        AutoRealistic = 0,
        /// <summary>暢玩（自動或設定寫死）。</summary>
        Casual,
        /// <summary>設定寫死成寫實。</summary>
        ForcedRealistic
    }

    /// <summary>介紹視窗這次要不要跳、跳的話有哪幾段、為什麼。</summary>
    public sealed class ModeNoticePlan
    {
        public ModeNoticeAction Action { get; set; }
        public bool ShowModeSection { get; set; }
        public bool ShowPersonalRelationsSection { get; set; }
        public bool ShowSeededNote { get; set; }
        public ModeSectionKind ModeKind { get; set; }
        public bool PersonalSectionOmittedBecauseSwitchOff { get; set; }

        /// <summary>跳視窗的原因（新戰役／剛搬過／模式變了…），給日誌用。</summary>
        public List<string> Reasons { get; set; } = new List<string>();

        public string DescribeSections()
        {
            var parts = new List<string>();
            if (ShowModeSection) parts.Add("mode");
            if (ShowPersonalRelationsSection) parts.Add("personal relations");
            if (ShowSeededNote) parts.Add("seeded note");
            return string.Join(", ", parts);
        }
    }

    public static class ModeNoticeSections
    {
        /// <param name="baseAction">既有規則（<see cref="ModeNotice.Evaluate"/>）的結果。</param>
        /// <param name="isNewCampaign">這次載入才鑄了戰役 id。</param>
        /// <param name="justSeeded">這次載入剛把跟玩家的好感搬給每個人。</param>
        /// <param name="switchOn">「跟你的好感各人各算」開著。</param>
        public static ModeNoticePlan Decide(string? configuredMode,
                                            RumorMode actualMode,
                                            ModeNoticeAction baseAction,
                                            bool isNewCampaign,
                                            bool justSeeded,
                                            bool switchOn)
        {
            var conf = (configuredMode ?? "auto").Trim().ToLowerInvariant();
            var plan = new ModeNoticePlan();

            if (conf == "realistic") plan.ModeKind = ModeSectionKind.ForcedRealistic;
            else if (conf == "casual") plan.ModeKind = ModeSectionKind.Casual;
            else plan.ModeKind = actualMode == RumorMode.Realistic ? ModeSectionKind.AutoRealistic : ModeSectionKind.Casual;

            if (isNewCampaign)
            {
                plan.Action = ModeNoticeAction.Popup;
                plan.Reasons.Add("new campaign");
                plan.ShowModeSection = true;
                plan.ShowPersonalRelationsSection = switchOn;
            }
            else if (justSeeded && switchOn)
            {
                plan.Action = ModeNoticeAction.Popup;
                plan.Reasons.Add("relations just seeded");
                plan.ShowModeSection = true;
                plan.ShowPersonalRelationsSection = true;
                plan.ShowSeededNote = true;
            }
            else
            {
                plan.Action = baseAction;
                if (baseAction == ModeNoticeAction.Popup)
                {
                    plan.Reasons.Add("mode changed");
                    plan.ShowModeSection = true;
                }
                return plan;
            }

            // 新戰役或剛搬過、同時模式也變了：一併記進原因
            if (baseAction == ModeNoticeAction.Popup) plan.Reasons.Add("mode changed");
            plan.PersonalSectionOmittedBecauseSwitchOff = !switchOn;
            return plan;
        }
    }
}

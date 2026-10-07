#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using VividWorld.Campaign;
using VividWorld.Core.Config;
using VividWorld.Core.Presentation;
using VividWorld.Presentation;

namespace VividWorld.UI
{
    public class ChronicleWindowVM : ViewModel
    {
        private string _titleText;
        private string _emptyText;
        private string _closeText;
        private MBBindingList<ChronicleEntryVM> _entries;

        internal ChronicleWindowVM(IReadOnlyList<ChronicleEntry> entries, PresentationConfig? cfg, HeroLookup? heroLookup)
        {
            _titleText = new TextObject("{=VividWorld_Chronicle_Title}What you have heard").ToString();
            _emptyText = new TextObject("{=VividWorld_Chronicle_Empty}You have not been told anything yet.").ToString();
            _closeText = new TextObject("{=VividWorld_Chronicle_Close}Close").ToString();

            var list = new MBBindingList<ChronicleEntryVM>();
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    list.Add(new ChronicleEntryVM(entry, cfg, heroLookup));
                }
            }
            _entries = list;
        }

        [DataSourceProperty]
        public string TitleText
        {
            get => _titleText;
            set
            {
                if (value != _titleText)
                {
                    _titleText = value;
                    OnPropertyChangedWithValue(value, nameof(TitleText));
                }
            }
        }

        [DataSourceProperty]
        public string EmptyText
        {
            get => _emptyText;
            set
            {
                if (value != _emptyText)
                {
                    _emptyText = value;
                    OnPropertyChangedWithValue(value, nameof(EmptyText));
                }
            }
        }

        [DataSourceProperty]
        public string CloseText
        {
            get => _closeText;
            set
            {
                if (value != _closeText)
                {
                    _closeText = value;
                    OnPropertyChangedWithValue(value, nameof(CloseText));
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<ChronicleEntryVM> Entries
        {
            get => _entries;
            set
            {
                if (value != _entries)
                {
                    _entries = value;
                    OnPropertyChangedWithValue(value, nameof(Entries));
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }

        [DataSourceProperty]
        public bool IsEmpty => _entries == null || _entries.Count == 0;

        public void ExecuteClose()
        {
            ChronicleWindowManager.Close();
        }
    }

    public class ChronicleEntryVM : ViewModel
    {
        private string _headlineText;
        private string _dayText;
        private bool _hasConflict;
        private string _conflictText;
        private MBBindingList<ChronicleMatterVM> _matters;
        private MBBindingList<ChronicleSourceVM> _sources;

        internal ChronicleEntryVM(ChronicleEntry entry, PresentationConfig? cfg, HeroLookup? heroLookup)
        {
            _sources = new MBBindingList<ChronicleSourceVM>();
            _matters = new MBBindingList<ChronicleMatterVM>();

            if (entry == null)
            {
                _headlineText = string.Empty;
                _dayText = string.Empty;
                _conflictText = string.Empty;
                return;
            }

            _hasConflict = entry.HasConflict;
            _conflictText = _hasConflict
                ? new TextObject("{=VividWorld_Chronicle_Conflicting}Conflicting accounts").ToString()
                : string.Empty;

            // 開紀事時已經算好就直接用（那時一併印了日誌）；沒算過才在這裡算
            _headlineText = entry.HeadlineText ?? ResolveHeadline(entry, heroLookup).Text;

            if (TaleWorlds.CampaignSystem.Campaign.Current != null)
            {
                _dayText = CampaignTime.Days((float)entry.Day).ToString();
            }
            else
            {
                _dayText = "Day " + entry.Day.ToString("0.0", CultureInfo.InvariantCulture);
            }

            if (entry.Matters != null && entry.Matters.Count > 0)
            {
                for (int i = 0; i < entry.Matters.Count; i++)
                {
                    var matter = entry.Matters[i];
                    var matterVM = new ChronicleMatterVM(entry, matter, i == 0, cfg, heroLookup);
                    _matters.Add(matterVM);

                    foreach (var sVM in matterVM.Sources)
                    {
                        _sources.Add(sVM);
                    }
                }
            }
            else if (entry.Sources != null)
            {
                foreach (var source in entry.Sources)
                {
                    _sources.Add(new ChronicleSourceVM(entry, source, cfg));
                }
            }

            entry.DayLabel = _dayText;
            entry.SourceHeroName = ChronicleSourceVM.NameOf(entry.SourceHeroId, heroLookup);
        }

        /// <summary>
        /// 算出這一筆的標題：帶上當事人的名字，名字查不到或目前語言沒有帶名字的那一句，
        /// 就退回原本不帶名字的標題。標題是純文字，不放百科連結。
        /// </summary>
        internal static ChronicleHeadlineResult ResolveHeadline(ChronicleEntry entry, HeroLookup? heroLookup)
        {
            return ChronicleHeadline.Resolve(
                entry,
                FallbackTextRenderer.LocalizedTemplate,
                nameVar =>
                {
                    int colon = nameVar.IndexOf(':');
                    string heroId = colon >= 0 ? nameVar.Substring(colon + 1) : nameVar;
                    return ChronicleSourceVM.NameOf(heroId, heroLookup);
                });
        }

        [DataSourceProperty]
        public string HeadlineText
        {
            get => _headlineText;
            set
            {
                if (value != _headlineText)
                {
                    _headlineText = value;
                    OnPropertyChangedWithValue(value, nameof(HeadlineText));
                }
            }
        }

        [DataSourceProperty]
        public string DayText
        {
            get => _dayText;
            set
            {
                if (value != _dayText)
                {
                    _dayText = value;
                    OnPropertyChangedWithValue(value, nameof(DayText));
                }
            }
        }

        [DataSourceProperty]
        public bool HasConflict
        {
            get => _hasConflict;
            set
            {
                if (value != _hasConflict)
                {
                    _hasConflict = value;
                    OnPropertyChangedWithValue(value, nameof(HasConflict));
                }
            }
        }

        [DataSourceProperty]
        public string ConflictText
        {
            get => _conflictText;
            set
            {
                if (value != _conflictText)
                {
                    _conflictText = value;
                    OnPropertyChangedWithValue(value, nameof(ConflictText));
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<ChronicleMatterVM> Matters
        {
            get => _matters;
            set
            {
                if (value != _matters)
                {
                    _matters = value;
                    OnPropertyChangedWithValue(value, nameof(Matters));
                }
            }
        }

        /// <summary>每個告訴過玩家這件事的人一塊。</summary>
        [DataSourceProperty]
        public MBBindingList<ChronicleSourceVM> Sources
        {
            get => _sources;
            set
            {
                if (value != _sources)
                {
                    _sources = value;
                    OnPropertyChangedWithValue(value, nameof(Sources));
                }
            }
        }
    }

    /// <summary>
    /// 紀事區塊裡的一件事：第一件事無小標，第二件事起有小標與日子。底下是該事攤平的所有來源。
    /// </summary>
    public class ChronicleMatterVM : ViewModel
    {
        private string _headingText;
        private string _dayText;
        private bool _hasHeading;
        private MBBindingList<ChronicleSourceVM> _sources;

        internal ChronicleMatterVM(ChronicleEntry blockEntry, ChronicleMatter matter, bool isFirstMatter, PresentationConfig? cfg, HeroLookup? heroLookup)
        {
            _sources = new MBBindingList<ChronicleSourceVM>();
            // 要不要小標由 Core 決定：源頭那件事玩家沒聽過時，第一段也是後來的事，仍要小標
            _hasHeading = matter.HasHeading;

            if (TaleWorlds.CampaignSystem.Campaign.Current != null)
            {
                _dayText = CampaignTime.Days((float)matter.Day).ToString();
            }
            else
            {
                _dayText = "Day " + matter.Day.ToString("0.0", CultureInfo.InvariantCulture);
            }
            matter.DayLabel = _dayText;

            if (_hasHeading)
            {
                _headingText = matter.HeadingText ?? ResolveMatterHeading(matter, heroLookup).Text;
                matter.HeadingText = _headingText;
            }
            else
            {
                _headingText = string.Empty;
            }

            if (matter.Sources != null)
            {
                foreach (var source in matter.Sources)
                {
                    _sources.Add(new ChronicleSourceVM(blockEntry, source, cfg));
                }
            }
        }

        internal static ChronicleHeadlineResult ResolveMatterHeading(ChronicleMatter matter, HeroLookup? heroLookup)
        {
            return ChronicleHeadline.Resolve(
                matter,
                FallbackTextRenderer.LocalizedTemplate,
                nameVar =>
                {
                    int colon = nameVar.IndexOf(':');
                    string heroId = colon >= 0 ? nameVar.Substring(colon + 1) : nameVar;
                    return ChronicleSourceVM.NameOf(heroId, heroLookup);
                });
        }

        [DataSourceProperty]
        public string HeadingText
        {
            get => _headingText;
            set
            {
                if (value != _headingText)
                {
                    _headingText = value;
                    OnPropertyChangedWithValue(value, nameof(HeadingText));
                }
            }
        }

        [DataSourceProperty]
        public string DayText
        {
            get => _dayText;
            set
            {
                if (value != _dayText)
                {
                    _dayText = value;
                    OnPropertyChangedWithValue(value, nameof(DayText));
                }
            }
        }

        [DataSourceProperty]
        public bool HasHeading
        {
            get => _hasHeading;
            set
            {
                if (value != _hasHeading)
                {
                    _hasHeading = value;
                    OnPropertyChangedWithValue(value, nameof(HasHeading));
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<ChronicleSourceVM> Sources
        {
            get => _sources;
            set
            {
                if (value != _sources)
                {
                    _sources = value;
                    OnPropertyChangedWithValue(value, nameof(Sources));
                }
            }
        }
    }

    /// <summary>
    /// 紀事裡的一份來源：
    /// 第一行：「〈誰〉：「〈他當時講的原句〉」」（有存原句）或「〈誰〉：〈事實〉」（沒存原句），
    /// 第二行小字：「傳了 N 手」（在場為「你當時在場」，不明來源為「傳了 N 手 · 不知道是誰說的」）。
    /// </summary>
    public class ChronicleSourceVM : ViewModel
    {
        private string _lineText;
        private string _provenanceText;

        internal ChronicleSourceVM(ChronicleEntry entry, ChronicleSource source, PresentationConfig? cfg)
        {
            bool linksEnabled = cfg?.EncyclopediaLinksEnabled ?? true;

            string line;
            if (source.HasProbeAnswer && !string.IsNullOrEmpty(source.ProbeAnswerKey))
            {
                var probeRender = FallbackTextRenderer.RenderProbeResponse(
                    source.ProbeAnswerKey!,
                    source.ProbeAnswerVars,
                    source.ProbeAddressKey,
                    source.ProbeAddressHeroId,
                    source.HeroId,
                    Hero.MainHero?.StringId,
                    linksEnabled);
                line = linksEnabled ? probeRender.DisplayText : probeRender.PlainText;
            }
            else
            {
                var renderResult = FallbackTextRenderer.RenderBoth(source.Body, cfg);
                line = linksEnabled ? renderResult.DisplayText : renderResult.PlainText;
            }

            if (!linksEnabled && line.IndexOf("<a ", StringComparison.Ordinal) >= 0)
            {
                ModLog.Warn($"Chronicle: body of {entry.EventId} still carries link markup while encyclopedia links are disabled.");
            }

            bool hasTeller = !string.IsNullOrEmpty(source.HeroId);
            string tellerName = hasTeller
                ? FallbackTextRenderer.ResolveVar("hero:" + source.HeroId, linksEnabled)
                : string.Empty;

            if (source.HasProbeAnswer)
            {
                _lineText = hasTeller
                    ? new TextObject("{=VividWorld_Chronicle_Quote}{NAME}: \u201C{LINE}\u201D")
                        .SetTextVariable("NAME", tellerName)
                        .SetTextVariable("LINE", line)
                        .ToString()
                    : line;
            }
            else if (source.HasSpokenLine)
            {
                _lineText = hasTeller
                    ? new TextObject("{=VividWorld_Chronicle_Quote}{NAME}: \u201C{LINE}\u201D")
                        .SetTextVariable("NAME", tellerName)
                        .SetTextVariable("LINE", line)
                        .ToString()
                    : line;
            }
            else
            {
                if (hasTeller && source.Hop > 0)
                {
                    _lineText = new TextObject("{=VividWorld_Chronicle_Plain}{NAME}: {FACTS}")
                        .SetTextVariable("NAME", tellerName)
                        .SetTextVariable("FACTS", line)
                        .ToString();
                }
                else
                {
                    _lineText = line;
                }
            }

            if (source.HasProbeAnswer)
            {
                _provenanceText = string.Empty;
            }
            else if (source.Hop <= 0)
            {
                _provenanceText = new TextObject("{=VividWorld_Chronicle_HopZero}you were there").ToString();
            }
            else
            {
                string hopText = new TextObject("{=VividWorld_Chronicle_Hop}passed through {HOPS} hands")
                    .SetTextVariable("HOPS", source.Hop)
                    .ToString();

                if (hasTeller)
                {
                    _provenanceText = hopText;
                }
                else
                {
                    string unknown = new TextObject("{=VividWorld_Chronicle_SourceUnknown}from someone").ToString();
                    _provenanceText = hopText + " · " + unknown;
                }
            }
        }

        internal static string? NameOf(string? heroId, HeroLookup? heroLookup)
        {
            if (string.IsNullOrEmpty(heroId)) return null;
            string? name = heroLookup?.Get(heroId!)?.Name?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                name = Hero.Find(heroId!)?.Name?.ToString();
            }
            return string.IsNullOrEmpty(name) ? null : name;
        }

        [DataSourceProperty]
        public string LineText
        {
            get => _lineText;
            set
            {
                if (value != _lineText)
                {
                    _lineText = value;
                    OnPropertyChangedWithValue(value, nameof(LineText));
                }
            }
        }

        [DataSourceProperty]
        public string ProvenanceText
        {
            get => _provenanceText;
            set
            {
                if (value != _provenanceText)
                {
                    _provenanceText = value;
                    OnPropertyChangedWithValue(value, nameof(ProvenanceText));
                }
            }
        }

        public void ExecuteLink(string link)
        {
            ChronicleWindowManager.OpenLink(link);
        }
    }
}

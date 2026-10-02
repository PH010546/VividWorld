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
        private MBBindingList<ChronicleSourceVM> _sources;

        internal ChronicleEntryVM(ChronicleEntry entry, PresentationConfig? cfg, HeroLookup? heroLookup)
        {
            _sources = new MBBindingList<ChronicleSourceVM>();

            if (entry == null)
            {
                _headlineText = string.Empty;
                _dayText = string.Empty;
                return;
            }

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

            foreach (var source in entry.Sources)
            {
                _sources.Add(new ChronicleSourceVM(entry, source, cfg));
            }

            entry.DayLabel = _dayText;
            entry.SourceHeroName = ChronicleSourceVM.NameOf(entry.SourceHeroId, heroLookup);
        }

        /// <summary>
        /// 算出這一筆的標題：俘虜類的消息帶上被抓的人的名字，名字查不到（人已不在遊戲裡、紀錄裡沒記是誰）
        /// 或目前語言沒有帶名字的那一句，就退回原本不帶名字的標題。標題是純文字，不放百科連結。
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

            var renderResult = FallbackTextRenderer.RenderBoth(source.Body, cfg);
            string line = linksEnabled ? renderResult.DisplayText : renderResult.PlainText;
            if (!linksEnabled && line.IndexOf("<a ", StringComparison.Ordinal) >= 0)
            {
                ModLog.Warn($"Chronicle: body of {entry.EventId} still carries link markup while encyclopedia links are disabled.");
            }

            bool hasTeller = !string.IsNullOrEmpty(source.HeroId);
            string tellerName = hasTeller
                ? FallbackTextRenderer.ResolveVar("hero:" + source.HeroId, linksEnabled)
                : string.Empty;

            if (source.HasSpokenLine)
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

            if (source.Hop <= 0)
            {
                _provenanceText = new TextObject("{=VividWorld_Chronicle_HopZero}you were there").ToString();
            }
            else
            {
                string hopText = new TextObject("{=VividWorld_Chronicle_Hop}at a remove of {HOPS}")
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

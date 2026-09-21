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
        private string _bodyText;
        private string _sourceText;
        private string _hopText;

        internal ChronicleEntryVM(ChronicleEntry entry, PresentationConfig? cfg, HeroLookup? heroLookup)
        {
            if (entry == null)
            {
                _headlineText = string.Empty;
                _dayText = string.Empty;
                _bodyText = string.Empty;
                _sourceText = string.Empty;
                _hopText = string.Empty;
                return;
            }

            _headlineText = FallbackTextRenderer.LocalizedTemplate(entry.HeadlineTextId, entry.HeadlineFallback);

            if (TaleWorlds.CampaignSystem.Campaign.Current != null)
            {
                _dayText = CampaignTime.Days((float)entry.Day).ToString();
            }
            else
            {
                _dayText = "Day " + entry.Day.ToString("0.0", CultureInfo.InvariantCulture);
            }

            _bodyText = FallbackTextRenderer.Render(entry.Body, cfg);

            if (!string.IsNullOrEmpty(entry.SourceHeroId))
            {
                string? name = heroLookup?.Get(entry.SourceHeroId!)?.Name?.ToString();
                if (string.IsNullOrEmpty(name))
                {
                    name = Hero.Find(entry.SourceHeroId!)?.Name?.ToString();
                }
                _sourceText = !string.IsNullOrEmpty(name)
                    ? name!
                    : new TextObject("{=VividWorld_Chronicle_SourceUnknown}from someone").ToString();
            }
            else
            {
                _sourceText = new TextObject("{=VividWorld_Chronicle_SourceUnknown}from someone").ToString();
            }

            if (entry.PlayerHop == 0)
            {
                _hopText = new TextObject("{=VividWorld_Chronicle_HopZero}you were there").ToString();
            }
            else
            {
                _hopText = new TextObject("{=VividWorld_Chronicle_Hop}{HOPS} tellings removed")
                    .SetTextVariable("HOPS", entry.PlayerHop)
                    .ToString();
            }

            entry.DayLabel = _dayText;
            entry.SourceHeroName = _sourceText;
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
        public string BodyText
        {
            get => _bodyText;
            set
            {
                if (value != _bodyText)
                {
                    _bodyText = value;
                    OnPropertyChangedWithValue(value, nameof(BodyText));
                }
            }
        }

        [DataSourceProperty]
        public string SourceText
        {
            get => _sourceText;
            set
            {
                if (value != _sourceText)
                {
                    _sourceText = value;
                    OnPropertyChangedWithValue(value, nameof(SourceText));
                }
            }
        }

        [DataSourceProperty]
        public string HopText
        {
            get => _hopText;
            set
            {
                if (value != _hopText)
                {
                    _hopText = value;
                    OnPropertyChangedWithValue(value, nameof(HopText));
                }
            }
        }
    }
}

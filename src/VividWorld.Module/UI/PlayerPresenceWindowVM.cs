#nullable enable

using System;
using TaleWorlds.Library;
using VividWorld.Core.Config;
using VividWorld.Core.Presentation;

namespace VividWorld.UI
{
    /// <summary>
    /// 玩家入局視窗的資料：標題、描述、選項清單、右側插圖、滑鼠指到的選項說明、底下的確認鈕。
    /// 版面照原版隨機事件視窗，但資料與按鈕全是自己的，不經過原版的事件流程（那條路會把事件記進存檔的冷卻清單）。
    /// </summary>
    public class PlayerPresenceWindowVM : ViewModel
    {
        private readonly Action<string> _onConfirmed;
        private readonly MBBindingList<PlayerPresenceOptionVM> _options = new MBBindingList<PlayerPresenceOptionVM>();
        private PlayerPresenceOptionVM? _hovered;
        private string _title;
        private string _description;
        private string _confirmText;
        private string _illustrationId;
        private string _activeHint = string.Empty;
        private bool _canConfirm;
        private bool _confirmed;

        /// <param name="onConfirmed">玩家按下確認時收到選到的那項的 id；回呼之前視窗已經關了。</param>
        internal PlayerPresenceWindowVM(
            string title,
            string description,
            string confirmText,
            string illustrationId,
            Action<string> onConfirmed)
        {
            _title = title ?? string.Empty;
            _description = description ?? string.Empty;
            _confirmText = confirmText ?? string.Empty;
            _illustrationId = illustrationId ?? string.Empty;
            _onConfirmed = onConfirmed ?? throw new ArgumentNullException(nameof(onConfirmed));
        }

        /// <summary>加一個選項。<paramref name="selected"/> 為真就開窗時已選（旁觀的「知道了」）。</summary>
        internal void AddOption(string id, string description, string hint, bool selected)
        {
            var option = new PlayerPresenceOptionVM(this, id, description, hint);
            _options.Add(option);
            if (selected) Select(option);
            else Refresh();
        }

        internal int OptionCount => _options.Count;

        internal PlayerPresenceOptionVM? SelectedOption
        {
            get
            {
                foreach (var o in _options)
                {
                    if (o.IsSelected) return o;
                }
                return null;
            }
        }

        internal void Select(PlayerPresenceOptionVM option)
        {
            foreach (var o in _options)
            {
                o.IsSelected = ReferenceEquals(o, option);
            }
            Refresh();
        }

        internal void Focus(PlayerPresenceOptionVM option)
        {
            _hovered = option;
            Refresh();
        }

        internal void Unfocus(PlayerPresenceOptionVM option)
        {
            if (ReferenceEquals(_hovered, option)) _hovered = null;
            Refresh();
        }

        private void Refresh()
        {
            var selected = SelectedOption;
            CanConfirm = selected != null;
            ActiveHint = PresenceHintPicker.Pick(_hovered?.Hint, selected?.Hint);
        }

        public void ExecuteConfirm()
        {
            if (_confirmed) return;
            var selected = SelectedOption;
            if (selected == null) return;
            _confirmed = true;
            string id = selected.Id;
            ModLog.Info($"Player presence window: confirmed '{id}'.");
            // 先關窗再回呼：回呼裡才會有人去動時間與數值，視窗不能還開著壓著暫停
            PlayerPresenceWindowManager.Close();
            try
            {
                _onConfirmed(id);
            }
            catch (Exception ex)
            {
                ModLog.Error("Player presence window: confirm callback failed", ex);
            }
        }

        [DataSourceProperty]
        public string Title
        {
            get => _title;
            set
            {
                if (value != _title)
                {
                    _title = value;
                    OnPropertyChangedWithValue(value, nameof(Title));
                }
            }
        }

        [DataSourceProperty]
        public string Description
        {
            get => _description;
            set
            {
                if (value != _description)
                {
                    _description = value;
                    OnPropertyChangedWithValue(value, nameof(Description));
                }
            }
        }

        [DataSourceProperty]
        public string ConfirmText
        {
            get => _confirmText;
            set
            {
                if (value != _confirmText)
                {
                    _confirmText = value;
                    OnPropertyChangedWithValue(value, nameof(ConfirmText));
                }
            }
        }

        [DataSourceProperty]
        public string IllustrationId
        {
            get => _illustrationId;
            set
            {
                if (value != _illustrationId)
                {
                    _illustrationId = value;
                    OnPropertyChangedWithValue(value, nameof(IllustrationId));
                }
            }
        }

        [DataSourceProperty]
        public string ActiveHint
        {
            get => _activeHint;
            set
            {
                if (value != _activeHint)
                {
                    _activeHint = value;
                    OnPropertyChangedWithValue(value, nameof(ActiveHint));
                }
            }
        }

        [DataSourceProperty]
        public bool CanConfirm
        {
            get => _canConfirm;
            set
            {
                if (value != _canConfirm)
                {
                    _canConfirm = value;
                    OnPropertyChangedWithValue(value, nameof(CanConfirm));
                }
            }
        }

        [DataSourceProperty]
        public MBBindingList<PlayerPresenceOptionVM> Options => _options;
    }

    /// <summary>視窗裡的一個選項：點它就選取、滑鼠指著它就把它的說明顯示在右下角。</summary>
    public class PlayerPresenceOptionVM : ViewModel
    {
        private readonly PlayerPresenceWindowVM _owner;
        private string _description;
        private bool _isSelected;

        internal PlayerPresenceOptionVM(PlayerPresenceWindowVM owner, string id, string description, string hint)
        {
            _owner = owner;
            Id = id;
            Hint = hint ?? string.Empty;
            _description = description ?? string.Empty;
        }

        internal string Id { get; }

        internal string Hint { get; }

        [DataSourceProperty]
        public string Description
        {
            get => _description;
            set
            {
                if (value != _description)
                {
                    _description = value;
                    OnPropertyChangedWithValue(value, nameof(Description));
                }
            }
        }

        [DataSourceProperty]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (value != _isSelected)
                {
                    _isSelected = value;
                    OnPropertyChangedWithValue(value, nameof(IsSelected));
                }
            }
        }

        public void ExecuteSelect() => _owner.Select(this);

        public void ExecuteFocus() => _owner.Focus(this);

        public void ExecuteUnfocus() => _owner.Unfocus(this);
    }
}

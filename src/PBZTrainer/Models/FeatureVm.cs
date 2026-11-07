using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PBZTrainer.Models
{
    /// <summary>界面上的单张功能卡片状态。</summary>
    public sealed class FeatureVm : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _valueText;
        private string _hotkey = "";
        private bool _isFavorite;
        private bool _isBusy;
        private bool _isSupported = true;
        private string _reason = "";
        private bool _suspended;

        public FeatureVm(FeatureInfo info)
        {
            Info = info;
            _valueText = info.DefaultValue;
        }

        public FeatureInfo Info { get; }
        public string Id => Info.Id;
        public string Title => Info.Title;
        public string Page => Info.Page;
        public string Description => Info.Description;
        public bool HasValue => Info.HasValue;
        public bool HasToggle => Info.HasToggle;
        public bool IsAction => Info.IsAction;
        public string ActionText => Info.IsAction ? "应用" : "设置快捷键";

        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (_isEnabled != value) { _isEnabled = value; Raise(); Raise(nameof(StateText)); } }
        }

        public string ValueText
        {
            get => _valueText;
            set { if (_valueText != value) { _valueText = value; Raise(); } }
        }

        public double NumericValue
        {
            get
            {
                return double.TryParse(_valueText, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 1.0;
            }
        }

        public string Hotkey
        {
            get => _hotkey;
            set { if (_hotkey != value) { _hotkey = value ?? ""; Raise(); Raise(nameof(HotkeyText)); Raise(nameof(HasHotkey)); } }
        }

        public string HotkeyText => string.IsNullOrEmpty(_hotkey) ? "设置快捷键" : _hotkey;
        public bool HasHotkey => !string.IsNullOrEmpty(_hotkey);

        public bool IsFavorite
        {
            get => _isFavorite;
            set { if (_isFavorite != value) { _isFavorite = value; Raise(); Raise(nameof(FavoriteGlyph)); } }
        }

        public string FavoriteGlyph => _isFavorite ? "★" : "☆";

        public bool IsBusy
        {
            get => _isBusy;
            set { if (_isBusy != value) { _isBusy = value; Raise(); } }
        }

        public bool IsSupported
        {
            get => _isSupported;
            set
            {
                if (_isSupported != value)
                {
                    _isSupported = value;
                    Raise();
                    Raise(nameof(StateText));
                    Raise(nameof(IsUnsupported));
                }
            }
        }

        public bool IsUnsupported => !_isSupported;
        public bool IsSuspended { get => _suspended; set { if (_suspended != value) { _suspended = value; Raise(); } } }

        public string Reason
        {
            get => _reason;
            set
            {
                if (_reason != value)
                {
                    _reason = value ?? "";
                    Raise();
                    Raise(nameof(StateText));
                }
            }
        }

        /// <summary>卡片右上角状态文字。</summary>
        public string StateText
        {
            get
            {
                if (!_isSupported && !string.IsNullOrEmpty(_reason)) return _reason;
                if (IsAction) return _isEnabled ? "已执行" : "";
                return _isEnabled ? "已开启" : "已关闭";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

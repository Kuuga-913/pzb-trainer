using System;
using System.Collections.Generic;
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
        private bool _applied = true;
        private string _extraNote = "";

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
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    Raise();
                    Raise(nameof(StateText));
                    Raise(nameof(ShowNotApplied));
                    Raise(nameof(Tooltip));
                }
            }
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

        /// <summary>桥接回报「已真正写进游戏」。enabled 但 applied=false 表示开关开了却没生效。</summary>
        public bool IsApplied
        {
            get => _applied;
            set
            {
                if (_applied != value)
                {
                    _applied = value;
                    Raise();
                    Raise(nameof(StateText));
                    Raise(nameof(ShowNotApplied));
                }
            }
        }

        /// <summary>额外说明（例如「闪避只能普通闪避，红光不可躲」这类桥接限制）。</summary>
        public string ExtraNote
        {
            get => _extraNote;
            set
            {
                if (_extraNote != value)
                {
                    _extraNote = value ?? "";
                    Raise();
                    Raise(nameof(Tooltip));
                    Raise(nameof(HasExtraNote));
                }
            }
        }

        public bool HasExtraNote => !string.IsNullOrEmpty(_extraNote);

        /// <summary>卡片上是否显示「未生效」提示。</summary>
        public bool ShowNotApplied => _isEnabled && !_applied && !IsAction;
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
                    Raise(nameof(Tooltip));
                }
            }
        }

        /// <summary>卡片右上角状态文字。</summary>
        public string StateText
        {
            get
            {
                if (!_isSupported) return string.IsNullOrEmpty(_reason) ? "不支持" : _reason;
                if (IsAction) return _isEnabled ? "已执行" : "";
                if (!_isEnabled) return "已关闭";
                if (!_applied) return string.IsNullOrEmpty(_reason) ? "已开启 · 未生效" : "未生效：" + _reason;
                return "已开启";
            }
        }

        /// <summary>鼠标悬停时的完整说明（含游戏侧回报的原因）。</summary>
        public string Tooltip
        {
            get
            {
                var parts = new List<string> { Title };
                if (!string.IsNullOrWhiteSpace(Description)) parts.Add(Description);
                if (!string.IsNullOrEmpty(_extraNote)) parts.Add(_extraNote);
                if (!IsSupported) parts.Add("当前版本不支持该功能");
                else if (!_applied && _isEnabled) parts.Add("已写入但未生效" + (string.IsNullOrEmpty(_reason) ? "（该功能可能未适配当前游戏版本）" : "：" + _reason));
                else if (IsSuspended) parts.Add("已挂起（切换场景后会自动恢复）");
                return string.Join("\n", parts);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

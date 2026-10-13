using System;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>WPF Controls/SparkleWallet.xaml.cs: a balance display with an explicit reward cue.
    /// Balance refreshes never imply a reward. WPF's one-shot DoubleAnimations (wave 620 ms, reward
    /// pulse 280 ms, sparkles 520 ms, badge rise 240 ms) and its 1.6 s gain timer run on two BeatLoops
    /// (P82: FrameClock beat, parked while hidden or minimised, no Effects).</summary>
    public partial class SparkleWallet : UserControl
    {
        internal const double WaveSeconds = .62, GainSeconds = 1.6;
        private static readonly (double At, double V)[] WaveKeys =
            { (0, 0), (.10, -17), (.22, 13), (.34, -13), (.47, 8), (.62, 0) }; // SparkleWallet.xaml.cs:175

        private readonly Polygon[] _sparkles = new Polygon[4];
        private readonly BeatLoop _wave, _reward;
        private int _balance;
        private long _rewardShown;
        private double _riseFrom;
        private bool _animate, _particles, _listening;
        public event EventHandler? VisitRequested;

        public SparkleWallet()
        {
            InitializeComponent();
            for (var i = 0; i < _sparkles.Length; i++)
            {
                var sparkle = new Polygon
                {
                    Points = { new(0, -4), new(2.4, 0), new(0, 4), new(-2.4, 0) },
                    Fill = i % 2 == 0 ? Brushes.LightGoldenrodYellow : Brushes.HotPink,
                    Opacity = 0, RenderTransform = new TranslateTransform()
                };
                Canvas.SetLeft(sparkle, 50 + i * 18); Canvas.SetTop(sparkle, 13 + i % 2 * 8);
                SparkleLayer.Children.Add(sparkle); _sparkles[i] = sparkle;
            }
            _wave = new BeatLoop(WaveArm, StepWave);
            _reward = new BeatLoop(this, StepReward);
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty && !IsVisible) { ClosePopup(); StopWave(); ClearReward(); }
            };
            WalletButton.Click += (_, _) => { HelpPopup.IsOpen = false; InvitePopup.IsOpen = !InvitePopup.IsOpen; };
            WalletButton.PointerEntered += (_, _) => Wave();
            WalletButton.PointerExited += (_, _) => StopWave();
            WalletButton.GotFocus += (_, _) => Wave();
            WalletButton.LostFocus += (_, _) => StopWave();
            InviteCard.CloseButton.Click += (_, _) => { ClosePopup(); WalletButton.Focus(); };
            InviteCard.VisitButton.Click += (_, _) => Visit();
            InvitePopup.Opened += (_, _) => InviteCard.VisitButton.Focus();
            InviteCard.AddHandler(KeyDownEvent, (_, e) => Escape(e, WalletButton), RoutingStrategies.Tunnel);
            HelpButton.Click += (_, _) => { InvitePopup.IsOpen = false; HelpPopup.IsOpen = !HelpPopup.IsOpen; };
            HelpCard.HelpLaterButton.Click += (_, _) => { ClosePopup(); HelpButton.Focus(); };
            HelpCard.HelpVisitButton.Click += (_, _) => Visit();
            HelpPopup.Opened += (_, _) => HelpCard.HelpVisitButton.Focus();
            HelpCard.AddHandler(KeyDownEvent, (_, e) => Escape(e, HelpButton), RoutingStrategies.Tunnel);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_listening) { LocalizationManager.Instance.LanguageChanged += OnLanguageChanged; _listening = true; }
            RefreshText();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_listening) { LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged; _listening = false; }
            ClosePopup(); StopWave(); ClearReward();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => RefreshText();

        private void Visit() { ClosePopup(); VisitRequested?.Invoke(this, EventArgs.Empty); }

        private void Escape(KeyEventArgs e, Button returnTo)
        {
            if (e.Key != Key.Escape) return;
            ClosePopup(); returnTo.Focus(); e.Handled = true;
        }

        private RotateTransform ArmTurn => (RotateTransform)WaveArm.RenderTransform!;
        private ScaleTransform BalancePulse => (ScaleTransform)BalanceRow.RenderTransform!;
        private TranslateTransform GainShift => (TranslateTransform)GainBadge.RenderTransform!;
        internal double ArmAngle => ArmTurn.Angle;
        internal int Balance => _balance;
        internal bool InviteOpen => InvitePopup.IsOpen;
        internal bool HelpOpen => HelpPopup.IsOpen;
        internal bool RewardShowing => GainBadge.IsVisible;
        internal bool Waving => _wave.IsRunning;
        internal string GainLabel => GainText.Text ?? "";

        /// <summary>Call for every balance update, including login, restore, spending and rewards.</summary>
        public void SetBalance(int balance)
        {
            _balance = Math.Max(0, balance);
            BalanceText.Text = _balance.ToString("N0", CultureInfo.CurrentCulture);
            AutomationProperties.SetName(WalletButton, Loc.GetF("sparkle_wallet_balance", BalanceText.Text));
        }

        /// <summary>Call only after a real local SP award. Never call from balance/property-change handlers.</summary>
        public void ShowReward(int amount)
        {
            if (amount <= 0 || TopLevel.GetTopLevel(this) is null || !IsEffectivelyVisible) return;
            _rewardShown = Math.Min(int.MaxValue, _rewardShown + amount);
            GainText.Text = Loc.GetF("sparkle_wallet_gain", _rewardShown.ToString("N0", CultureInfo.CurrentCulture));
            GainBadge.IsVisible = true;
            _animate = AnimateInteractions;
            _particles = _animate && AmbientFxCanvas.Env.AllowParticles;
            _riseFrom = _animate ? (AmbientFxCanvas.Env.Level == MotionLevel.Reduced ? 3 : 8) : 0;
            _reward.Stop(); // restart: the 1.6 s window and the cue begin again (WPF _gainTimer.Stop/Start)
            _reward.Start();
        }

        /// <summary>Static strings bind with {loc:Str}; this covers the formatted ones.</summary>
        public void RefreshText()
        {
            var hint = Loc.Get("sparkle_help_tooltip");
            ToolTip.SetTip(WalletButton, hint);
            ToolTip.SetTip(HelpButton, hint);
            AutomationProperties.SetName(HelpButton, Loc.Get("sparkle_help_title"));
            WalletLabel.Text = Loc.Get("sparkle_wallet_label");
            SetBalance(_balance);
            if (_rewardShown > 0) GainText.Text = Loc.GetF("sparkle_wallet_gain", _rewardShown.ToString("N0", CultureInfo.CurrentCulture));
        }

        public void ClosePopup() { InvitePopup.IsOpen = false; HelpPopup.IsOpen = false; }

        private static bool AnimateInteractions =>
            AmbientFxCanvas.Env.AllowTransitions && AmbientFxCanvas.Env.CurrentTier != PerformanceTier.Performance;

        private void Wave()
        {
            StopWave();
            if (AnimateInteractions) _wave.Start();
        }

        private void StepWave(double t)
        {
            if (t >= WaveSeconds) { StopWave(); return; }
            var scale = AmbientFxCanvas.Env.Level == MotionLevel.Reduced ? .45 : 1;
            ArmTurn.Angle = BeatLoop.Keys(t, WaveKeys, sine: true) * scale;
        }

        private void StopWave() { _wave.Stop(); ArmTurn.Angle = 0; }

        private void StepReward(double t)
        {
            if (t >= GainSeconds) { ClearReward(); return; }
            if (!_animate) return;
            var rise = Math.Min(1, t / .24); // QuadraticEase EaseOut, SparkleWallet.xaml.cs:120
            GainShift.Y = _riseFrom * (1 - rise) * (1 - rise);
            // Pulse 1 -> peak (95 ms, quad out) -> 1 (280 ms, sine in-out), SparkleWallet.xaml.cs:191-196.
            var peak = AmbientFxCanvas.Env.Level == MotionLevel.Reduced ? 1.025 : 1.09;
            var s = t < .095 ? 1 + (peak - 1) * (1 - Math.Pow(1 - t / .095, 2))
                : BeatLoop.Keys(t, new[] { (.095, peak), (.28, 1.0) }, sine: true);
            BalancePulse.ScaleX = BalancePulse.ScaleY = s;
            for (var i = 0; i < _sparkles.Length; i++)
            {
                var on = _particles && t < .52; // FillBehavior.Stop: back to 0 after 520 ms
                _sparkles[i].Opacity = on ? BeatLoop.Keys(t, new[] { (0, 0.0), (.07 + i * .02, 1.0), (.52, 0.0) }) : 0;
                ((TranslateTransform)_sparkles[i].RenderTransform!).Y = on ? 3 + (-8 - i % 2 * 4 - 3) * (t / .52) : 0;
            }
        }

        private void ClearReward()
        {
            _reward.Stop(); _rewardShown = 0;
            BalancePulse.ScaleX = BalancePulse.ScaleY = 1;
            foreach (var sparkle in _sparkles) { sparkle.Opacity = 0; ((TranslateTransform)sparkle.RenderTransform!).Y = 0; }
            GainShift.Y = 0; GainBadge.IsVisible = false;
        }
    }
}

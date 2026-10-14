using System;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// Everything one seat of the daily board needs to paint itself. Built by the shell's quest
    /// refresh, which owns the quest/localization/mod lookups; the card owns only how it looks.
    /// Copied from the WPF code-behind with <c>ImageSource</c> -> <see cref="IImage"/>.
    /// </summary>
    internal sealed class DailyQuestCardModel
    {
        public int Slot { get; init; }                  // zero-based
        public string Icon { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public int Current { get; init; }
        public int Target { get; init; }
        public bool IsCompleted { get; init; }
        public string XpText { get; init; } = "";       // "120 XP"; the gift is the XpIcon glyph
        public int StreakBonusPct { get; init; }        // 0 = none; drawn "+N%" + Fire
        public int RerollBonusPct { get; init; }        // 0 = none; drawn "+N%" + ArrowSync
        public IImage? Art { get; init; }
        public bool CanReroll { get; init; }
        public string RerollKey { get; init; } = "";    // btn_reroll_with_count (with RerollCount) / btn_reroll_none
        public int RerollCount { get; init; }
        public string? RerollTooltip { get; init; }
    }

    /// <summary>
    /// One card of the three-up daily board.
    ///
    /// PORTED from ConditioningControlPanel/Views/Controls/DailyQuestCard.xaml.cs. Deviations:
    ///  - <c>MotionFx</c> (HoverLift, BarFill, the reduced-motion gate) lives in the WPF head, so
    ///    the card runs the same curves on its own stepped timer (MOTION CLOCK below), and the
    ///    hover glow is the Root border's BoxShadow rather than a DropShadowEffect.
    ///  - <c>FrameworkElement</c> -> <see cref="Control"/>, <c>Visibility</c> -> <c>IsVisible</c>,
    ///    <c>MouseEnter/Leave</c> -> <c>PointerEntered/Exited</c>, <c>ToolTip=</c> ->
    ///    <c>ToolTip.SetTip</c>, <c>App.Logger</c> -> Serilog's static <c>Log</c>.
    ///  - The reroll caption goes on an inner TextBlock, not <c>Content</c>: Avalonia would read a
    ///    "_" in it as an access key.
    ///  - The parameterless constructor seeds a sample seat so <c>--render-all</c> draws a card
    ///    with real text rather than an empty frame. Production callers overwrite it with
    ///    <see cref="Apply"/>, exactly as the WPF original does.
    ///
    /// <para><b>The bar belongs to the card.</b> Apply can run before the tab has ever been
    /// measured, and a bar cannot be filled against a zero-width track, so each card remembers its
    /// own fraction and re-applies it on its track's SizeChanged.</para>
    /// </summary>
    public partial class DailyQuestCard : UserControl
    {
        /// <summary>Raised when this seat's reroll button is pressed. The shell spends the reroll;
        /// the card never touches quest state.</summary>
        internal event EventHandler? RerollRequested;

        /// <summary>Zero-based seat index, echoed back on <see cref="RerollRequested"/> by way of
        /// the sender. Set by <see cref="Apply"/>.</summary>
        internal int Slot { get; private set; }

        /// <summary>Exposed so a burst effect can anchor at the cap of the bar that just filled.</summary>
        internal Control ProgressTrack => _track;
        internal Control ProgressFill => _fill;

        private readonly Border _root;
        private readonly Border _track;
        private readonly Border _fill;
        private readonly Border _completedOverlay;
        private readonly TextBlock _txtSlot, _txtName, _txtDesc;
        private readonly TextBlock _txtXp, _txtXpBonus, _txtProgress, _txtRemaining;
        private readonly IconGlyph _questIcon, _xpIcon;
        private readonly IconLabel _reroll;
        private readonly Image _art;
        private readonly Button _btnReroll;

        private double _fraction;
        private bool _hasFraction;

        private static readonly IBrush GoldFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly IBrush DoneFill = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
        private static readonly IBrush RestBorder = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));
        private static readonly IBrush LiveBorder = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xD7, 0x00));
        private static readonly IBrush DoneBorder = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0xE6, 0x76));

        public DailyQuestCard()
        {
            AvaloniaXamlLoader.Load(this);

            _root = this.FindControl<Border>("Root")!;
            _track = this.FindControl<Border>("Track")!;
            _fill = this.FindControl<Border>("Fill")!;
            _completedOverlay = this.FindControl<Border>("CompletedOverlay")!;
            _txtSlot = this.FindControl<TextBlock>("TxtSlot")!;
            _questIcon = this.FindControl<IconGlyph>("QuestIcon")!;
            _xpIcon = this.FindControl<IconGlyph>("XpIcon")!;
            _txtName = this.FindControl<TextBlock>("TxtName")!;
            _txtDesc = this.FindControl<TextBlock>("TxtDesc")!;
            _txtXp = this.FindControl<TextBlock>("TxtXp")!;
            _txtXpBonus = this.FindControl<TextBlock>("TxtXpBonus")!;
            _txtProgress = this.FindControl<TextBlock>("TxtProgress")!;
            _txtRemaining = this.FindControl<TextBlock>("TxtRemaining")!;
            _reroll = this.FindControl<IconLabel>("RerollLabel")!;
            _art = this.FindControl<Image>("Art")!;
            _btnReroll = this.FindControl<Button>("BtnReroll")!;

            _track.SizeChanged += (_, _) => ApplyFraction(animate: false);
            _root.RenderTransform = _liftScale;   // WPF EnsureScale; Root has no transform of its own
            _root.PointerEntered += OnCardPointerEntered;
            _root.PointerExited += OnCardPointerExited;
            _btnReroll.Click += (_, _) => RerollRequested?.Invoke(this, EventArgs.Empty);

            // Render/design seat: sample data so the headless proof draws every string and a
            // partially filled bar. Overwritten by the first Apply.
            Apply(new DailyQuestCardModel
            {
                Slot = 0,
                Icon = QuestDefinition.DailyQuests[0].Icon,
                Name = "Surrender",
                Description = "Complete 5 minutes of guided breathing.",
                Current = 3,
                Target = 5,
                XpText = "120 XP",
                StreakBonusPct = 9,
                CanReroll = true,
                RerollKey = "btn_reroll_with_count",
                RerollCount = 1,
                RerollTooltip = "1 reroll left today",
            });
        }

        // ---- PAINT ---------------------------------------------------------------

        /// <summary>Paint this seat from live quest state.</summary>
        internal void Apply(DailyQuestCardModel m)
        {
            Slot = m.Slot;
            IsVisible = true;
            Opacity = 1.0;

            _txtSlot.Text = (m.Slot + 1).ToString();
            _questIcon.Emoji = m.Icon;
            _questIcon.IsVisible = !string.IsNullOrEmpty(m.Icon);
            _txtName.Text = m.Name;
            _txtDesc.Text = m.Description;
            _txtDesc.IsVisible = !string.IsNullOrWhiteSpace(m.Description);

            _txtXp.Text = m.XpText;
            _xpIcon.IsVisible = true;
            _txtXpBonus.IsVisible = SetBonus(_txtXpBonus, m.StreakBonusPct, m.RerollBonusPct);

            if (m.Art != null) _art.Source = m.Art;

            // A stamped seat reads 100% whatever its stored counter says: a quest completed by a
            // tracker that overshot its target (minutes arrive in whole-minute lumps) would
            // otherwise paint a bar that is not quite full under a green check.
            double fraction = m.IsCompleted ? 1.0
                            : m.Target > 0 ? Math.Max(0, Math.Min(1.0, (double)m.Current / m.Target))
                            : 0;

            int current = m.IsCompleted ? Math.Max(m.Current, m.Target) : m.Current;
            _txtProgress.Text = $"{current} / {m.Target}";

            if (m.IsCompleted)
            {
                _completedOverlay.IsVisible = true;
                _root.BorderBrush = DoneBorder;
                _fill.Background = DoneFill;
                _txtProgress.Foreground = DoneFill;
                // The separator lives in this string rather than in a third TextBlock, so an empty
                // remainder leaves no orphaned dot on the line.
                _txtRemaining.Text = "· done";
            }
            else
            {
                _completedOverlay.IsVisible = false;
                _root.BorderBrush = LiveBorder;
                _fill.Background = GoldFill;
                _txtProgress.Foreground = GoldFill;
                int left = Math.Max(0, m.Target - m.Current);
                _txtRemaining.Text = left > 0 ? $"· {left} to go" : "";
            }

            _reroll.Args = m.RerollKey == "btn_reroll_with_count" ? new object[] { m.RerollCount } : null;
            _reroll.Key = m.RerollKey;
            _btnReroll.IsEnabled = m.CanReroll;
            _btnReroll.IsVisible = !m.IsCompleted;
            ToolTip.SetTip(_btnReroll, m.RerollTooltip);

            SetFraction(fraction);
        }

        /// <summary>WPF's "+9%🔥 +10%🔃" multipliers: the text plus Fluent Fire / ArrowSync inline.
        /// Shared with the weekly card. False (and empty) when neither applies.</summary>
        internal static bool SetBonus(TextBlock t, int streakPct, int rerollPct, string open = "", string close = "")
        {
            var inlines = new InlineCollection();
            void Add(int pct, IconKind kind, IconVariant v, string? brush)
            {
                if (pct <= 0) return;
                if (inlines.Count > 0) inlines.Add(new Run(" "));
                inlines.Add(new Run($"{open}+{pct}%"));
                inlines.Add(new InlineUIContainer(IconGlyph.Of(kind, v, brush, t.FontSize)) { BaselineAlignment = BaselineAlignment.Center });
                if (close.Length > 0) inlines.Add(new Run(close));
            }
            Add(streakPct, IconKind.Fire, IconVariant.Filled, IconBrushes.Fire);
            Add(rerollPct, IconKind.ArrowSync, IconVariant.Regular, null);
            t.Inlines = inlines;
            return inlines.Count > 0;
        }

        /// <summary>
        /// A seat the pool could not fill. Rare (it needs every legal quest to be excluded or
        /// locked), but it must render as an obviously empty seat rather than as a broken card.
        /// </summary>
        internal void ShowEmpty(int slot, string title, string subtitle)
        {
            Slot = slot;
            IsVisible = true;
            Opacity = 0.55;

            _txtSlot.Text = (slot + 1).ToString();
            _questIcon.IsVisible = false;
            _xpIcon.IsVisible = false;
            _txtName.Text = title;
            _txtDesc.Text = subtitle;
            _txtDesc.IsVisible = true;
            _txtXp.Text = "";
            _txtXpBonus.IsVisible = false;
            _art.Source = null;
            _completedOverlay.IsVisible = false;
            _root.BorderBrush = RestBorder;
            _txtProgress.Text = "";
            _txtRemaining.Text = "";
            _btnReroll.IsVisible = false;

            SetFraction(0);
        }

        // ---- BAR -----------------------------------------------------------------

        private void SetFraction(double fraction)
        {
            if (double.IsNaN(fraction)) fraction = 0;
            _fraction = Math.Max(0, Math.Min(1, fraction));
            _hasFraction = true;
            ApplyFraction(animate: true);
        }

        /// <summary>
        /// Seat the fill against the track's CURRENT width. Called both from Apply (which can run
        /// before the tab has ever been measured, in which case the track is 0 wide and this is a
        /// no-op) and from the track's SizeChanged, which is what finally lands it. A resize is not
        /// progress, so that path never tweens. Progress tweens as WPF MotionFx.BarFill: 0.6 s
        /// QuadraticEase-out, snapped at MotionLevel.Off.
        /// </summary>
        private void ApplyFraction(bool animate)
        {
            try
            {
                if (!_hasFraction) return;
                double available = _track.Bounds.Width;
                if (available <= 0) return;

                double to = available * _fraction;
                double from = double.IsNaN(_fill.Width) ? 0 : _fill.Width;

                if (!animate || !AllowTransitions())
                {
                    _bar.Active = false;
                    _fill.Width = to;
                    return;
                }

                if (Math.Abs(to - from) < 0.5) return;
                Start(_bar, from, to, BarMs, quadOut: true);
            }
            catch (Exception ex) { Log.Debug("DailyQuestCard bar: {E}", ex.Message); }
        }

        // ---- INTERACTION ---------------------------------------------------------

        // WPF: MotionFx.HoverLift(Root) - scale to 1.02 over 150 ms QuadraticEase-out, snapped (not
        // skipped) at Off - plus a gold glow that blooms to blur 18 / opacity 0.5 over 180 ms and
        // fades over 160 ms, skipped at Off. WPF's glow is a DropShadowEffect attached only while
        // hovered; here it is the Root border's own BoxShadow (no Effect on a tile, no offscreen
        // layer), cleared once the fade lands so nothing is drawn at rest - the same contract.
        private void OnCardPointerEntered(object? sender, PointerEventArgs e)
        {
            try
            {
                if (!AllowTransitions()) { _lift.Active = false; SetLift(HoverLiftScale); return; }
                Start(_lift, _liftScale.ScaleX, HoverLiftScale, HoverMs, quadOut: true);
                Start(_glow, _glowLevel, 1, GlowInMs, quadOut: false);
            }
            catch (Exception ex) { Log.Debug("DailyQuestCard hover in: {E}", ex.Message); }
        }

        private void OnCardPointerExited(object? sender, PointerEventArgs e)
        {
            try
            {
                if (!AllowTransitions())
                {
                    _lift.Active = _glow.Active = false;
                    SetLift(1.0);
                    SetGlow(0);
                    return;
                }
                Start(_lift, _liftScale.ScaleX, 1.0, HoverMs, quadOut: true);
                Start(_glow, _glowLevel, 0, GlowOutMs, quadOut: false);
            }
            catch (Exception ex) { Log.Debug("DailyQuestCard hover out: {E}", ex.Message); }
        }

        // ---- MOTION CLOCK --------------------------------------------------------

        // ponytail: one FrameClock per card (frame-locked to the window's shared frame source, 16 ms
        // like WPF's render-rate DoubleAnimation), running only while a tween is in flight and
        // stopped when the last one lands: these are 150-600 ms interaction tweens, not an ambient
        // loop, so no BeatLoop. RunAsync is out: it cannot target a Transform.
        private const double HoverLiftScale = 1.02;
        private const double HoverMs = 150, GlowInMs = 180, GlowOutMs = 160, BarMs = 600;
        private const double GlowBlur = 18, GlowOpacity = 0.5;

        /// <summary>The animation clock. Tests swap in a stepped clock and call <see cref="Step"/>.</summary>
        internal static TimeProvider Time = TimeProvider.System;

        private sealed class Tween
        {
            public double From, To, Ms;
            public long Started;
            public bool Active, QuadOut;

            public double Sample()
            {
                double t = Math.Min(Time.GetElapsedTime(Started).TotalMilliseconds / Ms, 1);
                if (t >= 1) Active = false;
                return From + (To - From) * (QuadOut ? 1 - (1 - t) * (1 - t) : t);
            }
        }

        private readonly Tween _lift = new(), _glow = new(), _bar = new();
        private readonly ScaleTransform _liftScale = new(1, 1);
        private double _glowLevel;
        private FrameClock? _clock;

        private static bool AllowTransitions()
        {
            try { return AmbientFxCanvas.Env.AllowTransitions; }
            catch { return true; }   // settings not up yet: motion on, as WPF
        }

        private void Start(Tween tw, double from, double to, double ms, bool quadOut)
        {
            (tw.From, tw.To, tw.Ms, tw.QuadOut) = (from, to, ms, quadOut);
            tw.Started = Time.GetTimestamp();
            tw.Active = true;
            if (_clock == null)
            {
                _clock = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(16) };
                _clock.Tick += (_, _) => Step();
            }
            _clock.Start();
        }

        /// <summary>One frame at <see cref="Time"/>'s now; stops the timer once nothing is in flight.</summary>
        internal void Step()
        {
            try
            {
                if (_lift.Active) SetLift(_lift.Sample());
                if (_glow.Active) SetGlow(_glow.Sample());
                if (_bar.Active) _fill.Width = Math.Max(0, _bar.Sample());
            }
            catch (Exception ex) { _lift.Active = _glow.Active = _bar.Active = false; Log.Debug("DailyQuestCard motion: {E}", ex.Message); }
            if (!_lift.Active && !_glow.Active && !_bar.Active) _clock?.Stop();
        }

        private void SetLift(double scale) => _liftScale.ScaleX = _liftScale.ScaleY = scale;

        private void SetGlow(double level)
        {
            _glowLevel = Math.Max(0, Math.Min(1, level));
            // Faded out: detach, so a resting card carries no shadow at all.
            _root.BoxShadow = _glowLevel <= 0 ? default : new BoxShadows(new BoxShadow
            {
                Blur = GlowBlur * _glowLevel,
                Color = Color.FromArgb((byte)Math.Round(255 * GlowOpacity * _glowLevel), 0xFF, 0xD7, 0x00),
            });
        }
    }
}

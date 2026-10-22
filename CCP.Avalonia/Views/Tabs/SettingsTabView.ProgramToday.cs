using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The dashboard's Training Programs "Today" card: WPF MainWindow.ProgramsTab.cs
    /// RefreshProgramTodayCard (:2251-2318) and MainWindow.ProgramBanner.cs (banner art, fog, sheen,
    /// sparkles, accent breath, hover parallax). Read-only: it only reads App.Programs. The ambient
    /// loops run only while the card is on screen and MotionFx.AllowAmbientLoops (P01), on one 30 fps
    /// BeatLoop, and stop on detach (P65).
    /// </summary>
    public partial class SettingsTabView
    {
        /// <summary>WPF ProgramBanner.cs: art opacity and hover travel.</summary>
        private const double ProgramBannerArtOpacity = 0.45;
        private const double ProgramBannerParallax = 10;

        private ProgramService? _todaySubscribed;
        private BeatLoop? _programBannerLoop;
        private int _programBannerNudge;   // bumped to cancel a running nudge
        private bool _programBannerArtOn;

        /// <summary>True while the fog/sheen/sparkle/breath loops run (tests read it).</summary>
        internal bool ProgramBannerFxRunning => _programBannerLoop != null;

        /// <summary>Constructor hook.</summary>
        private void InitProgramTodayCard()
        {
            // WPF ProgramTodayCard_Click -> ShowTab("programs") (ProgramsTab.cs:2245).
            ProgramTodayCard.Click += (_, _) => Shell?.ShowTab("programs");
            // WPF ProgramBannerCard_MouseEnter/Leave -> NudgeProgramBannerArt.
            ProgramTodayCard.PointerEntered += (_, _) => NudgeProgramBannerArt(-ProgramBannerParallax);
            ProgramTodayCard.PointerExited += (_, _) => NudgeProgramBannerArt(0);
            ProgramTodayDecor.SizeChanged += (_, _) => { LayoutProgramBannerDecor(); SyncProgramBannerFx(); };
            ProgramTodayCard.PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) SyncProgramBannerFx(); };
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) { if (IsVisible) RefreshProgramTodayCard(); SyncProgramBannerFx(); } };
            AttachedToVisualTree += (_, _) =>
            {
                SubscribeProgramToday();
                AmbientFxCanvas.Env.MotionGateChanged += SyncProgramBannerFx;
                LocalizationManager.Instance.LanguageChanged += OnProgramTodayLanguageChanged;
                CoreMods.ModChanged += OnProgramTodayModChanged;   // WPF ProgramsTab.cs:114 -> :471
                RefreshProgramTodayCard();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                UnsubscribeProgramToday();
                AmbientFxCanvas.Env.MotionGateChanged -= SyncProgramBannerFx;
                LocalizationManager.Instance.LanguageChanged -= OnProgramTodayLanguageChanged;
                CoreMods.ModChanged -= OnProgramTodayModChanged;
                StopProgramBannerFx();
                _programBannerNudge++;
            };
        }

        private void OnProgramTodayLanguageChanged(object? sender, EventArgs e) => RefreshProgramTodayCard();
        private void OnProgramTodayModChanged(object? sender, ConditioningControlPanel.Models.ModPackage e) => OnProgramTodayChanged(sender, EventArgs.Empty);

        // WPF MarshalProgramRefresh (:164-190): TodayChanged / Lapsed / Graduated repaint the card even
        // behind a hidden tab - a handful of property writes.
        private void SubscribeProgramToday()
        {
            var svc = AvApp.Programs;
            if (ReferenceEquals(svc, _todaySubscribed)) return;
            UnsubscribeProgramToday();
            if (svc == null) return;
            svc.TodayChanged += OnProgramTodayChanged;
            svc.ProgramLapsed += OnProgramTodayChanged;
            svc.ProgramGraduated += OnProgramTodayChanged;
            _todaySubscribed = svc;
        }

        private void UnsubscribeProgramToday()
        {
            if (_todaySubscribed == null) return;
            _todaySubscribed.TodayChanged -= OnProgramTodayChanged;
            _todaySubscribed.ProgramLapsed -= OnProgramTodayChanged;
            _todaySubscribed.ProgramGraduated -= OnProgramTodayChanged;
            _todaySubscribed = null;
        }

        private void OnProgramTodayChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            if (VisualRoot is not null) RefreshProgramTodayCard();
        });

        /// <summary>WPF RefreshProgramTodayCard (ProgramsTab.cs:2251): collapsed when no program runs.</summary>
        internal void RefreshProgramTodayCard()
        {
            try
            {
                if (VisualRoot is not null) SubscribeProgramToday();   // a detached view never re-subscribes
                var svc = AvApp.Programs;
                var enrollment = svc?.ActiveEnrollment;
                var program = svc?.ActiveProgram;
                var day = svc?.Today;
                var record = svc?.TodayRecord;

                if (svc == null || enrollment == null || program == null || day == null || record == null ||
                    enrollment.State is ProgramEnrollmentState.Withdrawn or ProgramEnrollmentState.Graduated)
                {
                    ProgramTodayCard.IsVisible = false;
                    return;
                }

                var accent = Windows.MainShellWindow.AccentBrush(program.AccentColor);
                ProgramTodayCard.BorderBrush = accent;
                ProgramTodayAccent.Background = accent;
                TxtProgramTodayTitle.Foreground = accent;
                TxtProgramTodayTitle.Text = program.Title;

                string remainder;
                if (enrollment.State == ProgramEnrollmentState.Paused)
                    remainder = Loc.Get("programs_card_paused");
                else if (enrollment.State == ProgramEnrollmentState.Lapsed)
                    remainder = Loc.Get("programs_card_lapsed");
                else
                {
                    var parts = new List<string>();
                    if (!record.SessionCompleted) parts.Add(Loc.Get("programs_card_session_left"));
                    var tasksLeft = svc.RequiredTasks(day).Count(t => !svc.IsTaskComplete(record, t));
                    if (tasksLeft == 1) parts.Add(Loc.Get("programs_card_task_left_one"));
                    else if (tasksLeft > 1) parts.Add(Loc.GetF("programs_card_task_left_many", tasksLeft));
                    remainder = parts.Count == 0
                        ? Loc.Get("programs_card_all_done")
                        : Loc.GetF("programs_card_remaining", string.Join(", ", parts));
                }

                TxtProgramTodayLine.Text = string.Join("  ·  ", Loc.GetF("programs_card_day", enrollment.CurrentDay), day.Title, remainder);

                ApplyProgramBannerArt(program, accent);
                ProgramTodayCard.IsVisible = true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "RefreshProgramTodayCard failed");
            }
        }

        /// <summary>WPF ApplyProgramBannerArt (ProgramBanner.cs:71): art (incl. the default banner),
        /// accent fog, sheen and sparkles; the loops start when the card is on screen.</summary>
        internal void ApplyProgramBannerArt(ProgramDefinition program, IBrush accent)
        {
            var color = (accent as ISolidColorBrush)?.Color ?? Color.Parse("#FFFF69B4");
            var art = ModArt.FirstOf(ProgramArtPaths.Banner(program));
            _programBannerArtOn = art != null;
            ProgramTodayArt.Fill = art == null ? null : new ImageBrush(art)
            {
                Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center
            };
            ProgramTodayArt.Opacity = ProgramBannerArtOpacity;
            ProgramTodayArt.IsVisible = _programBannerArtOn;

            ProgramTodayFog.Fill = Gradient((WithAlpha(color, 0x00), 0), (WithAlpha(color, 0x22), 0.24),
                (WithAlpha(color, 0x00), 0.48), (WithAlpha(color, 0x18), 0.74), (WithAlpha(color, 0x00), 1));
            ProgramTodaySheen.Fill = Gradient((WithAlpha(Colors.White, 0x00), 0), (WithAlpha(Colors.White, 0x1A), 0.5),
                (WithAlpha(Colors.White, 0x00), 1));
            BuildProgramBannerSparkles(color);
            ProgramTodayDecor.IsVisible = true;
            LayoutProgramBannerDecor();
            SyncProgramBannerFx();
        }

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static Color TowardWhite(Color c, double amount)
        {
            byte Mix(byte v) => (byte)Math.Round(v + (255 - v) * amount);
            return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
        }

        private static IBrush Gradient(params (Color c, double at)[] stops)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            };
            foreach (var (c, at) in stops) brush.GradientStops.Add(new GradientStop(c, at));
            return brush;
        }

        /// <summary>WPF LayoutProgramBannerDecor: rounded clip (radius 9 = border 10 - 1), fog 1.6x wide.</summary>
        private void LayoutProgramBannerDecor()
        {
            var w = ProgramTodayDecor.Bounds.Width;
            var h = ProgramTodayDecor.Bounds.Height;
            if (w < 1 || h < 1) { ProgramTodayDecor.Clip = null; return; }
            ProgramTodayDecor.Clip = new RectangleGeometry(new Rect(0, 0, w, h), 9, 9);
            ProgramTodayFog.Width = w * 1.6;
            double[] fx = { 0.52, 0.63, 0.72, 0.84, 0.93 };
            double[] fy = { 0.30, 0.68, 0.22, 0.56, 0.40 };
            for (var i = 0; i < ProgramTodaySparkles.Children.Count && i < fx.Length; i++)
            {
                Canvas.SetLeft(ProgramTodaySparkles.Children[i], w * fx[i]);
                Canvas.SetTop(ProgramTodaySparkles.Children[i], h * fy[i]);
            }
        }

        private void BuildProgramBannerSparkles(Color accent)
        {
            if (ProgramTodaySparkles.Children.Count == 0)
                foreach (var size in new double[] { 4, 3, 5, 3, 4 })
                    ProgramTodaySparkles.Children.Add(new Ellipse { Width = size, Height = size, Opacity = 0, IsHitTestVisible = false });
            var fill = new SolidColorBrush(TowardWhite(accent, 0.55));
            foreach (var dot in ProgramTodaySparkles.Children.OfType<Ellipse>()) dot.Fill = fill;
        }

        /// <summary>Start when every gate is open, stop when any closes (WPF Start/StopProgramBannerFx,
        /// hooked to IsVisibleChanged; Avalonia hides the tab, not the card, so both are read).</summary>
        private void SyncProgramBannerFx()
        {
            var want = VisualRoot is not null && IsVisible && ProgramTodayCard.IsVisible && ProgramTodayDecor.IsVisible
                       && AmbientFxCanvas.Env.AllowAmbientLoops && ProgramTodayDecor.Bounds.Width >= 80;
            if (!want) StopProgramBannerFx();
            else if (_programBannerLoop == null) StartProgramBannerFx();
        }

        /// <summary>WPF StartProgramBannerFx (ProgramBanner.cs:290-340, 445-458): fog, sheen, sparkles and
        /// accent breath, all on one 30 fps beat (WPF SetDesiredFrameRate(AmbientFrameRate = 30)), never an
        /// infinite Animation (P82). The card's width is read every frame, so a resize stays correct.</summary>
        private void StartProgramBannerFx()
        {
            ProgramTodayFog.IsVisible = ProgramTodaySheen.IsVisible = ProgramTodaySparkles.IsVisible = true;
            var fog = Slide(ProgramTodayFog);
            var sheen = Slide(ProgramTodaySheen);
            var dots = ProgramTodaySparkles.Children.OfType<Ellipse>().ToArray();
            _programBannerLoop = new BeatLoop(ProgramTodayCard, t =>
            {
                var w = ProgramTodayDecor.Bounds.Width;
                fog.X = -w * 0.55 * BeatLoop.Breath(t, 26);              // FX 1: 26 s each way, sine in-out
                sheen.X = SheenAt(t % 11, w);                            // FX 2: 1.7 s across, rest of 11 s paused
                for (var i = 0; i < dots.Length; i++)                    // FX 3: staggered 1.15 s (BeginTime)
                    dots[i].Opacity = TwinkleAt(t - i * 1.15);
                ProgramTodayAccent.Opacity = 0.55 + 0.45 * BeatLoop.Breath(t, 3.4);   // FX 4: 3.4 s each way
            });
            _programBannerLoop.Start();
        }

        /// <summary>WPF sweep keyframes (:315-319): linear -160 -> w+60 by 1.7 s, held, discrete back at 1.75 s.</summary>
        internal static double SheenAt(double t, double w) =>
            t < 1.7 ? -160 + (w + 220) * t / 1.7 : t < 1.75 ? w + 60 : -160;

        /// <summary>WPF twinkle keyframes (:445-457): 0 -> 0.9 by 0.45 s (QuadraticEase out), -> 0 by 1.3 s
        /// linear, dark to 6 s; nothing before its BeginTime.</summary>
        internal static double TwinkleAt(double t)
        {
            if (t < 0) return 0;
            t %= 6;
            if (t < 0.45) { var p = 1 - t / 0.45; return 0.9 * (1 - p * p); }
            return t < 1.3 ? 0.9 * (1.3 - t) / 0.85 : 0;
        }

        private void StopProgramBannerFx()
        {
            if (_programBannerLoop == null) return;
            _programBannerLoop.Stop();
            _programBannerLoop = null;
            ProgramTodayFog.IsVisible = false;
            ProgramTodaySheen.IsVisible = false;
            ProgramTodaySparkles.IsVisible = false;
            ProgramTodayAccent.Opacity = 1;
            _programBannerNudge++;
            Slide(ProgramTodayArt).X = 0;
        }

        private static TranslateTransform Slide(Visual v)
        {
            if (v.RenderTransform is TranslateTransform t) return t;
            v.RenderTransform = t = new TranslateTransform();
            return t;
        }

        /// <summary>WPF NudgeProgramBannerArt (:478-490): 260 ms quadratic ease-out slide of the art, per
        /// rendered frame (an Animation cannot run on a Transform, P82). A newer nudge or a stop cancels it.</summary>
        private void NudgeProgramBannerArt(double to)
        {
            if (!_programBannerArtOn || !ProgramTodayArt.IsVisible) return;
            var slide = Slide(ProgramTodayArt);
            var from = slide.X;   // from where it is, so leaving mid-slide eases back
            var run = ++_programBannerNudge;
            var top = TopLevel.GetTopLevel(this);
            if (!AmbientFxCanvas.Env.AllowTransitions || top == null) { slide.X = to; return; }
            var start = BeatLoop.Time.GetTimestamp();   // elapsed on the beat's clock: a test steps it (P08)
            void Frame(TimeSpan _)
            {
                if (run != _programBannerNudge) return;
                var p = Math.Min(1, BeatLoop.Time.GetElapsedTime(start).TotalMilliseconds / 260);
                slide.X = from + (to - from) * (1 - (1 - p) * (1 - p));
                if (p < 1) top.RequestAnimationFrame(Frame);
            }
            top.RequestAnimationFrame(Frame);
        }
    }
}

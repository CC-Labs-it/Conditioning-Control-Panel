using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
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
    /// loops run only while the card is on screen and MotionFx.AllowAmbientLoops (P01), each on one
    /// CancellationTokenSource, and stop on detach (P65).
    /// </summary>
    public partial class SettingsTabView
    {
        /// <summary>WPF ProgramBanner.cs: art opacity and hover travel.</summary>
        private const double ProgramBannerArtOpacity = 0.45;
        private const double ProgramBannerParallax = 10;

        private ProgramService? _todaySubscribed;
        private CancellationTokenSource? _programBannerCts;
        private CancellationTokenSource? _programBannerNudgeCts;
        private bool _programBannerArtOn;

        /// <summary>True while the fog/sheen/sparkle/breath loops run (tests read it).</summary>
        internal bool ProgramBannerFxRunning => _programBannerCts != null;

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
                RefreshProgramTodayCard();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                UnsubscribeProgramToday();
                AmbientFxCanvas.Env.MotionGateChanged -= SyncProgramBannerFx;
                LocalizationManager.Instance.LanguageChanged -= OnProgramTodayLanguageChanged;
                StopProgramBannerFx();
                _programBannerNudgeCts?.Cancel();
            };
        }

        private void OnProgramTodayLanguageChanged(object? sender, EventArgs e) => RefreshProgramTodayCard();

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
                SubscribeProgramToday();
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
            else if (_programBannerCts == null) StartProgramBannerFx();
        }

        private void StartProgramBannerFx()
        {
            try
            {
                var w = ProgramTodayDecor.Bounds.Width;
                var token = (_programBannerCts = new CancellationTokenSource()).Token;

                // FX 1: drifting fog, 26 s each way, sine in-out.
                ProgramTodayFog.IsVisible = true;
                _ = Loop(TimeSpan.FromSeconds(26), PlaybackDirection.Alternate, TranslateTransform.XProperty,
                        (0, 0d), (1, -w * 0.55)).RunAsync(Slide(ProgramTodayFog), token);

                // FX 2: sheen sweep - 1.7 s across, then the flat tail of the 11 s cycle is the pause.
                ProgramTodaySheen.IsVisible = true;
                _ = Linear(Loop(TimeSpan.FromSeconds(11), PlaybackDirection.Normal, TranslateTransform.XProperty,
                        (0, -160d), (1.7 / 11, w + 60), (1.75 / 11, -160d), (1, -160d))).RunAsync(Slide(ProgramTodaySheen), token);

                // FX 3: sparkles - 0.45 s in, 0.85 s out, dark for the rest of 6 s, staggered 1.15 s.
                ProgramTodaySparkles.IsVisible = true;
                var i = 0;
                foreach (var dot in ProgramTodaySparkles.Children.OfType<Ellipse>())
                {
                    var twinkle = Loop(TimeSpan.FromSeconds(6), PlaybackDirection.Normal, OpacityProperty,
                        (0, 0d), (0.45 / 6, 0.9), (1.3 / 6, 0d), (1, 0d));
                    twinkle.Delay = TimeSpan.FromSeconds(i++ * 1.15);   // WPF BeginTime: first cycle only
                    _ = Linear(twinkle).RunAsync(dot, token);
                }

                // FX 4: accent-bar breath, 0.55 <-> 1 over 3.4 s.
                _ = Loop(TimeSpan.FromSeconds(3.4), PlaybackDirection.Alternate, OpacityProperty,
                        (0, 0.55), (1, 1d)).RunAsync(ProgramTodayAccent, token);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Program banner FX start failed");
            }
        }

        private void StopProgramBannerFx()
        {
            if (_programBannerCts == null) return;
            _programBannerCts.Cancel();
            _programBannerCts.Dispose();
            _programBannerCts = null;
            ProgramTodayFog.IsVisible = false;
            ProgramTodaySheen.IsVisible = false;
            ProgramTodaySparkles.IsVisible = false;
            ProgramTodayAccent.Opacity = 1;
            Slide(ProgramTodayArt).X = 0;
        }

        private static TranslateTransform Slide(Visual v)
        {
            if (v.RenderTransform is TranslateTransform t) return t;
            v.RenderTransform = t = new TranslateTransform();
            return t;
        }

        private static Animation Loop(TimeSpan duration, PlaybackDirection direction, AvaloniaProperty property,
            params (double cue, double value)[] frames)
        {
            var animation = new Animation
            {
                Duration = duration,
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = direction,
                Easing = new SineEaseInOut(),
            };
            foreach (var (cue, value) in frames)
                animation.Children.Add(new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(property, value) } });
            return animation;
        }

        /// <summary>WPF's sheen and twinkle are linear keyframes (the twinkle's 0.45 s rise is a
        /// quadratic ease-out there; linear here, 0.45 s of a 6 s cycle).</summary>
        private static Animation Linear(Animation a) { a.Easing = new LinearEasing(); return a; }

        /// <summary>WPF NudgeProgramBannerArt: 260 ms quadratic ease-out slide of the art.</summary>
        private void NudgeProgramBannerArt(double to)
        {
            try
            {
                if (!_programBannerArtOn || !ProgramTodayArt.IsVisible) return;
                _programBannerNudgeCts?.Cancel();
                var slide = Slide(ProgramTodayArt);
                if (!AmbientFxCanvas.Env.AllowTransitions) { slide.X = to; return; }
                _programBannerNudgeCts = new CancellationTokenSource();
                _ = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(260),
                    Easing = new QuadraticEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.XProperty, slide.X) } },
                        new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.XProperty, to) } },
                    },
                }.RunAsync(slide, _programBannerNudgeCts.Token);
            }
            catch { /* decoration */ }
        }
    }
}

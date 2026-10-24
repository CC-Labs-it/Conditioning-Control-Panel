using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Avalonia.VisualTree;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The ignition rig: WPF MainWindow.ProgramsFx.cs. One heat scalar (Core ProgramHeat) dresses the
    /// run view - wash, sigil breath, turning border, rail glow, counter shimmer, edge glow, boss flare,
    /// rail comet, particles - plus the day / chapter / graduation moments. Ambient loops run only while
    /// the tab is visible and attached, Motion allows them and the shell is not minimised (WPF
    /// ProgramsFx.cs:818; focus loss only parks the particle canvas, which watches IsActive itself) (P01); the rig and generated rail nodes own cancellation groups, stopped on hide and on detach (P65). Display only: nothing is written.
    /// ponytail: FireBurstAt sparks (task tick, seal, graduation) and the banner spring have no Avalonia twin yet.
    /// </summary>
    public partial class ProgramsTabView
    {
        private const double SigilRestOpacity = 0.55;              // WPF :63
        private const byte WashAlphaCold = 70, WashAlphaHot = 130;  // :66-67
        private const double AccentFadeSeconds = 1.2;              // :70
        private const double SealSeconds = 1.6;                    // :73
        private static readonly Color GraduationGold = Color.FromRgb(0xFF, 0xD7, 0x00);   // :77

        private SolidColorBrush? _runAccent;
        private string? _runAccentKey;
        private Color? _runAccentTarget;
        private CancellationTokenSource? _accentFadeCts;

        private double _heat;
        private ProgramHeatTier _tier;
        private bool _bossToday;
        private string? _fxSignature;
        private CancellationTokenSource? _ignitionCts, _momentCts;
        private ProgramEnrollment? _ignitionEnrollment;
        private int _cometAttempts;
        private AmbientFxCanvas? _particles;
        private RotateTransform? _borderRotate;
        private IBrush? _borderBrush, _washBrush, _sigilGlowBrush, _edgeBrush, _cometBrush;
        private LinearGradientBrush? _counterBrush;
        private RadialGradientBrush? _waveBrush;
        private BoxShadows _railGlow, _bossGlow;   // WPF DropShadowEffects; a BoxShadow under the comet/flare (P82)

        private string? _runKeySeen, _dayCompletePopped, _chapterSealed, _chapterBaselineRun, _graduationCelebrated;
        private bool _dayJustCompleted;
        private Window? _fxWindow;
        private CancellationTokenSource? _railPipCts;
        private readonly HashSet<ProgramDayPip> _ignitedPips = new();

        /// <summary>Tests read these: the heat tier last computed, and whether ambient clocks run.</summary>
        internal ProgramHeatTier IgnitionTier => _tier;
        internal bool IgnitionLoopsRunning => _ignitionCts != null;
        internal AmbientFxCanvas? IgnitionParticles => _particles;

        private static string RunKey(ProgramEnrollment e) => $"{e.ProgramId}:{e.AttemptNumber}";   // WPF ProgramsTab.cs:236

        private bool AmbientOn => Env.AllowAmbientLoops && IsVisible && VisualRoot is not null
                                  && Shell is not { WindowState: WindowState.Minimized };

        // ---- lifecycle: attach / detach / motion gate / window gate ----

        private void HookIgnition()
        {
            Env.MotionGateChanged += SyncIgnition;
            _fxWindow = TopLevel.GetTopLevel(this) as Window;
            if (_fxWindow == null) return;
            _fxWindow.PropertyChanged += OnFxWindowProperty;
        }

        private void UnhookIgnition()
        {
            Env.MotionGateChanged -= SyncIgnition;
            if (_fxWindow != null)
            {
                _fxWindow.PropertyChanged -= OnFxWindowProperty;
                _fxWindow = null;
            }
            StopIgnitionLoops();
            StopMoments();
            StopAccentFade();
        }

        /// <summary>One-shot moments are cancelled on hide/detach (and settle at rest via their done hooks).</summary>
        private void StopMoments()
        {
            _momentCts?.Cancel();
            _momentCts?.Dispose();
            _momentCts = null;
        }

        private void OnFxWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            // Minimise only, like WPF: an alt-tab must not rebuild the rig or reset the dust.
            if (e.Property == Window.WindowStateProperty) SyncIgnition();
        }

        /// <summary>Re-dress (or park) after any gate changes. The signature carries the gate, so an
        /// unchanged gate costs one string compare.</summary>
        private void SyncIgnition()
        {
            SyncSessionSheen();
            if (!IsVisible || VisualRoot is null) { StopIgnitionLoops(); StopMoments(); return; }
            if (_ignitionEnrollment != null && Find<StackPanel>("ProgramsRunPanel").IsVisible)
                ApplyIgnition(_ignitionEnrollment);
        }

        /// <summary>WPF ResetProgramRunPopsIfRunChanged (ProgramsTab.cs:1612): one-shots hang off the run.</summary>
        private void ResetRunPopsIfRunChanged(ProgramEnrollment? enrollment)
        {
            var key = enrollment == null ? null : RunKey(enrollment);
            if (string.Equals(key, _runKeySeen, StringComparison.Ordinal)) return;
            _runKeySeen = key;
            _dayCompletePopped = _chapterSealed = _chapterBaselineRun = _graduationCelebrated = null;
        }

        // ---- the shared accent (WPF :176-247) ----

        /// <summary>WPF ProgramRunAccent: ONE mutable brush per run, crossfaded when the chapter changes.</summary>
        private SolidColorBrush RunAccent(ProgramDefinition program, ProgramChapter? chapter, ProgramEnrollment enrollment)
        {
            var hex = !string.IsNullOrWhiteSpace(chapter?.AccentColor) ? chapter!.AccentColor : program.AccentColor;
            var target = ((ISolidColorBrush)Windows.MainShellWindow.AccentBrush(hex)).Color;
            var key = RunKey(enrollment);
            if (_runAccent == null || !string.Equals(_runAccentKey, key, StringComparison.Ordinal))
            {
                StopAccentFade();
                _runAccent = new SolidColorBrush(target);
                _runAccentKey = key;
                _runAccentTarget = target;
                return _runAccent;
            }
            if (_runAccentTarget == target) return _runAccent;
            _runAccentTarget = target;
            FadeAccent(target);
            return _runAccent;
        }

        private void FadeAccent(Color target)
        {
            StopAccentFade();
            var brush = _runAccent!;
            if (!Env.AllowTransitions) { brush.Color = target; return; }
            var from = brush.Color;
            _accentFadeCts = new CancellationTokenSource();
            Drive(AccentFadeSeconds, new QuadraticEaseInOut(), p => brush.Color = Mix(from, target, p),
                _accentFadeCts.Token, () => { if (_runAccentTarget == target) brush.Color = target; });
        }

        private void StopAccentFade()
        {
            _accentFadeCts?.Cancel();
            _accentFadeCts?.Dispose();
            _accentFadeCts = null;
        }

        private Color AccentNow => _runAccentTarget ?? _runAccent?.Color ?? Colors.HotPink;

        // ---- the entry points (WPF :261-333) ----

        /// <summary>WPF ComputeProgramHeat: the scalar every tier decision reads.</summary>
        private void ComputeHeat(ProgramDefinition program, ProgramEnrollment enrollment, ProgramDay? day)
        {
            _bossToday = day?.IsBoss == true;
            _heat = ProgramHeat.Compute(enrollment.CurrentDay, program.LengthDays, day?.Intensity ?? 0.0, _bossToday);
            _tier = ProgramHeat.TierOf(_heat);
        }

        /// <summary>WPF ApplyProgramIgnition: re-points brushes every rebuild, re-arms clocks per signature.</summary>
        private void ApplyIgnition(ProgramEnrollment enrollment)
        {
            try
            {
                _ignitionEnrollment = enrollment;
                var ambient = AmbientOn;
                var accent = AccentNow;
                var signature = string.Join("|", _runAccentKey ?? "-", enrollment.CurrentDay, (int)_tier,
                    Math.Round(_heat, 3).ToString(CultureInfo.InvariantCulture), (int)Env.Level,
                    (int)Env.CurrentTier, accent.ToString(), ambient);
                var rearm = !string.Equals(signature, _fxSignature, StringComparison.Ordinal);
                if (rearm)
                {
                    StopIgnitionLoops();
                    _fxSignature = signature;
                    BuildScenery(accent);
                }

                if (_washBrush != null) Find<Rectangle>("TodayHeroGlow").Fill = _washBrush;
                var sigilGlow = Find<Ellipse>("RunSigilGlow");
                if (_sigilGlowBrush != null && sigilGlow.IsVisible) sigilGlow.Fill = _sigilGlowBrush;
                if (_tier >= ProgramHeatTier.Charged && ambient && _borderBrush != null)
                {
                    var panel = Find<Border>("TodayPanel");
                    panel.BorderBrush = _borderBrush;
                    panel.BorderThickness = new Thickness(2);
                }
                if (!rearm) { ApplyRailPipFx(); return; }

                // Clocks only exist when ambient motion is allowed; otherwise every surface below is static.
                var token = ambient ? (_ignitionCts = new CancellationTokenSource()).Token : CancellationToken.None;
                var breath = ProgramHeat.BreathSeconds(_heat);

                // T1+: sigil breathes (WPF :451); hidden halo (no sigil art) costs nothing.
                if (_tier >= ProgramHeatTier.Warm && ambient && sigilGlow.IsVisible)
                {
                    var amp = ProgramHeat.BreathAmplitude(_heat);
                    sigilGlow.RenderTransformOrigin = RelativePoint.Center;
                    var scale = new ScaleTransform();
                    sigilGlow.RenderTransform = scale;
                    Pulse(sigilGlow, token, breath, Math.Clamp(SigilRestOpacity - amp, 0.10, 1.0),
                        Math.Clamp(SigilRestOpacity + amp, 0.10, 0.95), v => sigilGlow.Opacity = v);
                    Pulse(sigilGlow, token, breath, 1.0, 1.0 + amp * 0.35, v => scale.ScaleX = scale.ScaleY = v);
                }

                // T2+: the border turns (WPF :508); parked at 35 degrees otherwise.
                if (_borderRotate != null)
                {
                    _borderRotate.Angle = 35;
                    var rotate = _borderRotate;
                    if (_tier >= ProgramHeatTier.Charged && ambient)
                        Spin(Find<Border>("TodayPanel"), token, ProgramHeat.BorderLapSeconds(_heat), 0, 360, v => rotate.Angle = v);
                }

                // Wash: present at every tier, blooming from T3 (WPF :539).
                var wash = Find<Rectangle>("TodayHeroGlow");
                var peak = ProgramHeat.WashOpacity(_heat);
                wash.Opacity = peak;
                if (_tier >= ProgramHeatTier.Hot && ambient)
                    Pulse(wash, token, breath * 1.7, peak * 0.68, peak, v => wash.Opacity = v);

                // T2+: static rail glow (WPF :567).
                Find<Border>("RailProgressFill").BoxShadow = _tier >= ProgramHeatTier.Charged ? _railGlow : default;

                // T3+: counter shimmer through the text's own ink (WPF :578).
                var counter = Find<TextBlock>("TxtRunDayCounter");
                if (_tier >= ProgramHeatTier.Hot && ambient && _counterBrush != null)
                {
                    // Above the XAML DynamicResource (a local binding that otherwise keeps winning).
                    counter.SetValue(TextBlock.ForegroundProperty, _counterBrush, BindingPriority.Animation);
                    var ink = (TranslateTransform)_counterBrush.Transform!;
                    Spin(counter, token, breath * 2.2, -1, 1, v => ink.X = v * Math.Max(60, counter.Bounds.Width));
                }
                else counter.SetValue(TextBlock.ForegroundProperty, AvaloniaProperty.UnsetValue, BindingPriority.Animation);

                // T4 / boss: whole-panel edge glow (WPF :607).
                var edge = Find<Rectangle>("ProgramsEdgeGlow");
                if (_tier >= ProgramHeatTier.Ignited && _edgeBrush != null)
                {
                    edge.Fill = _edgeBrush;
                    var edgePeak = ProgramHeat.EdgeGlowOpacity(_heat);
                    edge.Opacity = edgePeak;
                    if (ambient) Pulse(edge, token, breath * 2.4, edgePeak * 0.55, edgePeak, v => edge.Opacity = v);
                }
                else { edge.Opacity = 0; edge.Fill = null; }

                // Boss day: the badge flares, a soft glow, never a flash (WPF :642).
                var badge = Find<Border>("TodayBossBadge");
                badge.BoxShadow = _bossToday ? _bossGlow : default;
                badge.Opacity = 1;
                if (_bossToday && ambient) Pulse(badge, token, breath * 1.35, 0.72, 1.0, v => badge.Opacity = v);

                ApplyRailComet(ambient, token);
                ApplyParticles(accent);
                ApplyRailPipFx();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Program ignition pass failed"); }
        }

        /// <summary>WPF BuildProgramIgnitionScenery (:335): every cached brush, once per signature.</summary>
        private void BuildScenery(Color accent)
        {
            var heat = _heat;
            var solid = new SolidColorBrush(accent);
            var washAlpha = (byte)Math.Round(WashAlphaCold + (WashAlphaHot - WashAlphaCold) * Math.Clamp(heat, 0, 1));
            _washBrush = Windows.MainShellWindow.ProgramRadialGlowBrush(solid, washAlpha, 0.78, 0.2, 0.9);
            _sigilGlowBrush = Windows.MainShellWindow.ProgramRadialGlowBrush(solid, 150);

            // Avalonia brushes take a Transform; origin set explicitly (Avalonia/WPF defaults differ, P71).
            _borderRotate = new RotateTransform(35);
            _borderBrush = Linear(new Point(0, 0), new Point(1, 1), _borderRotate,
                (WithAlpha(accent, 0x30), 0), (WithAlpha(accent, 0xC0), 0.35), (Lighten(accent, 0.55), 0.5),
                (WithAlpha(accent, 0xC0), 0.65), (WithAlpha(accent, 0x30), 1));
            var ink = this.TryFindResource("TextLightBrush", ActualThemeVariant, out var v) && v is ISolidColorBrush s
                ? s.Color : Colors.White;
            _counterBrush = Linear(new Point(0, 0.5), new Point(1, 0.5), new TranslateTransform(),
                (ink, 0), (ink, 0.38), (Lighten(accent, 0.45), 0.5), (ink, 0.62), (ink, 1));
            _cometBrush = Linear(new Point(0, 0.5), new Point(1, 0.5), null,
                (WithAlpha(accent, 0), 0), (WithAlpha(accent, 0x66), 0.62), (Lighten(accent, 0.7), 0.9), (WithAlpha(accent, 0), 1));
            _edgeBrush = Radial(0.72, (WithAlpha(accent, 0), 0), (WithAlpha(accent, 0), 0.62), (WithAlpha(accent, 0x8A), 1));
            // The wave grows by the brush's own scale, not the element's (WPF animates the radii).
            _waveBrush = Radial(1.15, (WithAlpha(accent, 0), 0), (WithAlpha(accent, 0xB0), 0.72), (WithAlpha(accent, 0), 1));
            _waveBrush.Transform = new ScaleTransform(0.02 / 1.15, 0.02 / 1.15);
            _waveBrush.TransformOrigin = RelativePoint.Center;

            if (Env.AllowGlow(Env.CurrentTier))
            {
                var cap = Env.MaxGlowBlurRadius(Env.CurrentTier);
                _railGlow = Glow(accent, Math.Min(cap, 6 + 10 * heat), 0.45 + 0.35 * heat);   // WPF :420
                _bossGlow = Glow(accent, Math.Min(cap, 14), 0.7);                            // WPF :427
            }
            else _railGlow = _bossGlow = default;
        }

        /// <summary>WPF ApplyProgramRailComet (:686): gate on a measured rail, retry below input, give up quietly.</summary>
        private void ApplyRailComet(bool ambient, CancellationToken token)
        {
            var host = Find<Grid>("RailCometHost");
            var comet = Find<Rectangle>("RailComet");
            if (_tier < ProgramHeatTier.Ignited || !ambient || _cometBrush == null)
            {
                host.IsVisible = false;
                comet.Opacity = 0;
                _cometAttempts = 0;
                return;
            }
            host.IsVisible = true;
            var gate = ProgramCometGate.Decide(host.Bounds.Width, Find<Grid>("ProgramDayRail").Bounds.Width, _cometAttempts);
            _cometAttempts = gate.Attempts;
            if (gate.Action != ProgramCometAction.Run)
            {
                comet.Opacity = 0;
                if (gate.Action == ProgramCometAction.GiveUp) { host.IsVisible = false; return; }
                Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested && IsVisible) ApplyRailComet(AmbientOn, token);
                }, DispatcherPriority.Background);
                return;
            }
            comet.Fill = _cometBrush;
            comet.Opacity = 1;
            var slide = (TranslateTransform)comet.RenderTransform!;
            Spin(comet, token, ProgramHeat.CometLapSeconds(_heat), -140, gate.Width + 40, v => slide.X = v);
        }

        /// <summary>WPF ApplyProgramParticles (:761): a share of the tier's budget, through the shared canvas.</summary>
        private void ApplyParticles(Color accent)
        {
            var wanted = ProgramHeat.ParticleCount(_heat);
            if (wanted <= 0 || !Env.AllowParticles || !AmbientOn) { _particles?.Stop(); return; }
            if (_particles == null)
            {
                _particles = new AmbientFxCanvas { IsHitTestVisible = false };
                Find<Grid>("TodayFxLayer").Children.Add(_particles);
                Shell?.RegisterTabFx("programs", _particles);   // the tab-switch park/resume governor
            }
            var budget = Math.Max(1, Env.MaxAmbientParticles(Env.CurrentTier));
            _particles.StartLayers(new AmbientFxConfig
            {
                Layers = AmbientFxLayers.DustField,
                Intensity = 0.55 + 0.45 * _heat,
                DustDensity = Math.Clamp(wanted / (double)budget, 0.0, 1.0),
                Tint = accent,
            });
        }

        /// <summary>WPF StopProgramIgnitionLoops (:842): every clock off, every surface at rest; clearing
        /// the signature re-arms it on the next pass.</summary>
        private void StopIgnitionLoops()
        {
            StopRailPipFx();
            _fxSignature = null;
            _cometAttempts = 0;
            _particles?.Stop();
            _ignitionCts?.Cancel();
            _ignitionCts?.Dispose();
            _ignitionCts = null;
            Find<Ellipse>("RunSigilGlow").Opacity = SigilRestOpacity;
            Find<Border>("TodayBossBadge").Opacity = 1;
            var edge = Find<Rectangle>("ProgramsEdgeGlow");
            edge.Opacity = 0;
            edge.Fill = null;
            Find<Rectangle>("RailComet").Opacity = 0;
            Find<Grid>("RailCometHost").IsVisible = false;
        }

        // WPF ProgramsTabView.xaml:626-698: the rail's generated containers own their
        // clocks. They are rebuilt on credits, so these clocks follow those containers rather
        // than the signature-cached hero rig. Every frame rides the shared 30 fps beat.
        private void ApplyRailPipFx()
        {
            StopRailPipFx();
            if (!IsVisible || VisualRoot == null || Shell is { WindowState: WindowState.Minimized }) return;
            _railPipCts = new CancellationTokenSource();
            var token = _railPipCts.Token;
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested || !IsVisible || VisualRoot == null) return;
                var strip = Find<ItemsControl>("ProgramDayStrip");
                foreach (var glow in strip.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Name == "PipGlow"))
                {
                    if (glow.DataContext is not ProgramDayPip pip) continue;
                    pip.Breathe = pip.IsCurrent && AmbientOn && _tier >= ProgramHeatTier.Warm;
                    if (!pip.Breathe) continue;
                    var scale = (ScaleTransform)glow.RenderTransform!;
                    var loop = new BeatLoop(glow, t =>
                    {
                        var phase = BeatLoop.Breath(t, 1.2);
                        glow.Opacity = .35 + .60 * phase;
                        scale.ScaleX = scale.ScaleY = .9 + .22 * phase;
                    });
                    loop.Start();
                    token.Register(() => { loop.Stop(); glow.Opacity = .65; scale.ScaleX = scale.ScaleY = 1; });
                }
                foreach (var flare in strip.GetVisualDescendants().OfType<Ellipse>().Where(e => e.Name == "PipIgnite"))
                {
                    if (flare.DataContext is not ProgramDayPip { Ignite: true } pip || !_ignitedPips.Add(pip)) continue;
                    // The moment survives Reduced, while Off consumes the carrier without replaying later.
                    if (!Env.AllowTransitions) continue;
                    var scale = (ScaleTransform)flare.RenderTransform!;
                    var keys = new[] { (0d, 0d), (.18, .95), (.9, 0d) };
                    var easing = new CubicEaseOut();
                    BeatLoop? loop = null;
                    loop = new BeatLoop(flare, t =>
                    {
                        var p = Math.Clamp(t / .9, 0, 1);
                        flare.Opacity = BeatLoop.Keys(t, keys);
                        scale.ScaleX = scale.ScaleY = .6 + .75 * easing.Ease(p);
                        if (p >= 1) loop!.Stop();
                    });
                    loop.Start();
                    token.Register(() => { loop.Stop(); flare.Opacity = 0; });
                }
            }, DispatcherPriority.Loaded);
        }

        private void StopRailPipFx()
        {
            _railPipCts?.Cancel();
            _railPipCts?.Dispose();
            _railPipCts = null;
        }

        // ---- the discrete moments (WPF :899-1192) ----

        /// <summary>WPF NoteProgramDayCompletion: the once-per-day key; a hidden tab records, never plays.</summary>
        private void NoteDayCompletion(ProgramEnrollment enrollment, ProgramDayRecord? record, bool tabVisible)
        {
            _dayJustCompleted = false;
            if (record?.DayCompleted != true) return;
            var dayKey = $"{RunKey(enrollment)}:{enrollment.CurrentDay}";
            if (string.Equals(_dayCompletePopped, dayKey, StringComparison.Ordinal)) return;
            _dayCompletePopped = dayKey;
            _dayJustCompleted = tabVisible;
        }

        /// <summary>WPF PlayProgramDayCompleteMoment: an accent wave across the band and the day's real XP.</summary>
        private void PlayDayCompleteMoment(ProgramDay? day)
        {
            var wave = Find<Rectangle>("TodayWave");
            var xp = Find<TextBlock>("TxtTodayXpFloat");
            if (!Env.AllowTransitions || _waveBrush == null) { wave.Opacity = 0; xp.Opacity = 0; return; }
            var token = (_momentCts ??= new CancellationTokenSource()).Token;

            wave.Fill = _waveBrush;
            var grow = (ScaleTransform)_waveBrush.Transform!;
            Drive(0.95, new CubicEaseOut(), p => grow.ScaleX = grow.ScaleY = 0.02 / 1.15 + (1 - 0.02 / 1.15) * p, token);
            _ = Run(Keys(0.95, OpacityProperty, (0, 0.0), (0.18, 0.75), (0.95, 0.0)), wave, token, () => wave.Opacity = 0);

            xp.Text = Loc.GetF("label_0_xp_3", ProgramHeat.DayXp(day?.Intensity ?? 0.0, day?.IsBoss == true));
            if (_runAccent != null) xp.Foreground = _runAccent;
            _ = Run(new Animation
            {
                Duration = TimeSpan.FromSeconds(1.5), Easing = new CubicEaseOut(), FillMode = FillMode.Forward,
                Children = { Frame(0, TranslateTransform.YProperty, 18.0), Frame(1, TranslateTransform.YProperty, -46.0) },
            }, xp, token);
            _ = Run(Keys(1.5, OpacityProperty, (0, 0.0), (0.22, 1.0), (0.95, 1.0), (1.5, 0.0)), xp, token, () => xp.Opacity = 0);
        }

        /// <summary>WPF NoteProgramChapterSeal: baseline per run, then only a change from it is live.</summary>
        private void NoteChapterSeal(ProgramDefinition program, ProgramEnrollment enrollment, bool tabVisible)
        {
            var ids = enrollment.CompletedChapterIds;
            var runKey = RunKey(enrollment);
            var latest = ids is { Count: > 0 } ? ids[^1] : "-";
            var key = $"{runKey}:{latest}";
            if (!string.Equals(_chapterBaselineRun, runKey, StringComparison.Ordinal))
            {
                _chapterBaselineRun = runKey;
                _chapterSealed = key;
                return;
            }
            if (string.Equals(_chapterSealed, key, StringComparison.Ordinal)) return;
            _chapterSealed = key;
            if (!tabVisible || latest == "-") return;
            var chapter = program.Chapters.Find(c => string.Equals(c.Id, latest, StringComparison.Ordinal));
            if (chapter != null) PlayChapterSeal(chapter, AccentNow);
        }

        /// <summary>WPF PlayProgramChapterSeal: the cleared chapter's name in an accent ring, ~1.6 s.</summary>
        private void PlayChapterSeal(ProgramChapter chapter, Color accent)
        {
            var host = Find<Grid>("ProgramsSealHost");
            var ink = new SolidColorBrush(accent);
            Find<TextBlock>("TxtProgramsSealTitle").Text = chapter.Name;
            Find<TextBlock>("TxtProgramsSealTitle").Foreground = ink;
            Find<IconGlyph>("ProgramsSealGlyph").Foreground = ink;
            Find<Border>("ProgramsSealRing").BorderBrush = ink;
            var days = chapter.Days;
            Find<TextBlock>("TxtProgramsSealSub").Text = days is { Count: > 0 }
                ? Loc.GetF("program_enroll_chapter_days", days[0].DayIndex, days[^1].DayIndex)
                : chapter.Subtitle;
            if (!Env.AllowTransitions) { host.IsVisible = false; host.Opacity = 0; return; }

            host.IsVisible = true;
            var token = (_momentCts ??= new CancellationTokenSource()).Token;
            _ = Run(Keys(SealSeconds, OpacityProperty, (0, 0.0), (0.22, 1.0), (1.15, 1.0), (SealSeconds, 0.0)), host, token,
                () => { host.Opacity = 0; host.IsVisible = false; });
            // WPF eases the landing with BackEase; per-keyframe easing is a KeySpline here, so linear segments.
            var stamp = Keys(SealSeconds, ScaleTransform.ScaleXProperty, (0, 1.35), (0.3, 1.0), (SealSeconds, 1.06));
            foreach (var (frame, value) in new[] { (0, 1.35), (1, 1.0), (2, 1.06) })
                stamp.Children[frame].Setters.Add(new Setter(ScaleTransform.ScaleYProperty, value));
            _ = Run(stamp, Find<StackPanel>("ProgramsSealStamp"), token);
            Serilog.Log.Information("[FX] program chapter sealed: {Chapter}", chapter.Name);
        }

        /// <summary>WPF CelebrateProgramGraduation: the accent hands over to gold, once per run.</summary>
        private void CelebrateGraduation(ProgramDefinition program, ProgramEnrollment enrollment)
        {
            var key = RunKey(enrollment);
            var already = string.Equals(_graduationCelebrated, key, StringComparison.Ordinal);
            _graduationCelebrated = key;
            _runAccent ??= new SolidColorBrush(((ISolidColorBrush)Windows.MainShellWindow.AccentBrush(program.AccentColor)).Color);
            _runAccentKey = key;
            Find<Border>("ProgramsGraduatedCard").BorderBrush = _runAccent;
            if (already) return;
            _runAccentTarget = GraduationGold;
            FadeAccent(GraduationGold);
            Serilog.Log.Information("[FX] program graduation celebrated");
        }

        // ---- helpers ----

        private static KeyFrame Frame(double cue, AvaloniaProperty property, object value) =>
            new() { Cue = new Cue(cue), Setters = { new Setter(property, value) } };

        /// <summary>A forever sine yo-yo (WPF AutoReverse + RepeatBehavior.Forever + SineEase) on the
        /// window's 30 fps beat (WPF SetDesiredFrameRate(AmbientFrameRate = 30), MainWindow.AmbientFx.cs:15),
        /// never an infinite Animation (P82). Parks while <paramref name="owner"/> is hidden or the window
        /// minimised; stops with the run's token.</summary>
        private static void Pulse(Visual owner, CancellationToken token, double seconds, double from, double to, Action<double> set) =>
            Beat(owner, token, t => set(from + (to - from) * BeatLoop.Breath(t, seconds)));

        /// <summary>A forever linear lap (WPF RepeatBehavior.Forever, no easing), same beat.</summary>
        private static void Spin(Visual owner, CancellationToken token, double seconds, double from, double to, Action<double> set) =>
            Beat(owner, token, t => set(from + (to - from) * (t / seconds % 1)));

        private static void Beat(Visual owner, CancellationToken token, Action<double> step)
        {
            var loop = new BeatLoop(owner, step);
            loop.Start();
            token.Register(loop.Stop);
        }

        private static BoxShadows Glow(Color c, double blur, double opacity) =>
            new(new BoxShadow { Blur = blur, Color = WithAlpha(c, (byte)Math.Round(255 * opacity)) });

        /// <summary>WPF DoubleAnimationUsingKeyFrames with linear frames, times in seconds.</summary>
        private static Animation Keys(double seconds, AvaloniaProperty property, params (double at, double value)[] frames)
        {
            var a = new Animation { Duration = TimeSpan.FromSeconds(seconds), Easing = new LinearEasing(), FillMode = FillMode.Forward };
            foreach (var (at, value) in frames) a.Children.Add(Frame(at / seconds, property, value));
            return a;
        }

        /// <summary>Runs one animation; a cancelled or failed run is decoration lost, never a crash. Targets
        /// are Visuals (a Transform setter reaches the RenderTransform): a bare brush or transform has no
        /// clock in Avalonia 12 and freezes at its first frame (measured), so those go through Drive.</summary>
        private static async Task Run(Animation animation, Visual target, CancellationToken token, Action? done = null)
        {
            try { await animation.RunAsync(target, token); }
            catch (Exception ex) { Serilog.Log.Debug("Program FX: {E}", ex.Message); }
            done?.Invoke();
        }

        /// <summary>Per-frame driver for brushes and their transforms: eased progress 0..1 over
        /// <paramref name="seconds"/>, ends with <paramref name="done"/> (also on cancel).</summary>
        private void Drive(double seconds, IEasing easing, Action<double> apply, CancellationToken token, Action? done = null)
        {
            TimeSpan? start = null;
            void Frame(TimeSpan now)
            {
                if (token.IsCancellationRequested) { done?.Invoke(); return; }
                start ??= now;
                var t = (now - start.Value).TotalSeconds / seconds;
                var end = t >= 1;
                apply(easing.Ease(Math.Min(t, 1)));
                if (end) done?.Invoke();
                else if (TopLevel.GetTopLevel(this) is { } top) top.RequestAnimationFrame(Frame);
            }
            if (TopLevel.GetTopLevel(this) is { } host) host.RequestAnimationFrame(Frame);
            else done?.Invoke();
        }

        private static Color Mix(Color a, Color b, double t)
        {
            byte L(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
        }

        private static LinearGradientBrush Linear(Point start, Point end, Transform? transform, params (Color c, double at)[] stops)
        {
            var b = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(start, RelativeUnit.Relative),
                EndPoint = new RelativePoint(end, RelativeUnit.Relative),
                Transform = transform,
                TransformOrigin = RelativePoint.Center,
            };
            foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
            return b;
        }

        private static RadialGradientBrush Radial(double radius, params (Color c, double at)[] stops)
        {
            var b = new RadialGradientBrush
            {
                RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
            };
            foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
            return b;
        }

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        private static Color Lighten(Color c, double amount)
        {
            var t = Math.Clamp(amount, 0.0, 1.0);
            byte Mix(byte v) => (byte)Math.Round(v + (255 - v) * t);
            return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
        }
    }
}

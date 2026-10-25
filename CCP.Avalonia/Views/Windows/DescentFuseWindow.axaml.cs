using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Descent;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// THE FUSE AT ZERO (CONTRACT-FUSE-0816 §2.3/§2.4) — the fullscreen show the countdown has been
    /// counting down to, and the two shows that hang off it.
    ///
    /// PORTED from ConditioningControlPanel/Windows/DescentFuseWindow.xaml.cs. The timeline itself
    /// (<see cref="DescentFuseTimeline"/>, <see cref="DescentIgnitionTimeline"/>,
    /// <see cref="DescentFuseHandoff"/>) already lives in Core, so the clock and every beat boundary
    /// are the real ones. Deviations:
    ///  - <c>DescentFuseStageVisual</c> is redrawn for this head (Controls/DescentFuseStageVisual.cs).
    ///  - <c>DispatcherTimer</c> is Avalonia's; <c>DispatcherPriority.Render</c> exists on both.
    ///  - WPF's <c>BeginAnimation(OpacityProperty, …)</c> becomes an Avalonia <c>Animation</c>
    ///    run from code (<see cref="FadeTo"/>), with <c>FillMode.Forward</c> so the ramp sticks at
    ///    its end value the way WPF's does.
    ///  - <c>MotionFx.Level</c> is <see cref="AmbientFxCanvas.Env"/>.<c>Level</c> here, which reads
    ///    the same <c>CoreSettings.Current.MotionLevel</c>. Reduced motion is therefore REAL on this
    ///    head and the Core timelines get the flag they expect.
    ///  - <c>Application.Current.MainWindow</c> is the desktop lifetime's <c>MainWindow</c>, and
    ///    Avalonia has no settable <c>Owner</c>, so ownership is <c>Show(parent)</c>.
    ///  - Room sounds remain a follow-up; the existing CoreAudio backend is the playback seam.
    ///
    /// <para>Retired like WPF (<see cref="ShowsRetired"/>): <see cref="Open"/> refuses every
    /// kind, so the Live zero and late zero open nothing. PanicSurfaces still closes any show.</para>
    /// </summary>
    public partial class DescentFuseWindow : Window
    {
        /// <summary>
        /// Live instances, so the panic path can clear the screen. Static because panic has no
        /// reference to hand: it is a global keystroke, not a UI interaction. It doubles as the
        /// single-open guard — see <see cref="Open"/>.
        /// </summary>
        private static readonly List<DescentFuseWindow> Live = new();

        /// <summary>The clock. ~50fps; the show is four brush ramps and some trigonometry, and a
        /// higher rate would buy nothing a projector could show.</summary>
        private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(20);

        /// <summary>How long the catch-up holds its bloom before dissolving.</summary>
        private const double CatchUpHoldSeconds = 1.2;

        private const double CatchUpFadeSeconds = 0.8;

        private readonly DescentShowKind _kind;
        private readonly bool _reduced;
        private readonly Stopwatch _clock = new();
        private readonly DescentFuseHandoff _handoff = new();

        /// <summary>The bloom's resync, kept so a test awaits it instead of polling.</summary>
        internal System.Threading.Tasks.Task LastResync { get; private set; } = System.Threading.Tasks.Task.CompletedTask;

        private readonly DescentFuseStageVisual _visual;
        private readonly Border _backdropLayer;
        private readonly TextBlock _showLine;

        private DispatcherTimer? _timer;
        private bool _witnessedMarked;
        private bool _backdropRaised;
        private bool _crackSounded;
        private bool _closing;

        private bool _holdingOffers;

        /// <summary>
        /// TRUE once this window saw the ceremony come up and started dissolving into it.
        /// </summary>
        public bool HandedOffToCeremony { get; private set; }

        /// <summary>Render constructor: the live show, so --render-all can discover the window.</summary>
        internal DescentFuseWindow() : this(DescentShowKind.Live)
        {
            // The show is wordless until the handoff times out, so a bare render would be a black
            // rectangle. Stand the room up at its bloom instead: the backdrop at full and the
            // standing line visible, which is the one frame of this window that has anything in it.
            _backdropLayer.Opacity = 1.0;
            _showLine.Text = DescentFuseCopy.ShowAwaits;
            _showLine.Opacity = 1.0;
            _visual.SetFrame(new DescentFuseFrame(DescentFuseStage.Held, 1, 0));
        }

        /// <summary>Test seam: the shows currently up.</summary>
        internal static DescentFuseWindow[] OnScreen { get { lock (Live) return Live.ToArray(); } }

        /// <summary>Test seam: the show's canvas.</summary>
        internal DescentFuseStageVisual Stage => _visual;

        /// <summary>WPF ResolveAccent: the mod's pink, or the app's own if the resource is missing.</summary>
        private global::Avalonia.Media.Color ResolveAccent()
        {
            try
            {
                if (this.TryFindResource("PinkColor", out var c) && c is global::Avalonia.Media.Color color) return color;
                if (this.TryFindResource("PinkBrush", out var b) && b is global::Avalonia.Media.ISolidColorBrush brush) return brush.Color;
            }
            catch (Exception ex) { Log.Debug("[Fuse] Accent lookup failed: {Error}", ex.Message); }
            return global::Avalonia.Media.Color.FromRgb(0xFF, 0x69, 0xB4);
        }

        private DescentFuseWindow(DescentShowKind kind)
        {
            _kind = kind;

            AvaloniaXamlLoader.Load(this);

            _backdropLayer = this.FindControl<Border>("BackdropLayer")!;
            _showLine = this.FindControl<TextBlock>("ShowLine")!;

            // MotionFx.Level != MotionLevel.Full, verbatim. MotionFx itself is WPF Storyboard code
            // and stays head-side, but its DECISION is CoreSettings.Current.MotionLevel, which this
            // head already reads through AmbientFxCanvas.Env. The flag is threaded into every Core
            // timeline call below, so "Reduced" now really does shorten the show here.
            // The one WPF input with no twin is the OS animation-effects cap (MotionFx.ResolveLevel
            // forces Reduced when it is off); Avalonia exposes no cross-platform equivalent, and
            // that cap can only ever REMOVE motion, so its absence shows what the user picked.
            _reduced = AmbientFxCanvas.Env.Level != MotionLevel.Full;

            // THE MOD'S ACCENT, resolved once (WPF DescentFuseWindow.xaml.cs:120 ResolveAccent).
            _visual = new DescentFuseStageVisual(ResolveAccent());
            _visual.Begin(kind, _reduced);
            this.FindControl<Grid>("StageHost")!.Children.Add(_visual);

            if (kind == DescentShowKind.Ignition)
            {
                // The ignition opens INTO the room rather than out of the dark: the ceremony that
                // just closed was already this backdrop at full.
                _backdropLayer.Opacity = 1.0;
                _backdropRaised = true;
            }

            if (kind == DescentShowKind.Live)
            {
                // No offer hold to take: WPF retired the ceremony, so there is no offer to hold.
                // Holding the ceremony back until the light returns is choreography, not defence:
                // without it an offer answered during the drain drops the ceremony on top of a
                // crack that is four seconds from finishing.
                _holdingOffers = true;
            }

            Loaded += OnLoadedInternal;
            Closed += OnClosedInternal;

            lock (Live) Live.Add(this);
        }

        /// <summary>THE CEREMONY IS OVER - WPF DescentFuseWindow.xaml.cs:168-181 (owner, 6.11.0: the
        /// cracked-screen animation "might just confuse people now"). Every show refuses to open;
        /// each caller treats null as "nothing played".</summary>
        internal const bool ShowsRetired = true;

        public static DescentFuseWindow? Open(DescentShowKind kind)
        {
            if (ShowsRetired)
            {
                Log.Information("[Fuse] The {Kind} show is retired (the ceremony is over) - not opening it.", kind);
                return null;
            }
            return OpenShow(kind);
        }

        /// <summary>
        /// The pre-retirement opener, kept so the drawn show stays testable. Returns null if one is
        /// already up: two fullscreen topmost windows over each other is unrecoverable without the panic key.
        /// </summary>
        internal static DescentFuseWindow? OpenShow(DescentShowKind kind)
        {
            try
            {
                lock (Live)
                {
                    if (Live.Count > 0)
                    {
                        Log.Information("[Fuse] A show is already on screen — not opening the {Kind} show over it.", kind);
                        return null;
                    }
                }

                var window = new DescentFuseWindow(kind);

                // WPF: `if (main != null && main.IsLoaded && !ReferenceEquals(main, window))
                // window.Owner = main;`. Avalonia's Owner is read-only - ownership is passed to
                // Show - so the same guards pick between the two overloads.
                //
                // IsVisible, not WPF's IsLoaded, and the difference matters: Avalonia's
                // Show(parent) THROWS when the parent is not visible, and a shell minimized to the
                // tray is loaded but not visible - which is exactly the state the app is in when a
                // countdown reaches zero unattended. A throw here is caught below and answers null,
                // i.e. the show silently never opens. Ownerless is a fine fallback: this is a
                // fullscreen topmost window, not a dialog.
                var main = (global::Avalonia.Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (main is { IsVisible: true } && !ReferenceEquals(main, window)) window.Show(main);
                else window.Show();
                window.Activate();

                Log.Information("[Fuse] {Kind} show opened ({Motion}).",
                    kind, window._reduced ? "reduced motion" : "full motion");
                return window;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] Could not open the {Kind} show.", kind);
                return null;
            }
        }

        /// <summary>Clear the show off the screen for the panic path. Safe at any beat.</summary>
        public static void ForceCloseAll()
        {
            List<DescentFuseWindow> snapshot;
            lock (Live) snapshot = new List<DescentFuseWindow>(Live);

            foreach (var w in snapshot)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("[Fuse] Show force-close failed: {Error}", ex.Message); }
            }
        }

        private void OnLoadedInternal(object? sender, EventArgs e)
        {
            try
            {
                _clock.Restart();

                _timer = new DispatcherTimer(FrameInterval, DispatcherPriority.Render, OnFrame);
                _timer.Start();

                // Paint frame zero immediately rather than waiting 20ms for the first tick.
                OnFrame(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] The show could not start its clock — closing rather than holding the screen.");
                SafeClose();
            }
        }

        private void OnClosedInternal(object? sender, EventArgs e)
        {
            lock (Live) Live.Remove(this);

            ReleaseOfferHold();

            try
            {
                _timer?.Stop();
                _timer = null;
                _clock.Stop();
            }
            catch (Exception ex) { Log.Debug("[Fuse] Show teardown: {Error}", ex.Message); }
        }

        // ------------------------------------------------------------------
        // The clock
        // ------------------------------------------------------------------

        private void OnFrame(object? sender, EventArgs e)
        {
            if (_closing) return;

            try
            {
                var elapsed = _clock.Elapsed.TotalSeconds;
                if (_kind == DescentShowKind.Ignition) FrameIgnition(elapsed);
                else FrameCrack(elapsed);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Fuse] A show frame threw — closing rather than freezing the screen.");
                SafeClose();
            }
        }

        // ---------------------------------------------------------- crack + handoff

        internal void FrameCrack(double elapsed)
        {
            var frame = DescentFuseTimeline.FrameAt(_kind, elapsed, _reduced);
            _visual.SetFrame(frame);

            // The sting lands ON the crack, not before it. Reduced motion never reaches the Crack
            // stage, so the crossfade stays silent by construction.
            if (frame.Stage == DescentFuseStage.Crack && !_crackSounded)
            {
                _crackSounded = true;
                // ponytail: the Crack ladder is now in Core (DescentRoomAudio.Play(DescentRoomCue.Crack,
                // ...)) and CoreAudio is seeded by LibVlcAudio on this head. Wiring the call here
                // (AudioEnabled + head sound resolver) belongs to lane win-descent-fuse.
            }

            if (frame.Stage >= DescentFuseStage.Bloom && !_backdropRaised)
            {
                _backdropRaised = true;
                RaiseBackdrop();

                // THE KEEPSAKE HOOK (§2.3): written at the START of the bloom, not at its end.
                // Live only — the catch-up is deliberately NOT a witnessed night.
                if (_kind == DescentShowKind.Live && !_witnessedMarked)
                {
                    _witnessedMarked = true;
                    App.DescentCountdown?.MarkLastNightWitnessed();
                }

                // The light is back, so the door may open.
                ReleaseOfferHold();
            }

            if (_kind == DescentShowKind.CatchUp)
            {
                // No handoff clock here: the catch-up's whole job is to be OVER before the ceremony
                // offer opens, so it holds its bloom for a moment and dissolves.
                var total = _reduced
                    ? DescentFuseTimeline.ReducedCrossfadeSeconds
                    : DescentFuseTimeline.CatchUpSeconds;

                if (elapsed >= total + CatchUpHoldSeconds) FadeOutAndClose(CatchUpFadeSeconds);
                return;
            }

            if (frame.SinceBloom < 0) return;

            // WPF IsCeremonyOpen is constant false since the ceremony retired. Keep its honest
            // timeout line; sync attempts can still settle the silent migration acknowledgment.
            switch (_handoff.Advance(frame.SinceBloom, ceremonyOpen: false))
            {
                case DescentHandoffAction.Resync:
                    LastResync = MainShellWindow.RequestFuseSync();
                    break;

                case DescentHandoffAction.CrossfadeToCeremony:
                    BeginCeremonyHandoff();
                    break;

                case DescentHandoffAction.SpeakAwaits:
                    Log.Information("[Fuse] No ceremony offer within {Seconds}s — showing the standing line and closing.",
                        DescentFuseHandoff.TimeoutSeconds);
                    _showLine.Text = DescentFuseCopy.ShowAwaits;
                    FadeTo(_showLine, _showLine.Opacity, 1.0, 0.9);
                    break;

                case DescentHandoffAction.Close:
                    // After a handoff the crossfade has already run this window down to nothing, so
                    // it just goes. After the standing line, "closes softly" means a fade.
                    if (HandedOffToCeremony) SafeClose();
                    else FadeOutAndClose(0.9);
                    break;
            }
        }

        /// <summary>
        /// The ceremony is up. Dissolve into it. WPF re-asserted Topmost here to push this window
        /// back to the front of the topmost band without stealing focus; Avalonia has no
        /// SWP_NOACTIVATE equivalent to lean on, and the ceremony would lose focus, so the Z
        /// re-assert is deliberately left out.
        /// </summary>
        private void BeginCeremonyHandoff()
        {
            HandedOffToCeremony = true;
            Log.Information("[Fuse] The ceremony is up — dissolving into it.");
            FadeTo(this, Opacity, 0.0, DescentFuseHandoff.CrossfadeSeconds);
        }

        // ------------------------------------------------------------- the ignition

        private void FrameIgnition(double elapsed)
        {
            _visual.SetIgnition(elapsed);
            var lineOpacity = DescentIgnitionTimeline.LineOpacity(elapsed, _reduced);
            if (lineOpacity > 0 && (_showLine.Text?.Length ?? 0) == 0) _showLine.Text = DescentFuseCopy.IgnitionLine;
            _showLine.Opacity = lineOpacity;

            Opacity = DescentIgnitionTimeline.ShowOpacity(elapsed, _reduced);

            if (elapsed >= DescentIgnitionTimeline.TotalSeconds(_reduced)) SafeClose();
        }

        // ------------------------------------------------------------------
        // Fades
        // ------------------------------------------------------------------

        /// <summary>
        /// WPF's <c>BeginAnimation(OpacityProperty, new DoubleAnimation(from, to, seconds))</c>,
        /// in Avalonia's terms. FillMode.Forward is what makes the ramp stick at <paramref name="to"/>
        /// once it ends, which is the WPF behaviour the show depends on at the bloom.
        /// </summary>
        private static void FadeTo(Visual target, double from, double to, double seconds)
        {
            try
            {
                _ = new Animation
                {
                    Duration = TimeSpan.FromSeconds(seconds),
                    Easing = new QuadraticEaseOut(),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, from) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, to) } },
                    },
                }.RunAsync(target);
            }
            catch (Exception ex)
            {
                Log.Debug("[Fuse] Fade failed: {Error}", ex.Message);
                target.Opacity = to;
            }
        }

        /// <summary>
        /// Bring the ceremony's backdrop up with the bloom, so the room the show is standing in
        /// arrives at the same moment the light does.
        /// </summary>
        private void RaiseBackdrop()
        {
            var seconds = _reduced ? DescentFuseTimeline.ReducedCrossfadeSeconds : 1.6;
            FadeTo(_backdropLayer, _backdropLayer.Opacity, 1.0, seconds);
        }

        private void FadeOutAndClose(double seconds)
        {
            if (_closing) return;
            _closing = true;

            FadeTo(this, Opacity, 0.0, seconds);
            DispatcherTimer.RunOnce(SafeClose, TimeSpan.FromSeconds(seconds), DispatcherPriority.Normal);
        }

        /// <summary>Drop this window's hold, at most once.</summary>
        private void ReleaseOfferHold()
        {
            if (!_holdingOffers) return;
            _holdingOffers = false;
            // Nothing to release: the ceremony (and its offers) is retired on both heads.
        }

        private void SafeClose()
        {
            try { Close(); }
            catch (Exception ex) { Log.Debug("[Fuse] Show close failed: {Error}", ex.Message); }
        }
    }
}

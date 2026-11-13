using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Threading;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.UI;               // MonitorTarget
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Owns the Pink Filter tint: one <see cref="TintOverlayWindow"/> per targeted monitor, kept
    /// in step with <see cref="Models.AppSettings"/>. The Avalonia counterpart of
    /// <c>OverlayService</c>'s Pink region (StartPinkFilter / StopPinkFilter /
    /// UpdatePinkFilterOpacity / RefreshFilterColor), minus everything that belongs to the session
    /// engine - the timed and sustained holds, the Deeper ramp override and the 500ms poll all
    /// live in <c>OverlayService</c> and have nothing on this head to drive them.
    ///
    /// <para><b>Static, and not a service, deliberately.</b> There is exactly one desktop and one
    /// tint on it; an instance would need an owner, and the only candidate owner is the shell
    /// window this thing has to outlive between tab switches.</para>
    ///
    /// <para><b>Two refusals, and both matter more than showing the tint:</b> a full-screen
    /// topmost window that is not click-through locks the user out of their own desktop, and a
    /// window whose transparency request was denied paints an opaque screen-sized block instead of
    /// a wash. Either one is caught in <see cref="Accept"/> and the window is closed rather than
    /// left up. That is why this feature does NOT work through the Avalonia head on Windows or
    /// under a native Wayland backend: <see cref="X11Overlay"/> is X11-only and answers false
    /// there, so no tint is shown at all rather than an un-dismissable one.</para>
    /// </summary>
    internal static class PinkFilterOverlay
    {
        /// <summary>A Takeover pulse holds the tint up with the engine off (WPF: the pulse starts
        /// OverlayService, #1180). MainShellWindow.Autonomy.cs sets and releases it.</summary>
        private static bool _pulseHold;
        internal static bool PulseHold
        {
            get => _pulseHold;
            set
            {
                if (!_pulseHold && value) App.NoteBrainDrainPulseStart();
                if (_pulseHold && !value) App.ResetBrainDrainQuestSampleAfterPulse();
                _pulseHold = value;
            }
        }

        // WPF voice "go pink": a 40% floor, without dimming a stronger configured tint.
        internal static double? VoiceOpacity { get; private set; }
        internal static void SetVoiceHold(Visual host, bool on)
        {
            VoiceOpacity = on ? Math.Clamp(Math.Max(0.4, CoreSettings.Current.PinkFilterOpacity / 100.0), 0, 1) : null;
            Refresh(host);
        }
        // WPF Session/SessionEngine.cs:799,814: a later session tick overwrites the
        // sustained opacity, while its visibility hold remains owned by the voice action.
        internal static bool ApplySessionOpacity(double? percent)
        {
            if (VoiceOpacity == null || percent == null) return false;
            var next = Math.Clamp(percent.Value / 100.0, 0, 1);
            var changed = VoiceOpacity != next;
            VoiceOpacity = next;
            return changed;
        }
        internal static void Stop() { VoiceOpacity = null; CloseAll(); }

        private static bool _refused;
        internal static bool SkipPlatformChecksForTests;
        internal static IReadOnlyList<TintOverlayWindow> Shown => Windows;
        private static double? _pulseWeight;
        private static double _pulseBase, _pulsePeak;

        // The voice hold (WPF "go pink" 40% floor) is the steady level while it lasts; a Takeover pulse
        // still interpolates from its captured endpoints.
        internal static double PaintedOpacity
        {
            get
            {
                var s = CoreSettings.Current;
                var steady = VoiceOpacity ?? (App.Sessions?.PinkOpacity ?? s.PinkFilterOpacity) / 100.0;
                return Math.Clamp(_pulseWeight is { } k ? _pulseBase + (_pulsePeak - _pulseBase) * k : steady, 0, 1);
            }
        }

        internal static void SetPulse(double? weight)
        {
            // WPF OverlayService.cs:657-660 captures endpoints before the gentle timer starts.
            if (_pulseWeight == null && weight != null)
            {
                _pulseBase = (App.Sessions?.PinkOpacity ?? CoreSettings.Current.PinkFilterOpacity) / 100.0;
                _pulsePeak = Math.Min(CoreSettings.Current.PinkFilterOpacity * 2, 100) / 100.0;
            }
            _pulseWeight = weight;
            var (r, g, b) = EffectiveColor();
            foreach (var w in Windows) w.SetTint(r, g, b, PaintedOpacity);
        }

        /// <summary>Whether a tint can actually reach the screen: X11 with click-through, a
        /// compositing manager, and no earlier refusal by <see cref="Accept"/>. Takeover picks its
        /// pink pulse only on this (WPF never picks an action that would do nothing). <paramref name="host"/>
        /// proves the windows this process opens are native ones: a reachable DISPLAY says nothing
        /// about a headless or native-Wayland backend, whose tint <see cref="Accept"/> would refuse.</summary>
        internal static bool CanShowTint(TopLevel host) =>
            !_refused && X11Overlay.BackendOf(host.TryGetPlatformHandle()) != OverlayBackend.None
            && X11Overlay.IsAvailable && X11Overlay.IsCompositing;

        private static readonly List<TintOverlayWindow> Windows = new();
        private static readonly List<TintOverlayWindow> FadingWindows = new();

        private static bool GentleWindows => GentleVisuals.On && !CoreSettings.Current.UnifiedOverlayHost;
        private static Transitions FadeTransition(double seconds) => new()
        {
            new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(seconds) }
        };

        /// <summary>WPF AchievementService:226-238 - achievement and quest minutes while the tint is on screen.</summary>
        internal static readonly OverlayQuestMinutes QuestMinutes = new(
            m => { App.Achievements?.TrackPinkFilterMinutes(m); App.Quests?.TrackPinkFilterMinutes(m); });

        /// <summary>The tint is on screen now.</summary>
        internal static bool IsShowing => Windows.Count > 0;
        private static int[] _shownOn = Array.Empty<int>();
        private static Window? _hookedOwner;

        /// <summary>The colour the tint renders: the user's pick if set, else the active mod's
        /// filter colour, which unseeded is the built-in default manifest's. Same order as
        /// <c>OverlayService.GetFilterRgb</c>.</summary>
        internal static (byte R, byte G, byte B) EffectiveColor() =>
            CoreMods.TryParseHexColor(CoreSettings.Current.PinkFilterColor, out var rgb)
                ? rgb
                : CoreMods.GetFilterColorRgb();

        internal static readonly TimedOverlayHold PayloadHold = new();
        internal static bool HasTimedPayload => PayloadHold.IsActive;
        internal static void ShowTimed(Visual host, int ms, double opacity)
        {
            var release = PayloadHold.Acquire(opacity);
            Refresh(host);
            global::Avalonia.Threading.DispatcherTimer.RunOnce(() =>
            {
                if (release()) Refresh(host);
            }, TimeSpan.FromMilliseconds(Math.Max(50, ms)));
        }

        /// <summary>
        /// Bring the tint in line with the current settings: show it, hide it, move it or repaint
        /// it as those say. Idempotent, so every card handler can just call this.
        /// </summary>
        /// <param name="host">Any attached visual - it is only used to reach <c>Screens</c> and
        /// the owning window. A detached one enumerates no screens and hides the tint.</param>
        public static void Refresh(Visual host)
        {
            var s = CoreSettings.Current;
            if (VoiceOpacity == null && !HasTimedPayload && (!s.PinkFilterEnabled || !ShouldShow())) { HideAll(); return; }

            var screens = ScreenList.Enumerate(host);
            var primary = -1;
            for (var i = 0; i < screens.Count; i++) if (screens[i].IsPrimary) { primary = i; break; }

            var want = ResolveScreenIndices(s.PinkFilterTargetMonitor, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
            var (r, g, b) = EffectiveColor();
            var opacity = PaintedOpacity;   // a session ramps it without writing it (#471)
            // WPF ResolveAdHocOverlayOpacity: an effect raises a live tint, never lowers it.
            if (HasTimedPayload)
                opacity = VoiceOpacity != null || (s.PinkFilterEnabled && ShouldShow()) ? Math.Max(opacity, PayloadHold.Opacity) : PayloadHold.Opacity;

            // Same monitors as last time: repaint the brushes and leave the windows alone. This is
            // the whole reason the slider does not tear down a full-screen window per tick.
            if (Windows.Count > 0 && want.SequenceEqual(_shownOn))
            {
                foreach (var w in Windows) w.SetTint(r, g, b, opacity);
                return;
            }

            CloseWindows();

            // Checked BEFORE anything is created, not only inside Accept: without this, a slider
            // drag on a platform that cannot do click-through flashes a topmost full-screen window
            // once per tick and logs a warning each time. Accept still runs - IsAvailable being
            // true does not promise THIS window is an X11 one, nor that the compositor granted
            // transparency.
            if (!SkipPlatformChecksForTests && !X11Overlay.IsAvailable)
            {
                Log.Debug("Pink filter: no X11 display, so no tint on this platform");
                return;
            }

            HookOwner(TopLevel.GetTopLevel(host) as Window);

            foreach (var i in want)
            {
                var w = new TintOverlayWindow();
                w.PlaceOn(screens[i]);
                w.SetTint(r, g, b, opacity);
                if (GentleWindows) w.Opacity = 0;
                w.Show();                       // Position -> Show -> click-through, the order
                                                // BubbleCountWindow uses; there is no XID before Show
                // A refusal is about the platform, not about this monitor, so drop the whole set
                // rather than tinting some screens and not others.
                // A refusal is not a stop: the payload timer still ends its hold.
                if (!SkipPlatformChecksForTests && !Accept(w)) { CloseWindows(); return; }
                Windows.Add(w);
                if (GentleWindows)
                {
                    w.Transitions = FadeTransition(GentleVisuals.FadeSeconds);
                    Dispatcher.UIThread.Post(() => { if (Windows.Contains(w)) w.Opacity = 1; });
                }
            }
            _shownOn = want;
            QuestMinutes.Follow(Windows.Count > 0);
            if (s.PinkFilterUnderEffects) SyncOrder();   // a fresh tint maps on top: put the effects back over it
            Log.Debug("Pink filter showing on {Count} screen(s) at {Opacity}%", Windows.Count, s.PinkFilterOpacity);
        }

        /// <summary>The effect windows the tint goes under when <see cref="Models.AppSettings.PinkFilterUnderEffects"/>
        /// is on (#1546): spirals, subliminals, bubbles and bouncing text, the layers WPF's
        /// <c>CompositorLayers.PinkTintUnder</c> slot sits beneath.</summary>
        internal static Func<IEnumerable<Window>> EffectWindows = () =>
            ((global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows
                ?? (IReadOnlyList<Window>)Array.Empty<Window>())
            .Where(w => w.IsVisible && w is SpiralOverlayWindow or SubliminalOverlayWindow or SharedOverlayStageWindow
                                              or BubbleOverlayWindow or BouncingTextOverlayWindow);

        internal static Func<Window, bool> RaiseWindow = w => X11Overlay.Raise(w);

        /// <summary>WPF <c>OverlayService.RefreshPinkFilterOrder</c> (#1546). WPF re-sorts its
        /// compositor layers; this head has one window per effect, so the order is the X stacking
        /// order: under raises the effect windows over the tint, over raises the tint back on top.
        /// No tint on screen: nothing to sort, the next show applies the setting.</summary>
        internal static void SyncOrder()
        {
            if (Windows.Count == 0) return;
            var raise = CoreSettings.Current.PinkFilterUnderEffects ? EffectWindows().ToArray() : Windows.ToArray<Window>();
            foreach (var w in raise)
            {
                try { RaiseWindow(w); }
                catch (Exception ex) { Log.Debug("Pink filter: re-sort raise failed: {E}", ex.Message); }
            }
        }

        private static void HideAll()
        {
            if (Windows.Count == 0) return;
            if (!GentleWindows) { CloseAll(); return; }
            _pulseWeight = null;
            var fading = Windows.ToArray();
            Windows.Clear();
            _shownOn = Array.Empty<int>();
            QuestMinutes.Follow(false);
            var duration = TimeSpan.FromSeconds(GentleVisuals.FadeSeconds);
            foreach (var w in fading)
            {
                FadingWindows.Add(w);
                var opacity = w.Opacity;
                w.Transitions = null;
                w.Opacity = opacity;
                w.Transitions = FadeTransition(duration.TotalSeconds);
                w.Opacity = 0;
                DispatcherTimer.RunOnce(() => { FadingWindows.Remove(w); Close(w); }, duration);
            }
        }

        public static void CloseAll()
        {
            PayloadHold.Clear();
            CloseWindows();
        }

        /// <summary><see cref="CloseAll"/> without dropping a bubble payload's timed hold.</summary>
        private static void CloseWindows()
        {
            _pulseWeight = null;
            foreach (var w in FadingWindows.ToArray()) Close(w);
            FadingWindows.Clear();
            foreach (var w in Windows)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("Pink filter: failed to close a tint window: {E}", ex.Message); }
            }
            Windows.Clear();
            _shownOn = Array.Empty<int>();
            QuestMinutes.Follow(false);
            if (_hookedOwner is not null) { _hookedOwner.Closed -= OnOwnerClosed; _hookedOwner = null; }
        }

        /// <summary>
        /// Which entries of the screen list an effect targets, ported case for case from
        /// <c>App.ResolveScreens</c> (ConditioningControlPanel/App.ScreenResolver.cs). The sentinel
        /// numbers are PERSISTED, so this has to agree with the WPF head exactly:
        /// <see cref="MonitorTarget.All"/> is every monitor, <see cref="MonitorTarget.FollowGlobal"/>
        /// follows the global "Show content on" pick (<c>GlobalTargetMonitor</c>, WPF ResolveFollowGlobal,
        /// App.ScreenResolver.cs:93-123), and 0..N is that one index. An index that no longer exists
        /// (unplugged monitor) falls back WITHOUT rewriting the setting, so the target survives a
        /// reconnect: an effect's own pick to FollowGlobal, the global pick to the primary.
        /// </summary>
        internal static int[] ResolveScreenIndices(int target, int globalTarget, bool dualMonitorEnabled, int screenCount, int primaryIndex)
        {
            if (screenCount <= 0) return Array.Empty<int>();
            if (primaryIndex < 0 || primaryIndex >= screenCount) primaryIndex = 0;

            if (target == MonitorTarget.All) return Enumerable.Range(0, screenCount).ToArray();
            if (target >= 0 && target < screenCount) return new[] { target };
            if (globalTarget == MonitorTarget.All) return Enumerable.Range(0, screenCount).ToArray();
            if (globalTarget >= 0) return new[] { globalTarget < screenCount ? globalTarget : primaryIndex };
            return dualMonitorEnabled ? Enumerable.Range(0, screenCount).ToArray() : new[] { primaryIndex };
        }

        /// <summary>
        /// WPF gates the persistent tint on the session engine: <c>RefreshOverlays</c> returns
        /// early when <c>!_isRunning</c>, so the checkbox there arms the effect and
        /// <c>OverlayService.Start()</c> is what puts it on screen.
        ///
        /// <para>Mirrored here: the desktop path seeds <see cref="CoreSession"/> from
        /// <see cref="CoreEngine"/>, and Start/Stop refresh the tint. Unseeded (renders, tests)
        /// means no engine at all, and the card owns the tint.</para>
        /// </summary>
        /// <para>A session counts as running (WPF ResumeSession's App.Overlay.Start() after a panic) and a
        /// paused one hides the tint (PauseSession's App.Overlay.Stop()).</para>
        private static bool ShouldShow()
            => PulseHold || (!RemoteCommands.OverlaysStopped && (RemoteCommands.OverlayRunning || App.Sessions?.IsPaused != true
               && (CoreSession.IsEngineRunningProvider is null || CoreSession.IsEngineRunning || App.Sessions?.IsRunning == true)));

        // Start (MainShellWindow.StartEngine) is what restores a tint left enabled - WPF OverlayService.Start().

        /// <summary>The two safety refusals. False means the window has already been closed.</summary>
        private static bool Accept(TintOverlayWindow w)
        {
            if (!X11Overlay.SetClickThrough(w, true))
            {
                Log.Warning("Pink filter: this platform cannot make the tint click-through, so it is not shown - a full-screen topmost window that swallows clicks would lock the desktop");
                _refused = true;
                Close(w);
                return false;
            }

            if (w.ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                Log.Warning("Pink filter: the window manager refused per-pixel transparency (no compositor), so the tint is not shown - it would paint an opaque block, not a wash");
                _refused = true;
                Close(w);
                return false;
            }

            return true;
        }

        private static void Close(Window w)
        {
            try { w.Close(); } catch (Exception ex) { Log.Debug("Pink filter: close failed: {E}", ex.Message); }
        }

        /// <summary>
        /// Avalonia's default <c>ShutdownMode</c> is <c>OnLastWindowClose</c>, and these windows
        /// are windows. Without this, closing the shell while the tint is up leaves a live process
        /// behind a pink sheet with no UI to turn it off.
        /// </summary>
        private static void HookOwner(Window? owner)
        {
            if (owner is null || ReferenceEquals(owner, _hookedOwner)) return;
            if (_hookedOwner is not null) _hookedOwner.Closed -= OnOwnerClosed;
            _hookedOwner = owner;
            owner.Closed += OnOwnerClosed;
        }

        private static void OnOwnerClosed(object? sender, EventArgs e) => CloseAll();
    }
}

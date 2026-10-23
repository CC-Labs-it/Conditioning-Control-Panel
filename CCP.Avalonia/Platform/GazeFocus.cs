using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Serilog;
using static ConditioningControlPanel.Services.GazeTargetScoring;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// PORTED (slice) from ConditioningControlPanel/Services/Tracking/GazeFocusService.cs: the shared
    /// dwell engine behind the Play door's Focus Gaze switch. Looking at an ambient bubble for 600 ms,
    /// or blinking while looking near it, pops it through the bubble's own pop pipeline (sound, XP,
    /// quests, haptics). Follows the camera, never powers it (WPF EvaluateDesiredState).
    ///
    /// <para>ponytail: the two-stage refine overlay and gaze-lock attractor/debug cursor remain unported.</para>
    ///
    /// <para>Coordinates: OnGazeMove is GAZE space (DIPs local to the calibrated monitor); bubbles are
    /// global DIPs (screen px / scaling). The conversion is the calibrated monitor's origin, a pure
    /// translation (WPF CalOriginDips), here read from the saved MonitorBounds.</para>
    /// </summary>
    internal sealed class GazeFocus
    {
        internal static GazeFocus Instance { get; } = new();

        internal const int DefaultDwellMs = 600;   // WPF DefaultDwellMs
        internal const int CooldownMs = 250;       // WPF CooldownMs
        private const int TickMs = 33;             // WPF TickMs (~30 FPS)

        internal TimeProvider Clock = TimeProvider.System;
        /// <summary>WPF canRun: camera running, calibrated, consent current. Tests swap it.</summary>
        internal Func<bool> CanRun = () => WebcamTracker.Instance.IsRunning
                                           && WebcamTracker.Instance.Calibration != null
                                           && WebcamConsent.IsCurrent(CoreSettings.Current);
        /// <summary>The bubble's own pop (WPF Bubble.PopByGaze -> Pop). Tests swap it.</summary>
        internal Action<AmbientBubble> PopBubble = BubbleOverlay.Pop;

        private DispatcherTimer? _timer;
        private Point? _lastGazePoint;
        private bool _faceLost, _subscribed, _attached, _masterEnabled;
        private DateTimeOffset _dwellStartedAt;
        private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;
        private AmbientBubble? _currentBubble;
        private FlashOverlayWindow? _currentFlash;
        private MandatoryVideoOverlay.Target? _currentVideo;
        private DateTimeOffset _lastLingerBoost = DateTimeOffset.MinValue;
        private AppSettings? _settings;

        public bool IsActive { get; private set; }
        public int DwellMs { get; set; } = DefaultDwellMs;

        /// <summary>Fires when IsActive flips, on the UI thread.</summary>
        public event Action<bool>? ActiveChanged;
        /// <summary>A bubble was popped by gaze (dwell or blink). UI thread.</summary>
        public event Action? GazePopped;

        /// <summary>The Focus Gaze switch: one of the consumers that want the engine alive.</summary>
        public bool MasterEnabled
        {
            get => _masterEnabled;
            set
            {
                if (_masterEnabled == value) return;
                _masterEnabled = value;
                EvaluateDesiredState();
            }
        }

        /// <summary>WPF ctor hooks: re-evaluate on the camera's state and on the per-feature gaze flags.
        /// Once per process; <see cref="Detach"/> undoes it (P41).</summary>
        internal void Attach()
        {
            if (_attached) return;
            _attached = true;
            WebcamTracker.Instance.StateChanged += EvaluateDesiredState;
            _settings = CoreSettings.Current;
            if (_settings != null) _settings.PropertyChanged += OnSettingsChanged;
            EvaluateDesiredState();
        }

        internal void Detach()
        {
            if (!_attached) return;
            _attached = false;
            WebcamTracker.Instance.StateChanged -= EvaluateDesiredState;
            if (_settings != null) _settings.PropertyChanged -= OnSettingsChanged;
            _settings = null;
            Stop();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AppSettings.FlashGazePopEnabled):
                case nameof(AppSettings.FlashGazeLingerEnabled):
                case nameof(AppSettings.BubbleGazePopEnabled):
                case nameof(AppSettings.VideoGazeClickEnabled):
                    EvaluateDesiredState();
                    break;
            }
        }

        private static bool AnyConsumerOn()
        {
            var s = CoreSettings.Current;
            return s != null && (s.FlashGazePopEnabled || s.FlashGazeLingerEnabled || s.BubbleGazePopEnabled || s.VideoGazeClickEnabled);
        }

        /// <summary>WPF BubblesGazeEnabled: the master switch or the bubble's own "stare to pop" flag.</summary>
        private bool BubblesGazeEnabled => _masterEnabled || CoreSettings.Current?.BubbleGazePopEnabled == true;

        /// <summary>WPF EvaluateDesiredState: run when a consumer wants it AND the camera can feed it.
        /// Never starts the camera and never asks for consent. Idempotent, UI-thread.</summary>
        public void EvaluateDesiredState()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(EvaluateDesiredState); return; }
            bool wants = _masterEnabled || AnyConsumerOn();
            bool canRun;
            try { canRun = CanRun(); } catch { canRun = false; }
            if (wants && canRun) { if (!IsActive) Start(); }
            else if (IsActive) Stop();
        }

        private void Start()
        {
            Subscribe();
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(TickMs) };
            _timer.Tick += OnTimerTick;
            _timer.Start();
            IsActive = true;
            try { ActiveChanged?.Invoke(true); } catch { }
            Log.Information("GazeFocus: active");
        }

        /// <summary>WPF Stop: leaves the camera running (other features share it).</summary>
        public void Stop()
        {
            if (!IsActive) return;
            Unsubscribe();
            if (_timer != null) { _timer.Stop(); _timer.Tick -= OnTimerTick; _timer = null; }
            ClearTarget();
            _lastGazePoint = null;
            _faceLost = false;
            _cooldownUntil = DateTimeOffset.MinValue;
            IsActive = false;
            try { ActiveChanged?.Invoke(false); } catch { }
            Log.Information("GazeFocus: inactive");
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            var t = WebcamTracker.Instance;
            t.OnGazeMove += HandleGazeMove;
            t.OnFaceLost += HandleFaceLost;
            t.OnFaceFound += HandleFaceFound;
            t.OnBlink += HandleBlink;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            var t = WebcamTracker.Instance;
            t.OnGazeMove -= HandleGazeMove;
            t.OnFaceLost -= HandleFaceLost;
            t.OnFaceFound -= HandleFaceFound;
            t.OnBlink -= HandleBlink;
            _subscribed = false;
        }

        /// <summary>WPF CalOriginDips/GazeToDesktop: the calibrated monitor's origin in global DIPs, or
        /// none for a primary-monitor (or pre-hotfix, DeviceName-less) calibration.</summary>
        internal static Point GazeToDesktop(Point p)
        {
            if (WebcamTracker.Instance.Calibration?.MonitorBounds is not { DeviceName: not null } mb || (mb.X == 0 && mb.Y == 0)) return p;
            double dpi = mb.DpiScale is > 0.25 and < 8.0 ? mb.DpiScale : 1.0;
            return new Point(p.X + mb.X / dpi, p.Y + mb.Y / dpi);
        }

        internal void HandleGazeMove(Point p) => _lastGazePoint = GazeToDesktop(p);
        internal void HandleFaceLost() => _faceLost = true;
        internal void HandleFaceFound() => _faceLost = false;

        /// <summary>WPF HandleBlink: a blink pops the best target under the gaze, then cools down.</summary>
        internal void HandleBlink()
        {
            try
            {
                var now = Clock.GetUtcNow();
                if (now < _cooldownUntil || _faceLost || _lastGazePoint is not { } p) return;
                var target = FindBestTarget(p);
                if (target is not AmbientBubble && target is not FlashOverlayWindow) return; // WPF: blinks never click video
                ClearTarget();
                if (target is AmbientBubble b) Pop(b);
                else if (CoreSettings.Current.FlashGazePopEnabled) FlashOverlay.Pop((FlashOverlayWindow)target, fromGaze: true);
                _cooldownUntil = now.AddMilliseconds(CooldownMs);
            }
            catch (Exception ex) { Log.Debug("GazeFocus blink handler error: {Error}", ex.Message); }
        }

        private void OnTimerTick(object? sender, EventArgs e) => Tick();

        /// <summary>WPF OnTick (bubble branch): cooldown, face lost, best target, dwell.</summary>
        internal void Tick()
        {
            try
            {
                if (Clock.GetUtcNow() < _cooldownUntil || _faceLost || _lastGazePoint is not { } p) { ClearTarget(); return; }
                var target = FindBestTarget(p);
                if (target == null) { ClearTarget(); return; }
                if (target is AmbientBubble b) AdvanceBubbleDwell(b);
                else AdvanceOtherDwell(target);
            }
            catch (Exception ex) { Log.Debug("GazeFocus tick error: {Error}", ex.Message); }
        }

        /// <summary>WPF FindBestTarget: topmost first within each type, geometry + type prior + sticky.</summary>
        private object? FindBestTarget(Point p)
        {
            object? best = null;
            double bestScore = ScoreThreshold;
            void Consider(object target, Rect r, bool flash, bool sticky)
            {
                if (r.Width <= 0 || r.Height <= 0 || !TargetOnCalibratedScreen(r)) return;
                var d = DistanceFromRectEdge(r.X, r.Y, r.Width, r.Height, p.X, p.Y);
                var score = (flash ? FlashBaseScore(d) : BubbleBaseScore(d)) + (sticky ? StickyBonus : 0);
                if (score > bestScore) { bestScore = score; best = target; }
            }
            var s = CoreSettings.Current;
            if (s.FlashGazePopEnabled || s.FlashGazeLingerEnabled)
                for (int i = FlashOverlay.Active.Count - 1; i >= 0; i--)
                {
                    var (w, r) = FlashOverlay.Active[i];
                    if (w.IsFadingOut || w.Exit != null) continue;
                    var k = w.DesktopScaling > 0 ? w.DesktopScaling : 1;
                    Consider(w, new Rect(r.X / k, r.Y / k, r.Width / k, r.Height / k), true, ReferenceEquals(_currentFlash, w));
                }
            if (BubblesGazeEnabled)
                for (int i = BubbleOverlay.Field.Bubbles.Count - 1; i >= 0; i--)
                {
                    var b = BubbleOverlay.Field.Bubbles[i];
                    if (b.Clickable && !b.Popping) Consider(b, new Rect(b.X, b.Y, b.Size, b.Size), false, ReferenceEquals(_currentBubble, b));
                }
            if (s.VideoGazeClickEnabled)
                foreach (var t in MandatoryVideoOverlay.Instance.GazeTargets)
                    Consider(t, t.GazeBounds, false, ReferenceEquals(_currentVideo, t));
            return best;
        }

        // WPF defensive gaze-read clamp: use the target centre; no calibration means fail open.
        internal static bool TargetOnCalibratedScreen(Rect r)
        {
            if (!CoreSettings.Current.RestrictGazeContentToCalibratedScreen || WebcamTracker.Instance.Calibration?.MonitorBounds?.DeviceName is not { } name) return true;
            // WPF GetCalibratedScreen: an unplugged/unknown calibration fails open.
            foreach (var screen in WebcamScreens.Enumerate())
                if (string.Equals(WebcamScreens.Name(screen), name, StringComparison.OrdinalIgnoreCase))
                {
                    var b = screen.Bounds; var k = screen.Scaling;
                    return new Rect(b.X / k, b.Y / k, b.Width / k, b.Height / k).Contains(r.Center);
                }
            return true;
        }

        private void AdvanceOtherDwell(object target)
        {
            var now = Clock.GetUtcNow();
            if (!ReferenceEquals(_currentFlash, target) && !ReferenceEquals(_currentVideo, target))
            {
                ClearTarget();
                _currentFlash = target as FlashOverlayWindow;
                _currentVideo = target as MandatoryVideoOverlay.Target;
                _dwellStartedAt = now;
            }
            var elapsed = (now - _dwellStartedAt).TotalMilliseconds;
            if (_currentFlash is { } flash)
            {
                var s = CoreSettings.Current;
                if (s.FlashGazeLingerEnabled && (now - _lastLingerBoost).TotalMilliseconds >= 250)
                {
                    flash.BoostLifetime(s.FlashGazeLingerExtensionMs);
                    _lastLingerBoost = now;
                }
                if (!s.FlashGazePopEnabled) return; // linger alone never inflates or dismisses
                flash.SetGazeDwellProgress(elapsed / DwellMs);
            }
            if (elapsed < DwellMs) return;
            var f = _currentFlash; var v = _currentVideo;
            ClearTarget();
            if (f != null) FlashOverlay.Pop(f, fromGaze: true);
            else v?.Hit();
            _cooldownUntil = now.AddMilliseconds(CooldownMs);
        }

        private void AdvanceBubbleDwell(AmbientBubble b)
        {
            var now = Clock.GetUtcNow();
            if (!ReferenceEquals(_currentBubble, b))
            {
                ClearTarget();
                _currentBubble = b;
                _dwellStartedAt = now;
            }
            var elapsedMs = (now - _dwellStartedAt).TotalMilliseconds;
            b.Scale = 1.0 + Math.Clamp(elapsedMs / DwellMs, 0, 1) * 0.25;   // WPF SetGazeDwellProgress inflate
            if (elapsedMs < DwellMs) return;
            _currentBubble = null;
            Pop(b);
            _cooldownUntil = now.AddMilliseconds(CooldownMs);
        }

        private void Pop(AmbientBubble b)
        {
            try
            {
                PopBubble(b);
                GazePopped?.Invoke();
                BarkHead.Engine?.Raise("GazePopped"); // WPF BarkService.WireSubscriptions
                App.Achievements?.TrackGazePop();   // WPF GamificationBridge.OnGazePopped
            }
            catch (Exception ex) { Log.Debug("Gaze bubble pop failed: {Error}", ex.Message); }
        }

        private void ClearTarget()
        {
            if (_currentBubble is { Popping: false } b) b.Scale = 1;
            _currentBubble = null;
            _currentFlash?.SetGazeDwellProgress(0);
            _currentFlash = null;
            _currentVideo = null;
            _lastLingerBoost = DateTimeOffset.MinValue;
        }
    }
}

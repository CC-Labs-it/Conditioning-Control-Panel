using System;
using System.ComponentModel;
using System.Linq;
using System.Collections.Generic;
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
    /// <para>The debug cursor remains unported.</para>
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
        internal Func<IReadOnlyList<object>, IReadOnlyList<Rect>, GazeRefineOverlay?> MakeRefine = GazeRefineOverlay.Open;
        private GazeRefineOverlay? _refine;
        private List<object>? _refineTargets;
        private DateTimeOffset _opened, _chipStart, _outside, _lost, _suppressedUntil;
        private int _chip = -1;
        private DateTimeOffset _lastRaise = DateTimeOffset.MinValue;
        internal Action<GazeRefineOverlay> RaiseRefine = p => X11Overlay.Raise(p);

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

        // WPF GazeFocusService.cs:1004-1010: base-score choices for a future refine panel.
        // These are recomputed on every target scan and never activate a target themselves.
        internal List<object> RefineCandidates { get; } = new();
        private readonly List<(object Target, GazeScore Score)> _scored = new();

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
            CloseRefine(false);
            _suppressedUntil = DateTimeOffset.MinValue;
            RefineCandidates.Clear();
            _scored.Clear();
            ClearTarget();
            SetCursorLock(null); // WPF GazeFocusService.cs:290
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

        /// <summary>Resolve on every gaze event so a sleeping display can return at a new origin.
        /// The primary fallback never replaces the calibration's saved identity or geometry.</summary>
        internal static Point GazeToDesktop(Point p)
        {
            var bounds = WebcamTracker.Instance.Calibration?.MonitorBounds;
            var screen = WebcamScreens.Resolve(WebcamScreens.Enumerate(), bounds?.DeviceName);
            if (screen == null) return p;
            // WPF CalOriginDips uses the saved calibration scale even after a display scale change.
            var dpi = bounds is { DpiScale: > 0.25 and < 8.0 } ? bounds.DpiScale : 1.0;
            return new Point(p.X + screen.Bounds.X / dpi, p.Y + screen.Bounds.Y / dpi);
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
                if (_refine != null || now < _cooldownUntil || _faceLost || _lastGazePoint is not { } p) return;
                var target = FindBestTarget(p);
                if (target is null) return;
                ClearTarget(); // WPF: a blink on video clicks nothing but still clears the dwell and cools down
                if (target is AmbientBubble b) Pop(b);
                else if (target is FlashOverlayWindow fw && CoreSettings.Current.FlashGazePopEnabled) FlashOverlay.Pop(fw, fromGaze: true);
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
                if (_refine != null) { TickRefine(); return; }
                if (Clock.GetUtcNow() < _cooldownUntil || _faceLost || _lastGazePoint is not { } p) { RefineCandidates.Clear(); _scored.Clear(); ClearTarget(); SetCursorLock(null); return; }
                var target = FindBestTarget(p);
                if (target == null) { ClearTarget(); SetCursorLock(null); return; }
                SetCursorLock(TargetBounds(target));
                if (target is AmbientBubble b) AdvanceBubbleDwell(b);
                else AdvanceOtherDwell(target);
            }
            catch (Exception ex) { Log.Debug("GazeFocus tick error: {Error}", ex.Message); if (_refine != null) CloseRefine(true); }
        }

        // WPF GazeFocusService.cs:531-592: lock every live target, in tracker-local DIPs.
        private static Rect? TargetBounds(object target)
        {
            if (target is AmbientBubble b) return new Rect(b.X, b.Y, b.Size, b.Size);
            if (target is MandatoryVideoOverlay.Target v) return v.GazeBounds;
            foreach (var (w, r) in FlashOverlay.Active)
                if (ReferenceEquals(w, target))
                {
                    var k = w.DesktopScaling > 0 ? w.DesktopScaling : 1;
                    return new Rect(r.X / k, r.Y / k, r.Width / k, r.Height / k);
                }
            return null;
        }

        internal static void SetCursorLock(Rect? bounds)
        {
            // Calibration's bubble test owns the same tracker attractor (WPF :574).
            if (Views.Windows.WebcamCalibrationWindow.IsShowing) return;
            var tracker = WebcamTracker.Instance;
            if (bounds is not { Width: > 0, Height: > 0 } r)
            {
                tracker.ClearGazeAttractor();
                return;
            }
            var origin = GazeToDesktop(default);
            tracker.SetGazeAttractor(r.Center.X - origin.X, r.Center.Y - origin.Y,
                Math.Clamp(Math.Max(r.Width, r.Height) / 2 + 30, 50, 240));
        }

        /// <summary>WPF FindBestTarget: topmost first within each type, geometry + type prior + sticky.</summary>
        private object? FindBestTarget(Point p)
        {
            object? best = null;
            double bestScore = ScoreThreshold;
            var scored = _scored;
            scored.Clear();
            RefineCandidates.Clear();
            void Consider(object target, Rect r, bool flash, bool sticky)
            {
                if (r.Width <= 0 || r.Height <= 0 || !TargetOnCalibratedScreen(r)) return;
                var d = DistanceFromRectEdge(r.X, r.Y, r.Width, r.Height, p.X, p.Y);
                var baseScore = flash ? FlashBaseScore(d) : BubbleBaseScore(d);
                scored.Add((target, new GazeScore(baseScore, r.X, r.Y, r.Width, r.Height)));
                var score = baseScore + (sticky ? StickyBonus : 0);
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
            var lead = AmbiguousLeader(scored, x => x.Score);
            if (lead >= 0 && ReferenceEquals(scored[lead].Target, best))
            {
                foreach (var choice in RefineChoices(scored, x => x.Score))
                {
                    // A linger-only flash must never be offered as a pop choice (WPF :687-691).
                    if (choice.Target is FlashOverlayWindow && !s.FlashGazePopEnabled) continue;
                    RefineCandidates.Add(choice.Target);
                }
                if (RefineCandidates.Count < 2) RefineCandidates.Clear();
            }
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
            if (TryRaiseRefine()) return;
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
            if (TryRaiseRefine()) return;
            _currentBubble = null;
            Pop(b);
            _cooldownUntil = now.AddMilliseconds(CooldownMs);
        }

        // WPF GazeFocusService.cs:605-870: the panel owns input until selection or cancellation.
        private bool TryRaiseRefine()
        {
            if (Clock.GetUtcNow() < _suppressedUntil || RefineCandidates.Count < 2 || Views.Windows.WebcamCalibrationWindow.IsShowing) return false;
            var choices = new List<object>();
            var bounds = new List<Rect>();
            foreach (var target in RefineCandidates)
                if (Alive(target) && TargetBounds(target) is { Width: > 0, Height: > 0 } r) { choices.Add(target); bounds.Add(r); if (choices.Count == MaxRefineCandidates) break; }
            if (choices.Count < 2) return false;
            try
            {
                var panel = MakeRefine(choices, bounds);
                if (panel == null) return false;
                _refine = panel;
                _refineTargets = choices;
                _opened = Clock.GetUtcNow(); _outside = _lost = _lastRaise = DateTimeOffset.MinValue; _chip = -1;
                ClearTarget(); SetCursorLock(null);
                return true;
            }
            catch (Exception ex) { Log.Debug("Gaze refine open failed: {Error}", ex.Message); CloseRefine(true); return false; }
        }

        private bool Alive(object target)
        {
            if (target is AmbientBubble b) return BubblesGazeEnabled && b.Clickable && !b.Popping && BubbleOverlay.Field.Bubbles.Contains(b);
            if (target is FlashOverlayWindow f) return CoreSettings.Current.FlashGazePopEnabled && !f.IsFadingOut && f.Exit == null && FlashOverlay.Active.Exists(x => ReferenceEquals(x.Window, f));
            if (target is MandatoryVideoOverlay.Target v) return CoreSettings.Current.VideoGazeClickEnabled && MandatoryVideoOverlay.Instance.GazeTargets.Contains(v) && v.GazeBounds is { Width: > 0, Height: > 0 };
            return false;
        }

        private void TickRefine()
        {
            var panel = _refine!; var now = Clock.GetUtcNow();
            if ((now - _lastRaise).TotalMilliseconds >= 200) { RaiseRefine(panel); _lastRaise = now; }
            if ((now - _opened).TotalMilliseconds >= 6000) { CloseRefine(true); return; }
            int alive = 0;
            foreach (var target in _refineTargets!) if (Alive(target)) alive++;
            if (alive < 2) { CloseRefine(false); return; }
            if (_faceLost || _lastGazePoint is not { } p)
            {
                if (_lost == DateTimeOffset.MinValue) _lost = now;
                else if ((now - _lost).TotalMilliseconds >= 1200) { CloseRefine(true); return; }
                panel.Progress(-1, 0); _chip = -1; SetCursorLock(null); return;
            }
            _lost = DateTimeOffset.MinValue;
            int hit = panel.HitTestChip(p);
            if (hit < 0)
            {
                panel.Progress(-1, 0); _chip = -1; SetCursorLock(null);
                if (panel.EscapeBounds.Contains(p)) { _outside = DateTimeOffset.MinValue; return; }
                if (_outside == DateTimeOffset.MinValue) _outside = now;
                else if ((now - _outside).TotalMilliseconds >= 700) CloseRefine(true);
                return;
            }
            _outside = DateTimeOffset.MinValue;
            SetCursorLock(panel.ChipBounds(hit));
            if (_chip != hit) { _chip = hit; _chipStart = now; }
            var elapsed = (now - _chipStart).TotalMilliseconds;
            panel.Progress(hit, elapsed / 550);
            if (elapsed < 550) return;
            var selected = _refineTargets![hit];
            CloseRefine(false);
            if (Alive(selected))
            {
                if (selected is AmbientBubble b) Pop(b);
                else if (selected is FlashOverlayWindow f) FlashOverlay.Pop(f, fromGaze: true);
                else if (selected is MandatoryVideoOverlay.Target v) v.Hit();
            }
            _cooldownUntil = now.AddMilliseconds(CooldownMs);
        }

        private void CloseRefine(bool suppress)
        {
            var panel = _refine; _refine = null; _refineTargets = null; _chip = -1;
            if (suppress) _suppressedUntil = Clock.GetUtcNow().AddMilliseconds(4000);
            try { panel?.Close(); } catch (Exception ex) { Log.Debug("Gaze refine close failed: {Error}", ex.Message); }
            SetCursorLock(null);
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

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The For You feed's ghost mode (WPF FypHostService.cs:428-830, FypGhostOverlay.cs): the feed covers its
    /// monitor, see-through at the stored FypWindowOpacity and click-through, with two clickable corner buttons
    /// (gear = leave ghost and open options, speaker = mute). WPF needs a DWM-thumbnail mirror because layered
    /// alpha turns a WebView2 child black (FypGhostOverlay.cs:14). On X11 the compositor applies
    /// _NET_WM_WINDOW_OPACITY to the whole redirected toplevel, the WebKitGTK child included, and an empty input
    /// shape on the toplevel clips every child's input, so the live feed window itself is the ghost. Windows uses
    /// WPF's own mechanism: the feed parks off-screen, still shown, and <see cref="Win32GhostMirror"/> shows a live
    /// DWM thumbnail of it (FypHostService.cs:503-563). Native Wayland or headless (no X11 calls) refuse, as WPF
    /// refuses a mirror that cannot compose (FypGhostOverlay.cs:94).
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>WPF FypGhostOverlay.Diagnose order: null when ghost mode can run here. On Windows the mirror
        /// itself checks composition (<see cref="Win32GhostMirror.Diagnose"/>).</summary>
        internal static string? FypGhostRefusal(bool windows, bool x11, bool compositing) =>
            windows ? null : !x11 ? "unsupported" : !compositing ? "composition-disabled" : null;

        // Seams (tests swap and restore them): the platform verdict and the two X11 calls.
        internal static Func<string?> FypGhostSupport = () =>
            FypGhostRefusal(OperatingSystem.IsWindows(), X11Overlay.IsAvailable, X11Overlay.IsCompositing);
        internal static Func<TopLevel, bool, bool> FypGhostClickThrough = X11Overlay.SetClickThrough;
        internal static Func<TopLevel, double, bool> FypGhostOpacity = X11Overlay.SetOpacity;
        internal static Func<DateTime> FypGhostNow = () => DateTime.UtcNow;
        internal static Func<Window, PixelRect, bool, bool> FypGhostButtonConfigure = (w, bounds, passive) => X11Overlay.SetOverrideRedirect(w, bounds, passive);
        internal static Func<TopLevel, bool> FypGhostRaise = X11Overlay.Raise;
        internal static Func<TopLevel, bool> FypGhostRaiseManaged = X11Overlay.RaiseManaged;
        internal static Func<Action, IDisposable> FypGhostStartTick = tick =>
            DispatcherTimer.Run(() => { tick(); return true; }, TimeSpan.FromSeconds(5));
        internal static Func<Action, IDisposable> FypGhostStartButtonRepair = tick =>
            DispatcherTimer.Run(() => { tick(); return true; }, TimeSpan.FromMilliseconds(250));
        private IDisposable? _fypGhostTimer;
        private IDisposable? _fypGhostButtonRepair;
        // Windows: the DWM mirror over the home monitor (physical px). Null where the feed itself is the ghost.
        internal static Func<TopLevel, PixelRect, double, (IDisposable? Mirror, string? Why)>? FypGhostMirror =
            OperatingSystem.IsWindows() ? Win32GhostMirror.Open : null;
        internal static Action<IDisposable, double> FypGhostMirrorOpacity = (m, a) => (m as Win32GhostMirror)?.SetOpacity(a);
        // WPF :797-803 watchdog + :828 topmost: false = colour key lost. :748 fresh thumbnail after a heal.
        internal static Func<IDisposable, bool> FypGhostMirrorTick = m => (m as Win32GhostMirror)?.Tick() ?? true;
        internal static Action<IDisposable> FypGhostMirrorRefresh = m => (m as Win32GhostMirror)?.Refresh();
        private IDisposable? _fypMirror;
        private bool _fypMirrored;
        private PixelPoint _fypPark;
        private static double ClampFypGhostOpacity(double value) =>
            double.IsFinite(value) ? Math.Clamp(value, 0.01, 1.0) : 1.0;

        private bool _fypGhosted;
        private DateTime _fypGhostExitUtc = DateTime.MinValue;
        private (PixelPoint Pos, double W, double H, WindowState State, WindowDecorations Deco, bool Top) _fypRestore;
        private readonly List<Window> _fypGhostButtons = new();
        private IconGlyph? _fypMuteGlyph;

        internal bool IsFypGhosted => _fypGhosted;
        internal IReadOnlyList<Window> FypGhostButtons => _fypGhostButtons;

        /// <summary>WPF RecentlyUnghosted (:58): the reflexive second press after rung 1.</summary>
        private bool FypRecentlyUnghosted => (FypGhostNow() - _fypGhostExitUtc).TotalMilliseconds < 1500;

        /// <summary>WPF EnterGhost (:458). Any refusal un-ghosts first, then tells the page why (:573-577).</summary>
        private void EnterFypGhost()
        {
            if (_fypGhosted || _closing) return;
            var why = FypGhostSupport();
            if (why == null)
            {
                _fypRestore = (Position, Width, Height, WindowState, WindowDecorations, Topmost);
                _fypGhosted = true;   // armed before the window moves: ExitFypGhost is the only way out (:493)
                try
                {
                    // Manual monitor-fill bounds, never a Maximized/FullScreen state change (FypGhostOverlay.cs:110).
                    var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
                    var home = screen?.Bounds ?? new PixelRect(Position, new PixelSize((int)Width, (int)Height));
                    WindowState = WindowState.Normal;
                    WindowDecorations = WindowDecorations.None;
                    Topmost = true;
                    if (screen != null)
                    {
                        Position = screen.Bounds.Position;
                        Width = screen.Bounds.Width / screen.Scaling;
                        Height = screen.Bounds.Height / screen.Scaling;
                    }
                    if (FypGhostMirror is { } open)
                    {
                        // WPF :503-525: park the real window past every monitor's right edge, still shown, so
                        // DWM keeps composing it. Never alpha or click-through on the feed: WebView2 goes black.
                        _fypMirrored = true;
                        Position = _fypPark = new PixelPoint(Screens.All.Select(s => s.Bounds.Right).Append(home.Right).Max() + 200, home.Y);
                        (_fypMirror, why) = open(this, home, ClampFypGhostOpacity(CoreSettings.Current.FypWindowOpacity));
                        if (why == null && _fypMirror == null) why = "mirror-failed";
                        if (why == null)
                        {
                            ShowFypGhostButtons(home, screen?.Scaling ?? 1);
                            _fypGhostTimer = FypGhostStartTick(ReassertFypGhost) ?? throw new InvalidOperationException("ghost timer failed to start");
                        }
                    }
                    else if (!FypGhostOpacity(this, ClampFypGhostOpacity(CoreSettings.Current.FypWindowOpacity)) || !FypGhostClickThrough(this, true))
                        why = "mirror-failed";
                    else
                    {
                        ShowFypGhostButtons(home, screen?.Scaling ?? 1);
                        _fypGhostTimer = FypGhostStartTick(ReassertFypGhost) ?? throw new InvalidOperationException("ghost timer failed to start");
                        _fypGhostButtonRepair = FypGhostStartButtonRepair(RepairFypGhostButtons) ?? throw new InvalidOperationException("ghost button timer failed to start");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning("[Game] fyp: ghost enter failed: {E}", ex.Message);
                    why = "mirror-threw";
                }
                if (why == null) { Log.Information("[Game] fyp: ghost mode on"); return; }
                ExitFypGhost();
            }
            Log.Information("[Game] fyp: ghost mode unavailable ({Why}), staying solid", why);
            Post(new { type = "clickThrough", on = false });
            Post(new { type = "ghost-unavailable", reason = why });
        }

        /// <summary>WPF ExitGhost (:630): buttons gone, input and alpha back, the window where it was, the page told.
        /// The panic ladder's first rung; idempotent.</summary>
        internal void ExitFypGhost()
        {
            if (!_fypGhosted) return;
            _fypGhosted = false;
            _fypGhostTimer?.Dispose();
            _fypGhostTimer = null;
            _fypGhostButtonRepair?.Dispose();
            _fypGhostButtonRepair = null;
            foreach (var b in _fypGhostButtons)
                try { b.Close(); } catch (Exception ex) { Log.Debug("[Game] fyp: ghost button close: {E}", ex.Message); }
            _fypGhostButtons.Clear();
            _fypMuteGlyph = null;
            try { _fypMirror?.Dispose(); } catch (Exception ex) { Log.Debug("[Game] fyp: ghost mirror close: {E}", ex.Message); }
            _fypMirror = null;
            try
            {
                if (!_fypMirrored)
                {
                    FypGhostClickThrough(this, false);
                    FypGhostOpacity(this, 1.0);
                }
                if (!_closing)
                {
                    var r = _fypRestore;
                    (WindowDecorations, Topmost, Position, Width, Height) = (r.Deco, r.Top, r.Pos, r.W, r.H);
                    WindowState = r.State;
                }
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost restore: {E}", ex.Message); }
            _fypMirrored = false;
            _fypGhostExitUtc = FypGhostNow();
            Post(new { type = "clickThrough", on = false });
            Log.Information("[Game] fyp: ghost mode off");
        }

        internal void ReassertFypGhost()
        {
            if (!_fypGhosted) return;
            if (_fypMirror != null && !FypGhostMirrorTick(_fypMirror))
            {
                // WPF :797-803: take the ghost down rather than leave a black sheet over the monitor.
                Log.Information("[Game] fyp: ghost colour key stopped applying, dropping ghost mode");
                ExitFypGhost();
                Post(new { type = "ghost-unavailable", reason = "colorkey-lost" });
                return;
            }
            try
            {
                if (!_fypMirrored) FypGhostRaiseManaged(this);   // Windows: the feed stays parked, the mirror was raised
                RaiseFypGhostButtons(_fypGhostButtons);
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost raise: {E}", ex.Message); }
        }

        // WM requests are asynchronous. Keep repairing the controls after a late frame raise,
        // rather than assuming a fixed delay is long enough for every window manager.
        private void RepairFypGhostButtons()
        {
            if (!_fypGhosted) return;
            try { RaiseFypGhostButtons(_fypGhostButtons); }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost button raise: {E}", ex.Message); }
        }

        /// <summary>WPF OnGhostWindowStateChanged (:748): a minimized parked source goes back to Normal, back to
        /// its parking spot, with a fresh thumbnail, and the ghost keeps running.</summary>
        internal void HealFypGhostSource()
        {
            if (!_fypGhosted || !_fypMirrored || _closing) return;
            if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; Position = _fypPark; }
            if (_fypMirror != null) FypGhostMirrorRefresh(_fypMirror);
            Log.Information("[Game] fyp: parked ghost source healed");
        }

        /// <summary>WPF GhostWndProc (:695): while mirrored, veto SC_MINIMIZE (Show Desktop, Win+D, Win+M) and
        /// the owner cascade hide, then heal (:721). Never swallows anything otherwise.</summary>
        internal IntPtr FypGhostWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!_fypGhosted || !_fypMirrored) return IntPtr.Zero;
            if (msg == 0x0112 /* WM_SYSCOMMAND */ && ((long)wParam & 0xFFF0) == 0xF020 /* SC_MINIMIZE */) handled = true;
            else if (msg == 0x0018 /* WM_SHOWWINDOW */ && wParam == IntPtr.Zero)
            {
                if (lParam != IntPtr.Zero) handled = true;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_fypGhosted) return;
                    if (OperatingSystem.IsWindows() && hwnd != IntPtr.Zero && !Win32GhostMirror.IsWindowVisible(hwnd))
                        Win32GhostMirror.ShowNative(hwnd, 8 /* SW_SHOWNA */);
                    HealFypGhostSource();
                });
            }
            return IntPtr.Zero;
        }

        internal static void RaiseFypGhostButtons(IReadOnlyList<Window> buttons)
        {
            foreach (var button in buttons.OrderByDescending(b => b.Width * b.Height))
                FypGhostRaise(button);
        }

        /// <summary>WPF ShowButtons (FypGhostOverlay.cs:154): gear top-left, speaker top-right, own clickable windows.</summary>
        private void ShowFypGhostButtons(PixelRect b, double scale)
        {
            int size = Math.Max(40, (int)Math.Round(b.Height * 0.037));
            int pad = size / 2;
            _fypGhostButtons.Add(FypGhostButton(IconKind.Settings, Loc.Get("tab_settings"),
                new PixelPoint(b.X + pad, b.Y + pad), size / scale, size, FypGhostGear, out _));
            _fypGhostButtons.Add(FypGhostButton(FypMuteKind(), Loc.Get("video_btn_mute"),
                new PixelPoint(b.Right - pad - size, b.Y + pad), size / scale, size, FypGhostMute, out _fypMuteGlyph));
        }

        /// <summary>WPF :294 the live ghost follows the slider: the mirror's thumbnail alpha on Windows.</summary>
        private void SetFypGhostAlpha(double alpha)
        {
            if (_fypMirror != null) FypGhostMirrorOpacity(_fypMirror, alpha);
            else if (!_fypMirrored) FypGhostOpacity(this, alpha);
        }

        private static IconKind FypMuteKind() => CoreSettings.Current.FypMuted ? IconKind.SpeakerMute : IconKind.Speaker2;

        private static Window FypGhostButton(IconKind kind, string name, PixelPoint at, double size, int pixels, Action click, out IconGlyph? glyph)
        {
            glyph = new IconGlyph { Kind = kind, Size = size * 0.45, Foreground = Brushes.White };
            var button = new Button
            {
                Content = glyph, Width = size, Height = size, Padding = new Thickness(0), Focusable = false,
                CornerRadius = new CornerRadius(size / 2), Background = new SolidColorBrush(Color.FromRgb(31, 31, 54)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 105, 180)), BorderThickness = new Thickness(1.5),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(button, name);
            global::Avalonia.Automation.AutomationProperties.SetName(button, name);
            button.Click += (_, _) =>
            {
                try { click(); } catch (Exception ex) { Log.Debug("[Game] fyp: ghost button: {E}", ex.Message); }
            };
            // Quiet until reached for (#1096): faint idle, solid on hover (FypGhostOverlay.cs:443-444).
            var window = new Window
            {
                Content = button, Width = size, Height = size, Position = at, Opacity = 0.14,
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false,
                Topmost = true, CanResize = false, Focusable = false, Background = Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            };
            window.PointerEntered += (_, _) => window.Opacity = 0.92;
            window.PointerExited += (_, _) => window.Opacity = 0.14;
            try
            {
                if (!FypGhostButtonConfigure(window, new PixelRect(at, new PixelSize(pixels, pixels)), true))
                    throw new InvalidOperationException("ghost button cannot be made passive");
                window.Show();
                return window;
            }
            catch { window.Close(); throw; }
        }

        /// <summary>WPF GhostGearClicked (:604): give the window back and open the options popover.</summary>
        internal void FypGhostGear()
        {
            ExitFypGhost();
            Activate();
            Post(new { type = "openOptions" });
        }

        /// <summary>WPF GhostMuteClicked (:618): flip mute without leaving ghost mode.</summary>
        internal void FypGhostMute()
        {
            var s = CoreSettings.Current;
            s.FypMuted = !s.FypMuted;
            Post(new { type = "setMuted", on = s.FypMuted });
            if (_fypMuteGlyph != null) _fypMuteGlyph.Kind = FypMuteKind();
        }

        /// <summary>WPF MainWindow.xaml.cs:1776-1795, the feed's legacy panic rungs: drop ghost, swallow the
        /// reflexive second press, else close. True = the press was consumed. An open DtRH goes first (:1740).</summary>
        internal static bool ForYouTakesPanicKey()
        {
            GameWindow? feed;
            lock (Open)
            {
                if (Open.Any(w => w.Spec.Id == DtrhId)) return false;
                feed = Open.FirstOrDefault(w => w.Spec.Id == FypId);
            }
            if (feed == null) return false;
            if (feed._fypGhosted) { feed.ExitFypGhost(); return true; }
            if (feed.FypRecentlyUnghosted) { Log.Information("[Game] fyp: panic press ignored (just un-ghosted)"); return true; }
            feed.Close();
            return true;
        }
    }
}

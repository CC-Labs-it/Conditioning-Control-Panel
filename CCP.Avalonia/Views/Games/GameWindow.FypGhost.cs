using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
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
    /// shape on the toplevel clips every child's input, so the live feed window itself is the ghost. Windows
    /// (WebView2, the same black child) and native Wayland or headless (no X11 calls) refuse, as WPF refuses a
    /// mirror that cannot compose (FypGhostOverlay.cs:94).
    /// </summary>
    internal sealed partial class GameWindow
    {
        /// <summary>WPF FypGhostOverlay.Diagnose order: null when ghost mode can run here.</summary>
        internal static string? FypGhostRefusal(bool windows, bool x11, bool compositing) =>
            windows || !x11 ? "unsupported" : !compositing ? "composition-disabled" : null;

        // Seams (tests swap and restore them): the platform verdict and the two X11 calls.
        internal static Func<string?> FypGhostSupport = () =>
            FypGhostRefusal(OperatingSystem.IsWindows(), X11Overlay.IsAvailable, X11Overlay.IsCompositing);
        internal static Func<TopLevel, bool, bool> FypGhostClickThrough = X11Overlay.SetClickThrough;
        internal static Func<TopLevel, double, bool> FypGhostOpacity = X11Overlay.SetOpacity;
        internal static Func<DateTime> FypGhostNow = () => DateTime.UtcNow;
        internal static Func<Window, PixelRect, bool, bool> FypGhostButtonConfigure = (w, bounds, passive) => X11Overlay.SetOverrideRedirect(w, bounds, passive);
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
                    WindowState = WindowState.Normal;
                    WindowDecorations = WindowDecorations.None;
                    Topmost = true;
                    if (screen != null)
                    {
                        Position = screen.Bounds.Position;
                        Width = screen.Bounds.Width / screen.Scaling;
                        Height = screen.Bounds.Height / screen.Scaling;
                    }
                    if (!FypGhostOpacity(this, ClampFypGhostOpacity(CoreSettings.Current.FypWindowOpacity)) || !FypGhostClickThrough(this, true))
                        why = "mirror-failed";
                    else
                        ShowFypGhostButtons(screen);
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
            foreach (var b in _fypGhostButtons)
                try { b.Close(); } catch (Exception ex) { Log.Debug("[Game] fyp: ghost button close: {E}", ex.Message); }
            _fypGhostButtons.Clear();
            _fypMuteGlyph = null;
            try
            {
                FypGhostClickThrough(this, false);
                FypGhostOpacity(this, 1.0);
                if (!_closing)
                {
                    var r = _fypRestore;
                    (WindowDecorations, Topmost, Position, Width, Height) = (r.Deco, r.Top, r.Pos, r.W, r.H);
                    WindowState = r.State;
                }
            }
            catch (Exception ex) { Log.Debug("[Game] fyp: ghost restore: {E}", ex.Message); }
            _fypGhostExitUtc = FypGhostNow();
            Post(new { type = "clickThrough", on = false });
            Log.Information("[Game] fyp: ghost mode off");
        }

        /// <summary>WPF ShowButtons (FypGhostOverlay.cs:154): gear top-left, speaker top-right, own clickable windows.</summary>
        private void ShowFypGhostButtons(Screen? screen)
        {
            var b = screen?.Bounds ?? new PixelRect(Position, new PixelSize((int)Width, (int)Height));
            double scale = screen?.Scaling ?? 1;
            int size = Math.Max(40, (int)Math.Round(b.Height * 0.037));
            int pad = size / 2;
            _fypGhostButtons.Add(FypGhostButton(IconKind.Settings, Loc.Get("tab_settings"),
                new PixelPoint(b.X + pad, b.Y + pad), size / scale, size, FypGhostGear, out _));
            _fypGhostButtons.Add(FypGhostButton(FypMuteKind(), Loc.Get("video_btn_mute"),
                new PixelPoint(b.Right - pad - size, b.Y + pad), size / scale, size, FypGhostMute, out _fypMuteGlyph));
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

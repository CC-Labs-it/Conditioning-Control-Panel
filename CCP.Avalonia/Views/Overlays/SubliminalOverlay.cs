using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;   // SubliminalCard
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>SubliminalService.ShowSubliminalVisuals</c>
    /// (ConditioningControlPanel/Services/Subliminal/SubliminalService.cs:666): one card per
    /// targeted screen, full-screen, click-through, override-redirect, faded 50 ms in, held
    /// SubliminalDuration x 17 ms (min 100), faded 50 ms out, to SubliminalOpacity. Core
    /// <see cref="CoreSubliminal"/> decides when and which phrase and calls <see cref="Show"/>.
    ///
    /// <para>A new card replaces the one on screen (WPF reuses one keep-alive window per screen,
    /// and SubliminalLayer draws the most recent card only).</para>
    ///
    /// <para>Whisper audio, ducking, XP and the Bambi Freeze/Reset pair: <see cref="SubliminalWhisperShow"/>.
    /// SubliminalStealsFocus opts into activation after a passive show, still click-through.
    /// Solid mode uses the shared passive host; focus requests stay on classic cards.</para>
    /// </summary>
    internal static class SubliminalOverlay
    {
        private static readonly List<SubliminalOverlayWindow> Active = new();
        private static bool _warnedUnavailable;
        private static readonly Dictionary<PixelRect, SharedOverlayHost.Lease> Hosts = new();
        private static readonly List<Action> RetireHosted = new();

        /// <summary>Solid mode goes to the shared stage unless focus is requested or a mandatory video owns the screen.</summary>
        internal static bool UseSolidHost(bool solidMode, bool stealsFocus, int liveVideoWindows) =>
            solidMode && !stealsFocus && liveVideoWindows == 0;

        internal static bool ShowHosted(PixelRect bounds, string text, double alpha, TimeSpan hold)
        {
            if (!Hosts.TryGetValue(bounds, out var lease))
            {
                lease = SharedOverlayHost.Acquire(bounds);
                if (lease == null) return false;
                Hosts.Add(bounds, lease);
            }
            var card = SubliminalOverlayWindow.Build(text, CoreSettings.Current);
            card.Opacity = 0;
            card.ZIndex = 1200; // WPF: above hosted image flashes
            lease.Canvas.Children.Add(card);
            var retired = false;
            Action retire = () =>
            {
                if (retired) return;
                retired = true;
                lease.Canvas.Children.Remove(card);
            };
            RetireHosted.Add(retire);
            void Step(double value, TimeSpan at) => DispatcherTimer.RunOnce(() =>
            {
                if (!retired) card.Opacity = Math.Clamp(value, 0, 1);
            }, at);
            var fade = TimeSpan.FromMilliseconds(SubliminalCard.DefaultFadeMs);
            for (var i = 1; i <= 3; i++)
            {
                Step(alpha * i / 3, fade * i / 3);
                Step(alpha * (1 - i / 3.0), fade + hold + fade * i / 3);
            }
            DispatcherTimer.RunOnce(() =>
            {
                retire();
                RetireHosted.Remove(retire);
                if (!CoreSubliminal.IsRunning && RetireHosted.Count == 0) ReleaseHosts();
            }, fade + hold + fade);
            return true;
        }

        private static void ClearCards()
        {
            foreach (var retire in RetireHosted) retire();
            RetireHosted.Clear();
            foreach (var w in Active.ToList()) w.Close();
        }

        private static void ReleaseHosts()
        {
            foreach (var lease in Hosts.Values) lease.Dispose();
            Hosts.Clear();
        }

        /// <summary>Draw one card for <paramref name="text"/> on every targeted screen; <paramref name="opacity"/>
        /// (percent) overrides the setting, as WPF FlashSubliminalCustom's does.</summary>
        public static void Show(Visual host, string text, int? opacity = null)
        {
            if (!X11Overlay.IsAvailable)
            {
                if (!_warnedUnavailable) Log.Warning("Subliminal: this platform cannot show click-through overlays, so subliminals are skipped");
                _warnedUnavailable = true;
                return;
            }
            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return;

            ClearCards();
            var s = CoreSettings.Current;
            var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
            var hold = TimeSpan.FromMilliseconds(SubliminalCard.HoldMs(s.SubliminalDuration));
            var alpha = SubliminalCard.Alpha(opacity ?? s.SubliminalOpacity);
            foreach (var i in PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary))
            {
                if (UseSolidHost(s.SubliminalSolidMode, s.SubliminalStealsFocus, MandatoryVideoOverlay.Instance.Windows.Count) &&
                    ShowHosted(screens[i].Bounds, text, alpha, hold)) continue;
                var w = new SubliminalOverlayWindow(text);
                if (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOpacity(w, 0) || !X11Overlay.SetOverrideRedirect(w, screens[i].Bounds))
                {
                    if (!_warnedUnavailable) Log.Warning("Subliminal: the platform refused a click-through topmost overlay window; skipped");
                    _warnedUnavailable = true;
                    w.Close();
                    return;
                }
                Present(w);
                w.Run(alpha, hold);
            }
        }

        /// <summary>Native focus request; tests substitute it without touching a desktop.</summary>
        internal static Action<Window> ActivateCard = w => RequestFocus(w, NativeFocus);

        /// <summary>The native request itself; a test seam for the retry below.</summary>
        internal static Func<Window, bool> NativeFocus = X11Overlay.ActivateClickThrough;
        private static bool _warnedFocus;

        /// <summary>X11: the request travels on our own connection and can beat Avalonia's XMapWindow
        /// (BadMatch on an unmapped window). Retry once after the dispatcher has run Avalonia's queue;
        /// only a second refusal is reported, once, at Warning. The card shows either way.</summary>
        internal static void RequestFocus(Window w, Func<Window, bool> focus)
        {
            if (focus(w)) return;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (!w.IsVisible || focus(w) || _warnedFocus) return;
                _warnedFocus = true;
                Log.Warning("Subliminal: the platform refused the focus request; cards show without focus");
            }, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        /// <summary>WPF ShowSubliminalVisuals: show passively, then activate when SubliminalStealsFocus
        /// is on (SubliminalService.cs:661-662). A refused focus request never prevents the card's
        /// visual envelope or its Stop lifecycle.</summary>
        internal static void Present(Window card)
        {
            var stealsFocus = CoreSettings.Current.SubliminalStealsFocus;
            if (card is SubliminalOverlayWindow overlay)
            {
                overlay.Closed += (_, _) => Active.Remove(overlay);
                Active.Add(overlay);
            }
            card.Show();
            if (!stealsFocus) return;
            try { ActivateCard(card); }
            catch (Exception ex) { Log.Debug("Subliminal: focus steal refused: {E}", ex.Message); }
        }

        /// <summary>Take every card off screen now (WPF <c>TearDownSurfaces</c> on Stop).</summary>
        public static void CloseAll()
        {
            ClearCards();
            ReleaseHosts();
        }
    }

    /// <summary>
    /// One subliminal card: WPF <c>BuildSubliminalContent</c> + <c>CreateTextBlock</c>
    /// (SubliminalService.cs:1028, :1335). Optional full-screen SubBackgroundColor, then 8 copies
    /// of the text in SubBorderColor at the WPF offsets, then the text in SubTextColor - bold,
    /// 120 DIP, SubliminalFont (Arial fallback), centred on the screen.
    /// </summary>
    internal sealed class SubliminalOverlayWindow : Window
    {
        /// <summary>--render-all only: a sample card at the user's settings.</summary>
        internal SubliminalOverlayWindow() : this("GOOD GIRL")
        {
            Width = 1280;
            Height = 720;
        }

        public SubliminalOverlayWindow(string text)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            Content = Build(text, CoreSettings.Current);
        }

        /// <summary>The card's visual tree, split out so a test can check it against WPF.</summary>
        internal static Grid Build(string text, Models.AppSettings s)
        {
            var grid = new Grid { IsHitTestVisible = false };
            if (!s.SubBackgroundTransparent)
                grid.Children.Add(new Rectangle { Fill = new SolidColorBrush(Parse(s.SubBackgroundColor, Colors.Black)) });

            var family = new FontFamily(s.SubliminalFont == "Fredoka (bundled)" ? Platform.AppFonts.Fredoka :
                string.IsNullOrWhiteSpace(s.SubliminalFont) ? "Arial" : s.SubliminalFont + ", Arial");
            TextBlock Text(Color c, double x, double y) => new()
            {
                Text = text,
                FontSize = SubliminalCard.FontDip,
                FontWeight = FontWeight.Bold,
                FontFamily = family,
                Foreground = new SolidColorBrush(c),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransform = new TranslateTransform(x, y),
            };
            var border = Parse(s.SubBorderColor, Colors.White);
            foreach (var (x, y) in SubliminalCard.OutlineOffsets) grid.Children.Add(Text(border, x, y));
            grid.Children.Add(Text(Parse(s.SubTextColor, Color.FromRgb(255, 0, 255)), 0, 0));
            return grid;
        }

        private static Color Parse(string? hex, Color fallback) => Color.TryParse(hex, out var c) ? c : fallback;

        /// <summary>WPF AnimateSubliminal: 0 -> alpha over 50 ms, hold, alpha -> 0 over 50 ms, then
        /// close. Compositor-side alpha (<see cref="X11Overlay.SetOpacity"/>), so the card renders once.</summary>
        public void Run(double alpha, TimeSpan hold)
        {
            var fade = TimeSpan.FromMilliseconds(SubliminalCard.DefaultFadeMs);
            var closed = false;
            Closed += (_, _) => closed = true;   // a newer card may have closed this one already
            // The envelope starts at the card's first frame, not at Show(): mapping and the first
            // full-screen paint take time a 100 ms hold cannot spare. A backstop from Show() caps the
            // lifetime anyway, so a late first frame shortens the card instead of stranding it.
            DispatcherTimer.RunOnce(() => { if (!closed) Close(); }, MaxLifetime(hold));
            RequestFrame(this, _ =>
            {
                if (closed) return;
                Ramp(0, alpha, TimeSpan.Zero, fade);
                Ramp(alpha, 0, fade + hold, fade);
                DispatcherTimer.RunOnce(() => { if (!closed) Close(); }, fade + hold + fade + TimeSpan.FromMilliseconds(1));
            });
        }

        /// <summary>Test seam: a test swaps in a first frame that never comes.</summary>
        internal static Action<TopLevel, Action<TimeSpan>> RequestFrame = (t, a) => t.RequestAnimationFrame(a);

        /// <summary>Show() to forced close: the whole envelope plus 100 ms of first-frame slack.</summary>
        internal static TimeSpan MaxLifetime(TimeSpan hold) => hold + TimeSpan.FromMilliseconds(4 * SubliminalCard.DefaultFadeMs);

        private void Ramp(double from, double to, TimeSpan start, TimeSpan span)
        {
            const int steps = 3;   // ~one step per 60 Hz frame across 50 ms
            for (var i = 1; i <= steps; i++)
            {
                var v = from + (to - from) * i / steps;
                DispatcherTimer.RunOnce(() => X11Overlay.SetOpacity(this, v), start + span * i / steps);
            }
        }
    }
}

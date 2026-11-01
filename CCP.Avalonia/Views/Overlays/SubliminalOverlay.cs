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
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services;   // SubliminalCard
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>SubliminalService.ShowSubliminalVisuals</c>
    /// (ConditioningControlPanel/Services/Subliminal/SubliminalService.cs:666): one card per
    /// targeted screen, full-screen, click-through, override-redirect, fading to SubliminalOpacity,
    /// held SubliminalDuration x 17 ms (min 100), then fading out. Core
    /// <see cref="CoreSubliminal"/> decides when and which phrase and calls <see cref="Show"/>.
    ///
    /// <para>A new card replaces the one on screen (WPF reuses one keep-alive window per screen,
    /// and SubliminalLayer draws the most recent card only).</para>
    ///
    /// <para>Whisper audio, ducking, XP and the Bambi Freeze/Reset pair: <see cref="SubliminalWhisperShow"/>.
    /// SubliminalStealsFocus opts into activation after a passive show, still click-through.
    /// ponytail: the solid-mode shared host remains a separate follow-up.</para>
    /// </summary>
    internal static class SubliminalOverlay
    {
        private static readonly List<SubliminalOverlayWindow> Active = new();
        internal static IReadOnlyList<SubliminalOverlayWindow> Shown => Active;
        // Headless tests drive Show without native windows, as for SpiralOverlay.
        internal static bool SkipPlatformChecksForTests;
        private static bool _warnedUnavailable;

        /// <summary>Draw one card for <paramref name="text"/> on every targeted screen; <paramref name="opacity"/>
        /// (percent) overrides the setting, as WPF FlashSubliminalCustom's does.</summary>
        public static void Show(Visual host, string text, int? opacity = null)
        {
            if (!SkipPlatformChecksForTests && !X11Overlay.IsAvailable)
            {
                if (!_warnedUnavailable) Log.Warning("Subliminal: this platform cannot show click-through overlays, so subliminals are skipped");
                _warnedUnavailable = true;
                return;
            }
            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return;

            CloseAll();
            var s = CoreSettings.Current;
            var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
            var hold = TimeSpan.FromMilliseconds(SubliminalCard.HoldMs(s.SubliminalDuration));
            var alpha = SubliminalCard.Alpha(opacity ?? s.SubliminalOpacity);
            foreach (var i in PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary))
            {
                var w = new SubliminalOverlayWindow(text);
                if (!SkipPlatformChecksForTests && (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOpacity(w, 0) || !X11Overlay.SetOverrideRedirect(w, screens[i].Bounds)))
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
            foreach (var w in Active.ToList()) w.Close();
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
            if (!s.SubBackgroundTransparent && !s.GentleVisuals)
                grid.Children.Add(new Rectangle { Fill = new SolidColorBrush(Parse(s.SubBackgroundColor, Colors.Black)) });

            var family = new FontFamily(string.IsNullOrWhiteSpace(s.SubliminalFont) ? "Arial" : s.SubliminalFont + ", Arial");
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

        private DispatcherTimer? _fadeTimer;
        private TimeSpan _fade, _hold;
        private long _fadeStart;
        private double _alpha;
        internal static TimeProvider Clock = TimeProvider.System;
        internal double CurrentOpacity { get; private set; }
        internal bool IsFading => _fadeTimer?.IsEnabled == true;

        /// <summary>WPF SubliminalService.cs:631-644,1344-1374: fade, hold, fade.
        /// Capture the Gentle duration per card and apply native alpha on a dispatcher timer.</summary>
        public void Run(double alpha, TimeSpan hold)
        {
            _alpha = alpha;
            _hold = hold;
            _fade = TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(SubliminalCard.DefaultFadeMs));
            var closed = false;
            var started = false;
            Closed += (_, _) => { closed = true; _fadeTimer?.Stop(); };
            // Bound lifetime even if a first frame never arrives, allowing 100 ms for mapping.
            DispatcherTimer.RunOnce(() => { if (!closed && !started) Close(); }, MaxLifetime(hold, _fade));
            RequestFrame(this, _ =>
            {
                if (closed || started) return;
                started = true;
                DispatcherTimer.RunOnce(() => { if (!closed) Close(); }, MaxLifetime(hold, _fade));
                _fadeStart = Clock.GetTimestamp();
                // A near-transparent XWayland window can stop producing frames. A timer keeps the
                // native opacity moving after the first frame, even when frame callbacks stall.
                _fadeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => FadeFrame());
                _fadeTimer.Start();
                FadeFrame();
            });
        }

        internal void FadeFrame()
        {
            var ticks = Clock.GetElapsedTime(_fadeStart).Ticks;
            var holdEnd = (_fade + _hold).Ticks;
            var end = holdEnd + _fade.Ticks;
            CurrentOpacity = _alpha * (ticks < _fade.Ticks ? (double)ticks / _fade.Ticks
                : ticks < holdEnd ? 1 : Math.Clamp((double)(end - ticks) / _fade.Ticks, 0, 1));
            X11Overlay.SetOpacity(this, CurrentOpacity);
            if (ticks >= end) { _fadeTimer?.Stop(); Close(); }
        }

        /// <summary>Test seam: a test swaps in a first frame that never comes.</summary>
        internal static Action<TopLevel, Action<TimeSpan>> RequestFrame = (t, a) => t.RequestAnimationFrame(a);

        /// <summary>Backstop for the first frame or a running envelope, with 100 ms slack.</summary>
        internal static TimeSpan MaxLifetime(TimeSpan hold) => MaxLifetime(hold, TimeSpan.FromMilliseconds(SubliminalCard.DefaultFadeMs));
        internal static TimeSpan MaxLifetime(TimeSpan hold, TimeSpan fade) => hold + fade + fade + TimeSpan.FromMilliseconds(100);
    }
}

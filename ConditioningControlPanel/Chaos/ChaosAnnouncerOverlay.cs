using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

// ChaosAnnounceKind, the queue and the timings live in CCP.Core (Services/Chaos/ChaosAnnouncer.cs).

/// <summary>
/// Full-screen, click-through overlay that flashes a fast bordered "subtitle" line in the
/// upper third of the screen to announce a Chaos pickup/beat (mantra drafted, temptation
/// taken, willpower gained, depth reached, streak milestone). Announcements queue so two
/// back-to-back beats play in sequence instead of stomping each other. Master-gated on
/// <c>AppSettings.ChaosAnnouncerEnabled</c> so the whole feature is one switch.
/// ONE window is created on first use and KEPT ALIVE between announcements (each one just
/// swaps the label) — announcing churns constantly mid-run, and creating/closing a layered
/// window mid-run can wedge the shared WPF render thread (Application Hang 1002 — see
/// ChaosEffectBannerOverlay). Closed only at run teardown via <see cref="CloseActive"/>.
/// </summary>
public sealed class ChaosAnnouncerOverlay : Window
{
    // ---- timing / layout tunables ----
    private const int IN_MS         = ChaosAnnounceTiming.InMs;      // scale-pop + fade-in
    private const int HOLD_MS       = ChaosAnnounceTiming.HoldMs;    // dwell (default — event beats: boon names, relapse, streaks)
    public  const int TEACH_HOLD_MS = ChaosAnnounceTiming.TeachHoldMs;   // onboarding/help lines linger so they can actually be read
    private const int OUT_MS        = ChaosAnnounceTiming.OutMs;     // fade-out ("fades almost immediately")
    private const double FONT_SIZE  = 60;
    private const double SUB_FONT_SIZE = 26;  // dynamic suffix under a banner image
    private const double ART_HEIGHT_DIP = 120; // banner image height (≈ the 60px text line's presence)
    private const double TOP_OFFSET_DIP = 92; // sits right under the effect-banner strip (wa.Top+6, 80 tall)

    private static ChaosAnnouncerOverlay? _active;
    // Priority queue (was FIFO): higher priority shows first; ties keep insertion order. Gameplay
    // announces enqueue at priority 0; the narrator (the Madam) enqueues above them, with STORY > REACTIVE.
    private static readonly ChaosAnnounceQueue _queue = new();
    private static bool _showing;

    /// <summary>
    /// Queue a bordered fading announcement. No-op unless the announcer is enabled.
    /// <paramref name="artKey"/> names an optional neon word-art banner
    /// (<c>assets/Chaos/announce/{artKey}.png</c>, boon ids included); when the file exists
    /// the image replaces the outlined text, with <paramref name="subText"/> (the dynamic
    /// part — counts, hints) rendered small beneath it. No file → the text shows as before,
    /// so unkeyed lines and missing art degrade gracefully.
    /// </summary>
    public static void Announce(string text, ChaosAnnounceKind kind,
                                string? artKey = null, string? subText = null, int? holdMs = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (App.Settings?.Current?.ChaosAnnouncerEnabled != true) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.Invoke(() =>
            {
                _queue.Add(new ChaosAnnouncement(text, kind, artKey, subText, holdMs ?? HOLD_MS, 0));
                if (!_showing) ShowNext();
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosAnnouncer.Announce: {E}", ex.Message); }
    }

    /// <summary>
    /// Queue a narrator (Madam) line. Gated on <c>NarrativeModeEnabled</c> (NOT the gameplay
    /// announcer toggle). <paramref name="bandPriority"/> is the cue's band (Ambient/Reactive/Story);
    /// narrator lines sit above gameplay announces and order STORY &gt; REACTIVE. STORY passes
    /// <paramref name="interrupt"/> = true, which cuts the current line short so she lands now.
    /// </summary>
    public static void AnnounceNarrator(string text, int bandPriority, bool interrupt, int holdMs)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            // Madam narrator lines are Story-only (silent in Free Desktop) and honor the user setting.
            if (!Services.Chaos.ChaosModeService.NarrativeActive) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.Invoke(() =>
            {
                _queue.Add(new ChaosAnnouncement(text, ChaosAnnounceKind.Narrator, null, null, holdMs, ChaosAnnounceQueue.NarratorPriority(bandPriority)));
                if (!_showing) ShowNext();
                else if (interrupt) _active?.CutShort();   // STORY: end the current line so she shows next
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosAnnouncer.AnnounceNarrator: {E}", ex.Message); }
    }

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Drop any queued/visible announcement and tear the window down (run teardown).</summary>
    public static void CloseActive()
    {
        try { _queue.Clear(); _showing = false; _active?.CloseNow(); } catch { }
    }

    private static void ShowNext()
    {
        if (!_queue.TryDequeue(out var dq)) { _showing = false; return; }
        _showing = true;
        var (text, kind, artKey, subText, holdMs, _) = dq;
        try
        {
            if (_active == null) { _active = new ChaosAnnouncerOverlay(); ((Window)_active).Show(); }
            else if (!_active.IsVisible) { try { ((Window)_active).Show(); } catch { } }   // idles hidden between announcements
            ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
            _active.Display(text, kind, artKey, subText, holdMs);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ChaosAnnouncer.ShowNext: {E}", ex.Message);
            _showing = false;
        }
    }

    private readonly Grid _host;
    private readonly DispatcherTimer _life;
    private (string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs)? _pending;   // first Display can land before Loaded

    private ChaosAnnouncerOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Opacity = 0;

        _host = new Grid();
        Content = _host;

        SourceInitialized += (_, _) => ApplyExStyles();
        Loaded += (_, _) =>
        {
            if (_pending is { } p) { _pending = null; DisplayCore(p.text, p.kind, p.artKey, p.subText, p.holdMs); }
        };

        _life = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IN_MS + HOLD_MS) };
        _life.Tick += (_, _) =>
        {
            _life.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(OUT_MS));
            fade.Completed += (_, _) =>
            {
                try { _host.Children.Clear(); } catch { }   // window stays — only content goes
                // Nothing queued → hide until the next announcement (an idle visible
                // full-virtual-screen layered surface taxes DWM composition every frame).
                if (_queue.Count == 0) { try { Hide(); } catch { } }
                ShowNext();
            };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    private void Display(string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs)
    {
        if (!IsLoaded) { _pending = (text, kind, artKey, subText, holdMs); return; }
        DisplayCore(text, kind, artKey, subText, holdMs);
    }

    private void DisplayCore(string text, ChaosAnnounceKind kind, string? artKey, string? subText, int holdMs)
    {
        _life.Stop();
        _life.Interval = TimeSpan.FromMilliseconds(IN_MS + Math.Max(0, holdMs));   // per-message dwell
        var (fill, stroke) = Palette(kind);
        var scale = new ScaleTransform(0.85, 0.85);
        var art = artKey != null ? Services.Chaos.ChaosArt.Resolve("announce", artKey) : null;

        FrameworkElement content;
        if (art != null)
        {
            // Picked neon word-art banner; subText carries whatever the image can't (counts, hints).
            var panel = new StackPanel { Orientation = Orientation.Vertical };
            panel.Children.Add(new Image
            {
                Source = art,
                Height = ART_HEIGHT_DIP,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            if (!string.IsNullOrWhiteSpace(subText))
            {
                var sub = new OutlinedText
                {
                    Text = subText!.ToUpperInvariant(),
                    FontSize = SUB_FONT_SIZE,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = 2.2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                };
                sub.Build();
                panel.Children.Add(sub);
            }
            content = panel;
        }
        else
        {
            var label = new OutlinedText
            {
                Text = text.ToUpperInvariant(),
                FontSize = FONT_SIZE,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 3.2,
            };
            label.Build();
            content = label;
        }

        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Top;
        content.RenderTransformOrigin = new Point(0.5, 0.5);
        content.RenderTransform = scale;
        // Anchored just under the effect-banner strip, centered over the PRIMARY work area.
        // This window spans the whole virtual screen, so centering against the window itself
        // drifts off the primary monitor whenever a second monitor extends the desktop
        // (e.g. a screen to the left pulls the virtual center to the primary's left edge).
        var wa = SystemParameters.WorkArea;
        var anchor = new Grid
        {
            Width = wa.Width,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(
                Math.Max(0, wa.Left - SystemParameters.VirtualScreenLeft),
                Math.Max(0, wa.Top - SystemParameters.VirtualScreenTop) + TOP_OFFSET_DIP, 0, 0),
        };
        anchor.Children.Add(content);
        _host.Children.Clear();
        _host.Children.Add(anchor);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(IN_MS)));
        var pop = new DoubleAnimation(0.85, 1.0, TimeSpan.FromMilliseconds(IN_MS + 70))
        { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        _life.Start();
    }

    /// <summary>End the currently-shown line ASAP so a higher-priority (STORY) line lands now.</summary>
    private void CutShort()
    {
        try { _life.Stop(); _life.Interval = TimeSpan.FromMilliseconds(1); _life.Start(); } catch { }
    }

    private void CloseNow()
    {
        try { _life.Stop(); } catch { }
        try { _host.Children.Clear(); } catch { }
        if (ReferenceEquals(_active, this)) _active = null;
        try { Close(); } catch { }
    }

    /// <summary>Accent (fill, stroke) per announcement kind. Stroke stays a near-black for contrast.</summary>
    private static (Brush fill, Brush stroke) Palette(ChaosAnnounceKind kind)
    {
        Color c = kind switch
        {
            ChaosAnnounceKind.Mantra     => Color.FromRgb(0xFF, 0xD2, 0x7A), // warm gold
            ChaosAnnounceKind.Temptation => Color.FromRgb(0xFF, 0x6B, 0x6B), // risky red
            ChaosAnnounceKind.Willpower  => Color.FromRgb(0x7A, 0xE0, 0xFF), // cyan
            ChaosAnnounceKind.Depth      => Color.FromRgb(0xFF, 0xFF, 0xFF), // white
            ChaosAnnounceKind.Streak     => Color.FromRgb(0xFF, 0xC8, 0x3C), // bright gold
            ChaosAnnounceKind.Item       => Color.FromRgb(0x7A, 0xFF, 0xD2), // mint
            ChaosAnnounceKind.PowerUp    => Color.FromRgb(0x9C, 0xE8, 0xA0), // green
            ChaosAnnounceKind.Narrator   => Color.FromRgb(0xE6, 0x9A, 0xFF), // the Madam — soft violet
            _                            => Colors.White,
        };
        var fill = new SolidColorBrush(c); if (fill.CanFreeze) fill.Freeze();
        var stroke = new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12)); if (stroke.CanFreeze) stroke.Freeze();
        return (fill, stroke);
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}

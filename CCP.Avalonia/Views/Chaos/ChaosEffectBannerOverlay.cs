using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// Port of WPF <c>ConditioningControlPanel/Chaos/ChaosEffectBannerOverlay.cs</c>: the
    /// top-of-screen, click-through strip that names every temporary Chaos bonus while it lasts
    /// (VIBEPOPPING, FREEZE, TIME SLOW, PORN DVD...). Same public surface (EnsureCreated / Show /
    /// End / RaiseActive / CloseActive), so the effect lanes only add calls.
    ///
    /// <para>What the strip shows and when lives in Core <see cref="ChaosEffectBannerStrip"/>; this
    /// window only draws it. Fade-in, the 1.0-1.03 throb and the fade-out are computed per frame
    /// from that state on a <see cref="FrameClock"/> (P82: no infinite Animation, nothing animated
    /// on a Transform), and the clock runs only while a label is on screen (P07).</para>
    ///
    /// <para>Like WPF, the window is born at run start (<see cref="EnsureCreated"/>), kept alive
    /// while empty and closed only at run teardown (<see cref="CloseActive"/>). Click-through is
    /// <c>X11Overlay.SetClickThrough</c> (WPF WS_EX_TRANSPARENT) on an override-redirect window
    /// over the primary screen's work area (WPF Left/Top+6/Width, Height 80). A platform that
    /// refuses either gets no banner rather than a strip that eats clicks.</para>
    ///
    /// <para>ponytail: nothing calls <see cref="Show"/> on this head yet - the slowmo / vibe / dvd /
    /// estim / rabbits / freeze effects (WPF ChaosModeService :2417-3063) and the hub run start are
    /// not ported.</para>
    /// </summary>
    internal sealed class ChaosEffectBannerOverlay : Window
    {
        private const double FONT_SIZE = 34;
        private const double ART_HEIGHT_DIP = 56;
        private const double STRIP_HEIGHT_DIP = 80;
        private static readonly Color StrokeColor = Color.FromRgb(0x0B, 0x08, 0x12);

        internal static TimeProvider Clock = TimeProvider.System;
        internal static bool SkipPlatformChecksForTests;
        internal static bool Refused;   // per run: one refusal, one Warning; cleared by CloseActive (run teardown)
        private static ChaosEffectBannerOverlay? _active;
        private static readonly Dictionary<string, Bitmap?> ArtCache = new();

        internal static ChaosEffectBannerOverlay? Active => _active;

        /// <summary>Create the empty banner window ahead of time (WPF ChaosModeService :410). Safe from any thread.</summary>
        public static void EnsureCreated() => OnUi(() => Create());

        /// <summary>Show (or keep) the banner for an effect. Safe from any thread. Uses
        /// <c>assets/Chaos/announce/{artKey ?? id}.png</c> when it exists, else the outlined text.</summary>
        public static void Show(string id, string text, Color accent, string? artKey = null) => OnUi(() =>
        {
            var w = Create();
            if (w == null) return;
            w.AddEntry(id, text, ChaosBoonColors.ForOrDefault(id, accent), artKey);
            if (!SkipPlatformChecksForTests) X11Overlay.Raise(w);   // WPF ChaosWindowZ.RaiseAboveVideo
        });

        /// <summary>Fade out + remove the banner for an effect (no-op if it isn't showing).</summary>
        public static void End(string id) => OnUi(() => _active?.FadeEntry(id));

        /// <summary>Re-stack the live window on top (WPF ChaosWindowZ.RaiseTopmost). UI thread only.</summary>
        public static void RaiseActive()
        {
            if (_active != null && !SkipPlatformChecksForTests) X11Overlay.Raise(_active);
        }

        /// <summary>Instant teardown (run end / shutdown).</summary>
        public static void CloseActive()
        {
            Refused = false;
            try { _active?.CloseNow(); } catch (Exception ex) { Log.Debug("ChaosEffectBanner.CloseActive: {E}", ex.Message); }
        }

        private static void OnUi(Action a)
        {
            void Run()
            {
                try { a(); }
                catch (Exception ex) { Log.Debug("ChaosEffectBanner: {E}", ex.Message); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Run();
            else Dispatcher.UIThread.Post(Run);
        }

        private static ChaosEffectBannerOverlay? Create()
        {
            if (_active != null) return _active;
            if (Refused) return null;
            // No compositor = no per-pixel alpha: the empty 80px strip would be an opaque band all run.
            if (!SkipPlatformChecksForTests && (!X11Overlay.IsAvailable || !X11Overlay.IsCompositing))
            {
                Log.Warning("ChaosEffectBanner: no composited click-through overlay on this platform, so no effect banners");
                Refused = true;
                return null;
            }
            var w = new ChaosEffectBannerOverlay();
            if ((!X11Overlay.SetClickThrough(w, true) | !X11Overlay.SetOverrideRedirect(w, w.StripBounds())) && !SkipPlatformChecksForTests)
            {
                Log.Warning("ChaosEffectBanner: the platform refused a click-through topmost window, so no effect banners");
                Refused = true;
                w.Close();
                return null;
            }
            _active = w;
            w.Show();
            return w;
        }

        private readonly ChaosEffectBannerStrip _strip = new();
        private readonly Dictionary<ChaosEffectBannerStrip.Entry, (Control Label, ScaleTransform Throb)> _labels = new();
        private readonly StackPanel _row;
        private readonly FrameClock _frames;

        internal ChaosEffectBannerStrip Strip => _strip;
        internal StackPanel Row => _row;
        internal bool IsAnimating => _frames.IsEnabled;

        public ChaosEffectBannerOverlay()
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 1280;
            Height = STRIP_HEIGHT_DIP;

            _row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
            };
            Content = _row;
            _frames = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(16) };
            _frames.Tick += (_, _) => Step();

            if (RenderProof.Rendering)   // --render-all: two settled labels, WPF's own ids/colours
            {
                AddEntry("freeze", "Freeze", ChaosBoonColors.ForOrDefault("freeze", Color.FromRgb(0x96, 0xD2, 0xFF)), null);
                AddEntry("vibe", "VibePopping", ChaosBoonColors.ForOrDefault("vibe", Color.FromRgb(0xFF, 0xB0, 0x3A)), null);
                _frames.Stop();
                foreach (var c in _row.Children) c.Opacity = 1;
            }
        }

        /// <summary>WPF: Left = WorkArea.Left, Top = WorkArea.Top + 6, Width = WorkArea.Width, Height = 80 (DIPs).</summary>
        private PixelRect StripBounds()
        {
            var screen = Screens?.Primary ?? Screens?.All.FirstOrDefault();
            if (screen == null) return new PixelRect(0, 6, 1280, (int)STRIP_HEIGHT_DIP);
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            var wa = screen.WorkingArea;
            return new PixelRect(wa.X, wa.Y + (int)Math.Round(6 * scale), wa.Width, (int)Math.Round(STRIP_HEIGHT_DIP * scale));
        }

        private void AddEntry(string id, string text, Color accent, string? artKey)
        {
            var entry = _strip.Show(id, text, artKey, Clock.GetUtcNow());
            if (entry == null) return;   // already on screen - let it ride

            var throb = new ScaleTransform(1, 1);
            Control label;
            if (ResolveArt(entry.ArtKey ?? id) is { } art)
            {
                label = new Image { Source = art, Height = ART_HEIGHT_DIP, Stretch = Stretch.Uniform };
            }
            else
            {
                var outlined = new OutlinedTextBlock
                {
                    Text = text.ToUpperInvariant(),
                    FontSize = FONT_SIZE,
                    Fill = new SolidColorBrush(accent),
                    Stroke = new SolidColorBrush(StrokeColor),
                    StrokeThickness = 2.6,
                };
                outlined.Build();
                label = outlined;
            }
            label.Margin = new Thickness(18, 0, 18, 0);
            label.VerticalAlignment = VerticalAlignment.Top;
            label.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            label.RenderTransform = throb;
            label.Opacity = 0;
            _labels[entry] = (label, throb);
            _row.Children.Add(label);
            _frames.Start();
        }

        private void FadeEntry(string id)
        {
            // The empty window stays alive (WPF: closing a layered window mid-run hung the render thread).
            if (_strip.End(id, Clock.GetUtcNow()) != null) _frames.Start();
        }

        /// <summary>One frame: opacity and throb from the strip state, faded labels removed. Tests call it directly.</summary>
        internal void Step()
        {
            var now = Clock.GetUtcNow();
            foreach (var gone in _strip.Prune(now))
                if (_labels.Remove(gone, out var v)) _row.Children.Remove(v.Label);
            foreach (var e in _strip.Row)
            {
                if (!_labels.TryGetValue(e, out var v)) continue;
                v.Label.Opacity = e.Opacity(now);
                v.Throb.ScaleX = v.Throb.ScaleY = e.Scale(now);
            }
            if (_strip.Row.Count == 0) _frames.Stop();
        }

        private void CloseNow()
        {
            if (ReferenceEquals(_active, this)) _active = null;
            _frames.Stop();
            _strip.Clear();
            _labels.Clear();
            _row.Children.Clear();
            try { Close(); } catch (Exception ex) { Log.Debug("ChaosEffectBanner close: {E}", ex.Message); }
        }

        /// <summary>WPF ChaosArt.Resolve("announce", key): user assets first, then the app folder.</summary>
        internal static Bitmap? ResolveArt(string key)
        {
            foreach (var root in new[] { Path.Combine(CorePaths.UserData, "assets"), AppContext.BaseDirectory })
            {
                var p = Path.Combine(root, "assets", "Chaos", "announce", key + ".png");
                if (!ArtCache.TryGetValue(p, out var bmp))
                {
                    try { bmp = File.Exists(p) ? new Bitmap(p) : null; }
                    catch (Exception ex) { Log.Debug("ChaosEffectBanner art {P}: {E}", p, ex.Message); bmp = null; }
                    ArtCache[p] = bmp;
                }
                if (bmp != null) return bmp;
            }
            return null;
        }
    }
}

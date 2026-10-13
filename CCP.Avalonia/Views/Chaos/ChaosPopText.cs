using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// WPF <c>ChaosPopText</c> (ConditioningControlPanel/Chaos/ChaosPopText.cs): a small, faint,
    /// colour-coded word that pops at a bubble's spot the instant its effect fires ("floating
    /// combat text"). One little click-through override-redirect window per word (the
    /// <see cref="BouncingTextOverlay"/> recipe), a quick rise + fade, gone in ~half a second.
    /// Master-gated on <c>ChaosAnnouncerEnabled</c> as WPF is.
    ///
    /// <para>Windows are POOLED (WPF :21-26): a retired window hides and goes back to the pool;
    /// past <see cref="ChaosPopTextMotion.POOL_MAX"/> live windows the word is dropped. They die
    /// only in <see cref="ShutdownPool"/>, which <see cref="ChaosRunHost"/> calls on teardown.</para>
    ///
    /// <para>The fade is the compositor's (<see cref="X11Overlay.SetOpacity"/>), like
    /// FlashOverlayWindow; the rise is a TranslateTransform on the label. Both are sampled from
    /// <see cref="ChaosPopTextMotion"/> on the shared 30 fps <see cref="BeatLoop"/>.</para>
    ///
    /// <para>ponytail: nothing calls <see cref="Show"/> yet. Every WPF caller is the bubble field
    /// (BubbleService.ShowChaosLabel), the toys (ripple "gathering…") or the payout (pop score,
    /// gold, focus refill, first-time bonus) - none ported to <see cref="ChaosRunHost"/>. WPF
    /// <c>RaiseActive</c> (re-stack above a mandatory video) waits on the same
    /// RaiseGameLayerAboveVideo port; a freshly mapped override-redirect window is already on top.</para>
    /// </summary>
    internal sealed class ChaosPopText : Window
    {
        internal static readonly ChaosPopPool<ChaosPopText> Pool = new(ChaosPopTextMotion.POOL_MAX);

        /// <summary>Click-through + invisible + override-redirect at <paramref name="rect"/> (screen
        /// px), before the first map. False = the platform refused; the word is dropped. Test seam.</summary>
        internal static Func<ChaosPopText, PixelRect, bool> Prepare = (w, rect) =>
            X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOpacity(w, 0) && X11Overlay.SetOverrideRedirect(w, rect);

        /// <summary>Gate for the static path; tests flip it (headless has no overlay backend).</summary>
        internal static Func<bool> PlatformCanShow = () => X11Overlay.IsAvailable;

        /// <summary>WPF Show: flash <paramref name="text"/> at a screen point in DIPs (primary-screen
        /// scaling), tinted by <paramref name="color"/>. No-op when empty or on-screen Chaos text is
        /// off. Safe from any thread.</summary>
        public static void Show(double anchorXDip, double anchorYDip, string text, Color color)
        {
            try
            {
                if (!ChaosPopTextMotion.ShouldShow(text, CoreSettings.Current.ChaosAnnouncerEnabled)) return;
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (!PlatformCanShow()) return;
                        var w = Pool.Acquire(() => new ChaosPopText(sample: false));
                        w?.Play(anchorXDip, anchorYDip, text, color);   // null = pool exhausted: drop the word
                    }
                    catch (Exception ex) { Log.Debug("ChaosPopText.Show inner: {E}", ex.Message); }
                });
            }
            catch (Exception ex) { Log.Debug("ChaosPopText.Show: {E}", ex.Message); }
        }

        /// <summary>WPF ShutdownPool: close every pooled window (chaos teardown - the only place
        /// they die). Inline on the UI thread so teardown leaves nothing behind.</summary>
        public static void ShutdownPool()
        {
            void Run()
            {
                foreach (var w in Pool.All) { try { w.Close(); } catch (Exception ex) { Log.Debug("ChaosPopText close: {E}", ex.Message); } }
                Pool.Clear();
            }
            if (Dispatcher.UIThread.CheckAccess()) Run(); else Dispatcher.UIThread.Post(Run);
        }

        private readonly Grid _root = new();
        private readonly OutlinedTextBlock _label = new() { FontSize = ChaosPopTextMotion.FONT_SIZE, Stroke = StrokeBrush };
        private readonly TranslateTransform _rise = new(0, ChaosPopTextMotion.RISE_FROM);
        private readonly BeatLoop _beat;
        private bool _prepared, _closed;

        /// <summary>The last alpha handed to the compositor (the WPF window Opacity).</summary>
        internal double Alpha { get; private set; }
        internal string LabelText => _label.Text;
        internal IBrush LabelFill => _label.Fill;
        internal double RiseY => _rise.Y;

        private static readonly IBrush StrokeBrush = new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12));

        /// <summary>--render-all only: one word mid-life on a grey plate (the proof has no desktop).</summary>
        internal ChaosPopText() : this(sample: true) { }

        private ChaosPopText(bool sample)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = CoreSettings.Current.ChaosPinOnTop;   // WPF ChaosWindowZ.BornTopmost
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = ChaosPopTextMotion.WIN_W;
            Height = ChaosPopTextMotion.WIN_H;
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.VerticalAlignment = VerticalAlignment.Center;
            _label.IsHitTestVisible = false;
            _label.RenderTransform = _rise;
            _root.Children.Add(_label);
            Content = _root;
            _beat = new BeatLoop(_root, t => Frame(t * 1000));
            if (!sample) return;
            Background = Brushes.DimGray;
            Alpha = ChaosPopTextMotion.PEAK_OPAC;
            SetLabel("SNAP", Color.FromRgb(0xFF, 0x5A, 0x5A));
        }

        private void SetLabel(string text, Color tint)
        {
            var (r, g, b) = ChaosPopTextMotion.Lift(tint.R, tint.G, tint.B);
            _label.Text = text.ToUpperInvariant();
            _label.Fill = new SolidColorBrush(Color.FromRgb(r, g, b));
            _label.Build();
        }

        /// <summary>WPF Play: place, show, then fade in / hold / fade out while rising.</summary>
        internal void Play(double anchorXDip, double anchorYDip, string text, Color color)
        {
            if (_closed) return;
            var (left, top) = ChaosPopTextMotion.Origin(anchorXDip, anchorYDip);
            var k = Screens.Primary?.Scaling is > 0 and var s ? s : 1.0;
            var rect = new PixelRect((int)Math.Round(left * k), (int)Math.Round(top * k),
                (int)Math.Round(ChaosPopTextMotion.WIN_W * k), (int)Math.Round(ChaosPopTextMotion.WIN_H * k));
            if (!_prepared)
            {
                if (!Prepare(this, rect))
                {
                    Log.Warning("Chaos pop text: the platform refused a click-through overlay window; dropped");
                    Pool.Forget(this);
                    Close();
                    return;
                }
                _prepared = true;
            }
            else Position = rect.Position;   // override-redirect: moves straight away, no WM round trip

            SetLabel(text, color);
            base.Show();   // first call maps it; later calls just unhide
            _beat.Start();   // steps t=0 at once (alpha 0, rise +6) in this same job, before any frame
        }

        /// <summary>One frame at <paramref name="ms"/> into the word's life; retires it at the end.</summary>
        internal void Frame(double ms)
        {
            if (_closed) return;
            if (ChaosPopTextMotion.Done(ms)) { Retire(); return; }
            Alpha = ChaosPopTextMotion.Opacity(ms);
            X11Overlay.SetOpacity(this, Alpha);
            _rise.Y = ChaosPopTextMotion.Rise(ms);
        }

        private void Retire()
        {
            _beat.Stop();
            Alpha = 0;
            X11Overlay.SetOpacity(this, 0);
            _label.Text = "";
            _label.Build();
            Hide();
            Pool.Return(this);
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _beat.Stop();
            base.OnClosed(e);
        }
    }

    /// <summary>WPF ChaosPopText's tunables and curves (:30-39, Play :135-170, Palette :190), as
    /// pure functions of the time since the word appeared.</summary>
    internal static class ChaosPopTextMotion
    {
        public const int IN_MS = 60, HOLD_MS = 230, OUT_MS = 200, LIFE_MS = IN_MS + HOLD_MS + OUT_MS;
        public const double FONT_SIZE = 22, PEAK_OPAC = 0.58, RISE_FROM = 6, RISE_DIP = 22, WIN_W = 280, WIN_H = 88;
        public const int POOL_MAX = 14;

        public static bool ShouldShow(string? text, bool announcerEnabled) =>
            announcerEnabled && !string.IsNullOrWhiteSpace(text);

        /// <summary>Window top-left in DIPs: centred on the anchor.</summary>
        public static (double Left, double Top) Origin(double x, double y) => (x - WIN_W / 2, y - WIN_H / 2);

        /// <summary>0 -> 0.58 over 60 ms, held to 290 ms, 0.58 -> 0 over 200 ms (all linear, as WPF's DoubleAnimations).</summary>
        public static double Opacity(double ms)
        {
            if (ms <= 0) return 0;
            if (ms < IN_MS) return PEAK_OPAC * ms / IN_MS;
            if (ms < IN_MS + HOLD_MS) return PEAK_OPAC;
            if (ms < LIFE_MS) return PEAK_OPAC * (LIFE_MS - ms) / OUT_MS;
            return 0;
        }

        /// <summary>Upward drift 6 -> -22 DIP across the whole 490 ms life.</summary>
        public static double Rise(double ms) =>
            RISE_FROM + (-RISE_DIP - RISE_FROM) * Math.Clamp(ms / LIFE_MS, 0, 1);

        public static bool Done(double ms) => ms >= LIFE_MS;

        /// <summary>WPF Palette: lift the tint 28% toward white so the faint word reads over any desktop.</summary>
        public static (byte R, byte G, byte B) Lift(byte r, byte g, byte b)
        {
            static byte L(byte c) => (byte)Math.Clamp(c + (255 - c) * 0.28, 0, 255);
            return (L(r), L(g), L(b));
        }
    }

    /// <summary>WPF's _pool stack + _all list: reuse a retired item, create up to the cap, else null.</summary>
    internal sealed class ChaosPopPool<T> where T : class
    {
        private readonly Stack<T> _free = new();
        private readonly List<T> _all = new();
        private readonly int _max;

        public ChaosPopPool(int max) => _max = max;

        public IReadOnlyList<T> All => _all;
        public int FreeCount => _free.Count;

        public T? Acquire(Func<T> create)
        {
            if (_free.Count > 0) return _free.Pop();
            if (_all.Count >= _max) return null;
            var item = create();
            _all.Add(item);
            return item;
        }

        public void Return(T item)
        {
            if (_all.Contains(item) && !_free.Contains(item)) _free.Push(item);
        }

        /// <summary>An item that died before use (platform refused it): frees its slot.</summary>
        public void Forget(T item) => _all.Remove(item);

        public void Clear()
        {
            _all.Clear();
            _free.Clear();
        }
    }
}

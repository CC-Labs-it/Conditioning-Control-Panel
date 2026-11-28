using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Services.Looks;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Super Afterglow on this head (WPF <c>Services/Compositor/AfterglowLineLayer</c>): every
    /// subliminal that flashes leaves a ghost that joins a line along the bottom of one monitor; a
    /// full line glows as one mantra and sinks. Timing is Core's <see cref="AfterglowLine"/>, the
    /// host rules <see cref="AfterglowLineRules"/>, the picture <see cref="AfterglowLineDraw"/>; this
    /// class only feeds the words in, steps the line on a 30 fps clock while words are up and hosts
    /// one <see cref="AfterglowLineCanvas"/> on a passive subliminal stage
    /// (<see cref="SharedOverlayHost"/>, the solid-mode card stage). Visual only: no XP, no sound.
    ///
    /// <para><b>Threads.</b> The line steps on the UI thread and is drawn on the render thread; both
    /// take <see cref="SteamOverlay.Gate"/>, which also covers the stage reads (the pool's eye) and
    /// the fog band the finished line is remembered in.</para>
    /// </summary>
    internal static class AfterglowLineOverlay
    {
        private static readonly AfterglowLine Line = new();
        private static SharedOverlayHost.Lease? _lease;
        private static AfterglowLineCanvas? _canvas;
        private static PixelRect _bounds;
        private static volatile System.Drawing.Rectangle[] _band = Array.Empty<System.Drawing.Rectangle>();
        private static DispatcherTimer? _timer;
        private static long _last;
        private static volatile int _drawnStyle = -1;
        private static bool _hooked, _warned;

        /// <summary>Can this platform host the line at all (a click-through, per-pixel transparent stage).
        /// False: Afterglow does not run here, so the ambient card never rests for it. Tests swap it.</summary>
        internal static Func<bool> SurfaceAvailable = () => X11Overlay.IsAvailable && X11Overlay.IsCompositing;

        /// <summary>The window whose screens are read; tests swap it.</summary>
        internal static Func<Visual?> HostProvider = () =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        /// <summary>The glow colour: the pink filter's own, as WPF reads <c>PinkTintLayer.LastLook</c>.</summary>
        internal static SKColor Glow { get; private set; } = new(255, 105, 180);

        /// <summary>The face the words are drawn in (the player's Subliminal Font once picked).</summary>
        internal static SKTypeface Face { get; private set; } = AfterglowLineDraw.FaceFor(null);

        /// <summary>The last finished line, which every <see cref="SteamCanvas"/> hands to its
        /// <c>SteamWords.Line</c> (WPF <c>SteamLayer.Line</c>); only the monitor whose fog band was
        /// remembered wakes the line slot. Read and written under <see cref="SteamOverlay.Gate"/>.</summary>
        internal static string? FogLine { get; private set; }

        /// <summary>The fog forgets the line (WPF <c>SteamLayer.Hide</c>): the fog going off, panic
        /// (Core <c>SuperStageHost.Stop</c>) and the Afterglow star going off. Any thread.</summary>
        internal static void ClearFogLine()
        {
            lock (SteamOverlay.Gate) FogLine = null;
        }

        /// <summary>The stage is up with words on it.</summary>
        internal static bool Up => _lease != null;
        internal static int Count { get { lock (SteamOverlay.Gate) return Line.Count; } }
        internal static AfterglowLineCanvas? Canvas => _canvas;

        /// <summary>Afterglow can run right now: the star is on (tier, switch and base, Core's gate) and
        /// this platform can show the line.</summary>
        internal static bool Runs() => SuperAccess.IsOn(SuperEffect.Afterglow) && SurfaceAvailable();

        /// <summary>WPF <c>SubliminalService.AfterglowRestsCard</c>: should the ambient card rest this time.</summary>
        internal static bool RestsCard()
        {
            try
            {
                SuperEffect? trying = null;
                try { trying = SuperAccess.TryingProvider(); } catch { }
                return AfterglowBase.CardRests(Runs(), trying == SuperEffect.Afterglow, CoreSettings.Current.AfterglowAlsoBase);
            }
            catch { return false; }
        }

        /// <summary>A pool subliminal was just shown (WPF <c>AfterglowLineLayer.WordShown</c>). Any thread.</summary>
        internal static void WordShown(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (Dispatcher.UIThread.CheckAccess()) Add(text);
            else Dispatcher.UIThread.Post(() => Add(text));
        }

        private static void Add(string text)
        {
            try
            {
                if (!Runs()) return;
                if (!_hooked)
                {
                    _hooked = true;
                    SuperAccess.Changed += e =>
                    {
                        if (e != SuperEffect.Afterglow || SuperAccess.IsOn(e)) return;
                        ClearFogLine();
                        if (Dispatcher.UIThread.CheckAccess()) StopAll(); else Dispatcher.UIThread.Post(StopAll);
                    };
                }
                ReadLook();
                lock (SteamOverlay.Gate)
                {
                    AfterglowLineRules.Apply(Line, CoreSettings.Current);
                    if (!Line.Add(text.ToLowerInvariant())) return;
                }
                if (!EnsureSurface()) { lock (SteamOverlay.Gate) Line.Clear(); return; }
                if (_timer == null)
                {
                    _last = 0;
                    _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, Tick);
                    _timer.Start();
                }
                _canvas?.InvalidateVisual();
            }
            catch (Exception ex) { Log.Debug("Afterglow line: {E}", ex.Message); }
        }

        /// <summary>One line, on the primary monitor of the app's content target (two copies of a mantra
        /// building at once read as noise).</summary>
        private static bool EnsureSurface()
        {
            if (_lease != null) return true;
            if (HostProvider() is not { } host) return false;
            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return false;
            var s = CoreSettings.Current;
            var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
            var want = PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
            if (want.Length == 0) return false;
            var screen = screens[want.Contains(primary) ? primary : want[0]];
            var lease = SharedOverlayHost.Acquire(screen.Bounds);
            if (lease == null)
            {
                if (!_warned) Log.Warning("Afterglow: the platform refused a click-through transparent stage, so the line is not shown");
                _warned = true;
                return false;
            }
            _lease = lease;
            _bounds = screen.Bounds;
            _canvas = new AfterglowLineCanvas(Line, screen.Bounds, screen.Scaling > 0 ? screen.Scaling : 1);
            // The stage is shared per screen (cards, flashes, spirals): WPF AfterglowLineLayer.ZIndex = Subliminal + 1.
            _canvas.ZIndex = ConditioningControlPanel.Services.Compositor.CompositorLayers.Subliminal + 1;
            lease.Canvas.Children.Add(_canvas);
            lease.Raise();
            _band = new[] { AfterglowLineRules.LineBand(Rect(_bounds)) };
            return true;
        }

        private static System.Drawing.Rectangle Rect(PixelRect b) => new(b.X, b.Y, b.Width, b.Height);

        private static void ReadLook()
        {
            var (r, g, b) = PinkFilterOverlay.EffectiveColor();
            Glow = new SKColor(r, g, b);
            Face = AfterglowLineDraw.FaceFor(CoreSettings.Current.SubliminalFont);
        }

        private static void Tick(object? sender, EventArgs e)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                double dt = _last == 0 ? 0 : Math.Min((now - _last) / (double)Stopwatch.Frequency, 0.1);
                _last = now;
                Step(dt);
            }
            catch (Exception ex) { Log.Debug("Afterglow line: tick failed: {E}", ex.Message); }
        }

        /// <summary>One frame (WPF <c>AfterglowLineLayer.Update</c>). UI thread; tests call it with a fixed step.</summary>
        internal static void Step(double dt)
        {
            bool dirty, animating;
            int count;
            ReadLook();
            lock (SteamOverlay.Gate)
            {
                dirty = AfterglowLineRules.Apply(Line, CoreSettings.Current);
                if (Line.Step(dt) is { } done)
                {
                    // The fog writes the finished line across the middle of this monitor.
                    FogLine = done;
                    var f = AfterglowLineRules.FogBand(Rect(_bounds));
                    SuperStageHost.Stage.Remember(f.Left, f.Top, f.Right, f.Bottom);
                }
                count = Line.Count;
                animating = Line.Animating;
            }
            if (count == 0) { TearDown(); return; }
            // The clock is 30 fps already: a moving line repaints every tick, a waiting one only when
            // the ghost strength or the worn word style (shop Wear) changed.
            if (animating || dirty || (int)WornWordStyle.Current != _drawnStyle) _canvas?.InvalidateVisual();
        }

        /// <summary>Render thread: the style the canvas just drew.</summary>
        internal static void NoteDrawn(WordStyle style) => _drawnStyle = (int)style;

        /// <summary>Gone now: panic, the subliminal service stopping, the star going off. UI thread.</summary>
        internal static void StopAll()
        {
            lock (SteamOverlay.Gate) Line.Clear();
            TearDown();
        }

        private static void TearDown()
        {
            _timer?.Stop();
            _timer = null;
            _last = 0;
            _band = Array.Empty<System.Drawing.Rectangle>();
            var lease = _lease;
            _lease = null;
            _canvas = null;
            _bounds = default;
            lease?.Dispose();
        }

        /// <summary>The line's band in desktop pixels while words are up, for "Ignore CCP's own windows"
        /// (WPF <c>AfterglowLineLayer.GetActiveTextRectsPx</c>): never a standing blind spot. Any thread.</summary>
        internal static System.Drawing.Rectangle[] ActiveTextRects() => _band;
    }

    /// <summary>The line on one monitor: draws Core's picture of the shared <see cref="AfterglowLine"/>.</summary>
    internal sealed class AfterglowLineCanvas : Control
    {
        private readonly AfterglowLine _line;
        private readonly AfterglowLineDraw _draw = new();
        private readonly PixelRect _px;
        private readonly double _scale;
        private bool _gone;

        /// <summary>--render-all only: a full line of sample words, lit.</summary>
        internal AfterglowLineCanvas() : this(Sample(), new PixelRect(0, 0, 1100, 780), 1) { }

        /// <param name="px">The monitor in virtual-desktop device pixels (the stage's space).</param>
        /// <param name="scale">Device pixels per DIP on that monitor.</param>
        internal AfterglowLineCanvas(AfterglowLine line, PixelRect px, double scale)
        {
            _line = line;
            _px = px;
            _scale = scale > 0 ? scale : 1;
            IsHitTestVisible = false;
        }

        private static AfterglowLine Sample()
        {
            var line = new AfterglowLine();
            foreach (var w in new[] { "deeper", "softer", "slower", "still" }) { line.Add(w); line.Step(0.3); }
            line.Step(AfterglowLine.LandSeconds);   // the last ghost lands: the full line lights up
            line.Step(AfterglowLine.FinaleRise);
            return line;
        }

        public override void Render(DrawingContext context)
            => context.Custom(new Op(this, new Rect(Bounds.Size)));

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            lock (SteamOverlay.Gate)
            {
                if (_gone) return;
                _gone = true;
                _draw.Dispose();
            }
        }

        /// <summary>Render thread, under the gate. The canvas arrives in DIPs; the line is laid out in
        /// the monitor's pixels, so the picture is drawn in those and scaled back.</summary>
        internal void Draw(SKCanvas canvas)
        {
            lock (SteamOverlay.Gate)
            {
                if (_gone || _line.Count == 0) return;
                var (fx, fy) = AfterglowLineRules.WordsFrom(SuperStageHost.Stage, _px.X, _px.Y, _px.Width, _px.Height);
                var style = WornWordStyle.Current;
                AfterglowLineOverlay.NoteDrawn(style);
                canvas.Save();
                try
                {
                    canvas.Scale((float)(1 / _scale));
                    canvas.Translate(-_px.X, -_px.Y);
                    _draw.Draw(canvas, new SKRect(_px.X, _px.Y, _px.Right, _px.Bottom), _line, AfterglowLineOverlay.Face,
                        AfterglowLineOverlay.Glow, (float)fx, (float)fy, style, WornWordStyle.Motion);
                }
                finally { canvas.Restore(); }
            }
        }

        private sealed class Op : ICustomDrawOperation
        {
            private readonly AfterglowLineCanvas _owner;
            public Op(AfterglowLineCanvas owner, Rect bounds) { _owner = owner; Bounds = bounds; }
            public Rect Bounds { get; }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (lease == null) return;
                using var leased = lease.Lease();
                try { _owner.Draw(leased.SkCanvas); }
                catch (Exception ex) { Log.Debug("Afterglow line: draw failed: {E}", ex.Message); }
            }
        }
    }
}

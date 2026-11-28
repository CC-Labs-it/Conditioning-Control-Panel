using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Super Undertow on this head: a feathered sharp lens cut through Brain Drain's blur under the
    /// cursor (WPF <c>Services/Compositor/BrainDrainLayer</c> Undertow half). Rules are Core's
    /// (<see cref="UndertowLens"/>, <see cref="UndertowOptions"/>, <see cref="SuperBase.UndertowLit"/>),
    /// the picture is Core's <see cref="UndertowDraw"/>; this class steps the lens and
    /// <see cref="UndertowCanvas"/> draws one monitor's blurred frame with it.
    ///
    /// <para><b>No blur on this head yet.</b> <c>Platform/BrainDrainPlayer</c> is audio only: the desktop
    /// capture, blur and melt host is not ported (docs/avalonia-parity.html row
    /// views-studio-brain-drain-feature). Undertow is a hole in that blur, so until a blur host sets
    /// <see cref="BlurShowing"/> and hands its frames to an <see cref="UndertowCanvas"/>, Undertow never
    /// lights and stays out of <c>SuperBox.HostedOnThisHead</c> (no star, no weekly try on nothing).</para>
    ///
    /// <para>Undertow never touches <c>SuperStageHost.Stage</c> (as on WPF), so it takes its own
    /// <see cref="Gate"/> between the UI step and the render-thread draw, not <c>SteamOverlay.Gate</c>.
    /// It is show content riding the blur, not an ambient loop: no ambient stand-down, as on WPF.</para>
    /// </summary>
    internal static class UndertowOverlay
    {
        /// <summary>True while a Brain Drain blur draws a capture on this head. Nothing sets it yet.</summary>
        internal static Func<bool> BlurShowing { get; set; } = () => false;

        /// <summary>A timed drain (Brain Drain bubble pop, Deeper point) with the base off; none on this head yet.</summary>
        internal static Func<bool> TimedDrain { get; set; } = () => false;

        // Owner call owed (AGENTS.md SKIA STRETCH TRAP): WPF ships no click waves (SkiaSharp 2.88 refuses src.eval); 3.119 compiles them, so they stay off for parity.
        internal static bool WavesOn;

        internal static readonly object Gate = new();
        internal static readonly UndertowState State = new();

        /// <summary>True while the lens is lit (UI thread writes under <see cref="Gate"/>).</summary>
        internal static bool On { get; private set; }
        private static bool _polled, _wasDown;

        /// <summary>WPF UpdateUndertow's switch: the blur is up and Core says Undertow is lit.
        /// Tier through <see cref="SuperAccess"/> only, so a free account never lights it.</summary>
        internal static bool Lit()
        {
            try
            {
                return BlurShowing() && SuperBase.UndertowLit(SuperAccess.IsOn(SuperEffect.Undertow),
                    SuperAccess.IsSelected(SuperEffect.Undertow), TimedDrain());
            }
            catch { return false; }
        }

        /// <summary>WPF UndertowMotionNow: MotionFx.Level (the setting capped by the OS and Gentle Visuals).</summary>
        internal static UndertowMotion Motion() => AmbientFxCanvas.Env.Level switch
        {
            MotionLevel.Off => UndertowMotion.Off,
            MotionLevel.Reduced => UndertowMotion.Reduced,
            _ => UndertowMotion.Full,
        };

        /// <summary>UI thread, once per blur frame (WPF: once per engine tick while the pump runs):
        /// re-read the switch, follow the cursor, pay out a click, shrink. True when the drawn frame
        /// changed, so the blur host repaints only then. <paramref name="screenWidth"/> = the width
        /// in device pixels of the monitor under the cursor.</summary>
        internal static bool Step(double dt, double screenWidth)
        {
            bool on = Lit();
            lock (Gate)
            {
                if (on != On)
                {
                    State.Reset();
                    _polled = _wasDown = false;
                    On = on;
                    return true;
                }
                if (!on || SuperStageHost.CursorProvider() is not { } c) return false;
                var s = CoreSettings.Current;
                // The player's options, read live so a slider moves the lens at once.
                State.Tuning = UndertowOptions.Tuning(s.UndertowLensSize, s.UndertowShrinkSpeed, s.UndertowClickWiden);
                var motion = Motion();
                double w = Math.Max(1, screenWidth);
                bool changed = UndertowLens.Step(State, Math.Max(0, dt), c.X, c.Y, w, motion);
                if (PollClick()) { UndertowLens.Click(State, c.X, c.Y, w, motion); changed = true; }
                return changed;
            }
        }

        /// <summary>A press since the last step (never swallowed: the stage's pointer seam only looks).
        /// The first poll after the lens lit only clears, so an old press never widens it. On X11
        /// either button counts and a press shorter than one step can be missed (L0 notes); WPF
        /// hooks the left button only.</summary>
        private static bool PollClick()
        {
            if (SuperStageHost.ClickProvider() is not { } b) return false;
            bool first = !_polled;
            _polled = true;
            bool pressed = !first && (b.Tapped || (b.Down && !_wasDown));
            _wasDown = b.Down;
            return pressed;
        }

        /// <summary>Panic, emergency exit, every stop: the lens and every ripple go at once (WPF
        /// ReleaseCaptures -> StopUndertow). Idempotent, safe when idle.</summary>
        internal static void Stop()
        {
            lock (Gate)
            {
                State.Reset();
                On = false;
                _polled = _wasDown = false;
            }
        }

        private static SKRuntimeEffect? _waveEffect;
        private static bool _waveTried;

        /// <summary>The click-wave effect on this head's SkiaSharp (3.119: <c>CreateShader</c>), or null.
        /// Built only when asked; with <see cref="WavesOn"/> false nothing asks.</summary>
        internal static SKRuntimeEffect? WaveEffect()
        {
            if (_waveTried) return _waveEffect;
            _waveTried = true;
            try
            {
                _waveEffect = SKRuntimeEffect.CreateShader(UndertowDraw.WaveSksl, out var err);
                if (_waveEffect == null) Log.Debug("Undertow wave shader: {E}", err);
            }
            catch (Exception ex) { Log.Debug("Undertow wave shader: {E}", ex.Message); }
            return _waveEffect;
        }
    }

    /// <summary>One monitor's Brain Drain blur, drawn from a small blurred frame stretched over the
    /// monitor at the blur's draw alpha, with the Undertow lens cut in it while Undertow is lit (WPF
    /// <c>BrainDrainLayer.Render</c>). The future blur host sets <see cref="SetFrame"/> and <see cref="Alpha"/>.</summary>
    internal sealed class UndertowCanvas : Control
    {
        private readonly PixelRect _px;
        private readonly double _scale;
        private readonly UndertowState? _sample;
        private readonly UndertowDraw _painter = new();
        private readonly SKPaint _paint = new() { FilterQuality = SKFilterQuality.Low };   // bilinear, as WPF
        private readonly float[] _w = new float[24], _k = new float[24], _p = new float[3];
        private SKImage? _frame;

        /// <summary>The blur's draw alpha (WPF BrainDrainLayer.AlphaFor), 0..255.</summary>
        internal byte Alpha { get; set; } = 255;

        /// <summary>True when the last draw built a wave paint (tests: the waves stay off).</summary>
        internal bool DrewWave { get; private set; }

        /// <param name="px">The monitor in virtual-desktop device pixels (what the cursor is read in).</param>
        /// <param name="scale">Device pixels per DIP on that monitor.</param>
        public UndertowCanvas(PixelRect px, double scale)
        {
            _px = px;
            _scale = scale > 0 ? scale : 1;
            IsHitTestVisible = false;
        }

        /// <summary>--render-all only: one 1280x720 monitor at scale 1, a drawn stand-in for a blurred
        /// capture (no desktop is read), and its own lens rested 2 s mid-screen with one click.</summary>
        internal UndertowCanvas() : this(new PixelRect(0, 0, 1280, 720), 1)
        {
            _sample = new UndertowState();
            for (int i = 0; i < 60; i++) UndertowLens.Step(_sample, 1 / 30.0, 640, 360, 1280, UndertowMotion.Full);
            UndertowLens.Click(_sample, 640, 360, 1280, UndertowMotion.Full);
            UndertowLens.Step(_sample, 0.1, 640, 360, 1280, UndertowMotion.Full);
            Alpha = 0xD9;   // AlphaFor(100)
            SetFrame(SampleFrame(320, 180));
        }

        /// <summary>UI thread: the newest blurred frame for this monitor (the canvas owns it).</summary>
        internal void SetFrame(SKImage? frame)
        {
            SKImage? old;
            lock (UndertowOverlay.Gate) { old = _frame; _frame = frame; }
            if (!ReferenceEquals(old, frame)) old?.Dispose();
        }

        public override void Render(DrawingContext context) => context.Custom(new Op(this, new Rect(Bounds.Size)));

        /// <summary>Render thread, under the gate. The canvas arrives in DIPs; the lens lives in desktop
        /// pixels, so the frame is drawn in those and scaled back. True when it drew.</summary>
        internal bool Draw(SKCanvas canvas)
        {
            lock (UndertowOverlay.Gate)
            {
                DrewWave = false;
                if (_frame == null) return false;
                var state = _sample ?? UndertowOverlay.State;
                bool lens = _sample != null || (UndertowOverlay.On && state.Placed);
                if (!lens) _painter.Release();
                _paint.Color = SKColors.White.WithAlpha(Alpha);
                var dest = new SKRect(_px.X, _px.Y, _px.Right, _px.Bottom);
                canvas.Save();
                try
                {
                    canvas.Scale((float)(1 / _scale));
                    canvas.Translate(-_px.X, -_px.Y);
                    if (!lens) { canvas.DrawImage(_frame, dest, _paint); return true; }
                    var motion = _sample != null ? UndertowMotion.Full : UndertowOverlay.Motion();
                    var wave = UndertowOverlay.WavesOn ? Wave(state, dest, motion) : null;
                    DrewWave = wave != null;
                    try { _painter.Draw(canvas, state, _frame, dest, _paint, _px.Width, motion, _scale, wave); }
                    finally { wave?.Shader?.Dispose(); wave?.Dispose(); }
                }
                finally { canvas.Restore(); }
                return true;
            }
        }

        /// <summary>The click-wave paint (WPF BeginWaves on this head's SkiaSharp), or null.</summary>
        private SKPaint? Wave(UndertowState s, SKRect dest, UndertowMotion motion)
        {
            var fx = UndertowOverlay.WaveEffect();
            if (fx == null || _frame == null || !UndertowDraw.FillWaves(s, _px.Width, motion, _scale, _w, _k, _p)) return null;
            var m = SKMatrix.CreateScale(dest.Width / _frame.Width, dest.Height / _frame.Height)
                .PostConcat(SKMatrix.CreateTranslation(dest.Left, dest.Top));
            using var src = _frame.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, m);
            var uniforms = new SKRuntimeEffectUniforms(fx) { ["w"] = _w, ["p"] = _p, ["k"] = _k };
            var children = new SKRuntimeEffectChildren(fx) { ["src"] = src };
            return new SKPaint { Shader = fx.ToShader(uniforms, children), Color = SKColors.White.WithAlpha(Alpha) };
        }

        /// <summary>A soft violet-to-pink wash with blurred blocks, standing in for a capture.</summary>
        private static SKImage? SampleFrame(int w, int h)
        {
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) return null;
            var c = surface.Canvas;
            using var wash = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                    new[] { new SKColor(0x5B, 0x4B, 0x8A), new SKColor(0xE8, 0x9C, 0xC8) }, null, SKShaderTileMode.Clamp),
            };
            c.DrawRect(0, 0, w, h, wash);
            using var block = new SKPaint { Color = new SKColor(0xF5, 0xF0, 0xFA, 0xC0), ImageFilter = SKImageFilter.CreateBlur(3, 3) };
            for (int i = 0; i < 6; i++) c.DrawRect(20 + i * 50, 30 + (i % 2) * 60, 36, 24, block);
            return surface.Snapshot();
        }

        private sealed class Op : ICustomDrawOperation
        {
            private readonly UndertowCanvas _owner;
            public Op(UndertowCanvas owner, Rect bounds) { _owner = owner; Bounds = bounds; }
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
                catch (Exception ex) { Log.Debug("Undertow: draw failed: {E}", ex.Message); }
            }
        }
    }
}

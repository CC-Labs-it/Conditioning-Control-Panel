using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Steam on this head: the pink filter's Super look (WPF <c>Services/Compositor/SteamLayer</c>).
    /// The rules and the picture are Core's (<see cref="SuperStageHost"/>, <see cref="SteamDraw"/>,
    /// <see cref="SteamWordsDraw"/>); this class seeds the stage's seams, steps it on a 30 fps clock
    /// and swaps the tint windows' flat wash for a <see cref="SteamCanvas"/> while the fog is on.
    ///
    /// <para><b>Rides the tint windows on purpose.</b> They already carry the two refusals that
    /// matter (click-through and per-pixel transparency, <see cref="PinkFilterOverlay"/>), the
    /// monitor target and the stacking order, so the fog can never be shown where the tint could
    /// not. No tint window, no fog.</para>
    ///
    /// <para><b>Threads.</b> The stage steps on the UI thread; the fog is drawn on the render
    /// thread. Both take <see cref="Gate"/>, and the Skia objects are only ever touched under it.</para>
    /// </summary>
    internal static class SteamOverlay
    {
        /// <summary>Held for every stage step and every fog draw.</summary>
        internal static readonly object Gate = new();

        /// <summary>The fog is what the tint windows draw right now.</summary>
        internal static bool On { get; private set; }

        /// <summary>The filter's own colour and strength, as of the last tick.</summary>
        internal static (byte R, byte G, byte B, double Opacity) Look { get; private set; }

        /// <summary>Seconds the fog has been drawing, for the billow.</summary>
        internal static double T { get; private set; }

        /// <summary>The player's own subliminal words, read now and then.</summary>
        internal static string[] Pool { get; private set; } = Array.Empty<string>();

        private static DispatcherTimer? _timer;
        private static long _last;
        private static double _poolAge = 1e9;
        private static bool _seeded;

        /// <summary>Called once at startup, before anything can light a Super effect.</summary>
        internal static void Seed()
        {
            if (_seeded) return;
            _seeded = true;
            SuperStageHost.PostToUi = a =>
            {
                if (Dispatcher.UIThread.CheckAccess()) a();
                else Dispatcher.UIThread.Post(a);
            };
            // This head has no compositor engine: a Super surface is up when its base overlay is.
            SuperStageHost.EngineUp = () => PinkFilterOverlay.IsShowing || SpiralOverlay.IsShowing;
            // WPF SuperStageWpf.cs: the cursor and the restless pool's click, Win32 or X11.
            SuperStageHost.CursorProvider = SuperPointer.Cursor;
            SuperStageHost.ClickProvider = SuperPointer.Click;
            SuperStageHost.SteamSurface = Set;
            // WPF App.xaml.cs MotionFx.Level: the worn fog mix, the step-aside fade and the worn
            // word motion read the player's motion level (capped by the OS and Gentle visuals).
            SuperStepAside.LevelProvider = () => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level;
            // WPF FontPickerHelper.BundledFaceStream: "Fredoka (bundled)" off the app's own asset
            // (the Bold static cut, as the subliminal card draws it), for the Afterglow line's face.
            global::ConditioningControlPanel.Services.Compositor.SubliminalLayer.BundledFaceStream ??= BundledFace;
            // The tier-events hook (a lapse tears every Super effect down) is AccountSeed's: it binds
            // the providers it creates and the identity change, and rebinds them on a re-seed.
        }

        /// <summary>The bundled Fredoka file for the picker's sentinel name, or null for any other name.</summary>
        internal static System.IO.Stream? BundledFace(string name)
        {
            if (!string.Equals(name, "Fredoka (bundled)", StringComparison.OrdinalIgnoreCase)) return null;
            try { return AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/fonts/fredoka/Fredoka-Bold.ttf")); }
            catch (Exception ex) { Log.Debug("Steam: bundled Fredoka: {E}", ex.Message); return null; }
        }

        /// <summary>The stage's Steam surface switch. UI thread. Idempotent: it also dresses tint
        /// windows that were opened since the last call. Off (the fog going off, the Creep star going
        /// off, panic through Core <c>SuperStageHost.Stop</c>) also forgets the Afterglow line, as WPF
        /// <c>SteamLayer.Hide</c> does.</summary>
        private static void Set(bool on)
        {
            On = on;
            if (!on) AfterglowLineOverlay.ClearFogLine();
            foreach (var w in PinkFilterOverlay.Shown) w.SetSteam(on);
            if (on)
            {
                if (_timer != null) return;
                _last = 0;
                ReadLook();
                _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, Tick);
                _timer.Start();
            }
            else if (_timer != null)
            {
                _timer.Stop();
                _timer = null;
            }
        }

        private static void Tick(object? sender, EventArgs e)
        {
            try
            {
                long now = Stopwatch.GetTimestamp();
                double dt = _last == 0 ? 0 : Math.Min((now - _last) / (double)Stopwatch.Frequency, 0.1);
                _last = now;
                _poolAge += dt;
                if (_poolAge > 50) ReadPool();
                ReadLook();
                lock (Gate)
                {
                    SuperStageHost.Step();
                    T += dt;
                }
                foreach (var w in PinkFilterOverlay.Shown) w.InvalidateSteam();
            }
            catch (Exception ex) { Log.Debug("Steam: tick failed: {E}", ex.Message); }
        }

        private static void ReadLook()
        {
            var (r, g, b) = PinkFilterOverlay.EffectiveColor();
            Look = (r, g, b, PinkFilterOverlay.PaintedOpacity);
        }

        private static void ReadPool()
        {
            _poolAge = 0;
            try
            {
                var pool = CoreSettings.Current.SubliminalPool;
                Pool = pool == null
                    ? Array.Empty<string>()
                    : pool.Where(k => k.Value && k.Key.Length is > 0 and <= 16).Select(k => k.Key.ToLowerInvariant()).ToArray();
            }
            catch (Exception ex) { Log.Debug("Steam words: {E}", ex.Message); }
        }
    }

    /// <summary>One monitor's fog: fills a tint window and draws the shared stage's field for it.</summary>
    internal sealed class SteamCanvas : Control
    {
        private static readonly SKTypeface Face = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;

        private readonly PixelRect _px;
        private readonly double _scale;
        private readonly SteamDraw _fog = new();
        private readonly SteamWords _words = new();
        private readonly SteamWordsDraw _wordsDraw = new();
        private string[]? _poolSeen;

        /// <summary>This monitor's fog words (tests read the Afterglow line slot).</summary>
        internal SteamWords Words => _words;
        private double _lastT = -1;
        private bool _gone;

        /// <param name="px">The monitor in virtual-desktop device pixels (what the cursor is read in).</param>
        /// <param name="scale">Device pixels per DIP on that monitor.</param>
        public SteamCanvas(PixelRect px, double scale)
        {
            _px = px;
            _scale = scale > 0 ? scale : 1;
            IsHitTestVisible = false;
        }

        /// <summary>--render-all only (RenderProof builds every view with no arguments): one
        /// 1280x720 monitor at scale 1. Internal, so no production caller ships the sample.</summary>
        internal SteamCanvas() : this(new PixelRect(0, 0, 1280, 720), 1) { }

        public override void Render(DrawingContext context)
            => context.Custom(new Op(this, new Rect(Bounds.Size)));

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            lock (SteamOverlay.Gate)
            {
                if (_gone) return;
                _gone = true;
                _fog.Dispose();
                _wordsDraw.Dispose();
            }
        }

        /// <summary>Render thread, under the gate. The canvas arrives in this control's DIPs; the
        /// field lives in desktop pixels, so the picture is drawn in those and scaled back.</summary>
        internal void Draw(SKCanvas canvas)
        {
            lock (SteamOverlay.Gate)
            {
                if (_gone) return;
                var rect = new System.Drawing.Rectangle(_px.X, _px.Y, _px.Width, _px.Height);
                SuperStageHost.NoteScreen(rect, _scale);
                var stage = SuperStageHost.Stage;
                if (!stage.SteamOn) return;
                var field = stage.Field(rect.ToString(), rect.X, rect.Y, rect.Width, rect.Height);
                // The gold-box options, read live: the next stage step heals and billows with them.
                var set = CoreSettings.Current;
                field.Heal = SteamOptions.HealRate(set.SteamFogReturn);
                field.Drift = SteamOptions.Drift(set.SteamDrift);

                var pool = SteamOverlay.Pool;
                if (!ReferenceEquals(pool, _poolSeen)) { _poolSeen = pool; _words.SetPool(pool); }

                // The base filter's own colour and strength: thicker than the flat tint, never opaque.
                var look = SteamOverlay.Look;
                double strength = Math.Clamp(0.3 + look.Opacity * 1.6, 0.3, 0.82);
                var colour = new SKColor(look.R, look.G, look.B);
                var bounds = new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
                double t = SteamOverlay.T;

                canvas.Save();
                try
                {
                    canvas.Scale((float)(1 / _scale));
                    canvas.Translate(-rect.X, -rect.Y);
                    _fog.Draw(canvas, bounds, field, colour, strength, Array.Empty<string>(), null, Face, t);

                    double dt = _lastT < 0 ? 0 : Math.Clamp(t - _lastT, 0, 0.1);
                    _lastT = t;
                    // A finished Afterglow line, written across the middle where its band was remembered.
                    _words.Line = AfterglowLineOverlay.FogLine;
                    _words.Step(field, dt, GentleVisuals.On, set.SteamWordsInFog);
                    _wordsDraw.Draw(canvas, bounds, field, _words, colour, Face);
                }
                finally { canvas.Restore(); }
                field.ClearDirty();
            }
        }

        private sealed class Op : ICustomDrawOperation
        {
            private readonly SteamCanvas _owner;
            public Op(SteamCanvas owner, Rect bounds) { _owner = owner; Bounds = bounds; }
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
                catch (Exception ex) { Log.Debug("Steam: draw failed: {E}", ex.Message); }
            }
        }
    }
}

// Linux beta fix round, lane perf (owner, 2026-11-06, WSLg on the CPU: "kinda laggy to refresh ... even
// the marquee is kinda laggy"). Under WSLg Avalonia blacklists Mesa llvmpipe and draws with Skia on the CPU;
// VMs, old laptops and some Linux desktops land on the same path. The render thread then sat at ~100%
// of one core on Home and on the launcher, so every hover, tooltip close and marquee step waited for a
// frame. This answers "is this process drawing without a GPU?" once, from the first window's first frame,
// so the FX budget (AmbientFxCanvas.Env.CurrentTier) can take WPF's Performance mode tier on such a machine.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Detects Skia's CPU backend: a <see cref="CompositionCustomVisual"/> asks the render thread for the
    /// Skia lease of the frame it draws, and a lease without a <c>GRContext</c> is a raster (CPU) surface.
    /// Whether the lighter tier applies is <see cref="FloorActive"/>: Settings ▸ Performance ▸ Lighter effects
    /// (Auto = this probe, On, Off). <c>CCP_SOFTWARE_RENDER=1|0</c> is a dev override above the setting.
    /// <c>CCP_FRAME_STATS=1</c> keeps the visual on every window it probed and logs the render thread's
    /// frame rate, the shared beat's tick rate and the running frame clocks every five seconds ([FRAMESTATS]).
    /// </summary>
    internal static class SoftwareRender
    {
        /// <summary>What the probe found: null until it answered, then fixed for the process.</summary>
        internal static bool? Active { get; private set; }

        /// <summary>CCP_SOFTWARE_RENDER=1|0: a developer override that beats the setting (A/B runs).</summary>
        internal static bool? DevOverride { get; private set; } = ParseOverride(Environment.GetEnvironmentVariable("CCP_SOFTWARE_RENDER"));

        /// <summary>
        /// Does the lighter FX tier apply? The dev override, else Settings ▸ Performance ▸ Lighter effects:
        /// On and Off decide; Auto (the default) follows the probe.
        /// </summary>
        internal static bool FloorActive => DevOverride ?? Decide(Setting, Active);

        internal static bool Decide(global::ConditioningControlPanel.Models.LighterEffectsMode mode, bool? detected) => mode switch
        {
            global::ConditioningControlPanel.Models.LighterEffectsMode.On => true,
            global::ConditioningControlPanel.Models.LighterEffectsMode.Off => false,
            _ => detected == true,
        };

        private static global::ConditioningControlPanel.Models.LighterEffectsMode Setting
        {
            get
            {
                try { return CoreSettings.Current.LighterEffects; }
                catch { return global::ConditioningControlPanel.Models.LighterEffectsMode.Auto; }
            }
        }

        /// <summary>Raised once on the UI thread when <see cref="Active"/> becomes known from a frame.</summary>
        internal static event Action<bool>? Detected;

        internal static readonly bool FrameStats = Environment.GetEnvironmentVariable("CCP_FRAME_STATS") == "1";

        /// <summary>Set by a real launch (Program.Main, --perf-bench). Headless tests and offscreen renders
        /// draw on Skia's CPU backend by design and must keep the tier their assertions were written for.</summary>
        internal static bool Armed;

        private static long _probeAt;   // when the probe in flight started (0 = none)
        private static readonly HashSet<TopLevel> Watched = new();

        internal static bool? ParseOverride(string? value) => value?.Trim() switch
        {
            "1" or "true" or "on" => true,
            "0" or "false" or "off" => false,
            _ => null,
        };

        /// <summary>
        /// The early answer, before any window is built (App.OnFrameworkInitializationCompleted): the platform
        /// registers an <see cref="IPlatformGraphics"/> only when it brought up a GPU backend (ANGLE, WGL, GLX,
        /// Vulkan); without one every surface is Skia's raster framebuffer. Read through Avalonia's internal
        /// service locator, so it fails closed: any surprise leaves the answer to the first frame's probe.
        /// Only "no GPU" is taken early; a registered backend can still fall back per surface, and the frame says so.
        /// </summary>
        internal static void DetectEarly()
        {
            if (!Armed || Active != null) return;
            try
            {
                var locator = typeof(AvaloniaObject).Assembly.GetType("Avalonia.AvaloniaLocator");
                var current = locator?.GetProperty("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
                var get = current?.GetType().GetMethod("GetService", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, new[] { typeof(Type) });
                if (get == null) return;
                if (get.Invoke(current, new object[] { typeof(IPlatformGraphics) }) == null) Report(true);
            }
            catch (Exception ex) { Serilog.Log.Debug("SoftwareRender early probe: {E}", ex.Message); }
        }

        /// <summary>Called by every FrameClock start on a window (cheap after the first answer).</summary>
        internal static void Probe(TopLevel top)
        {
            if (!Armed) return;
            if (FrameStats) { Watch(top); return; }
            if (Active != null) return;
            // A probe whose window never drew (closed first, hidden) gives the next window a turn.
            long now = Environment.TickCount64;
            if (Interlocked.Read(ref _probeAt) is var at && at != 0 && now - at < 3000) return;
            Interlocked.Exchange(ref _probeAt, now);
            if (!Attach(top, stats: false)) Interlocked.Exchange(ref _probeAt, 0);
        }

        /// <summary>Test seam and the probe's answer: fixes <see cref="Active"/> and tells the FX gates.</summary>
        internal static void Report(bool software)
        {
            if (Active != null) return;
            Active = software;
            Serilog.Log.Information(software
                ? "[FXPERF] Skia is drawing on the CPU (no GPU); lighter effects {Mode}, applied {Applied} (WPF's Performance mode tier)"
                : "[FXPERF] Skia is drawing on the GPU (lighter effects {Mode}, applied {Applied})", Setting, DevOverride ?? Decide(Setting, software));
            Detected?.Invoke(software);
            // Every running loop re-reads its gate and budget (FX built before the answer read Quality).
            if (FloorActive) Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        }

        /// <summary>Tests: back to the unanswered, disarmed state a test process starts in.</summary>
        internal static void ResetForTests()
        {
            Active = null;
            DevOverride = ParseOverride(Environment.GetEnvironmentVariable("CCP_SOFTWARE_RENDER"));
            Armed = false;
            Interlocked.Exchange(ref _probeAt, 0);
        }

        private static void Watch(TopLevel top)
        {
            lock (Watched)
                if (!Watched.Add(top)) return;
            // CCP_DEBUG_OVERLAYS=DirtyRects,Fps (with CCP_FRAME_STATS=1): Avalonia's own overlays on every window.
            if (Enum.TryParse<global::Avalonia.Rendering.RendererDebugOverlays>(Environment.GetEnvironmentVariable("CCP_DEBUG_OVERLAYS"), true, out var overlays))
                top.RendererDiagnostics.DebugOverlays = overlays;
            Attach(top, stats: true);
        }

        private static bool Attach(TopLevel top, bool stats)
        {
            try
            {
                var compositor = ElementComposition.GetElementVisual(top)?.Compositor;
                if (compositor == null || ElementComposition.GetElementChildVisual(top) != null) return false;
                var handler = new ProbeHandler(stats, top is Window w ? w.Title : top.GetType().Name, top);
                var visual = compositor.CreateCustomVisual(handler);
                visual.Size = new global::Avalonia.Vector(1, 1);
                ElementComposition.SetElementChildVisual(top, visual);
                visual.SendHandlerMessage(ProbeHandler.Go);
                if (!stats)
                {
                    // One frame answers it; the visual leaves on the next UI turn after the answer.
                    void Done(bool _)
                    {
                        Detected -= Done;
                        if (ReferenceEquals(ElementComposition.GetElementChildVisual(top), visual))
                            ElementComposition.SetElementChildVisual(top, null);
                    }
                    Detected += Done;
                }
                return true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("SoftwareRender probe: {E}", ex.Message);
                return false;
            }
        }

        /// <summary>The shared beat's ticks (FrameClock), counted only while <see cref="FrameStats"/> is on.</summary>
        internal static int BeatTicks;

        private sealed class ProbeHandler : CompositionCustomVisualHandler
        {
            internal static readonly object Go = new();
            private readonly bool _stats;
            private readonly string _name;
            private bool _answered;
            private int _frames;
            private readonly Stopwatch _sw = new();
            private readonly WeakReference<TopLevel> _top;

            public ProbeHandler(bool stats, string? name, TopLevel top) { _stats = stats; _name = name ?? "window"; _top = new(top); }

            public override void OnMessage(object message)
            {
                if (!ReferenceEquals(message, Go)) return;
                Invalidate();
                if (_stats) { _sw.Start(); RegisterForNextAnimationFrameUpdate(); }
            }

            public override void OnAnimationFrameUpdate()
            {
                _frames++;
                if (_sw.Elapsed.TotalSeconds >= 5)
                {
                    double s = _sw.Elapsed.TotalSeconds;
                    int beats = Interlocked.Exchange(ref BeatTicks, 0);
                    double fps = _frames / s;
                    Dispatcher.UIThread.Post(() =>
                    {
                        string clocks = _top.TryGetTarget(out var t) ? Controls.Fx.TopLevelFrameSource.For(t).DescribeSubscribers() : "";
                        Serilog.Log.Information("[FRAMESTATS] {Name}: render loop {R:F1} fps, beat {B:F1} ticks/s (all windows); clocks: {Clocks}",
                            _name, fps, beats / s, clocks);
                    });
                    _frames = 0;
                    _sw.Restart();
                }
                RegisterForNextAnimationFrameUpdate();
            }

            public override void OnRender(ImmediateDrawingContext context)
            {
                if (_answered) return;
                _answered = true;
                bool? software = null;
                try
                {
                    var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                    if (feature != null)
                        using (var lease = feature.Lease())
                            software = lease.GrContext == null;
                }
                catch (Exception ex) { Serilog.Log.Debug("SoftwareRender lease: {E}", ex.Message); }
                if (software is { } sw) Dispatcher.UIThread.Post(() => Report(sw));
            }
        }
    }
}

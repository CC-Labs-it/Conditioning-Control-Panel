using System;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Stops the headless compositor's wall clock so a test renders compositor animations at exact times and
/// reads the values the compositor draws with, not a recomputation of them. Avalonia exposes neither, so this
/// reaches its internal server clock and server visual by reflection; a rename fails loudly here.</summary>
internal sealed class CompositorTime : IDisposable
{
    private static readonly FieldInfo Elapsed = typeof(Stopwatch).GetField("_elapsed", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly PropertyInfo ServerOf = typeof(CompositionObject).GetProperty("Server", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly Type ServerVisual = typeof(CompositionObject).Assembly.GetType("Avalonia.Rendering.Composition.Server.ServerCompositionVisual", true)!;

    private readonly Stopwatch _clock;
    private long _origin;

    private CompositorTime(Stopwatch clock) => (_clock, _origin) = (clock, clock.ElapsedTicks);

    /// <summary>Freezes the clock of the compositor drawing <paramref name="attached"/>; time 0 is now.</summary>
    public static CompositorTime Freeze(Visual attached)
    {
        var compositor = ElementComposition.GetElementVisual(attached)!.Compositor;
        var server = typeof(Compositor).GetProperty("Server", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(compositor)!;
        var clock = (Stopwatch)server.GetType().GetProperty("Clock")!.GetValue(server)!;
        Assert.NotNull(Elapsed);
        clock.Stop();
        return new CompositorTime(clock);
    }

    /// <summary>Moves time 0 to the current frozen instant (before the next switch).</summary>
    public void Mark() => _origin = _clock.ElapsedTicks;

    /// <summary>Commits pending changes and renders one compositor frame <paramref name="ms"/> after time 0.</summary>
    public void RenderAt(double ms)
    {
        Elapsed.SetValue(_clock, _origin + (long)Math.Round(ms * Stopwatch.Frequency / 1000));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    public static bool Visible(Visual v) => (bool)ServerVisual.GetProperty("Visible")!.GetValue(Server(v))!;

    public static float Opacity(Visual v) => (float)ServerVisual.GetProperty("Opacity")!.GetValue(Server(v))!;

    public static Vector3D Translation(Visual v) => (Vector3D)ServerVisual.GetProperty("Translation")!.GetValue(Server(v))!;

    private static object Server(Visual v) => ServerOf.GetValue(ElementComposition.GetElementVisual(v)!)!;

    public void Dispose() => _clock.Start();
}

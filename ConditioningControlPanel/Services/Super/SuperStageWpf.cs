using System.Runtime.InteropServices;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Compositor;

namespace ConditioningControlPanel.Services.Super;

/// <summary>
/// The Windows side of <see cref="SuperStageHost"/> (which lives in Core): the UI thread, the
/// overlay engine check, the cursor and the two compositor layers that draw Steam and the Whirlpool.
/// </summary>
internal static class SuperStageWpf
{
    private static SteamLayer? _steam;
    private static WhirlpoolLayer? _pool;

    /// <summary>Called once at startup, before anything can light a Super effect.</summary>
    public static void Seed()
    {
        SuperStageHost.PostToUi = a => DispatcherHelper.RunOnUI(a);
        SuperStageHost.EngineUp = () => App.Overlay?.IsRunning == true && App.CompositorEnabled && App.Compositor != null;
        SuperStageHost.CursorProvider = () => GetCursorPos(out var p) ? (p.X, p.Y) : null;
        SuperStageHost.SteamSurface = on =>
        {
            if (on)
            {
                if (_steam == null) { _steam = new SteamLayer(App.Compositor!); App.Compositor!.RegisterLayer(_steam); }
                _steam.Show();
            }
            else _steam?.Hide();
        };
        SuperStageHost.PoolSurface = on =>
        {
            if (on)
            {
                if (_pool == null) { _pool = new WhirlpoolLayer(App.Compositor!); App.Compositor!.RegisterLayer(_pool); }
                _pool.Show();
            }
            else _pool?.Hide();
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}

using System.Windows;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// WPF hooks for the Core <see cref="ChaosFxLayer"/> (moved to Core in chaos-skia-fx): the engine wake and
/// primary-monitor stage scale it used to read itself, and the WPF <see cref="Point"/> call shapes, so
/// every WPF caller passes exactly what it did before.
/// </summary>
internal static class ChaosFxLayerWpf
{
    public static ChaosFxLayer Create(CompositorEngine engine) => new(engine.Wake, PrimaryScale);

    /// <summary>The primary monitor's DPI scale (the layer's old RefreshScale body).</summary>
    private static float PrimaryScale()
    {
        var scr = System.Windows.Forms.Screen.PrimaryScreen;
        return scr != null ? (float)CompositorHostWindow.GetDpiScaleForScreen(scr) : 1f;
    }

    private static SKPoint Sk(Point p) => new((float)p.X, (float)p.Y);

    public static void EmitTrail(this ChaosFxLayer l, Point centerPx, double lifeSec, bool warm, double dirX = 0, double dirY = 0) =>
        l.EmitTrail(Sk(centerPx), lifeSec, warm, dirX, dirY);

    public static void EmitBurst(this ChaosFxLayer l, Point centerPx, SKColor col, float scale) => l.EmitBurst(Sk(centerPx), col, scale);

    public static void EmitRipple(this ChaosFxLayer l, Point centerPx, double radiusPx, double lifeMs, bool strong) =>
        l.EmitRipple(Sk(centerPx), radiusPx, lifeMs, strong);

    public static void AddStrike(this ChaosFxLayer l, IReadOnlyList<(Point From, Point To)> boltsPx)
    {
        if (boltsPx == null) return;
        var pts = new (SKPoint From, SKPoint To)[boltsPx.Count];
        for (int i = 0; i < pts.Length; i++) pts[i] = (Sk(boltsPx[i].From), Sk(boltsPx[i].To));
        l.AddStrike(pts);
    }
}

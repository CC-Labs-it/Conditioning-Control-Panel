using System.Windows;
using ConditioningControlPanel.Fx;

namespace ConditioningControlPanel.Services.FirstShow;

// The placement rule lives in Core (FirstShowLayout); this only converts WPF Rect / Size / Point.
// Rect.Empty (infinite origin, negative size) maps to Core's "no target".
internal static class FirstShowLayoutWpf
{
    private static RectD Core(Rect r) => r.IsEmpty ? FirstShowLayout.Empty : new(r.X, r.Y, r.Width, r.Height);
    private static Rect Wpf(RectD r) => new(r.Left, r.Top, r.Width, r.Height);

    internal static Rect Speech(Rect bounds, Size size, Rect body, Rect target, Point preferred) =>
        Wpf(FirstShowLayout.Speech(Core(bounds), new(size.Width, size.Height), Core(body), Core(target),
            new Vec2(preferred.X, preferred.Y)));

    internal static Rect GuideBody(Rect bounds, Size body, Size speech, Rect target) =>
        Wpf(FirstShowLayout.GuideBody(Core(bounds), new(body.Width, body.Height),
            new(speech.Width, speech.Height), Core(target)));
}

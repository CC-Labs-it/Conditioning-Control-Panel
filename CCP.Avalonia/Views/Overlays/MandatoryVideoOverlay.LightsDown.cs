using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>What Super Lights Down (<see cref="LightsDownOverlay"/>) reads off the video host each frame, the
/// twin of WPF VideoService's Lights Down hooks (HasOpenWindows, IsCleaningUp, FloatingVideoUp,
/// FloatingVideoScreens, CopyFloatingVideoRects, CopyAttentionBounds). Read only, UI thread.</summary>
internal sealed partial class MandatoryVideoOverlay
{
    /// <summary>WPF IsCleaningUp: a teardown is running.</summary>
    internal bool LightsDownClosing => _closing;

    /// <summary>WPF HasOpenWindows || HasFloatingInstances.</summary>
    internal bool LightsDownHasWindows => _surfaces.Count > 0 || _floaters.Count > 0;

    /// <summary>WPF FloatingVideoUp: the run's clip floats, or joined floating windows hold the run.</summary>
    internal bool LightsDownFloating => Floating || _floaters.Count > 0;

    /// <summary>WPF FloatingVideoScreens: every screen a floating video may open on (the Video card's monitor pick).</summary>
    internal List<(PixelRect Bounds, double Scale)> LightsDownScreens(Window reference)
    {
        var list = new List<(PixelRect, double)>();
        foreach (var s in Targets(reference, fillAll: true))
            if (s != null) list.Add((s.Bounds, s.Scaling > 0 ? s.Scaling : 1));
        return list;
    }

    /// <summary>WPF CopyFloatingVideoRects: the floating windows a player can see, in desktop pixels. A window
    /// still veiled before its first frame is left out (a halo around nothing); a closed one is gone, so its
    /// hole eases shut where it was.</summary>
    internal void CopyLightsDownFloats(List<(Window Window, PixelRect Px)> into)
    {
        into.Clear();
        foreach (var f in _floaters)
            if (f.Revealed && !f.Closed && f.Window.IsVisible) into.Add((f.Window, PxOf(f.Window)));
        if (Floating && _floatingOnScreen)
            foreach (var x in _surfaces)
                if (x.Window.IsVisible) into.Add((x.Window, PxOf(x.Window)));
    }

    /// <summary>Everything that must stay over a floating dim: the floating windows and their grace cards.</summary>
    internal void CopyLightsDownAbove(List<TopLevel> into)
    {
        into.Clear();
        foreach (var x in AllSurfaces)
        {
            if (x.Window.IsVisible) into.Add(x.Window);
            if (x.GraceHost is { IsVisible: true } card) into.Add(card);
        }
    }

    /// <summary>WPF CopyAttentionBounds: the live attention targets in desktop DIPs.</summary>
    internal void CopyLightsDownCuts(List<LightsDownRect> into)
    {
        into.Clear();
        foreach (var t in _targets)
        {
            var r = t.GazeBounds;
            if (r.Width > 0 && r.Height > 0) into.Add(new LightsDownRect(r.X, r.Y, r.Width, r.Height));
        }
    }

    private static PixelRect PxOf(Window w)
    {
        var k = w.RenderScaling > 0 ? w.RenderScaling : 1;
        return new PixelRect(w.Position, PixelSize.FromSize(w.ClientSize, k));
    }
}

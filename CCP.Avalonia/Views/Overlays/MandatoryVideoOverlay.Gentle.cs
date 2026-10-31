using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;
internal sealed partial class MandatoryVideoOverlay
{
    private bool _naturalEnd;
    private readonly List<(TintOverlayWindow Window, DateTime Start, double Seconds)> _curtains = new();
    private DispatcherTimer? _curtainTimer;
    internal static bool SkipGentlePlatformChecks;
    internal IReadOnlyList<TintOverlayWindow> GentleCurtains => _curtains.Select(c => c.Window).ToArray();
    // WPF VideoService.cs:7413: only natural completion leaves a fading desktop curtain.
    internal void Ended() { _naturalEnd = true; try { Scheduler.Ended(); } finally { _naturalEnd = false; } }
    // WPF GentleCurtain.cs:33,81,126: black, passive and click-through; refuse unsupported backends.
    internal void CoverGentle(IEnumerable<Window> owners, bool waiting)
    {
        DropGentle();
        if (!GentleVisuals.On) return;
        foreach (var owner in owners)
        {
            var w = new TintOverlayWindow { Position = owner.Position, Width = owner.Bounds.Width, Height = owner.Bounds.Height };
            w.SetTint(0, 0, 0, 1);
            var rect = new PixelRect(owner.Position, PixelSize.FromSize(owner.Bounds.Size, owner.DesktopScaling));
            if (!SkipGentlePlatformChecks && (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOverrideRedirect(w, rect))) { w.Close(); continue; }
            try { w.Show(); } catch { w.Close(); continue; }
            if (!SkipGentlePlatformChecks && w.ActualTransparencyLevel == WindowTransparencyLevel.None) { w.Close(); continue; }
            _curtains.Add((w, waiting ? Now.AddSeconds(3) : DateTime.MaxValue, GentleVisuals.FadeSeconds));
        }
        _curtainTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => GentleFrame());
        if (_curtains.Count > 0) _curtainTimer.Start();
    }
    // WPF VideoService.cs:7385-7399: Playing lifts once; a 3 s backstop handles a missing event.
    internal void LiftGentle()
    {
        for (var i = 0; i < _curtains.Count; i++)
            if (_curtains[i].Start > Now) _curtains[i] = (_curtains[i].Window, Now, _curtains[i].Seconds);
    }
    internal void GentleFrame()
    {
        for (var i = _curtains.Count - 1; i >= 0 && i < _curtains.Count; i--)
        {
            var c = _curtains[i];
            c.Window.SetTint(0, 0, 0, 1 - Math.Clamp((Now - c.Start).TotalSeconds / c.Seconds, 0, 1));
            if ((Now - c.Start).TotalSeconds < c.Seconds) continue;
            _curtains.RemoveAt(i); c.Window.Close();
        }
        if (_curtains.Count == 0) _curtainTimer?.Stop();
    }
    internal void DropGentle()
    {
        _curtainTimer?.Stop();
        var live = _curtains.ToArray(); _curtains.Clear();
        foreach (var c in live) c.Window.Close();
    }
}

// WPF AvatarTubeWindow.Windowing.cs:893-923,998-1033. Reuse the Core placement and ownership rules.
using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.AvatarTubeLayout;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private readonly MakeRoomState _makeRoom = new();
    private bool _applyingRoom;

    private Box MakeRoom(Box parent, PixelRect work, double painted)
    {
        if (_applyingRoom || !IsVisible || _parentWindow is not { } p) return parent;
        _makeRoom.NoteParentRect(parent);
        if (!_makeRoom.CanAdjust || p.WindowState != WindowState.Normal || LockdownService.Current?.IsActive == true)
            return parent;
        var screen = p.Screens.ScreenFromWindow(p);
        if (screen != null && parent.X <= screen.Bounds.X && parent.Y <= screen.Bounds.Y
            && parent.Right >= screen.Bounds.Right && parent.Bottom >= screen.Bounds.Bottom) return parent;
        double k = p.DesktopScaling;
        if (MakeRoomPlacement.Plan(parent, Math.Max(p.MinWidth * k, 800), painted, ToBox(work)) is not { } target)
            return parent;
        var rounded = new Box(Math.Round(target.X), Math.Round(target.Y), Math.Round(target.W), Math.Round(target.H));
        _makeRoom.Applied(parent, rounded);
        ApplyParentRoom(rounded, k);
        return rounded;
    }

    private void ApplyParentRoom(Box box, double scaling)
    {
        if (_parentWindow is not { } p) return;
        _applyingRoom = true;
        try
        {
            p.Width = box.W / scaling;
            p.Height = box.H / scaling;
            p.Position = new PixelPoint((int)box.X, (int)box.Y);
        }
        finally { _applyingRoom = false; }
    }

    private void RestoreMadeRoom()
    {
        if (_parentWindow is not { } p) return;
        if (_makeRoom.TakeRestore(p.WindowState == WindowState.Minimized) is { } saved)
            ApplyParentRoom(saved, p.DesktopScaling);
    }
}

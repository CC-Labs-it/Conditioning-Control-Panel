// WPF AvatarTubeWindow.Windowing.cs:97-98,730-750,801-809: legacy PNG sprite bob.
using System;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private BeatLoop? _floatLoop;
    // Keep the sprite's motion separate from the parent's reply bounce (Chat.cs).
    private readonly TranslateTransform _avatarFloat = new();

    internal void StartFloatingAnimation()
    {
        StopFloatingAnimation();
        _imgAvatar.RenderTransform = _avatarFloat;
        // Preserve WPF's 0.05 radians per 16 ms on the shared window beat. Elapsed time
        // keeps the speed independent of the frame rate; BeatLoop parks hidden windows.
        _floatLoop ??= new BeatLoop(_imgAvatar,
            seconds => _avatarFloat.Y = Math.Sin(seconds * (0.05 / 0.016)) * 4);
        _floatLoop.Start();
    }

    private void StopFloatingAnimation()
    {
        _floatLoop?.Stop();
        _avatarFloat.Y = 0;
    }
}

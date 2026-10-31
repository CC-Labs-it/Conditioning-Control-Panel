// WPF AvatarTubeWindow.Windowing.cs:97-98,730-750,801-809: legacy PNG sprite bob.
using System;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube;

public partial class AvatarTubeWindow
{
    private DispatcherTimer? _floatTimer;
    private double _floatPhase;
    // Keep the sprite's motion separate from the parent's reply bounce (Chat.cs).
    private readonly TranslateTransform _avatarFloat = new();

    internal void StartFloatingAnimation()
    {
        StopFloatingAnimation();
        _floatPhase = 0;
        _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _floatTimer.Tick += (_, _) => TickFloatingAnimation();
        _floatTimer.Start();
    }

    // One production timer tick, exposed to headless tests without wall-clock sleeps.
    internal void TickFloatingAnimation()
    {
        if (_floatTimer?.IsEnabled != true) return;
        _floatPhase += 0.05;
        _avatarFloat.Y = Math.Sin(_floatPhase) * 4;
    }

    private void StopFloatingAnimation()
    {
        _floatTimer?.Stop();
        _floatTimer = null;
        _avatarFloat.Y = 0;
    }
}

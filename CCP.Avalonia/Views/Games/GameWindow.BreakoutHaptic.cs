using System;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Haptics;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal Action<BackRoomHaptic> BreakoutHaptic = BackRoomHapticDirector.OnHaptic;
    private void StopBreakoutHaptic() => BreakoutHaptic(new BackRoomHaptic("breakout", 0, 0, "host"));
}

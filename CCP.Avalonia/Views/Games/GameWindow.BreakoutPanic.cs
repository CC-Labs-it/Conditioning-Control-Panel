// WPF BackRoomHostService.cs:610-627,639-652: Breakout shares the room's panic ladder.
using System;
using System.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private bool _breakoutPanicSuspended;
    private DateTime _breakoutPanicTime;

    internal static bool HandleBreakoutPanic(DateTime now)
    {
        GameWindow? w;
        lock (Open) w = Open.FirstOrDefault(w => w.IsBreakout);
        if (w == null) return false;
        if (w._breakoutPanicSuspended && now - w._breakoutPanicTime <= TimeSpan.FromSeconds(2))
        {
            w.Close();
            return true;
        }
        w._breakoutPanicSuspended = true;
        w._breakoutPanicTime = now;
        w.Post(new { type = "suspend", on = true, reason = "panic" });
        return true;
    }

    internal void ResumeBreakoutAfterPanic(DateTime now)
    {
        if (!_breakoutPanicSuspended || now - _breakoutPanicTime <= TimeSpan.FromSeconds(2)) return;
        _breakoutPanicSuspended = false;
        Post(new { type = "suspend", on = false, reason = "panic" });
    }
}

using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// The taps placed inside existing services for things they raise no app-level event for. A tap only
/// raises; the meter decides whether anything counts. Nothing here ever throws into the caller.
/// </summary>
public static class DareTaps
{
    /// <summary>A session ran to its end (SessionEngine, the branch that raises SessionCompleted).</summary>
    public static event Action<SessionDifficulty>? SessionFinished;

    public static void RaiseSessionFinished(SessionDifficulty difficulty)
    {
        try { SessionFinished?.Invoke(difficulty); }
        catch (Exception ex) { Serilog.Log.Debug("Dares session tap failed: {E}", ex.Message); }
    }
}

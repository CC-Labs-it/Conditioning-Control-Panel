using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Silent intake card accrual (WPF IntakePunchCardService:87,154).</summary>
internal static class IntakePunchCardHead
{
    internal static void Draft(string folder, string? id) => Update(folder, state =>
    {
        if (string.IsNullOrWhiteSpace(id) || state.CompletedUtc.HasValue || state.PunchedCount >= 8 ||
            state.PendingDrafts.Any(d => d.SessionId == id)) return false;
        state.PendingDrafts.Add(new PendingIntakeDraft { SessionId = id, DraftedUtc = DateTime.UtcNow });
        while (state.PendingDrafts.Count > 8) state.PendingDrafts.RemoveAt(0);
        return true;
    });

    internal static void Progress(string folder, string? id, double percent, bool completed = false) => Update(folder, state =>
    {
        if ((!completed && percent < 50) || string.IsNullOrWhiteSpace(id) ||
            state.CompletedUtc.HasValue || state.PunchedCount >= 8) return false;
        var draft = state.PendingDrafts.FirstOrDefault(d => d.SessionId == id);
        if (draft == null) return false;
        state.PendingDrafts.Remove(draft);
        var now = DateTime.UtcNow;
        state.PunchedCount = Math.Min(8, state.PunchedCount + 1);
        state.PunchedUtc.Add(now);
        if (state.PunchedCount == 8) state.CompletedUtc = now;
        return true;
    });

    private static void Update(string folder, Func<IntakePunchCardState, bool> change)
    {
        try
        {
            var path = Path.Combine(folder, IntakePunchCardState.FileName);
            var tmp = path + ".tmp";
            IntakePunchCardState? state = null;
            foreach (var candidate in new[] { path, tmp })
            {
                if (!File.Exists(candidate)) continue;
                try { state = JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(candidate)); }
                catch (Exception ex) { Log.Warning(ex, "Cannot read intake card {Path}", candidate); }
                if (state != null) break;
            }
            // Do not replace an unreadable card with a new one.
            if (state == null && (File.Exists(path) || File.Exists(tmp))) return;
            state ??= new IntakePunchCardState();
            state.PendingDrafts ??= new();
            state.PunchedUtc ??= new();
            state.PunchedCount = Math.Clamp(state.PunchedCount, 0, 8);
            if (!state.CardStartedUtc.HasValue)
            {
                state.CardStartedUtc = DateTime.UtcNow;
                state.PunchedCount = 1;
                state.PunchedUtc.Add(state.CardStartedUtc.Value);
            }
            if (!change(state)) return;
            Directory.CreateDirectory(folder);
            File.WriteAllText(tmp, JsonSerializer.Serialize(state));
            File.Move(tmp, path, true);
        }
        catch (Exception ex) { Log.Warning(ex, "Cannot save intake card"); }
    }
}

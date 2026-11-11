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

    // WPF constructs and sanitizes the card before the first-versus-returning launch decision.
    internal static bool HasEverCompleted(string folder) =>
        Update(folder, _ => false)?.EverCompletedIntake() == true;

    private static IntakePunchCardState? Update(string folder, Func<IntakePunchCardState, bool> change)
    {
        try
        {
            var path = Path.Combine(folder, IntakePunchCardState.FileName);
            var tmp = path + ".tmp";
            IntakePunchCardState? state = null;
            var recovered = false;
            foreach (var candidate in new[] { path, tmp })
            {
                if (!File.Exists(candidate)) continue;
                try { state = JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(candidate)); }
                catch (Exception ex) { Log.Warning(ex, "Cannot read intake card {Path}", candidate); }
                if (state != null) { recovered = candidate == tmp; break; }
            }
            // Do not replace an unreadable card with a new one.
            if (state == null && (File.Exists(path) || File.Exists(tmp))) return null;
            state ??= new IntakePunchCardState();
            var dirty = recovered;
            if (state.PendingDrafts == null) { state.PendingDrafts = new(); dirty = true; }
            if (state.PunchedUtc == null) { state.PunchedUtc = new(); dirty = true; }
            var count = Math.Clamp(state.PunchedCount, 0, 8);
            if (count != state.PunchedCount) { state.PunchedCount = count; dirty = true; }
            if (state.PunchedCount >= 8 && !state.CompletedUtc.HasValue)
            {
                state.CompletedUtc = state.PunchedUtc.Count > 0 ? state.PunchedUtc[^1] : DateTime.UtcNow;
                dirty = true;
            }
            var cutoff = DateTime.UtcNow.AddDays(-90);
            if (state.PendingDrafts.RemoveAll(d => string.IsNullOrWhiteSpace(d.SessionId) || d.DraftedUtc < cutoff) > 0)
                dirty = true;
            while (state.PendingDrafts.Count > 8) { state.PendingDrafts.RemoveAt(0); dirty = true; }
            if (!state.CardStartedUtc.HasValue)
            {
                state.CardStartedUtc = DateTime.UtcNow;
                state.PunchedCount = 1;
                state.PunchedUtc.Add(state.CardStartedUtc.Value);
                dirty = true;
            }
            dirty |= change(state);
            if (!dirty) return state;
            Directory.CreateDirectory(folder);
            File.WriteAllText(tmp, JsonSerializer.Serialize(state));
            File.Move(tmp, path, true);
            return state;
        }
        catch (Exception ex) { Log.Warning(ex, "Cannot save intake card"); return null; }
    }
}

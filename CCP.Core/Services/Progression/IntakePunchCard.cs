using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>
/// PORTED from ConditioningControlPanel/Services/Progression/IntakePunchCardService.cs (WPF keeps its own
/// copy; new name per P25): the eight-hole intake card's stamping. A finished intake queues half a stamp
/// on the session it drafted; the hole lands once that session runs past
/// <see cref="SessionCreditPercent"/> or completes. WPF hides the card's UI (UiEnabled = false) but keeps
/// stamping, so this port has no UI either. It writes the same file WPF does, which is what
/// <see cref="IntakePunchCardState.ReadEverCompletedIntake"/> reads to decide the intake's duck.
/// Every change is saved at once (WPF flushes a pending draft on a 30 s timer or at exit).
/// </summary>
public sealed class IntakePunchCard
{
    public const int TotalHoles = 8;
    public const double SessionCreditPercent = 50.0;
    private const int MaxPendingDrafts = TotalHoles;
    private static readonly TimeSpan PendingDraftMaxAge = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _statePath;
    private bool _isDirty;

    public IntakePunchCardState State { get; }

    public IntakePunchCard(string statePath)
    {
        _statePath = statePath;
        State = LoadState();
        if (!State.CardStartedUtc.HasValue)   // EnsureCardStarted: the first hole is on the house
        {
            var now = DateTime.UtcNow;
            State.CardStartedUtc = now;
            State.PunchedCount = 1;
            State.PunchedUtc.Add(now);
            _isDirty = true;
            Log.Information("IntakePunchCard: new card opened, first hole on the house");
        }
        if (_isDirty) Save();
    }

    public int PunchedCount => Math.Clamp(State.PunchedCount, 0, TotalHoles);
    public bool IsComplete => State.CompletedUtc.HasValue || PunchedCount >= TotalHoles;

    /// <summary>WPF SessionEngine.ProgressUpdated / SessionCompleted -> MainWindow.Presets.cs:1861 / :1653.</summary>
    public void Attach(SessionRunner runner)
    {
        runner.Ticked += () =>
        {
            if (runner.CurrentSession is { } s) NotifySessionProgress(s.Id, runner.ProgressPercent);
        };
        runner.Stopped += (s, completed) => { if (completed) NotifySessionCompleted(s.Id); };
    }

    /// <summary>An intake drafted <paramref name="sessionId"/>: half a stamp, redeemed when it runs.</summary>
    public void NotifyIntakeCompleted(string? sessionId)
    {
        try
        {
            if (IsComplete || string.IsNullOrWhiteSpace(sessionId)) return;
            if (State.PendingDrafts.Any(d => string.Equals(d.SessionId, sessionId, StringComparison.Ordinal))) return;
            State.PendingDrafts.Add(new PendingIntakeDraft { SessionId = sessionId!, DraftedUtc = DateTime.UtcNow });
            while (State.PendingDrafts.Count > MaxPendingDrafts) State.PendingDrafts.RemoveAt(0);
            Log.Information("IntakePunchCard: intake completed, stamp pending on session {Id}", sessionId);
            Save();
        }
        catch (Exception ex) { Log.Warning("IntakePunchCard.NotifyIntakeCompleted: {E}", ex.Message); }
    }

    /// <summary>Safe every tick: redemption removes the draft, so a hole lands once.</summary>
    public void NotifySessionProgress(string? sessionId, double progressPercent)
    {
        if (progressPercent < SessionCreditPercent) return;
        TryRedeem(sessionId, "progress");
    }

    /// <summary>A session that ran to its natural end redeems regardless of the percentage.</summary>
    public void NotifySessionCompleted(string? sessionId) => TryRedeem(sessionId, "completed");

    private void TryRedeem(string? sessionId, string via)
    {
        try
        {
            if (IsComplete || string.IsNullOrWhiteSpace(sessionId)) return;
            var draft = State.PendingDrafts.FirstOrDefault(d => string.Equals(d.SessionId, sessionId, StringComparison.Ordinal));
            if (draft == null) return;
            State.PendingDrafts.Remove(draft);

            var now = DateTime.UtcNow;
            State.PunchedCount = Math.Min(TotalHoles, State.PunchedCount + 1);
            State.PunchedUtc.Add(now);
            if (State.PunchedCount >= TotalHoles && !State.CompletedUtc.HasValue)
            {
                State.CompletedUtc = now;
                Log.Information("IntakePunchCard: card COMPLETE - prize awaiting claim");
            }
            Log.Information("IntakePunchCard: hole {N}/{Total} punched (via {Via})", State.PunchedCount, TotalHoles, via);
            Save();
        }
        catch (Exception ex) { Log.Warning("IntakePunchCard.TryRedeem: {E}", ex.Message); }
    }

    /// <summary>Main file, then a half-written .tmp, then a fresh card (WPF LoadState).</summary>
    private IntakePunchCardState LoadState()
    {
        var tmpPath = _statePath + ".tmp";
        foreach (var path in new[] { _statePath, tmpPath })
        {
            if (!File.Exists(path)) continue;
            try
            {
                if (JsonSerializer.Deserialize<IntakePunchCardState>(File.ReadAllText(path)) is { } loaded)
                {
                    if (path == tmpPath)
                    {
                        Log.Warning("IntakePunchCard: recovered state from .tmp");
                        try { File.Move(tmpPath, _statePath, overwrite: true); } catch { }
                    }
                    return Sanitize(loaded);
                }
            }
            catch (Exception ex) { Log.Warning(ex, "IntakePunchCard: {Path} unreadable", Path.GetFileName(path)); }
        }
        return new IntakePunchCardState();
    }

    /// <summary>WPF Sanitize: repairs are marked dirty so the file on disk heals.</summary>
    private IntakePunchCardState Sanitize(IntakePunchCardState s)
    {
        if (s.PunchedUtc == null) { s.PunchedUtc = new(); _isDirty = true; }
        if (s.PendingDrafts == null) { s.PendingDrafts = new(); _isDirty = true; }
        var clamped = Math.Clamp(s.PunchedCount, 0, TotalHoles);
        if (clamped != s.PunchedCount) { s.PunchedCount = clamped; _isDirty = true; }
        if (s.PunchedCount >= TotalHoles && !s.CompletedUtc.HasValue)
        {
            s.CompletedUtc = s.PunchedUtc.Count > 0 ? s.PunchedUtc[^1] : DateTime.UtcNow;
            _isDirty = true;
        }
        var cutoff = DateTime.UtcNow - PendingDraftMaxAge;
        if (s.PendingDrafts.RemoveAll(d => string.IsNullOrWhiteSpace(d.SessionId) || d.DraftedUtc < cutoff) > 0) _isDirty = true;
        while (s.PendingDrafts.Count > MaxPendingDrafts) { s.PendingDrafts.RemoveAt(0); _isDirty = true; }
        return s;
    }

    /// <summary>Atomic write through a .tmp (WPF Save).</summary>
    private void Save()
    {
        _isDirty = false;
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmpPath = _statePath + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(State, WriteOptions));
            File.Move(tmpPath, _statePath, overwrite: true);
        }
        catch (Exception ex) { Log.Error(ex, "IntakePunchCard: failed to save state"); }
    }
}

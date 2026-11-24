using System;
using System.IO;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>A Lockdown owed on a bet, started on this PC. <see cref="Outcome"/> is null while it runs,
/// then "served" or "ended_early" until the server has heard it.</summary>
public sealed record DareServe(string Account, string BetId, int Minutes, DateTimeOffset StartedUtc, string? Outcome);

/// <summary>Where the serve in progress is kept across a crash. Tests use memory.</summary>
public interface IDareServeStore
{
    DareServe? Read();
    void Write(DareServe? serve);
}

/// <summary>Served or not: the Lockdown ran its whole length (a few seconds of slack for the last tick).
/// The exit phrase, a crash, a kill and a power cut all read as ended early: they cannot be told apart. Pure.</summary>
public static class DareLockdownRule
{
    public const string Served = "served";
    public const string EndedEarly = "ended_early";

    public static readonly TimeSpan Slack = TimeSpan.FromSeconds(5);

    public static bool RanItsLength(TimeSpan set, TimeSpan ran) => set > TimeSpan.Zero && ran >= set - Slack;

    public static string Outcome(TimeSpan set, TimeSpan ran) => RanItsLength(set, ran) ? Served : EndedEarly;
}

/// <summary>
/// A LOCKDOWN STAKE, SERVED. Runs the real Lockdown (<see cref="IDareHost.StartLockdown"/>: the card's own
/// Activate, exit phrase and panic rules untouched, no new lock) and remembers the serve on disk BEFORE it
/// starts, so a crash or a kill mid-Lockdown is reported as ended early at the next start (beside the
/// Lockdown's own recovery file). The service sends the outcome; <see cref="Delivered"/> clears it.
/// </summary>
public sealed class DareLockdownKeeper
{
    private readonly IDareHost _host;
    private readonly IDareServeStore _store;
    private DareServe? _serve;

    public DareLockdownKeeper(IDareHost host, IDareServeStore store)
    {
        _host = host;
        _store = store;
        try { _serve = _store.Read(); } catch { _serve = null; }
        // Crash recovery: a serve that never came back, with no Lockdown running now, was cut short.
        if (_serve is { Outcome: null } && !_host.LockdownActive)
        {
            App.Logger?.Information("Dares: a Lockdown stake was interrupted (bet {Bet}); reporting it ended early", _serve.BetId);
            Set(_serve with { Outcome = DareLockdownRule.EndedEarly });
        }
        _host.LockdownEnded += OnEnded;
    }

    /// <summary>A serve still owed to the server (running, or ended and not yet heard).</summary>
    public DareServe? Pending => _serve;

    /// <summary>A dare Lockdown is running on this PC now.</summary>
    public bool Serving => _serve is { Outcome: null };

    /// <summary>Raised when a dare Lockdown ended (its outcome is ready to send).</summary>
    public event Action<DareServe>? Ended;

    /// <summary>Starts the real Lockdown for a debt. False when another Lockdown runs or it did not start.</summary>
    public bool Begin(string account, string betId, int minutes, DateTimeOffset now)
    {
        if (minutes <= 0 || _host.LockdownActive || Serving) return false;
        Set(new DareServe(account, betId, minutes, now, null));
        if (_host.StartLockdown(TimeSpan.FromMinutes(minutes))) return true;
        Set(null);
        return false;
    }

    /// <summary>The server took the outcome of <paramref name="betId"/>.</summary>
    public void Delivered(string betId)
    {
        if (_serve != null && _serve.BetId == betId && _serve.Outcome != null) Set(null);
    }

    private void OnEnded(TimeSpan set, TimeSpan ran)
    {
        if (_serve is { Outcome: null } s)
        {
            var done = s with { Outcome = DareLockdownRule.Outcome(TimeSpan.FromMinutes(s.Minutes), ran) };
            Set(done);
            try { Ended?.Invoke(done); }
            catch (Exception ex) { App.Logger?.Debug("Dares serve handler failed: {E}", ex.Message); }
        }
        // Anything else is one of the player's own Lockdowns: none of a dare's business.
    }

    private void Set(DareServe? s)
    {
        _serve = s;
        try { _store.Write(s); }
        catch (Exception ex) { App.Logger?.Debug("Dares serve file failed: {E}", ex.Message); }
    }

    public sealed class MemoryStore : IDareServeStore
    {
        private DareServe? _v;
        public DareServe? Read() => _v;
        public void Write(DareServe? serve) => _v = serve;
    }

    /// <summary>One JSON file; absent = nothing owed.</summary>
    public sealed class FileStore : IDareServeStore
    {
        private readonly string _path;
        public FileStore(string path) { _path = path; }

        public DareServe? Read()
        {
            try { return File.Exists(_path) ? JsonConvert.DeserializeObject<DareServe>(File.ReadAllText(_path)) : null; }
            catch { return null; }
        }

        public void Write(DareServe? serve)
        {
            if (serve == null) { if (File.Exists(_path)) File.Delete(_path); return; }
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(serve));
            File.Move(tmp, _path, overwrite: true);
        }
    }
}

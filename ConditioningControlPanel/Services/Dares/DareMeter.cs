using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Progression;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>What is running right now, as far as counting and the camera care.</summary>
public readonly record struct DareLive(bool Engine, bool Session, bool Video)
{
    public bool EngineOrSession => Engine || Session;
}

/// <summary>One bet's numbers for a <c>report</c>: the monotonic count and the two camera sums.</summary>
public readonly record struct DareReading(int Count, int SeenSeconds, int CountedSeconds, bool Camera);

/// <summary>Where the meter keeps its numbers across restarts (one JSON text). Tests use memory.</summary>
public interface IDareMeterStore
{
    string? Read();
    void Write(string text);
}

/// <summary>
/// THE COUNTING RULES (CONTRACT.md "Counting"). Counts this player's own use for every bet this client
/// reports (a running bet where it is the receiver of a "can't", or either side of a duel), only
/// between <c>accepted_at</c> and <c>ends_at</c>. Totals only go up and survive a restart.
///
/// <list type="bullet">
/// <item>pics: images a flash put on screen (<see cref="NotePics"/>).</item>
/// <item>minutes: real time the engine or a session ran, through <see cref="ConditioningClock"/>.</item>
/// <item>video: real time any video played; several at once is ONE clock, because the sampler asks
/// "is any video playing" once per step, never once per window.</item>
/// <item>session: 1 once a session of the named difficulty or harder completed.</item>
/// <item>bubbles: bubbles popped.</item>
/// </list>
///
/// <para>The camera sums (seconds counted, seconds a face was seen) run only while a camera bet's own
/// counting is live (<see cref="IsLive"/>). No clock of its own: every call carries the time.
/// Thread safe.</para>
/// </summary>
public sealed class DareMeter
{
    private sealed class Tally
    {
        public DareKind Kind { get; set; }
        public string? Target { get; set; }
        public double Value { get; set; }
        public double Pending { get; set; }
        public double Seen { get; set; }
        public double Counted { get; set; }
        public bool Camera { get; set; }
        public DateTimeOffset From { get; set; }
        public DateTimeOffset Until { get; set; }
    }

    private sealed class FileShape
    {
        public string? Account { get; set; }
        public Dictionary<string, Tally> Bets { get; set; } = new();
    }

    private readonly object _gate = new();
    private readonly IDareMeterStore _store;
    private FileShape _data = new();
    private bool _dirty;

    public DareMeter(IDareMeterStore? store = null)
    {
        _store = store ?? new MemoryStore();
        try
        {
            var text = _store.Read();
            if (!string.IsNullOrWhiteSpace(text)) _data = JsonConvert.DeserializeObject<FileShape>(text!) ?? new FileShape();
        }
        catch (Exception ex) { App.Logger?.Debug("Dares meter unreadable: {E}", ex.Message); _data = new FileShape(); }
        _data.Bets ??= new();
    }

    /// <summary>True for a bet this client counts and reports.</summary>
    public static bool Reports(DareBet b) =>
        b.State == DareState.Running
        && b.What.Kind != DareKind.Dare
        && (b.Type == DareType.Duel || b.Me == DareSide.To);

    /// <summary>Is a bet of <paramref name="kind"/> being counted at this moment (the camera only looks then).</summary>
    public static bool IsLive(DareKind kind, DareLive live) => kind switch
    {
        DareKind.Pics => live.EngineOrSession,
        DareKind.Minutes => live.EngineOrSession,
        DareKind.Video => live.Video,
        DareKind.Session => live.Session,
        _ => false,
    };

    /// <summary>The account the numbers belong to. A different one drops the bets.</summary>
    public void SetAccount(string? account)
    {
        lock (_gate)
        {
            if (string.Equals(_data.Account, account, StringComparison.Ordinal)) return;
            _data.Account = account;
            _data.Bets.Clear();
            _dirty = true;
        }
    }

    /// <summary>The bets to count: every reporting bet gets a tally (kept if it has one), and a
    /// tally whose bet is no longer running is dropped.</summary>
    public void Track(IEnumerable<DareBet> bets)
    {
        lock (_gate)
        {
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in bets)
            {
                if (!Reports(b)) continue;
                keep.Add(b.Id);
                if (!_data.Bets.TryGetValue(b.Id, out var t))
                {
                    t = new Tally { Kind = b.What.Kind, Target = b.What.Target, From = b.AcceptedAt ?? b.At };
                    _data.Bets[b.Id] = t;
                    _dirty = true;
                }
                if (t.Until != b.EndsAt || t.Camera != b.Camera) { t.Until = b.EndsAt; t.Camera = b.Camera; _dirty = true; }
            }
            foreach (var id in _data.Bets.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                _data.Bets.Remove(id);
                _dirty = true;
            }
        }
    }

    /// <summary>Anything inside its window now (the 1 s sampler runs only then).</summary>
    public bool AnyInWindow(DateTimeOffset now)
    {
        lock (_gate) return _data.Bets.Values.Any(t => InWindow(t, now));
    }

    /// <summary>A camera bet inside its window whose counting is live now.</summary>
    public bool CameraWanted(DateTimeOffset now, DareLive live)
    {
        lock (_gate) return _data.Bets.Values.Any(t => t.Camera && InWindow(t, now) && IsLive(t.Kind, live));
    }

    public void NotePics(int images, DateTimeOffset now) => Add(DareKind.Pics, Math.Max(0, images), now);

    public void NoteBubbles(int popped, DateTimeOffset now) => Add(DareKind.Bubbles, Math.Max(0, popped), now);

    public void NoteSession(SessionDifficulty difficulty, DateTimeOffset now)
    {
        lock (_gate)
            foreach (var t in _data.Bets.Values)
                if (t.Kind == DareKind.Session && InWindow(t, now) && t.Value < 1 && DareSlipRules.SessionMakes(difficulty, t.Target))
                {
                    t.Value = 1;
                    _dirty = true;
                }
    }

    /// <summary>One sampler step: <paramref name="gapSeconds"/> of real time since the last one.</summary>
    public void Sample(double gapSeconds, DareLive live, DateTimeOffset now)
    {
        if (!double.IsFinite(gapSeconds) || gapSeconds <= 0) return;
        var gap = Math.Min(gapSeconds, ConditioningClock.MaxGapSeconds);
        lock (_gate)
            foreach (var t in _data.Bets.Values)
            {
                if (!InWindow(t, now)) continue;
                if (t.Kind == DareKind.Minutes && live.EngineOrSession)
                {
                    var pending = t.Pending;
                    var due = ConditioningClock.Tick(ref pending, gap);
                    t.Pending = pending;
                    t.Value += due;
                    _dirty = true;
                }
                else if (t.Kind == DareKind.Video && live.Video)
                {
                    t.Value += gap;
                    _dirty = true;
                }
            }
    }

    /// <summary>One camera look, <paramref name="stepSeconds"/> long: counted for every camera bet
    /// whose counting is live, seen too when a face was there.</summary>
    public void CameraSample(double stepSeconds, bool seen, DareLive live, DateTimeOffset now)
    {
        if (!double.IsFinite(stepSeconds) || stepSeconds <= 0) return;
        var step = Math.Min(stepSeconds, 2.0);
        lock (_gate)
            foreach (var t in _data.Bets.Values)
            {
                if (!t.Camera || !InWindow(t, now) || !IsLive(t.Kind, live)) continue;
                t.Counted += step;
                if (seen) t.Seen += step;
                _dirty = true;
            }
    }

    /// <summary>The numbers to report for a bet, or null when this client does not count it.</summary>
    public DareReading? Reading(string betId)
    {
        lock (_gate)
        {
            if (!_data.Bets.TryGetValue(betId, out var t)) return null;
            var count = t.Kind == DareKind.Video ? (int)Math.Floor(t.Value / 60) : (int)Math.Floor(t.Value);
            return new DareReading(count, (int)Math.Floor(t.Seen), (int)Math.Floor(t.Counted), t.Camera);
        }
    }

    /// <summary>Writes the numbers when anything moved since the last save.</summary>
    public void Save()
    {
        string text;
        lock (_gate)
        {
            if (!_dirty) return;
            text = JsonConvert.SerializeObject(_data);
            _dirty = false;
        }
        try { _store.Write(text); }
        catch (Exception ex) { App.Logger?.Debug("Dares meter save failed: {E}", ex.Message); }
    }

    private void Add(DareKind kind, int n, DateTimeOffset now)
    {
        if (n <= 0) return;
        lock (_gate)
            foreach (var t in _data.Bets.Values)
                if (t.Kind == kind && InWindow(t, now))
                {
                    t.Value += n;
                    _dirty = true;
                }
    }

    private static bool InWindow(Tally t, DateTimeOffset now) => now >= t.From && now < t.Until;

    private sealed class MemoryStore : IDareMeterStore
    {
        private string? _v;
        public string? Read() => _v;
        public void Write(string text) => _v = text;
    }

    /// <summary>A JSON file in the user data folder, written through a temp file.</summary>
    public sealed class FileStore : IDareMeterStore
    {
        private readonly string _path;
        public FileStore(string path) { _path = path; }

        public string? Read()
        {
            try { return File.Exists(_path) ? File.ReadAllText(_path) : null; }
            catch { return null; }
        }

        public void Write(string text)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, _path, overwrite: true);
        }
    }
}

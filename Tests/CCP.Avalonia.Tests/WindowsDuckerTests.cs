using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WindowsDucker (WPF AudioService's WASAPI ducker) with the sessions stubbed: what it lowers, what
/// it never touches, the ref count and generation, and the crash journal in WPF's shape.</summary>
public sealed class WindowsDuckerTests : IDisposable
{
    private const int Own = 999;
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-winduck").FullName;
    private readonly Dictionary<int, float> _vol = new() { [10] = 0.8f, [Own] = 1f, [0] = 1f, [30] = 1f };
    private readonly Dictionary<int, string> _name = new() { [10] = "Spotify", [11] = "Discord", [Own] = "ConditioningControlPanel", [0] = "", [30] = "msedgewebview2" };
    private Action<int, float>? _onSet;

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string JournalFile => Path.Combine(_dir, DuckRecovery.WpfFileName);
    private Dictionary<int, float> ReadJournal() => JsonSerializer.Deserialize<Dictionary<int, float>>(File.ReadAllText(JournalFile))!;

    private List<DuckSession> Live() => _vol.Keys.Select(pid => new DuckSession(pid, () => _vol[pid], v =>
    {
        _onSet?.Invoke(pid, v);
        if (!_vol.ContainsKey(pid)) throw new InvalidOperationException("session gone");
        _vol[pid] = v;
    })).ToList();

    private WindowsDucker Make() => new(body => body(Live()), pid => _name.TryGetValue(pid, out var n) ? n : "", _dir, () => true, Own);

    [Fact]
    public void DucksOtherAppsByTheStrengthButNeverOurselvesPidZeroOrWebView2_ThenHandsBack()
    {
        var d = Make();
        d.Duck(80);
        d.Drain();
        Assert.Equal(0.16f, _vol[10], 3);
        Assert.Equal(1f, _vol[Own]);
        Assert.Equal(1f, _vol[0]);
        Assert.Equal(1f, _vol[30]);
        Assert.True(d.IsDucked);

        d.Unduck(-1);
        d.Drain();
        Assert.Equal(0.8f, _vol[10], 3);
        Assert.False(File.Exists(JournalFile));   // nothing owed = no file
    }

    [Fact]
    public void TheDuckIsJournalledBeforeAnyVolumeDrops()
    {
        var journalledFirst = (bool?)null;
        _onSet = (pid, v) =>
        {
            if (journalledFirst != null || v >= 0.8f) return;
            journalledFirst = File.Exists(JournalFile) && ReadJournal().TryGetValue(pid, out var o) && Math.Abs(o - 0.8f) < 1e-4;
        };
        var d = Make();
        d.Duck(80);
        d.Drain();
        Assert.True(journalledFirst == true, "ducking_recovery.json must hold the original before the volume drops");
        Assert.Equal("Spotify", JsonSerializer.Deserialize<Dictionary<int, string>>(File.ReadAllText(Path.Combine(_dir, DuckRecovery.WpfNamesFileName)))![10]);
        d.ForceUnduck();
        d.Drain();
    }

    [Fact]
    public void RefCountGenerationAndForceUnduck()
    {
        var d = Make();
        d.Duck(50);
        d.Duck(50);
        d.Drain();
        var stale = d.Generation;
        d.Unduck(-1);
        Assert.True(d.IsDucked);                // one ref still held
        d.ForceUnduck();                         // panic: advances the generation
        d.Unduck(stale);                         // a stale callback is a no-op
        d.Drain();
        Assert.False(d.IsDucked);
        Assert.Equal(0.8f, _vol[10], 3);
    }

    [Fact]
    public void RescanCatchesANewSessionAndNeverRecapturesADuckedOne()
    {
        var d = Make();
        d.Duck(50);
        d.Drain();
        _vol[11] = 0.6f;                         // Discord pings during the duck
        d.Rescan();
        d.Drain();
        Assert.Equal(0.4f, _vol[10], 3);         // not lowered twice
        Assert.Equal(0.3f, _vol[11], 3);
        d.Unduck(-1);
        d.Drain();
        Assert.Equal(0.8f, _vol[10], 3);
        Assert.Equal(0.6f, _vol[11], 3);
    }

    [Fact]
    public void ARestoreThatCannotLandStaysJournalledAndTheLaunchRecoveryHandsItBack()
    {
        var d = Make();
        d.Duck(80);
        d.Drain();
        _vol.Remove(10);                         // Spotify closed while ducked
        d.Unduck(-1);
        d.Drain();
        Assert.Equal(0.8f, ReadJournal()[10], 3);   // parked, in WPF's shape

        // Next launch: Spotify is back under a new PID; P2's recovery reads the same file.
        _vol[12] = 0.16f;
        _name[12] = "Spotify";
        var n = DuckRecovery.RecoverWpfJournal(_dir, () => Live().Select(s => (s.Pid, s.SetVolume)).ToList(), pid => _name.TryGetValue(pid, out var x) ? x : "", Own);
        Assert.Equal(1, n);
        Assert.Equal(0.8f, _vol[12], 3);
        Assert.False(File.Exists(JournalFile));
    }

    [Fact]
    public void ADuckOfAnAppWithAParkedRestoreKeepsItsTrueOriginal()
    {
        var d = Make();
        d.Duck(80);
        d.Drain();
        var parkedAt = _vol[10];                 // 0.16, the ducked level
        _vol.Remove(10);                         // its session went away: the hand-back is parked
        d.Unduck(-1);
        d.Drain();
        _vol[10] = parkedAt;                     // it plays again, still at the ducked level
        d.Duck(80);
        d.Drain();
        Assert.Equal(0.8f, ReadJournal()[10], 3);   // the TRUE original, not 0.16
        d.Unduck(-1);
        d.Drain();
        Assert.Equal(0.8f, _vol[10], 3);
    }

    [Fact]
    public void AGoneEntryNeverLandsOnALiveAppThatHasItsOwnEntry()
    {
        _vol[11] = 0.5f;
        _name[11] = "Spotify";                   // a second Spotify process, ducked too
        var d = Make();
        d.Duck(80);
        d.Drain();
        _vol.Remove(10);                         // the first one exits
        d.Unduck(-1);
        d.Drain();
        Assert.Equal(0.5f, _vol[11], 3);         // its own original, not 10's
        Assert.Equal(0.8f, ReadJournal()[10], 3);   // 10 is parked, not "restored" onto 11
    }

    [Fact]
    public void WindowsDuckingIsSeededOnlyFromAppStartUp()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Platform", "LibVlcAudio.cs"));
        var start = src.IndexOf("internal void StartDuckJournal(", StringComparison.Ordinal);
        var seed = src.IndexOf("public void Seed()", StringComparison.Ordinal);
        var win = src.IndexOf("_win = WindowsDucker.ForApp(", StringComparison.Ordinal);
        Assert.True(start >= 0 && win > start && win < seed, "the Windows ducker is made in StartDuckJournal, never in Seed (probes and tests call Seed)");
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

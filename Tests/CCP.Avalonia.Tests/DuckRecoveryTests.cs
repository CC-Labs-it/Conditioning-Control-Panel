using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Ducking crash recovery on the Avalonia head (WPF AudioService RecoverFromCrash + DuckRecoveryRule):
/// the pactl journal on Linux, and a WPF build's leftover journal on Windows. pactl and WASAPI are stubbed.</summary>
public sealed class DuckRecoveryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-duck").FullName;
    private string Journal => Path.Combine(_dir, DuckRecovery.PulseFileName);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static string Stream(int index, int pid, string binary, int vol = 65536) =>
        $"{{\"index\":{index},\"volume\":{{\"front-left\":{{\"value\":{vol}}},\"front-right\":{{\"value\":{vol}}}}}," +
        $"\"properties\":{{\"application.process.id\":\"{pid}\",\"application.process.binary\":\"{binary}\"}}}}";

    [Fact]
    public void ADuckIsJournalledUntilItsUnduckHandsItBack()
    {
        var sets = new List<string>();
        var audio = new LibVlcAudio(args => args.StartsWith("-f json") ? "[" + Stream(7, 4242, "spotify") + "]" : Record(sets, args), Journal);
        audio.Duck(80);
        audio.Drain();
        var owed = DuckRecovery.LoadPulse(Journal);
        Assert.Equal((4242, "spotify"), (owed[7].Pid, owed[7].Name));
        Assert.Equal(new[] { "65536", "65536" }, owed[7].Volumes);

        audio.Unduck(-1);
        audio.Drain();
        Assert.False(File.Exists(Journal));   // nothing owed = no file
    }

    [Fact]
    public void ARestoreThatCannotLandStaysJournalled()
    {
        var gone = false;
        var audio = new LibVlcAudio(args =>
        {
            if (args.StartsWith("-f json")) return "[" + Stream(7, 4242, "spotify") + "]";
            if (gone) throw new InvalidOperationException("No such entity");
            return "";
        }, Journal);
        audio.Duck(80);
        audio.Drain();
        gone = true;                           // the app closed while ducked
        audio.Unduck(-1);
        audio.Drain();
        Assert.True(DuckRecovery.LoadPulse(Journal).ContainsKey(7));
    }

    [Fact]
    public void AParkedRestoreIsRetriedWhileTheRunGoesOn()
    {
        var gone = false;
        var listing = "[" + Stream(7, 4242, "spotify") + "]";
        var sets = new List<string>();
        var audio = new LibVlcAudio(args =>
        {
            if (args.StartsWith("-f json")) return listing;
            if (gone) throw new InvalidOperationException("No such entity");
            return Record(sets, args);
        }, Journal);
        audio.Duck(80);
        audio.Drain();
        gone = true;                           // the app's stream ended while ducked
        audio.Unduck(-1);
        audio.Drain();
        Assert.True(audio.RetryArmed);         // WPF RestoreRetryTick: every 5 s for 3 min
        gone = false;
        listing = "[" + Stream(9, 4242, "spotify", 13107) + "]";   // it plays again on a new stream
        sets.Clear();
        audio.RetryParked();
        Assert.Equal(new[] { "set-sink-input-volume 9 65536 65536" }, sets);
        Assert.False(File.Exists(Journal));
        Assert.False(audio.RetryArmed);
    }

    [Fact]
    public void ADuckOfAParkedStreamKeepsItsTrueOriginal()
    {
        var failOnce = false;
        var listing = "[" + Stream(7, 4242, "spotify") + "]";
        var sets = new List<string>();
        var audio = new LibVlcAudio(args =>
        {
            if (args.StartsWith("-f json")) return listing;
            if (failOnce) { failOnce = false; throw new InvalidOperationException("busy"); }
            return Record(sets, args);
        }, Journal);
        audio.Duck(80);
        audio.Drain();
        failOnce = true;                       // the hand-back misses: 65536 is parked
        audio.Unduck(-1);
        audio.Drain();
        listing = "[" + Stream(7, 4242, "spotify", 13107) + "]";   // still at the ducked level
        audio.Duck(80);
        audio.Drain();
        Assert.Equal(new[] { "65536", "65536" }, DuckRecovery.LoadPulse(Journal)[7].Volumes);
        sets.Clear();
        audio.Unduck(-1);
        audio.Drain();
        Assert.Equal(new[] { "set-sink-input-volume 7 65536 65536" }, sets);
    }

    [Fact]
    public void LaunchRestoresARestartedAppByNameAndDropsTheJournal()
    {
        DuckRecovery.SavePulse(Journal, new Dictionary<int, DuckRecovery.PulseStream>
        {
            [7] = new(4242, "spotify", new[] { "60000", "60000" }),
            [8] = new(4343, "gone-app", new[] { "50000", "50000" }),
        });
        var sets = new List<string>();
        var own = Environment.ProcessId;
        var audio = new LibVlcAudio(args => args.StartsWith("-f json")
            ? "[" + Stream(12, 5555, "spotify", 13107) + "," + Stream(13, own, "spotify", 13107) + "]"
            : Record(sets, args), Journal);
        audio.RecoverDucks();
        audio.Drain();
        Assert.Equal(new[] { "set-sink-input-volume 12 60000 60000" }, sets);   // never our own stream
        Assert.False(File.Exists(Journal));
    }

    [Fact]
    public void LaunchRecoveryRunsBeforeANewDuckJournals()
    {
        DuckRecovery.SavePulse(Journal, new Dictionary<int, DuckRecovery.PulseStream> { [7] = new(4242, "spotify", new[] { "60000", "60000" }) });
        var sets = new List<string>();
        var audio = new LibVlcAudio(args => args.StartsWith("-f json") ? "[" + Stream(7, 4242, "spotify", 12000) + "]" : Record(sets, args), Journal);
        audio.RecoverDucks();
        audio.Duck(50);
        audio.Drain();
        Assert.Equal("set-sink-input-volume 7 60000 60000", sets[0]);
        Assert.Equal(new[] { "12000", "12000" }, DuckRecovery.LoadPulse(Journal)[7].Volumes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrainWaitsForLaunchRecoveryEvenPastTheShutdownBudget(bool windows)
    {
        var release = new System.Threading.Tasks.TaskCompletionSource();
        var sets = new List<string>();
        Action drain;
        if (windows)
        {
            var audio = new WindowsDucker(_ => 0, _ => "", null, () => false, 999, release.Task);
            drain = () => audio.Drain();
        }
        else
        {
            DuckRecovery.SavePulse(Journal, new Dictionary<int, DuckRecovery.PulseStream>
                { [7] = new(4242, "spotify", new[] { "60000", "60000" }) });
            var audio = new LibVlcAudio(args =>
            {
                if (!args.StartsWith("-f json")) return Record(sets, args);
                release.Task.GetAwaiter().GetResult();
                return "[" + Stream(7, 4242, "spotify", 12000) + "]";
            }, Journal);
            audio.RecoverDucks();
            drain = () => audio.Drain();
        }
        using var started = new System.Threading.ManualResetEventSlim();
        var drained = System.Threading.Tasks.Task.Factory.StartNew(() => { started.Set(); drain(); },
            System.Threading.CancellationToken.None, System.Threading.Tasks.TaskCreationOptions.LongRunning,
            System.Threading.Tasks.TaskScheduler.Default);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            // The launch work is still held beyond the old 2 s timeout. Drain must not claim it finished.
            Assert.False(drained.Wait(TimeSpan.FromSeconds(3)));
        }
        finally
        {
            release.TrySetResult();
            drained.GetAwaiter().GetResult();
            drain();
        }
        if (!windows)
        {
            Assert.Equal(new[] { "set-sink-input-volume 7 60000 60000" }, sets);
            Assert.False(File.Exists(Journal));
        }
    }

    [Fact]
    public void ADifferentChannelLayoutGetsOneVolume()
    {
        Assert.Equal("60000 60000", DuckRecovery.VolumeArgs(new[] { "60000", "60000" }, 2));
        Assert.Equal("60000", DuckRecovery.VolumeArgs(new[] { "60000" }, 6));
        Assert.Equal("60000", DuckRecovery.VolumeArgs(new[] { "50000", "60000" }, 1));
    }

    [Fact]
    public void AWpfJournalIsRestoredBySameAppThenDeleted()
    {
        File.WriteAllText(Path.Combine(_dir, DuckRecovery.WpfFileName), "{\"100\":0.8,\"200\":0.5,\"300\":0.9}");
        File.WriteAllText(Path.Combine(_dir, DuckRecovery.WpfNamesFileName), "{\"100\":\"Spotify\",\"200\":\"chrome\",\"300\":\"vlc\"}");
        var set = new Dictionary<int, float>();
        var own = 999;
        var sessions = new List<(int, Action<float>)>
        {
            (100, v => set[100] = v),   // same PID, same app
            (201, v => set[201] = v),   // chrome restarted
            (300, v => set[300] = v),   // PID 300 recycled by another app
            (own, v => set[own] = v),
        };
        var names = new Dictionary<int, string> { [100] = "Spotify", [201] = "chrome", [300] = "notepad", [own] = "chrome" };
        var n = DuckRecovery.RecoverWpfJournal(_dir, () => sessions, pid => names[pid], own);
        Assert.Equal(2, n);
        Assert.Equal(new Dictionary<int, float> { [100] = 0.8f, [201] = 0.5f }, set);
        Assert.False(File.Exists(Path.Combine(_dir, DuckRecovery.WpfFileName)));
        Assert.False(File.Exists(Path.Combine(_dir, DuckRecovery.WpfNamesFileName)));
    }

    [Fact]
    public void WasapiSessionsEnumerateWithoutTouchingAnything()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var scope = WasapiSessions.Collect();   // read-only: PIDs only, no volume is set
        Assert.All(scope.Sessions, s => Assert.True(s.Pid >= 0));
    }

    [Fact]
    public void StartUpWiresBothRecoveries()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "App.axaml.cs"));
        Assert.Contains("Platform.DuckRecovery.RecoverWpfJournalAtLaunch(CorePaths.UserData)", src);
        Assert.Contains("vlc.StartDuckJournal(Path.Combine(CorePaths.UserData, Platform.DuckRecovery.PulseFileName))", src);
    }

    private static string Record(List<string> sets, string args) { lock (sets) sets.Add(args); return ""; }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Arcademy meta blob now that it lives in Core: the attendance path the tardy slips feed (which
/// needed a dispatcher in WPF and could only be tripwired there), the page's write guard, and the
/// debounced atomic save that replaced the DispatcherTimer. Each test owns a temp file.
/// </summary>
public sealed class ArcademyMetaStoreTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-arcademy-" + System.Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string TempPath() => Path.Combine(_dir, System.Guid.NewGuid().ToString("N"), "arcademy_meta.json");

    private static ArcademyMetaStore Store(string path, List<object>? sent = null, System.TimeProvider? time = null) =>
        // Never the system clock: a real armed debounce would fire after Dispose and recreate _dir.
        new(m => sent?.Add(m), path, time ?? new PopQuizSchedulerTests.FakeClock());

    private static void GiveSlips(ArcademyMetaStore store, int n) =>
        store.Set(ArcademyMetaStore.WalletKey, new JObject
        {
            ["t"] = 0,
            ["inv"] = new JObject { [ArcademyEconomy.SkuLateSlip] = new JObject { ["n"] = n } },
        });

    [Fact]
    public void TwoSlipsCarryTheStreakOverTwoMissedNights()
    {
        var store = Store(TempPath());
        Assert.Equal(1, store.RecordAttendance("2026-09-01", "echo").Streak);
        Assert.Equal(2, store.RecordAttendance("2026-09-02", "echo").Streak);
        GiveSlips(store, 2);

        var r = store.RecordAttendance("2026-09-05", "echo");   // 3rd and 4th missed

        Assert.Equal((3, 2), (r.Streak, r.SlipsSpent));
        Assert.Equal(0, ArcademyEconomy.Held(store.WalletSnapshot(), ArcademyEconomy.SkuLateSlip));
    }

    [Fact]
    public void AThirdMissedNightBreaksTheStreakAndSpendsNothing()
    {
        var store = Store(TempPath());
        store.RecordAttendance("2026-09-01", "echo");
        GiveSlips(store, 2);

        var r = store.RecordAttendance("2026-09-05", "echo");   // 2nd, 3rd and 4th missed

        Assert.Equal((1, 0), (r.Streak, r.SlipsSpent));
        Assert.Equal(2, ArcademyEconomy.Held(store.WalletSnapshot(), ArcademyEconomy.SkuLateSlip));
    }

    [Fact]
    public void ThePageCannotWriteAHostOwnedKeyAndIsToldWhatIsStored()
    {
        var sent = new List<object>();
        var store = Store(TempPath(), sent);
        store.RecordAttendance("2026-09-01", "echo");

        store.Handle(new JObject { ["op"] = "set", ["key"] = ArcademyMetaStore.StreakKey, ["value"] = 99 });
        store.Handle(new JObject { ["op"] = "set", ["key"] = "theme", ["value"] = "drone" });

        Assert.Equal(1, (int?)store.Get(ArcademyMetaStore.StreakKey));
        Assert.Equal("drone", (string?)store.Get("theme"));
        Assert.Equal(1, (int?)JObject.FromObject(sent[0])["value"]);
    }

    [Fact]
    public void AWriteReachesDiskOnTheDebounceWithoutAFlush()
    {
        var path = TempPath();
        var clock = new PopQuizSchedulerTests.FakeClock();
        var store = Store(path, time: clock);
        store.Set("theme", "drone");

        clock.Advance(System.TimeSpan.FromMilliseconds(499));
        Assert.False(File.Exists(path));   // debounced, not written through
        clock.Advance(System.TimeSpan.FromMilliseconds(1));
        Assert.True(File.Exists(path), "the 500 ms debounce never wrote");
        Assert.Equal("drone", (string?)Store(path).Get("theme"));
    }

    [Fact]
    public void FlushSaveWritesNowAndAnUnreadableMainFileFallsBackToTheBak()
    {
        var path = TempPath();
        var store = Store(path);
        store.Set("theme", "drone");
        store.FlushSave();
        store.Set("theme", "snowday");
        store.FlushSave();   // second write leaves the first behind as .bak

        Assert.True(File.Exists(path + ".bak"));
        File.WriteAllText(path, "{ truncated");

        Assert.Equal("drone", (string?)Store(path).Get("theme"));
    }
}

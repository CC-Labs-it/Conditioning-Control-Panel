using System.Collections.Generic;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Arcademy meta blob now that it lives in Core: the attendance path the tardy slips feed (which
/// needed a dispatcher in WPF and could only be tripwired there), the page's write guard, and the
/// debounced atomic save that replaced the DispatcherTimer. Each test owns a temp file.
/// </summary>
public class ArcademyMetaStoreTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "ccp-arcademy-" + System.Guid.NewGuid().ToString("N"), "arcademy_meta.json");

    private static ArcademyMetaStore Store(string path, List<object>? sent = null) =>
        new(m => sent?.Add(m), path);

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
        var store = Store(path);
        store.Set("theme", "drone");

        Assert.False(File.Exists(path));   // debounced, not written through
        Assert.True(SpinWait.SpinUntil(() => File.Exists(path), 5000), "the 500 ms debounce never wrote");
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

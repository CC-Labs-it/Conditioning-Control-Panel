using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class MediaTempFileLedgerTests
{
    [Fact]
    public void Track_RetriesRefusedFilesBelowTheCap_AndKeepsNewest()
    {
        var refused = new HashSet<string> { "playing" };
        var deleted = new List<string>();
        var ledger = new MediaTempFileLedger(2, path =>
        {
            if (refused.Contains(path)) return false;
            deleted.Add(path);
            return true;
        });
        ledger.Track("playing");
        ledger.Track("second");
        ledger.Track("third");
        Assert.Equal(1, ledger.HeldCount);
        Assert.Equal(2, ledger.LiveCount);
        Assert.Empty(deleted);
        ledger.Release("second");
        refused.Clear();
        ledger.Track("fourth"); // at the cap: held files must still be retried
        Assert.Contains("playing", deleted);
        Assert.DoesNotContain("third", deleted);
        Assert.DoesNotContain("fourth", deleted);
        Assert.Equal(0, ledger.HeldCount);
        ledger.SweepAll();
        Assert.Equal(0, ledger.LiveCount);
        Assert.Contains("third", deleted);
        Assert.Contains("fourth", deleted);
    }

    [Fact]
    public void EveryRelease_RetriesEarlierRefusedDeletes()
    {
        bool blocked = true;
        var deleted = new List<string>();
        var ledger = new MediaTempFileLedger(50, path =>
        {
            if (blocked) throw new System.IO.IOException("player holds file");
            deleted.Add(path);
            return true;
        });
        ledger.Release("first");
        ledger.Release("first");
        Assert.Equal(1, ledger.HeldCount);
        blocked = false;
        ledger.Release("second");
        Assert.Equal(0, ledger.HeldCount);
        Assert.Equal(new[] { "second", "first" }, deleted);
    }
}

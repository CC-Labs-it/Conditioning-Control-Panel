using System.Collections.Generic;
using ConditioningControlPanel.Services.MediaFiles;
using Xunit;

namespace CCP.Core.Tests;

public class MediaTempFileLedgerTests
{
    [Fact]
    public void RefusedFilesStayOnRecordAndAreRetriedAtTheNextSweep()
    {
        var blocked = new HashSet<string> { "playing" };
        var deleted = new List<string>();
        var ledger = new MediaTempFileLedger(1, p =>
        {
            if (blocked.Contains(p)) return false;
            deleted.Add(p);
            return true;
        });
        ledger.Track("playing");
        ledger.Track("next");
        Assert.Equal(1, ledger.HeldCount);
        Assert.DoesNotContain("playing", deleted);
        blocked.Clear();
        ledger.Track("last");
        Assert.Equal(0, ledger.HeldCount);
        Assert.Contains("playing", deleted);
        Assert.DoesNotContain("last", deleted);
        ledger.SweepAll();
        Assert.Contains("last", deleted);
        Assert.Equal(0, ledger.LiveCount);
    }

    [Fact]
    public void ReleaseAndRepeatedSweepsRetainRefusedFilesWithoutDuplicates()
    {
        bool refused = true;
        var ledger = new MediaTempFileLedger(0, _ => !refused);
        ledger.Track("a");
        ledger.Release("a");
        ledger.SweepAll();
        Assert.Equal(1, ledger.HeldCount);
        refused = false;
        ledger.SweepAll();
        Assert.Equal(0, ledger.HeldCount);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeldFilesAreRetriedBelowCapOnTrackOrAnotherRelease(bool release)
    {
        bool refused = true;
        var deleted = new List<string>();
        var ledger = new MediaTempFileLedger(10, path =>
        {
            if (path == "held" && refused) return false;
            deleted.Add(path);
            return true;
        });
        ledger.Release("held");
        Assert.Equal(1, ledger.HeldCount);
        refused = false;
        if (release) ledger.Release("other");
        else ledger.Track("new");
        Assert.Contains("held", deleted);
        Assert.Equal(0, ledger.HeldCount);
        Assert.Equal(release ? 0 : 1, ledger.LiveCount);
        Assert.DoesNotContain("new", deleted);
    }

}

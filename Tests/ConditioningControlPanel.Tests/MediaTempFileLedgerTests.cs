using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1433 review: a temp file whose delete is refused (a player still has it open)
/// used to be dropped from tracking and stayed on disk until the next launch.
/// </summary>
public class MediaTempFileLedgerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-ledger-tests-" + Guid.NewGuid().ToString("N"));

    public MediaTempFileLedgerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string NewFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    [Fact]
    public void FileOpenAtSweepTime_IsHeldThenDeletedOnceReleased()
    {
        var ledger = new MediaTempFileLedger(cap: 1);
        var playing = NewFile("playing.mp4");

        // FileShare.Read = no FILE_SHARE_DELETE, the way LibVLC holds a clip on Windows.
        using (new FileStream(playing, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ledger.Track(playing);
            ledger.Track(NewFile("next.mp4"));   // over the cap: the sweep hits the open file

            Assert.True(File.Exists(playing));
            Assert.Equal(1, ledger.HeldCount);   // still on the books, not forgotten
        }

        ledger.Track(NewFile("later.mp4"));      // the player let go: the next sweep takes it

        Assert.False(File.Exists(playing));
        Assert.Equal(0, ledger.HeldCount);
    }

    [Fact]
    public void LongRun_DiskNeverHoldsMoreThanCapPlusOpenFiles()
    {
        var ledger = new MediaTempFileLedger(cap: 5);
        for (int i = 0; i < 200; i++)
        {
            var path = NewFile($"f{i}.gif");
            // Each file is open while the next few arrive, like a flash that is still on screen.
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ledger.Track(path);
            }
        }
        ledger.Track(NewFile("last.gif"));

        Assert.True(Directory.GetFiles(_dir).Length <= 6);
    }

    [Fact]
    public void Release_OfAnOpenFile_IsRetriedBySweepAll()
    {
        var ledger = new MediaTempFileLedger(cap: 50);
        var path = NewFile("owner.jpg");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            ledger.Release(path);
            Assert.True(File.Exists(path));
            Assert.Equal(1, ledger.HeldCount);
        }

        ledger.SweepAll();

        Assert.False(File.Exists(path));
        Assert.Equal(0, ledger.HeldCount);
    }

    [Fact]
    public void RefusedDeletes_AreNeverListedTwice()
    {
        int calls = 0;
        var ledger = new MediaTempFileLedger(cap: 0, delete: _ => { calls++; return false; });

        ledger.Track("a");
        ledger.Track("b");
        ledger.Release("a");

        Assert.Equal(2, ledger.HeldCount);
        Assert.True(calls >= 3);
    }

    [Fact]
    public void TryDeleteFile_MissingFileCountsAsGone()
    {
        Assert.True(MediaTempFileLedger.TryDeleteFile(Path.Combine(_dir, "never-there.png")));
    }
}

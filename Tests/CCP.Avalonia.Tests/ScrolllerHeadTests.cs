using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The head half of the Scrolller licence work (WPF App.xaml.cs:2507-2516 start, App.OnExit:6206-6209
/// flush): App startup calls <see cref="ScrolllerHead.Start"/> and the exit path <see cref="ScrolllerHead.Flush"/>.
/// No network: a sandbox never reads the switch from the production proxy.</summary>
public sealed class ScrolllerHeadTests
{
    [Fact]
    public void SandboxReadsTheSwitchOnlyFromItsOwnApi()
    {
        Assert.True(ScrolllerHead.ReadsSwitch(null, BackRoomApi.ProductionBaseUrl));          // a real install
        Assert.False(ScrolllerHead.ReadsSwitch("/tmp/sbx", BackRoomApi.ProductionBaseUrl));   // sandbox: never prod
        Assert.True(ScrolllerHead.ReadsSwitch("/tmp/sbx", "http://127.0.0.1:5055"));         // sandbox with its fake
    }

    /// <summary>Start warms the device cache and starts its licence sweep (first tick right after the load).</summary>
    [Fact]
    public async Task StartSweepsTheDeviceCache()
    {
        var root = Directory.CreateTempSubdirectory("scrolller-head-").FullName;
        var (oldRoot, oldRoots, oldSeeds, oldSweep, oldPurge) = (ScrolllerDiskCache.RootProvider, ScrolllerDiskCache.RootsFileProvider,
            ScrolllerDiskCache.SeedRootsProvider, ScrolllerDiskCache.SweepAllCopies, ScrolllerDiskCache.PurgeAllCopies);
        var swept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            ScrolllerDiskCache.RootProvider = () => Path.Combine(root, "cache");
            ScrolllerDiskCache.RootsFileProvider = () => Path.Combine(root, "roots.json");
            ScrolllerDiskCache.SeedRootsProvider = () => Enumerable.Empty<string>();
            ScrolllerDiskCache.SweepAllCopies = () => swept.TrySetResult();
            ScrolllerDiskCache.PurgeAllCopies = null;
            ScrolllerHead.Start("/tmp/ccp-sandbox-scrolller");   // sandboxed: the switch is not read
            await swept.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Start seeds the off switch's purge with this head's copies (StartSeedsTheSweepAndPurge... proves what it deletes).
            Assert.Equal((Action<string>)ScrolllerHead.PurgeAll, ScrolllerDiskCache.PurgeAllCopies);
        }
        finally
        {
            ScrolllerDiskCache.SweepAllCopies = oldSweep;
            ScrolllerDiskCache.PurgeAllCopies = oldPurge;
            (ScrolllerDiskCache.RootProvider, ScrolllerDiskCache.RootsFileProvider, ScrolllerDiskCache.SeedRootsProvider) = (oldRoot, oldRoots, oldSeeds);
            ScrolllerDiskCache.Flush();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>Main 700da958f/a1a08fce2: the hooks Start seeds (not Start itself: its sweeper timer is process-wide and
    /// StartSweepsTheDeviceCache needs its first tick) carry this head's copies, so the hourly
    /// sweep drops a first-show picture 23 h old (one period ahead) and the off switch deletes the rest.</summary>
    [Fact]
    public void StartSeedsTheSweepAndPurgeOfTheFirstShowCopies()
    {
        var root = Directory.CreateTempSubdirectory("scrolller-copies-").FullName;
        var (oldRoot, oldRoots, oldSeeds, oldSweep, oldPurge) = (ScrolllerDiskCache.RootProvider, ScrolllerDiskCache.RootsFileProvider,
            ScrolllerDiskCache.SeedRootsProvider, ScrolllerDiskCache.SweepAllCopies, ScrolllerDiskCache.PurgeAllCopies);
        var dir = FirstShowStageWindow.TempDir;
        Directory.CreateDirectory(dir);
        var old = Path.Combine(dir, "head-test-old-" + Guid.NewGuid().ToString("N"));
        var fresh = Path.Combine(dir, "head-test-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            ScrolllerDiskCache.RootProvider = () => Path.Combine(root, "cache");
            ScrolllerDiskCache.RootsFileProvider = () => Path.Combine(root, "roots.json");
            ScrolllerDiskCache.SeedRootsProvider = () => Enumerable.Empty<string>();
            ScrolllerDiskCache.SweepAllCopies = null;
            ScrolllerDiskCache.PurgeAllCopies = null;
            ScrolllerHead.SeedHooks();
            Assert.NotNull(ScrolllerDiskCache.SweepAllCopies);
            Assert.NotNull(ScrolllerDiskCache.PurgeAllCopies);

            File.WriteAllText(old, "x");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow - TimeSpan.FromHours(23) - TimeSpan.FromMinutes(1));
            File.WriteAllText(fresh, "x");
            ScrolllerDiskCache.SweepAllCopies!();
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));

            ScrolllerDiskCache.PurgeAllCopies!("policy off");   // what ScrolllerPolicyService.Accept calls on enabled=false
            Assert.False(File.Exists(fresh));
        }
        finally
        {
            ScrolllerDiskCache.SweepAllCopies = oldSweep;
            ScrolllerDiskCache.PurgeAllCopies = oldPurge;
            (ScrolllerDiskCache.RootProvider, ScrolllerDiskCache.RootsFileProvider, ScrolllerDiskCache.SeedRootsProvider) = (oldRoot, oldRoots, oldSeeds);
            ScrolllerDiskCache.Flush();
            try { File.Delete(old); File.Delete(fresh); Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>Flush writes the daily share's ledger now instead of after its 5 s debounce.</summary>
    [Fact]
    public void FlushWritesTheLedger()
    {
        var root = Directory.CreateTempSubdirectory("scrolller-ledger-").FullName;
        var ledger = Path.Combine(root, "ledger.json");
        var (oldPath, oldLegacy) = (ScrolllerAllowance.LedgerPath, ScrolllerAllowance.LegacyLedgerPath);
        try
        {
            ScrolllerAllowance.ResetForTests();
            ScrolllerAllowance.LedgerPath = () => ledger;
            ScrolllerAllowance.LegacyLedgerPath = () => null;
            ScrolllerAllowance.Charge(4096);
            ScrolllerHead.Flush();
            Assert.True(File.Exists(ledger));
            Assert.Contains("4096", File.ReadAllText(ledger));
        }
        finally
        {
            ScrolllerAllowance.Flush();
            (ScrolllerAllowance.LedgerPath, ScrolllerAllowance.LegacyLedgerPath) = (oldPath, oldLegacy);
            ScrolllerAllowance.ResetForTests();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}

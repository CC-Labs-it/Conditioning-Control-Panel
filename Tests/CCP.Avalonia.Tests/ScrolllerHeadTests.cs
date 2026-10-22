using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
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
        var (oldRoot, oldRoots, oldSeeds, oldSweep) = (ScrolllerDiskCache.RootProvider, ScrolllerDiskCache.RootsFileProvider,
            ScrolllerDiskCache.SeedRootsProvider, ScrolllerDiskCache.SweepAllCopies);
        var swept = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            ScrolllerDiskCache.RootProvider = () => Path.Combine(root, "cache");
            ScrolllerDiskCache.RootsFileProvider = () => Path.Combine(root, "roots.json");
            ScrolllerDiskCache.SeedRootsProvider = () => Enumerable.Empty<string>();
            ScrolllerDiskCache.SweepAllCopies = () => swept.TrySetResult();
            ScrolllerHead.Start("/tmp/ccp-sandbox-scrolller");   // sandboxed: the switch is not read
            await swept.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            ScrolllerDiskCache.SweepAllCopies = oldSweep;
            (ScrolllerDiskCache.RootProvider, ScrolllerDiskCache.RootsFileProvider, ScrolllerDiskCache.SeedRootsProvider) = (oldRoot, oldRoots, oldSeeds);
            ScrolllerDiskCache.Flush();
            try { Directory.Delete(root, true); } catch { }
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

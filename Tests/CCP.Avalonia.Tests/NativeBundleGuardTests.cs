using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class NativeBundleGuardTests
{
    [Fact]
    public void HealthyProbeClearsStampAndContinues()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            NativeBundleRepair.WriteStamp(dir, "1", DateTime.UtcNow);
            Assert.True(NativeBundleGuard.VerifyOrRepairCore(null, "cache", "app", dir, "1",
                (_, _) => throw new Exception(), (_, _) => throw new Exception()));
            Assert.False(NativeBundleRepair.RepairedRecently(dir, "1", DateTime.UtcNow));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void FailedProbeLaunchesOnceThenShowsGiveUp()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var failure = new DllNotFoundException();
            int launches = 0, dialogs = 0;
            bool Launch(string root, string exe) { Assert.Equal("cache", root); Assert.Equal("app", exe); launches++; return true; }
            void GiveUp(string? root, Exception ex) { Assert.Equal("cache", root); Assert.Same(failure, ex); dialogs++; }
            Assert.False(NativeBundleGuard.VerifyOrRepairCore(failure, "cache", "app", dir, "1", Launch, GiveUp));
            Assert.Equal(1, launches);
            Assert.Equal(0, dialogs);
            Assert.False(NativeBundleGuard.VerifyOrRepairCore(failure, "cache", "app", dir, "1", Launch, GiveUp));
            Assert.Equal(1, launches);
            Assert.Equal(1, dialogs);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void DialogFailureStillStopsStartup()
    {
        Assert.False(NativeBundleGuard.VerifyOrRepairCore(new DllNotFoundException(), null, null,
            Path.GetTempPath(), "1", (_, _) => throw new Exception(), (_, _) => throw new Exception()));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("cache", false)]
    public void MissingCacheOrFailedHelperShowsGiveUp(string? root, bool launchResult)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            int launches = 0, dialogs = 0;
            Assert.False(NativeBundleGuard.VerifyOrRepairCore(new DllNotFoundException(), root, "app", dir, "1",
                (_, _) => { launches++; return launchResult; }, (_, _) => dialogs++));
            Assert.Equal(root == null ? 0 : 1, launches);
            Assert.Equal(1, dialogs);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}

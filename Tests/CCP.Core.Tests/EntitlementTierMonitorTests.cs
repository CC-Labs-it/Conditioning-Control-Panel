using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The focus throttle of WPF EntitlementTierSync.OnAppFocused, and nothing else: the reading a focus
/// fetches lands through the head's one tier path (Avalonia AccountSeedTests covers rise and lower there).
/// AuthToken lives in CoreSecrets, so the store is a dictionary for the test.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class EntitlementTierMonitorTests : IDisposable
{
    private readonly Func<string, string?>? _get = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _set = CoreSecrets.StoreProvider;

    public EntitlementTierMonitorTests()
    {
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
    }

    public void Dispose() { CoreSecrets.RetrieveProvider = _get; CoreSecrets.StoreProvider = _set; }

    private static AppSettings SignedIn() => new() { UnifiedId = "test-account", AuthToken = "test-token" };

    [Fact]
    public async Task FocusReadsAreThrottledSingleFlightAndNeverRunOffline()
    {
        var settings = SignedIn();
        var monitor = new EntitlementTierMonitor();
        var now = DateTime.UtcNow;
        var release = new TaskCompletionSource();
        var reads = 0;
        var first = monitor.OnAppFocusedAsync(settings, now, async id =>
        {
            Assert.Equal("test-account", id);
            reads++;
            await release.Task;
        });
        await monitor.OnAppFocusedAsync(settings, now.AddSeconds(21), _ => { reads++; return Task.CompletedTask; });
        Assert.Equal(1, reads);                      // one read in flight: the second caller gets nothing
        release.SetResult();
        await first;
        await monitor.OnAppFocusedAsync(settings, now.AddSeconds(19), _ => { reads++; return Task.CompletedTask; });
        Assert.Equal(1, reads);                      // inside FocusCheckGap
        await monitor.OnAppFocusedAsync(settings, now.AddSeconds(20), _ => { reads++; return Task.CompletedTask; });
        Assert.Equal(2, reads);
        settings.OfflineMode = true;
        await monitor.OnAppFocusedAsync(settings, now.AddSeconds(40), _ => throw new Exception("Offline read"));
    }

    [Fact]
    public async Task SignedOutNeverReadsAndAFailedReadFreesTheSlot()
    {
        var monitor = new EntitlementTierMonitor();
        var now = DateTime.UtcNow;
        await monitor.OnAppFocusedAsync(new AppSettings { UnifiedId = "test-account" }, now, _ => throw new Exception("No token"));
        await monitor.OnAppFocusedAsync(new AppSettings { AuthToken = "test-token" }, now, _ => throw new Exception("No account"));

        var settings = SignedIn();
        await Assert.ThrowsAsync<InvalidOperationException>(() => monitor.OnAppFocusedAsync(settings, now, _ => throw new InvalidOperationException("server down")));
        var reads = 0;
        await monitor.OnAppFocusedAsync(settings, now.AddSeconds(20), _ => { reads++; return Task.CompletedTask; });
        Assert.Equal(1, reads);                      // the throw released the in-flight slot
    }
}

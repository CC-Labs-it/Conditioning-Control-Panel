using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

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
    public void RiseSavesBeforeNotifyingAndLowerReadingsNeverRevokeGrace()
    {
        var settings = SignedIn();
        var monitor = new EntitlementTierMonitor();
        var saves = 0;
        var rises = 0;
        var now = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var observed = (Saves: 0, Tier: 0, Stored: 0, Until: (DateTime?)null);
        monitor.TierRaised += tier =>
        {
            rises++;
            observed = (saves, tier, settings.PatreonTier, settings.PatreonLabValidUntil);
        };
        Assert.True(monitor.Apply(settings, "test-account", 2, now, () => saves++));
        Assert.Equal((1, 2, 2, (DateTime?)now.AddDays(14)), observed);
        Assert.False(monitor.Apply(settings, "test-account", 2, now.AddDays(1), () => saves++));
        Assert.Equal(now.AddDays(14), settings.PatreonLabValidUntil); // same is not a perpetual renewal
        Assert.False(monitor.Apply(settings, "test-account", 0, now, () => saves++));
        Assert.Equal(0, settings.ServerTierSeen);
        Assert.Equal(2, settings.PatreonTier);
        Assert.Equal(now.AddDays(14), settings.PatreonPremiumValidUntil);
        Assert.False(monitor.Apply(settings, "test-account", 2, now, () => saves++));
        Assert.Null(settings.ServerTierSeen);
        Assert.Equal(1, rises);
        Assert.Equal(3, saves);
    }

    [Fact]
    public void MissingInvalidOfflineAndOldAccountReadingsAreIgnored()
    {
        var settings = SignedIn();
        var monitor = new EntitlementTierMonitor();
        void Reject(string id, int? tier) => Assert.False(monitor.Apply(settings, id, tier, DateTime.UtcNow,
            () => throw new Exception("Unexpected save")));
        Reject("other-account", 2);
        Reject("test-account", null);
        Reject("test-account", 3);
        settings.OfflineMode = true;
        Reject("test-account", 2);
        settings.OfflineMode = false;
        settings.AuthToken = null;
        Reject("test-account", 2);
        Assert.Equal(0, settings.PatreonTier);
    }

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
        Assert.Equal(1, reads);
        release.SetResult();
        await first;
        await monitor.OnAppFocusedAsync(settings, now.AddSeconds(19), _ => { reads++; return Task.CompletedTask; });
        Assert.Equal(1, reads);
        await monitor.OnAppFocusedAsync(settings, now.AddSeconds(20), _ => { reads++; return Task.CompletedTask; });
        Assert.Equal(2, reads);
        settings.OfflineMode = true;
        await monitor.OnAppFocusedAsync(settings, now.AddSeconds(40), _ => throw new Exception("Offline read"));
    }
}

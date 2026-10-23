using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The WPF-only half of the conversion tests (ProfileSyncService is head-only); the rest moved to CCP.Core.Tests.</summary>
public class ConversionBackupExclusionTests
{
    [Fact]
    public void Member_fields_never_ride_the_cloud_backup()
    {
        // They mirror the server profile; the next profile read refills them on any device.
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.PatreonMember)));
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.PostGrants)));
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.ServerPaidUntil)));
    }

    [Fact]
    public void Conversion_state_never_rides_the_cloud_backup()
    {
        // C9: synced = no. A restore must not bring back a stale tier reading or a stamp set.
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.ServerTierSeen)));
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.InboxStamps)));
    }
}

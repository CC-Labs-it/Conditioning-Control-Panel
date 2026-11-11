using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ConversionBackupTests
{
    [Theory]
    [InlineData(nameof(AppSettings.ServerTierSeen))]
    [InlineData(nameof(AppSettings.InboxStamps))]
    [InlineData(nameof(AppSettings.PatreonMember))]
    [InlineData(nameof(AppSettings.PostGrants))]
    [InlineData(nameof(AppSettings.ServerPaidUntil))]
    public void ConversionFieldsNeverEnterAnUpload(string field)
    {
        var settings = new AppSettings
        {
            ServerTierSeen = 1, PatreonMember = true, ServerPaidUntil = DateTime.UtcNow,
        };
        settings.InboxStamps["welcome"] = "2026-10-30";
        settings.PostGrants["dtrh"] = "2026-11-01T00:00:00Z";
        var upload = CloudSettingsBackup.StripForUpload(settings);
        Assert.True(CloudSettingsBackup.IsExcludedFromBackup(field));
        foreach (var property in upload.Properties())
            Assert.False(string.Equals(field, CloudSettingsBackup.PropertyNameOfJsonKey(property.Name), StringComparison.OrdinalIgnoreCase));
    }
}

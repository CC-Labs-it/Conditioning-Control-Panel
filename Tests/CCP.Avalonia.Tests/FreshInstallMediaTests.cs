using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FreshInstallMediaTests
{
    [Theory]
    [InlineData("local", false, "mixed")]
    [InlineData("online", false, "online")]
    [InlineData("local", true, "local")]
    public void ClaimChangesOnlyTheFreshDefault(string source, bool welcomed, string expected)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService(path);
        CoreSettings.ServiceProvider = () => service;
        try
        {
            var s = service.Current;
            s.MediaSource = source;
            s.Welcomed = welcomed;
            s.RemoteMediaConsented = false;
            s.FypOnlineConsented = false;
            Assert.Equal(!welcomed, FirstRunWizard.ShouldRunAndClaim());
            Assert.Equal(expected, s.MediaSource);
            Assert.False(s.HasRemoteMediaConsent);
            Assert.False(s.UsesRemoteMedia);
        }
        finally
        {
            service.SealForReset();
            CoreSettings.ServiceProvider = oldProvider;
            if (Directory.Exists(Path.GetDirectoryName(path)!)) Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}

using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ShellTitleVersionTests
{
    [Fact]
    public void VersionIsShownAndSurvivesNavigationAndSyncHealth() => AvaloniaTestDispatcher.Run(() =>
    {
        var window = new MainShellWindow();
        try
        {
            var expected = "Conditioning Control Panel v" + CoreReleaseContent.AppVersion;
            Assert.Equal(expected, window.Title);
            Assert.Equal(expected, window.FindControl<TextBlock>("TxtTitleBarVersion")!.Text);
            window.UpdateNavTitle(NavSections.Play, "lobby", "Lobby");
            Assert.StartsWith(expected + " - ", window.Title);
            Assert.Contains("Lobby", window.Title);
            window.OnSyncHealthChanged(null, 3);
            Assert.StartsWith(expected + " - ", window.Title);
            Assert.Contains("Lobby", window.Title);
            Assert.EndsWith(" - Cloud sync issue", window.Title);
        }
        finally { window.Close(); }
    });
}

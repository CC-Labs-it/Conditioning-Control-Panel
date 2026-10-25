using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserPopoutTests
{
    [Fact]
    public Task PopoutPreservesHostAndNavigation_ClosingAndPanicReturnIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.Current.OfflineMode;
        CoreSettings.Current.OfflineMode = false;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var slot = tab.FindControl<Grid>("BrowserContainer")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            shell.PopOutBrowser();
            var popout = Assert.IsType<Window>(shell.BrowserPopout);
            Assert.Same(web, popout.Content);
            Assert.DoesNotContain(web, slot.Children);
            Assert.Equal(1024, popout.Width);
            Assert.Equal(768, popout.Height);
            Assert.True(tab.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
            shell.PopOutBrowser();
            Assert.Same(popout, shell.BrowserPopout);
            Assert.True(shell.NavigateToUrlInBrowser("http://127.0.0.1:3001/"));
            Assert.Equal("127.0.0.1", web.Source!.Host);
            popout.Close();
            Assert.Null(shell.BrowserPopout);
            Assert.Contains(web, slot.Children);
            shell.PopOutBrowser();
            var surface = PanicSurfaces.All.Single(s => s.Id == "browser-popout");
            surface.Stop(null);
            Assert.Null(shell.BrowserPopout);
            Assert.Contains(web, slot.Children);
            CoreSettings.Current.OfflineMode = true;
            shell.PopOutBrowser();
            Assert.Null(shell.BrowserPopout);
            CoreSettings.Current.OfflineMode = false;
            shell.PopOutBrowser();
            shell.Close();
            Assert.Null(shell.BrowserPopout);
        }
        finally { CoreSettings.Current.OfflineMode = old; shell.Close(); }
        return Task.CompletedTask;
    });
}

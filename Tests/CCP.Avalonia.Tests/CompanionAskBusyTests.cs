using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Companion.Asks;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class CompanionAskBusyTests
{
    [Fact]
    public void BrowserPlaybackAndSlotEachKeepAsksQuiet()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var app = Application.Current!;
            var field = typeof(Application).GetField("_applicationLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var oldLifetime = field.GetValue(app);
            var oldBusy = CompanionAskService.BusyProvider;
            var settings = CoreSettings.Current;
            var oldProtect = settings.ProtectBrowserVideoPlayback;
            var shell = new MainShellWindow();
            var tab = shell.Named<SettingsTabView>("SettingsTab")!;
            var host = tab.FindControl<WebHost>("BrowserWebHost")!;
            try
            {
                field.SetValue(app, new ClassicDesktopStyleApplicationLifetime { MainWindow = shell });
                settings.ProtectBrowserVideoPlayback = false;
                AvApp.SeedCompanionTubeSeams();
                Assert.False(CompanionAskService.BusyProvider!());
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
                tab.ClearBrowserMediaClaim();
                Assert.True(tab.BrowserMediaPlaying);
                Assert.False(tab.BrowserMediaOwnsSlot);
                Assert.True(CompanionAskService.BusyProvider!());
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
                Assert.False(CompanionAskService.BusyProvider!());
                // A claimed takeover can own the slot before playback starts.
                typeof(SettingsTabView).GetProperty(nameof(SettingsTabView.BrowserMediaOwnsSlot), BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tab, true);
                Assert.False(tab.BrowserMediaPlaying);
                Assert.True(CompanionAskService.BusyProvider!());
                tab.ClearBrowserMediaClaim();
                Assert.False(CompanionAskService.BusyProvider!());
            }
            finally
            {
                CompanionAskService.Instance.Stop();
                CompanionAskService.BusyProvider = oldBusy;
                tab.ClearBrowserMediaClaim();
                shell.Close();
                field.SetValue(app, oldLifetime);
                settings.ProtectBrowserVideoPlayback = oldProtect;
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}

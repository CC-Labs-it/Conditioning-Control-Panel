using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>One physical press heard by XInput2 and the portal is one panic press: counted twice it
/// took the double-press exit rung, closing the app on a single Escape (seen on Hyprland).</summary>
public sealed class PanicRouteTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void One_press_heard_on_both_routes_counts_once(bool x11First) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.PanicKeyEnabled = true;
        CoreSettings.Current.MasterVolume = 0;
        var shell = new MainShellWindow { PanicRouteClock = () => 1000 };
        try
        {
            if (x11First) { shell.OnX11PanicPress(); shell.OnPortalPanicActivated(); }
            else { shell.OnPortalPanicActivated(); shell.OnX11PanicPress(); }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, shell.PanicPressCountForTest);
            Assert.True(shell.PlatformImpl is not null, "one press must not have closed the shell");
        }
        finally
        {
            CoreSettings.ServiceProvider = oldProvider;
            shell.Close();
        }
    });
}

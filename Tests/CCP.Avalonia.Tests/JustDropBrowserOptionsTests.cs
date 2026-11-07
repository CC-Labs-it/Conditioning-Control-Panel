using System.Runtime.CompilerServices;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class JustDropBrowserOptionsTests
{
    [Fact]
    public void WindowsOptionsArePerHostAndLeaveOtherProfilesUntouched()
    {
        // These options are plain managed properties. No engine or window is needed.
        var host = (WebHost)RuntimeHelpers.GetUninitializedObject(typeof(WebHost));
        var args = (WindowsWebView2EnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
        args.UserDataFolder = "default-profile";
        args.AdditionalBrowserArguments = "default-arguments";
        host.ApplyWindowsOptions(args);
        Assert.Equal("default-profile", args.UserDataFolder);
        Assert.Equal("default-arguments", args.AdditionalBrowserArguments);
        host.WindowsBrowserArguments = GameWindow.JustDropBrowserArguments;
        host.WindowsUserDataFolder = "isolated-profile";
        host.ApplyWindowsOptions(args);
        Assert.Equal("isolated-profile", args.UserDataFolder);
        Assert.Equal("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-backgrounding-occluded-windows", args.AdditionalBrowserArguments);
        host.ApplyWindowsOptions(null);
        host.ApplyWindowsOptions(new object());
    }
}

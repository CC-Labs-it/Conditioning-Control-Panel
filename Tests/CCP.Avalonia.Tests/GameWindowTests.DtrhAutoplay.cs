using System;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Dtrh_WebView2AutoplaysAndKeepsMotionInItsOwnProfile() => AvaloniaTestDispatcher.Run(() =>
    {
        var window = new GameWindow(GameWindow.Games[GameWindow.DtrhId]);
        try
        {
            var args = (WindowsWebView2EnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
            Assert.True(WebViewProfile.Apply(args));
            window.Web.ApplyWindowsOptions(args);
            window.Web.OnEnvironmentRequested(args);
            Assert.Contains("--autoplay-policy=no-user-gesture-required", args.AdditionalBrowserArguments);
            Assert.Contains("--force-prefers-", args.AdditionalBrowserArguments);
            Assert.Equal(Path.Combine(CorePaths.UserData, "browser_data_avalonia_dtrh_" + (CoreSettings.Current.MotionLevel == ConditioningControlPanel.Models.MotionLevel.Off ? "reduce" : "motion")), args.UserDataFolder);
        }
        finally { window.Close(); }
    });
}

using Avalonia.Controls;
using Avalonia.Platform;
using System.Runtime.CompilerServices;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void EveryHostSetsMotionArgumentAndJustDropKeepsAutoplay() => AvaloniaTestDispatcher.Run(() =>
    {
        foreach (var spec in GameWindow.Games.Values)
        {
            var window = new GameWindow(spec);
            try
            {
                var args = (WindowsWebView2EnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
                if (spec.Id == GameWindow.JustDropId) window.Web.WindowsBrowserArguments = GameWindow.JustDropBrowserArguments;
                window.Web.ApplyWindowsOptions(args);
                window.Web.OnEnvironmentRequested(args);
                Assert.Contains("--force-prefers-", args.AdditionalBrowserArguments);
                Assert.True(window.Web.HostedPageMotion);
                if (spec.Id == GameWindow.JustDropId) Assert.Contains("--autoplay-policy=no-user-gesture-required", args.AdditionalBrowserArguments);
            }
            finally { window.Close(); }
        }
        var intake = new IntakeHostWindow();
        try
        {
            var args = (WindowsWebView2EnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
            intake.Web.OnEnvironmentRequested(args);
            Assert.Contains("--force-prefers-", args.AdditionalBrowserArguments);
            Assert.True(intake.Web.HostedPageMotion);
        }
        finally { intake.Close(); }
    });

    [Fact]
    public void NativePageFullscreenChangesGameAndIntakeWindowsButNotJustDrop() => AvaloniaTestDispatcher.Run(() =>
    {
        foreach (var spec in GameWindow.Games.Values)
        {
            var window = new GameWindow(spec);
            try
            {
                window.WindowState = WindowState.Maximized;
                window.Web.OnNativeFullscreenEntered();
                Assert.Equal(spec.Id == GameWindow.JustDropId ? WindowState.Maximized : WindowState.FullScreen, window.WindowState);
                window.Web.OnNativeFullscreenExited();
                Assert.Equal(WindowState.Maximized, window.WindowState);
            }
            finally { window.Close(); }
        }
        var intake = new IntakeHostWindow();
        try
        {
            intake.WindowState = WindowState.Maximized;
            intake.Web.OnNativeFullscreenEntered();
            Assert.Equal(WindowState.FullScreen, intake.WindowState);
            intake.Web.OnNativeFullscreenExited();
            Assert.Equal(WindowState.Maximized, intake.WindowState);
        }
        finally { intake.Close(); }
    });
}

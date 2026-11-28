using Avalonia.Controls;
using Avalonia.Platform;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WebView2 refuses one user-data folder opened with two argument sets, so a host carrying the motion
    // switch must leave the shared WebViewProfile.Folder, and each switch value gets its own folder.
    [Fact]
    public void EveryHostSetsMotionArgumentOffTheSharedProfileAndJustDropKeepsAutoplay() => AvaloniaTestDispatcher.Run(() =>
    {
        var saved = ConditioningControlPanel.CoreSettings.Current.MotionLevel;
        try
        {
            var folders = new Dictionary<MotionLevel, HashSet<string?>>();
            foreach (var level in new[] { MotionLevel.Off, MotionLevel.Full })
            {
                ConditioningControlPanel.CoreSettings.Current.MotionLevel = level;
                var seen = folders[level] = new HashSet<string?>();
                foreach (var spec in GameWindow.Games.Values)
                {
                    var window = new GameWindow(spec);
                    try
                    {
                        if (spec.Id == GameWindow.JustDropId) window.Web.WindowsBrowserArguments = GameWindow.JustDropBrowserArguments;
                        var args = RequestNativeEnvironment(window.Web);
                        Assert.Contains("--force-prefers-", args.AdditionalBrowserArguments);
                        Assert.NotEqual(WebViewProfile.Folder, args.UserDataFolder);
                        seen.Add(args.UserDataFolder);
                        Assert.True(window.Web.HostedPageMotion);
                        if (spec.Id == GameWindow.JustDropId) Assert.Contains("--autoplay-policy=no-user-gesture-required", args.AdditionalBrowserArguments);
                    }
                    finally { window.Close(); }
                }
                var intake = new IntakeHostWindow();
                try
                {
                    var args = RequestNativeEnvironment(intake.Web);
                    Assert.Contains("--force-prefers-", args.AdditionalBrowserArguments);
                    Assert.NotEqual(WebViewProfile.Folder, args.UserDataFolder);
                    seen.Add(args.UserDataFolder);
                    Assert.True(intake.Web.HostedPageMotion);
                }
                finally { intake.Close(); }
            }
            // DtRH and the shared hosted profile split by motion; other own-profile games keep their folder.
            Assert.Equal(2, folders[MotionLevel.Off].Except(folders[MotionLevel.Full]).Count());
            Assert.Equal(2, folders[MotionLevel.Full].Except(folders[MotionLevel.Off]).Count());
        }
        finally { ConditioningControlPanel.CoreSettings.Current.MotionLevel = saved; }
    });

    // The native adapter's EnvironmentRequested chain in WebHost.CreateNativeView order: shared profile,
    // per-host options, persistent profile, then the host's own subscribers.
    private static WindowsWebView2EnvironmentRequestedEventArgs RequestNativeEnvironment(ConditioningControlPanel.Avalonia.Views.Controls.WebHost web)
    {
        var args = (WindowsWebView2EnvironmentRequestedEventArgs)RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
        Assert.True(WebViewProfile.Apply(args));
        web.ApplyWindowsOptions(args);
        web.ApplyPersistentProfile(args);
        web.OnEnvironmentRequested(args);
        return args;
    }

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
                if (spec.Id != GameWindow.JustDropId) AssertHostFullscreenExitsTitled(window, window.Web);
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
            AssertHostFullscreenExitsTitled(intake, intake.Web);
        }
        finally { intake.Close(); }
    });

    // WPF ChaosWebViewHost.cs:598,1661: one fullscreen state, so a page exit leaves a host-entered
    // fullscreen for a titled window, and a mode remembered before a host exit is not reused.
    private static void AssertHostFullscreenExitsTitled(Window window, ConditioningControlPanel.Avalonia.Views.Controls.WebHost web)
    {
        window.WindowState = WindowState.FullScreen;
        web.OnNativeFullscreenEntered();
        web.OnNativeFullscreenExited();
        Assert.Equal(WindowState.Normal, window.WindowState);
        window.WindowState = WindowState.Maximized;
        web.OnNativeFullscreenEntered();
        window.WindowState = WindowState.Normal;
        window.WindowState = WindowState.FullScreen;
        web.OnNativeFullscreenEntered();
        web.OnNativeFullscreenExited();
        Assert.Equal(WindowState.Normal, window.WindowState);
    }
}

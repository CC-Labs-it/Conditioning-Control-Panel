using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Owner 11-06: the tube and EMI showed a dim "ghost" rectangle with a shadow under WSLg. Weston's
/// X window manager paints a shadow tile in the frame of every undecorated window unless the window's
/// type list carries _KDE_NET_WM_WINDOW_TYPE_OVERRIDE (measured on the owner's WSLg: plain = ghost,
/// with the type = only the painted pixels).</summary>
public sealed class FramelessShadowTests
{
    [Fact]
    public void TheHintIsAppendedAfterAnyTypeAlreadyThere()
    {
        var kde = (IntPtr)901;
        Assert.Equal(new[] { kde }, X11Overlay.WithAtom(Array.Empty<IntPtr>(), kde));
        // Weston and every other WM read the FIRST atom as the type: an existing type keeps its place.
        Assert.Equal(new[] { (IntPtr)7, (IntPtr)8, kde }, X11Overlay.WithAtom(new[] { (IntPtr)7, (IntPtr)8 }, kde));
        Assert.Null(X11Overlay.WithAtom(new[] { (IntPtr)7, kde }, kde));   // already there: nothing to write
    }

    [Theory]
    [InlineData("Weston WM", null, true)]       // WSLg and plain Weston
    [InlineData("Openbox", null, false)]        // compositing desktops show only painted pixels already
    [InlineData("Xfwm4", null, false)]
    [InlineData("KWin", null, false)]
    [InlineData(null, null, false)]             // no EWMH manager (yet)
    [InlineData("Weston WM", "keep", false)]
    [InlineData("Openbox", "strip", true)]
    [InlineData("Weston WM", "STRIP", true)]
    public void OnlyWestonsShadowIsStripped(string? wm, string? env, bool expected)
        => Assert.Equal(expected, FramelessShadow.WmDrawsGhostShadow(wm, env));

    [Fact]
    public Task OnlyFramelessTransparentWindowsQualify() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        Assert.False(FramelessShadow.IsFramelessTransparent(new Window()));
        Assert.False(FramelessShadow.IsFramelessTransparent(new Window { WindowDecorations = WindowDecorations.None }));   // opaque, like the shell
        Assert.False(FramelessShadow.IsFramelessTransparent(new Window
            { TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent } }));   // has the system frame
        Assert.True(FramelessShadow.IsFramelessTransparent(new Window
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
        }));
        return Task.CompletedTask;
    });

    /// <summary>The widgets the owner named, plus EMI's floating desk windows: every one is asked for no
    /// shadow when it shows, and before it maps (WindowOpened is raised before PlatformImpl.Show).</summary>
    [Fact]
    public Task TubeBubbleAndEmiWindowsAreStrippedWhenShown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarTubeDetached = false;
        var asked = new List<Window>();
        bool? visibleWhenAsked = null;
        FramelessShadow.StripRequestedForTests = w => { asked.Add(w); visibleWhenAsked ??= w.IsVisible; };
        var main = new Window { Width = 1000, Height = 700, Position = new PixelPoint(2000, 200), Background = Brushes.Black };
        AvatarTubeWindow? tube = null;
        EmiDeskWindow? emi = null;
        FirstShowStageWindow? stage = null;
        try
        {
            main.Show();
            Assert.DoesNotContain(main, asked);   // opaque: keeps the WM frame

            tube = new AvatarTubeWindow(main);
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(tube, asked);
            Assert.False(visibleWhenAsked);       // asked from ShowCore, ahead of the map
            Assert.NotNull(tube.BubbleWindow);
            Assert.True(FramelessShadow.IsFramelessTransparent(tube.BubbleWindow!));
            tube.BubbleWindow!.Show();
            Assert.Contains(tube.BubbleWindow, asked);

            emi = new EmiDeskWindow();
            emi.Show();
            Assert.Contains(emi, asked);

            // The stage itself is override-redirect on X11 (no WM frame at all); its speech window is managed.
            stage = new FirstShowStageWindow();
            Assert.True(FramelessShadow.IsFramelessTransparent(stage));
            stage.GuideSpeechWindow.Show();
            Assert.Contains(stage.GuideSpeechWindow, asked);
        }
        finally
        {
            FramelessShadow.StripRequestedForTests = null;
            stage?.GuideSpeechWindow.Close();
            stage?.Close();
            emi?.Close();
            tube?.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        FramelessShadow.Install();   // the headless setup never reaches OnFrameworkInitializationCompleted
    }
}

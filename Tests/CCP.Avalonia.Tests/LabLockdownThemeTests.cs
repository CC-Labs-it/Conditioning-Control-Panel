using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LabLockdownThemeTests
{
    private static void Run(Action<MainShellWindow, LockdownService> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldSettings, oldLockdown) = (CoreSettings.ServiceProvider, LockdownService.Current);
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.MotionLevel = MotionLevel.Off;
        s.LockdownDoseKeeperEnabled = s.LockdownPossessionEnabled = false;
        s.LockdownForceStrictLock = s.LockdownDisablePanicKey = s.LockdownBlockSystemKeys = false;
        using var ld = new LockdownService();
        LockdownService.Current = ld;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            body(shell, ld);
        }
        finally
        {
            ld.Deactivate();
            Dispatcher.UIThread.RunJobs();
            shell?.Close();
            (CoreSettings.ServiceProvider, LockdownService.Current) = (oldSettings, oldLockdown);
        }
    });

    [Fact]
    public void ActivationPaintsTheCardAndLevelGlow_ExitRestoresTheirOriginalBindings() => Run((shell, ld) =>
    {
        shell.ShowTab("lockdown");
        var tab = shell.Named<LockdownTabView>("LockdownTab")!;
        var card = tab.FindControl<Border>("LockdownCardBorder")!;
        var glow = Assert.IsType<DropShadowEffect>(shell.Named<Border>("LevelChip")!.Effect);
        var (background, border, color) = (card.Background, card.BorderBrush, glow.Color);
        ld.Activate(TimeSpan.FromMinutes(30));
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.LockdownThemed);
        Assert.Equal(Color.Parse("#1A0A0A"), Assert.IsType<SolidColorBrush>(card.Background).Color);
        Assert.Equal(Color.Parse("#DC143C"), Assert.IsType<SolidColorBrush>(card.BorderBrush).Color);
        Assert.Equal(Color.Parse("#DC143C"), glow.Color);
        shell.PaintLockdownFlash(600);
        if (Environment.GetEnvironmentVariable("CCP_LAB_THEME_FRAMES") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            shell.CaptureRenderedFrame()?.Save(Path.Combine(dir, "shell-lab-lockdown-active.png"));
        }
        var resources = Application.Current!.Resources;
        resources.TryGetValue("PinkColor", out var originalAccent);
        var nextAccent = Color.Parse("#3C88CC");
        try
        {
            resources["PinkColor"] = nextAccent;
            Assert.Equal(Color.Parse("#DC143C"), glow.Color); // held while the theme is active
            ld.Deactivate();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.LockdownThemed);
            Assert.Same(background, card.Background);
            Assert.Same(border, card.BorderBrush);
            Assert.Equal(nextAccent, glow.Color); // the binding, rather than an old colour, comes back
        }
        finally
        {
            if (originalAccent != null) resources["PinkColor"] = originalAccent;
            else resources.Remove("PinkColor");
        }
        Assert.Equal(color, glow.Color);
    });

    [Fact]
    public void QueuedActivationCannotCreateAFlashAfterTheShellCloses() => Run((shell, ld) =>
    {
        ld.Activate(TimeSpan.FromMinutes(30));
        ld.Deactivate();
        shell.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(shell.LockdownFlash);
        Assert.False(shell.LockdownThemed);
    });

    // Review P3: close while Lockdown is still ACTIVE, so the queued Refresh would re-theme a closed shell.
    // The user's close is refused under Lockdown (Tray.cs OnClosing); only an OS/app shutdown gets through,
    // which headless cannot raise, so the test stands in for it with the shutdown flag.
    [Fact]
    public void QueuedRefreshCannotThemeAShellClosedDuringLockdown() => Run((shell, ld) =>
    {
        ld.Activate(TimeSpan.FromMinutes(30));
        shell.MarkExitRequestedForTest();
        shell.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.LockdownThemed);
        Assert.Null(shell.LockdownFlash);
    });
}

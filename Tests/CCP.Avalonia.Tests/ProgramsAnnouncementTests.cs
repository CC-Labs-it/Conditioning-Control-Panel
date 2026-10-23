using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using ConditioningControlPanel.Services.UI;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

// Isolated: changes process-wide settings/clock and inspects the live window/loop registry.
[Collection(RunsAloneCollection.Name)]
public sealed class ProgramsAnnouncementTests
{
    private sealed class Clock : TimeProvider
    {
        private const long Start = 1_000_000;
        public long Now = Start;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void At(double seconds)
        {
            Now = Start + TimeSpan.FromSeconds(seconds).Ticks;
            BeatLoop.StepAll();
        }
    }

    private static void Run(Action<MainShellWindow, Clock> body, Action<AppSettings>? configure = null) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var previousSettings = CoreSettings.ServiceProvider;
            var previousTime = BeatLoop.Time;
            var service = new SettingsService();
            var settings = service.Current;
            settings.Welcomed = settings.HasAcceptedAgeVerification = true;
            settings.WhatMovedCardShown = 1;
            settings.HasSeenProgramsIntro = true;
            settings.HasSeenProgramsTab = false;
            settings.AvatarEnabled = false;
            settings.MotionLevel = MotionLevel.Full;
            settings.PerformanceMode = false;
            settings.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            configure?.Invoke(settings);
            CoreSettings.ServiceProvider = () => service;
            var clock = new Clock();
            BeatLoop.Time = clock;
            StartupLadder.ResetForTests();
            MainShellWindow? shell = null;
            try
            {
                shell = new MainShellWindow();
                body(shell, clock);
            }
            finally
            {
                if (shell != null)
                {
                    foreach (var owned in shell.OwnedWindows.ToArray()) owned.Close();
                    shell.Close();
                }
                Dispatcher.UIThread.RunJobs();
                StartupLadder.ResetForTests();
                BeatLoop.Time = previousTime;
                CoreSettings.ServiceProvider = previousSettings;
            }
        });

    private static Button Header(MainShellWindow shell) => shell.NavAnchorForTab("programs")!;

    [Fact]
    public void LaunchWaitsThenDipsTheOwningRailFourTimesAndRests() => Run((shell, clock) =>
    {
        shell.Show();
        var header = Header(shell);
        Assert.Same(shell.Named<Button>("DoorYou"), header);
        clock.At(1.199);
        Assert.Equal(1, header.Opacity);
        for (int cycle = 0; cycle < 4; cycle++)
        {
            clock.At(1.2 + cycle * 1.4 + 0.35);
            Assert.Equal(0.675, header.Opacity, 3);
            clock.At(1.2 + cycle * 1.4 + 0.7);
            Assert.Equal(0.35, header.Opacity, 3);
        }
        clock.At(6.801);
        Assert.Equal(1, header.Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == header);
        Assert.False(CoreSettings.Current.HasSeenProgramsTab); // the cue alone does not spend it
    });

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.9)]
    public void AnyRouteIntoProgramsCancelsPendingOrLiveCueAndSpendsSeenFlag(double at) => Run((shell, clock) =>
    {
        shell.Show();
        clock.At(at);
        shell.ShowTab("programs");
        Assert.True(CoreSettings.Current.HasSeenProgramsTab);
        Assert.True(shell.Named<ProgramsTabView>("ProgramsTab")!.IsVisible);
        Assert.Equal(1, Header(shell).Opacity);
        clock.At(3.3);
        Assert.Equal(1, Header(shell).Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == Header(shell));
    });

    [Theory]
    [InlineData("quiet")]
    [InlineData("reduced")]
    [InlineData("off")]
    [InlineData("performance")]
    [InlineData("seen")]
    [InlineData("section")]
    public void LaunchGatesSwallowTheAnnouncement(string gate) => Run((shell, clock) =>
    {
        if (gate == "quiet") StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        if (gate == "section") shell.ShowTab("achievements");
        shell.Show();
        clock.At(1.9);
        Assert.Equal(1, Header(shell).Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == Header(shell));
    }, settings =>
    {
        if (gate == "reduced") settings.MotionLevel = MotionLevel.Reduced;
        if (gate == "off") settings.MotionLevel = MotionLevel.Off;
        if (gate == "performance") settings.PerformanceMode = true;
        if (gate == "seen") settings.HasSeenProgramsTab = true;
    });

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.9)]
    public void PanicCancelsPendingAndLiveCue(double at) => Run((shell, clock) =>
    {
        shell.Show();
        clock.At(at);
        PanicSurfaces.All.Single(s => s.Id == "programs-announcement").Stop(shell);
        clock.At(3.3);
        Assert.Equal(1, Header(shell).Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == Header(shell));
    });

    [Fact]
    public void HideAndMinimizeParkTheBeatAndMotionChangeOrCloseReleasesIt() => Run((shell, clock) =>
    {
        shell.Show();
        clock.At(1.9);
        var loop = Assert.Single(BeatLoop.Running.Where(l => l.Owner == Header(shell)));
        shell.Hide();
        Assert.False(loop.IsTicking);
        shell.Show();
        Assert.True(loop.IsTicking);
        shell.WindowState = WindowState.Minimized;
        Assert.False(loop.IsTicking);
        shell.WindowState = WindowState.Normal;
        Assert.True(loop.IsTicking);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(loop.IsRunning);
        Assert.Equal(1, Header(shell).Opacity);
    });

    [Fact]
    public void ClosingMidDipRestoresTheRailAndDropsTheClock() => Run((shell, clock) =>
    {
        shell.Show();
        clock.At(1.9);
        var header = Header(shell);
        shell.Close();
        Assert.Equal(1, header.Opacity);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == header);
    });

    [Theory]
    [InlineData("presentation")]
    [InlineData("firmware_install")]
    public void LockedCardClickOpensAccountBeforeReadOnlyAndCapabilityChecks(string programId) => Run((shell, _) =>
    {
        var path = Path.Combine(TestUserDataProfile.Root, Guid.NewGuid().ToString("N") + ".json");
        using var programs = new ProgramService(path, readOnly: true);
        var previousPrograms = AvApp.Programs;
        var previousPremium = CoreEntitlement.HasPremiumProvider;
        try
        {
            AvApp.Programs = programs;
            CoreEntitlement.HasPremiumProvider = () => false;
            shell.Show();
            shell.ShowTab("programs");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<ProgramsTabView>("ProgramsTab")!;
            var action = tab.FindControl<ListBox>("ProgramLibraryList")!
                .GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, programId));
            Assert.True(action.IsEffectivelyEnabled);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Equal("account", shell.AppSettingsPage!.CheckedSectionKey);
            Assert.Empty(shell.OwnedWindows.OfType<ProgramEnrollDialog>());
            Assert.Null(programs.ActiveEnrollment);
            Assert.False(File.Exists(path));
        }
        finally
        {
            AvApp.Programs = previousPrograms;
            CoreEntitlement.HasPremiumProvider = previousPremium;
        }
    });
}

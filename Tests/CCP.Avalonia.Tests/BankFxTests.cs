using System;
using System.Linq;
using System.IO;
using System.Reflection;
using ConditioningControlPanel.Avalonia.Helpers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BankFxTests
{
    [Fact]
    public void CompletionHoldsOnlyTheReadoutWhileTheLedgerAlreadyHasTheAward() => WithShell((shell, s) =>
    {
        var before = shell.FindControl<TextBlock>("TxtXP")!.Text;
        ProgressionBank.Add(5, "Quest");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(15, s.PlayerXP);
        Assert.Equal(before, shell.FindControl<TextBlock>("TxtXP")!.Text);
    });

    [Theory]
    [InlineData("Mantra", MotionLevel.Full)]
    [InlineData("Quest", MotionLevel.Reduced)]
    [InlineData("Session", MotionLevel.Off)]
    public void WeatherAndReducedMotionWriteTruthImmediately(string source, MotionLevel motion) => WithShell((shell, s) =>
    {
        s.MotionLevel = motion;
        ProgressionBank.Add(5, source);
        Dispatcher.UIThread.RunJobs();
        shell.SettleXpOdometerForTests();
        Assert.False(shell.BankHolding);
        Assert.StartsWith("15 / ", shell.FindControl<TextBlock>("TxtXP")!.Text);
    });

    [Fact]
    public void FractionalAwardsUseWpfsRoundedReadout() => WithShell((shell, s) =>
    {
        s.MotionLevel = MotionLevel.Off;
        ProgressionBank.Add(0.8, "Mantra");
        Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("11 / ", shell.FindControl<TextBlock>("TxtXP")!.Text);
    });

    [Fact]
    public void TokensDeliverTheCompletionThenReleaseInterveningWeatherOnTruth() => WithShell((shell, s) =>
    {
        double now = 0;
        shell.BankClockOverride = () => now;
        var before = shell.FindControl<TextBlock>("TxtXP")!.Text;
        ProgressionBank.Add(40, "Quest");
        Dispatcher.UIThread.RunJobs();
        ProgressionBank.Add(3, "Mantra");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, shell.FindControl<TextBlock>("TxtXP")!.Text);
        now = BankAccumulator.WindowMs - 1;
        shell.BankPollTickForTests();
        Assert.Null(shell.BankLayer);
        now++;
        shell.BankPollTickForTests();
        Assert.True(shell.BankFlightLive);
        Assert.NotNull(shell.BankLayer);
        shell.BankLayer!.StepBankTokensForTests(0.15f);
        shell.BankLayer.Pause(); // freeze this frame while headless layout/render catches up
        using (var frame = shell.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
            frame!.Save("/tmp/ccp-bank-flight.png");
        }
        shell.BankLayer.StepBankTokensForTests(2);
        shell.SettleXpOdometerForTests();
        Assert.False(shell.BankHolding);
        Assert.False(shell.BankFlightLive);
        Assert.False(shell.BankPollRunning);
        Assert.StartsWith("53 / ", shell.FindControl<TextBlock>("TxtXP")!.Text);
        shell.BankPopRun!.Seek(90);
        Assert.Equal(1.28, shell.BankPopScaleNow, 6);
        shell.BankPopRun.Finish();
        Assert.Equal(1, shell.BankPopScaleNow);
    });

    [Theory]
    [InlineData("panic")]
    [InlineData("watchdog")]
    [InlineData("minimize")]
    public void InterruptedFlightsReleaseTruthAndLeaveNoBankPoll(string reason) => WithShell((shell, s) =>
    {
        double now = 0;
        shell.BankClockOverride = () => now;
        ProgressionBank.Add(40, "Session");
        Dispatcher.UIThread.RunJobs();
        now = BankAccumulator.WindowMs;
        shell.BankPollTickForTests();
        Assert.True(shell.BankFlightLive);
        var oldLayer = shell.BankLayer!;
        ProgressionBank.Add(3, "Mantra");
        Dispatcher.UIThread.RunJobs();
        if (reason == "panic") PanicSurfaces.All.Single(x => x.Id == "bank-fx").Stop(shell);
        else if (reason == "minimize") shell.WindowState = WindowState.Minimized;
        else { now += 6001; shell.BankPollTickForTests(); }
        oldLayer.StepBankTokensForTests(2);
        shell.SettleXpOdometerForTests();
        Assert.False(shell.BankHolding);
        Assert.False(shell.BankFlightLive);
        Assert.False(shell.BankPollRunning);
        Assert.StartsWith("53 / ", shell.FindControl<TextBlock>("TxtXP")!.Text);
    });

    [Fact]
    public void ALevelUpPoisonsThePotAndReleasesThePostLevelTruth() => WithShell((shell, s) =>
    {
        ProgressionBank.Add(40, "Quest");
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.BankHolding);
        ProgressionBank.Add(XpCurve.GetXPForLevel(s.PlayerLevel, XpCurve.EpochOf(s)), "Quest");
        Dispatcher.UIThread.RunJobs();
        shell.SettleXpOdometerForTests();
        Assert.Equal(11, s.PlayerLevel);
        Assert.False(shell.BankHolding);
        Assert.False(shell.BankFlightLive);
        Assert.StartsWith("50 / ", shell.FindControl<TextBlock>("TxtXP")!.Text);
    });

    [Fact]
    public void LandingThudUsesTheModOverrideAndScalesMasterVolume() => AvaloniaTestDispatcher.Run(() =>
    {
        string file = Path.Combine(TestUserDataProfile.Root, "bank-thud-test.mp3");
        File.WriteAllBytes(file, new byte[] { 0 });
        var oldArt = CoreModArt.AudioOverridePathProvider;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var s = CoreSettings.Current;
        int oldVolume = s.MasterVolume;
        (string? Path, float Volume, string? Tag) played = (null, -1, null);
        CoreModArt.AudioOverridePathProvider = rel => rel == "ui/bank_thud.mp3" ? file : null;
        CoreAudio.PlayOneShotProvider = (path, volume, tag, _, _) => played = (path, volume, tag);
        try
        {
            s.MasterVolume = 50;
            typeof(MainShellWindow).GetMethod("PlayBankThud", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            Assert.Equal(file, played.Path);
            Assert.Equal(0.11f, played.Volume);
            Assert.Equal("ui-bank", played.Tag);
        }
        finally
        {
            CoreModArt.AudioOverridePathProvider = oldArt;
            CoreAudio.PlayOneShotProvider = oldAudio;
            s.MasterVolume = oldVolume;
            File.Delete(file);
        }
    });

    private static void WithShell(Action<MainShellWindow, AppSettings> check) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        var previous = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.OfflineMode = true;
        s.OfflineUsername = "bank-test";
        s.PlayerLevel = 10;
        s.PlayerXP = 10;
        s.MotionLevel = MotionLevel.Full;
        s.MasterVolume = 0;
        s.PerformanceMode = false;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        FxTrack.ManualClock = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsActive);
            shell.SettleXpOdometerForTests();
            check(shell, s);
        }
        finally
        {
            FxTrack.ManualClock = false;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = previous;
        }
    });
}

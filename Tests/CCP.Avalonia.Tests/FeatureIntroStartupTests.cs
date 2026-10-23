using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FeatureIntroStartupTests
{
    private static Task Run(string last, Func<MainShellWindow, List<Window>, StartupLadder.TestClock, Task> body, bool accepted = true) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current == null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var oldSettings = CoreSettings.ServiceProvider;
            var oldTutorial = CoreTutorial.IsActiveProvider;
            CoreTutorial.IsActiveProvider = () => false;
            var oldGet = CoreSecrets.RetrieveProvider;
            var oldSet = CoreSecrets.StoreProvider;
            var file = Path.Combine(CorePaths.UserData, "settings.json");
            var before = File.Exists(file) ? File.ReadAllBytes(file) : null;
            var secrets = new Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = k => secrets.GetValueOrDefault(k);
            CoreSecrets.StoreProvider = (k, v) => secrets[k] = v;
            var service = new ConditioningControlPanel.Services.SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = accepted;
            s.WhatMovedCardShown = 1; // unrelated relocation card already read in these fixtures
            s.LastSeenVersion = last == "current" ? CoreReleaseContent.AppVersion : last;
            foreach (var k in FeatureIntros.All.Keys) if (!s.SeenFeatureIntros.Contains(k)) s.SeenFeatureIntros.Add(k);
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            var clock = (StartupLadder.TestClock)StartupLadder.Time;
            var opened = new List<Window>();
            using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                await body(shell, opened, clock);
            }
            finally
            {
                shell.StopStartupModalsForPanic();
                foreach (var w in opened.Where(w => w != shell).ToArray()) w.Close();
                shell.Close();
                StartupLadder.ResetForTests();
                FeatureIntroPopup.ResetForTests();
                MainShellWindow.IsStartupDialogShowing = false;
                CoreSettings.ServiceProvider = oldSettings;
                CoreTutorial.IsActiveProvider = oldTutorial;
                CoreSecrets.RetrieveProvider = oldGet;
                CoreSecrets.StoreProvider = oldSet;
                if (before != null) File.WriteAllBytes(file, before); else File.Delete(file);
            }
        });

    private static async Task Step(StartupLadder.TestClock clock, double milliseconds)
    {
        clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    [Fact]
    public Task UpgradeHoldsCardsUnspentAndStampsOnlyAfterTheDialogCloses() => Run("previous", async (shell, opened, clock) =>
    {
        CoreSettings.Current.SeenFeatureIntros.Remove("haptics");
        FeatureIntroPopup.ShowWhenStartupSettles("haptics", shell, "play");
        Assert.Empty(opened.OfType<WhatsNewDialog>());
        Assert.False(StartupLadder.Inbox.Contains("intro:haptics")); // merely queued: defer
        await Step(clock, 749);
        Assert.Empty(opened.OfType<WhatsNewDialog>());
        await Step(clock, 1);
        var dialog = Assert.Single(opened.OfType<WhatsNewDialog>());
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        Assert.Equal("previous", CoreSettings.Current.LastSeenVersion);
        Assert.Equal("What's New in v" + CoreReleaseContent.AppVersion, dialog.FindControl<TextBlock>("TxtTitle")!.Text);
        Assert.Equal(CoreReleaseContent.PatchNotes, dialog.FindControl<TextBlock>("TxtNotes")!.Text);
        StartupLadder.PumpHeld();
        Assert.True(StartupLadder.Inbox.Contains("intro:haptics"));
        Assert.DoesNotContain("haptics", CoreSettings.Current.SeenFeatureIntros);
        dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
    });

    [Fact]
    public Task UpgradeNotesWaitForTheExistingAgeGate() => Run("previous", async (_, opened, clock) =>
    {
        await Step(clock, 750);
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        Assert.Empty(opened.OfType<WhatsNewDialog>());
        Assert.Single(opened.OfType<MessageDialog>()).FindControl<Button>("BtnOk")!
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Step(clock, 500);
        Assert.True(CoreSettings.Current.HasAcceptedAgeVerification);
        Assert.Single(opened.OfType<WhatsNewDialog>());
    }, accepted: false);

    [Theory]
    [InlineData("")]
    [InlineData("current")]
    public Task FreshOrCurrentVersionIsSilent(string last) => Run(last, async (_, opened, clock) =>
    {
        await Step(clock, 750);
        Assert.Empty(opened.OfType<WhatsNewDialog>());
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
    });

    [Fact]
    public Task UpgradeTourStartsAfterTheStartupFlagClears() => Run("previous", async (_, opened, clock) =>
    {
        var old = CoreTutorial.StartAction;
        string? tour = null;
        try
        {
            CoreTutorial.StartAction = name => { Assert.False(MainShellWindow.IsStartupDialogShowing); tour = name; };
            await Step(clock, 750);
            Assert.Single(opened.OfType<WhatsNewDialog>()).FindControl<Button>("BtnTour")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.Equal("UpgradeTour", tour);
        }
        finally { CoreTutorial.StartAction = old; }
    });

    [Fact]
    public Task ModalTurnsAreOrderedDeduplicatedAndAwaited() => Run("current", async (shell, _, clock) =>
    {
        var order = new List<string>();
        var release = new TaskCompletionSource();
        StartupLadder.EnqueueModal("later", 50, shell, () => { order.Add("later"); return Task.CompletedTask; });
        StartupLadder.EnqueueModal("first", 30, shell, () => { order.Add("first"); return release.Task; });
        StartupLadder.EnqueueModal("FIRST", 30, shell, () => throw new Exception("duplicate"));
        Dispatcher.UIThread.RunJobs();
        await Step(clock, 750);
        Assert.Equal(new[] { "first" }, order);
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        release.SetResult();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Step(clock, 750);
        Assert.Equal(new[] { "first", "later" }, order);
        Assert.False(MainShellWindow.IsStartupDialogShowing);
    });

    [Fact]
    public Task GameParkingDoesNotSpendTheModalWaitBudget() => Run("current", async (shell, _, clock) =>
    {
        var old = PanicSurfaces.All;
        bool game = true, shown = false;
        try
        {
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("test-game", _ => { }, () => game) };
            StartupLadder.EnqueueModal("parked", 30, shell, () => { shown = true; return Task.CompletedTask; });
            Dispatcher.UIThread.RunJobs();
            await Step(clock, 750);
            for (int i = 0; i < 601; i++) await Step(clock, 500);
            Assert.False(shown);
            game = false;
            await Step(clock, 500);
            Assert.True(shown);
        }
        finally { PanicSurfaces.All = old; }
    });

    /// <summary>A card held behind a waiting modal turn keeps ONE watch (no per-second rebuild), and a panic with no
    /// shell still cancels the queued turn, after which the held card opens.</summary>
    [Fact]
    public Task HeldCardKeepsItsWatchAndShellLessPanicCancelsTheTurn() => Run("current", async (shell, _, clock) =>
    {
        var stop = PanicSurfaces.All.Single(s => s.Id == "startup-modals");
        bool shown = false;
        int opens = 0;
        shell.Hide();   // the turn waits for its owner: the ladder is busy and nothing else owns the user
        StartupLadder.EnqueueModal("waiting", 30, shell, () => { shown = true; return Task.CompletedTask; });
        Dispatcher.UIThread.RunJobs();
        await Step(clock, 750);
        var before = clock.Timers.ToList();
        StartupLadder.PresentOrInbox(new InboxItem { Key = "held", Open = () => opens++ });
        var watch = Assert.Single(clock.Timers.Except(before), t => t.Period > TimeSpan.Zero);
        for (int i = 0; i < 6; i++) await Step(clock, 500);
        Assert.Contains(watch, clock.Timers);
        Assert.Equal(0, opens);
        Assert.Equal(0, StartupLadder.Inbox.UnreadCount);
        stop.Stop(null);
        Assert.False(StartupLadder.World(clock.Now.UtcDateTime).LadderBusy);
        await Step(clock, 1000);
        Assert.Equal(1, opens);
        Assert.False(shown);
    });

    [Fact]
    public Task LauncherParksTheQueueUntilThePanelReturns() => Run("previous", async (shell, opened, clock) =>
    {
        var launcher = LauncherWindow.OpenFor(shell);
        shell.Hide();
        await Step(clock, 750);
        Assert.True(StartupLadder.World(clock.Now.UtcDateTime).LauncherHolding);
        for (int i = 0; i < 601; i++) await Step(clock, 500);
        Assert.Empty(opened.OfType<WhatsNewDialog>());
        // The launcher's exit fade runs on the real frame clock; motion Off hands over at once (AfterExitBeat).
        CoreSettings.Current.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Off;
        launcher.OpenPanel();
        await Step(clock, 500);
        Assert.Single(opened.OfType<WhatsNewDialog>());
    });

    [Fact]
    public Task HiddenShellTimesOutWithoutSpendingTheVersion() => Run("previous", async (shell, opened, clock) =>
    {
        shell.Hide();
        await Step(clock, 750);
        for (int i = 0; i < 600; i++) await Step(clock, 500);
        shell.Show();
        await Step(clock, 1000);
        Assert.Empty(opened.OfType<WhatsNewDialog>());
        Assert.Equal("previous", CoreSettings.Current.LastSeenVersion);
        Assert.False(StartupLadder.IsQuiet);
    });

    [Fact]
    public Task EmptyMediaSitesShareAnUnspentInboxOfferAndPanicClosesTheCard() => Run("current", async (shell, opened, clock) =>
    {
        CoreSettings.Current.MediaSource = "local";
        CoreSettings.Current.SeenFeatureIntros.Remove("remotemedia");
        MainShellWindow.IsStartupDialogShowing = true;
        FeatureIntroPopup.OfferRemoteMediaSource(shell);
        await Step(clock, 500);
        Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        MainShellWindow.IsStartupDialogShowing = false;
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        await Step(clock, 500);
        FeatureIntroPopup.OfferRemoteMediaSource(shell); // another empty-media site: same launch budget
        Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "intro:remote-media"));
        Assert.DoesNotContain("remotemedia", CoreSettings.Current.SeenFeatureIntros);
        StartupLadder.Inbox.Open(StartupLadder.Inbox.Items.Single(i => i.Key == "intro:remote-media"));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(opened.OfType<FeatureIntroPopup>());
        Assert.Contains("remotemedia", CoreSettings.Current.SeenFeatureIntros);
        FeatureIntroPopup.OfferRemoteMediaSource(shell);
        Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
        PanicSurfaces.All.Single(s => s.Id == "feature-intros").Stop(shell);
        Assert.All(opened.OfType<FeatureIntroPopup>(), w => Assert.False(w.IsVisible));
    });

    [Fact]
    public Task PanicCancelsAnIntroQueuedBeforeItsWindowOpens() => Run("current", (shell, opened, _) =>
    {
        CoreSettings.Current.SeenFeatureIntros.Remove("haptics");
        FeatureIntroPopup.ShowIfFirstTime("haptics", shell);
        PanicSurfaces.All.Single(s => s.Id == "feature-intros").Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("haptics", CoreSettings.Current.SeenFeatureIntros);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmptyMediaOfferDoesNotRecommendTheSourceAlreadyInUse() => Run("current", (_, opened, _) =>
    {
        CoreSettings.Current.MediaSource = "remote";
        FeatureIntroPopup.OfferRemoteMediaSource();
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicClosesTheNewStartupSurfaceAndCancelsQueuedTurns() => Run("previous", async (shell, opened, clock) =>
    {
        await Step(clock, 750);
        Assert.True(Assert.Single(opened.OfType<WhatsNewDialog>()).IsVisible);
        Assert.Contains(PanicSurfaces.All, s => s.Id == "startup-modals");
        PanicSurfaces.All.Single(s => s.Id == "startup-modals").Stop(shell);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.All(opened.OfType<WhatsNewDialog>(), w => Assert.False(w.IsVisible));
        await Step(clock, 1000);
        Assert.False(MainShellWindow.IsStartupDialogShowing);
    });
}

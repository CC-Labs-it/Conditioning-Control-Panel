using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class FeatureIntroUpgradeTests
{
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public Task FreshAndCurrentVersionsStaySilent(string? previous) => Run(previous, async shell =>
    {
        await shell.ShowWhatsNewIfNeeded();
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
    });
    [Fact]
    public Task ShellOpenedStampsVersionlessWelcomedProfileWithoutNotes() => Run("", shell =>
    {
        CoreReleaseContent.AppVersionProvider = () => "9.8.7";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("9.8.7", CoreSettings.Current.LastSeenVersion);
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        return Task.CompletedTask;
    }, detachStartup: false);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MissingNotesStaySilentWithoutSpendingCancelledUpgrade(bool cancelled) => Run("old-version", async shell =>
    {
        CoreReleaseContent.PatchNotesProvider = () => "";
        if (cancelled) MainShellWindow.StopUpgradeIntroForPanic(shell);
        await shell.ShowWhatsNewIfNeeded();
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal(cancelled ? "old-version" : CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
        Assert.False(StartupLadder.ModalPending);
        Assert.False(LadderBusy());
    });
    [Fact]
    public Task UpgradeHoldsCardsAndStampsOnlyAfterDismissal() => Run("old-version", async shell =>
    {
        var showing = shell.ShowWhatsNewIfNeeded();
        await Step();
        var news = Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        Assert.Equal("old-version", CoreSettings.Current.LastSeenVersion);
        Assert.Equal($"What's New in v{CoreReleaseContent.AppVersion}", news.FindControl<TextBlock>("TxtTitle")!.Text);
        var notes = news.FindControl<TextBlock>("TxtNotes")!.Text;
        Assert.False(string.IsNullOrWhiteSpace(notes));
        Assert.Contains(CoreReleaseContent.AppVersion, notes);
        Assert.Equal(CoreReleaseContent.PatchNotes, notes);
        Assert.True(news.FindControl<Button>("BtnTour")!.IsVisible);
        Assert.Equal("Show me around (60s)", news.FindControl<TextBlock>("TourLabel")!.Text);
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        Assert.True(StartupLadder.Inbox.Contains("intro:lockdown"));
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        news.Close();
        await showing;
        await Step();
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
        await shell.ShowWhatsNewIfNeeded();
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
    });
    [Fact]
    public Task UpgradeTakesPriorityThirtyTurnOnStartupLadder() => Run("old-version", async shell =>
    {
        // WPF Marquee.cs:342 / AppUpdater.cs:201 / FirstRun.cs:62: wizard 20, What's New 30, update 80.
        var order = new List<string>();
        StartupLadder.EnqueueModal("update-available", 80, () => { order.Add("update"); return Task.CompletedTask; });
        var showing = shell.ShowWhatsNewIfNeeded();
        StartupLadder.EnqueueModal("first-run-wizard", 20, () => { order.Add("wizard"); return Task.CompletedTask; });
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        await Step();
        Assert.Equal(new[] { "wizard" }, order);
        await Step();
        var news = Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal(new[] { "wizard" }, order);
        news.Close();
        await showing;
        await Step();
        await Step();
        Assert.Equal(new[] { "wizard", "update" }, order);
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
    });

    [Fact]
    public Task BusyStartupAbandonsUnspent() => Run("old-version", async shell =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        var showing = shell.ShowWhatsNewIfNeeded();
        await Step();
        var clock = (StartupLadder.TestClock)StartupLadder.Time;
        for (var i = 0; i < (int)(StartupLadder.ModalMaxWait / StartupLadder.ModalPoll); i++)
        { clock.Advance(StartupLadder.ModalPoll); for (var j = 0; j < 5; j++) await Task.Yield(); }
        await showing;
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal("old-version", CoreSettings.Current.LastSeenVersion);
        Assert.True(MainShellWindow.IsStartupDialogShowing); // somebody else's flag stays theirs
    });

    [Fact]
    public Task UnacceptedAgeGateLeavesUpgradeUnspent() => Run("old-version", async shell =>
    {
        CoreSettings.Current.HasAcceptedAgeVerification = false;
        var showing = shell.ShowWhatsNewIfNeeded();
        await Step();
        await showing;
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal("old-version", CoreSettings.Current.LastSeenVersion);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ParkedUpgradeStopsOnPanicOrClose(bool close) => Run("old-version", async shell =>
    {
        StartupLadder.Hold();
        CoreSettings.Current.StrictLockEnabled = true;
        CoreSettings.Current.LockdownForceStrictLock = true;
        var showing = shell.ShowWhatsNewIfNeeded();
        await Step();
        if (close) shell.Close();
        else PanicSurfaces.All.Single(s => s.Id == "upgrade-intro").Stop(shell);
        StartupLadder.Release();
        await Step();
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        await showing;
        Assert.False(LadderBusy());
        Assert.Equal("old-version", CoreSettings.Current.LastSeenVersion);
        Assert.False(StartupLadder.ModalPending);
        Assert.True(CoreSettings.Current.StrictLockEnabled);
        Assert.True(CoreSettings.Current.LockdownForceStrictLock);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicClosesUpgradeAndReleasesModalGate(bool tray) => Run("old-version", async shell =>
    {
        var showing = shell.ShowWhatsNewIfNeeded();
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        await Step();
        Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        PanicSurfaces.All.Single(s => s.Id == "upgrade-intro").Stop(tray ? null : shell);
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        await showing;
        await Step();
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(TimeSpan.FromSeconds(5));
        StartupLadder.PumpHeld(); // panic parked the offer instead of presenting it
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(((StartupLadder.TestClock)StartupLadder.Time).Timers);
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        Assert.True(StartupLadder.Inbox.Contains("intro:lockdown"));
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.Equal("old-version", CoreSettings.Current.LastSeenVersion);
        // Re-offering stays deduplicated; the parked card is opened only by the user.
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        StartupLadder.Inbox.Open(Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "intro:lockdown"));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(shell.OwnedWindows.OfType<FeatureIntroPopup>());
    }, desktop: true);
    [Fact]
    public Task RealShellOpenedCallbackReachesUpgradeDialog() => Run("old-version", async shell =>
    {
        await Step();
        await Step();
        var news = Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.True(MainShellWindow.IsStartupDialogShowing);
        news.Close();
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(CoreReleaseContent.AppVersion, CoreSettings.Current.LastSeenVersion);
    }, detachStartup: false);
    [Fact]
    public Task QueuedUpgradeDefersFeatureCardWithoutSpendingIt() => Run("old-version", async shell =>
    {
        var showing = shell.ShowWhatsNewIfNeeded();
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        Assert.False(StartupLadder.Inbox.Contains("intro:lockdown"));
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        Assert.Empty(((StartupLadder.TestClock)StartupLadder.Time).Timers); // WPF modal defer never arms a watch
        await Step();
        var news = Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(TimeSpan.FromSeconds(3));
        Assert.True(LadderBusy());
        Assert.False(StartupLadder.Inbox.Contains("intro:lockdown"));
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        news.Close();
        await showing;
        await Step();
        Assert.False(LadderBusy());
        Assert.Single(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        Assert.Contains("lockdown", CoreSettings.Current.SeenFeatureIntros);
    });
    [Fact]
    public Task PanicWhileWaitingNeverOpensOrSpendsUpgrade() => Run("old-version", async shell =>
    {
        var showing = shell.ShowWhatsNewIfNeeded();
        Dispatcher.UIThread.RunJobs(); // the ladder has selected the notes and is settling
        PanicSurfaces.All.Single(s => s.Id == "upgrade-intro").Stop(shell);
        Assert.False(StartupLadder.ModalPending);
        await showing;
        await Step();
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal("old-version", CoreSettings.Current.LastSeenVersion);
        Assert.False(LadderBusy());
    });
    [Fact]
    public Task CurrentVersionSkipsSettleAndModal() => Run(null, async shell =>
    {
        await shell.ShowWhatsNewIfNeeded();
        Assert.Empty(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.False(LadderBusy());
    });
    [Fact]
    public Task TourButtonStartsUpgradeTourAfterModalCloses() => Run("old-version", async shell =>
    {
        global::ConditioningControlPanel.Avalonia.Tours.TutorialHead.Seed();
        global::ConditioningControlPanel.Avalonia.Tours.TutorialHead.ResetForTests();
        TutorialHeadHooks.ShellProvider = () => shell;
        var showing = shell.ShowWhatsNewIfNeeded();
        await Step();
        var news = Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        news.FindControl<Button>("BtnTour")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await showing;
        await Step();
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        Assert.NotNull(global::ConditioningControlPanel.Avalonia.Tours.TutorialHead.ShellOverlay);
        global::ConditioningControlPanel.Avalonia.Tours.TutorialHead.OnPanic();
    });
    [Theory]
    [InlineData("en", "What's New in v{0}", "Show me around (60s)")]
    [InlineData("de", "Neu in v{0}", "Rundgang starten (60 s)")]
    [InlineData("es", "Novedades de v{0}", "Mostrar recorrido (60 s)")]
    [InlineData("fr", "Nouveautés de la v{0}", "Voir la visite (60 s)")]
    [InlineData("it", "Novità della v{0}", "Mostra il tour (60 s)")]
    [InlineData("ja", "v{0} の新機能", "使い方を見る (60秒)")]
    [InlineData("ko", "v{0}의 새로운 기능", "둘러보기 (60초)")]
    [InlineData("pt-BR", "Novidades da v{0}", "Mostrar o tour (60 s)")]
    [InlineData("ru", "Что нового в v{0}", "Показать обзор (60 с)")]
    [InlineData("zh-CN", "v{0} 版本更新", "查看导览 (60秒)")]
    public Task UpgradeNotesUseSelectedLanguage(string language, string title, string tour) => Run("old-version", async shell =>
    {
        LocalizationManager.Instance.SetLanguage(language);
        var showing = shell.ShowWhatsNewIfNeeded();
        await Step();
        var news = Assert.Single(shell.OwnedWindows.OfType<WhatsNewDialog>());
        Assert.Equal(title.Replace("{0}", CoreReleaseContent.AppVersion), news.FindControl<TextBlock>("TxtTitle")!.Text);
        Assert.Equal(tour, news.FindControl<TextBlock>("TourLabel")!.Text);
        news.Close();
        await showing;
    });

    [Fact]
    public Task PanicDisposesWatchAndInvalidatesQueuedTimerTick() => Run("old-version", async shell =>
    {
        StartupLadder.ModalPending = false; // a passive watch predating the upgrade modal
        StartupLadder.PresentOrInbox(new() { Key = "prime", Open = () => { } });
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        var clock = (StartupLadder.TestClock)StartupLadder.Time;
        var timer = Assert.Single(clock.Timers);
        var dismissed = false;
        var offered = false;
        StartupLadder.PresentOrInbox(new() { Key = "held", Open = () => offered = true, Dismiss = () => dismissed = true });
        var showing = shell.ShowWhatsNewIfNeeded();
        Task.Run(timer.Fire).GetAwaiter().GetResult(); // tick queued, UI thread still occupied
        PanicSurfaces.All.Single(s => s.Id == "upgrade-intro").Stop(shell);
        Assert.Empty(clock.Timers);
        Assert.False(dismissed); // panic is not the user's dismiss action
        await showing;
        Dispatcher.UIThread.RunJobs(); // the cancelled ladder drains before the next offer
        // A new offer after cancellation must not be opened by the stale queued tick.
        var opened = false;
        StartupLadder.PresentOrInbox(new() { Key = "new-offer", Open = () => opened = true });
        clock.Now += TimeSpan.FromSeconds(5); // advance without firing the NEW timer
        Task.Run(timer.Fire).GetAwaiter().GetResult(); // disposed timer callback races with cancellation
        Dispatcher.UIThread.RunJobs();
        Assert.False(opened);
        clock.Advance(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        Assert.True(opened);
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.False(offered);
        StartupLadder.Inbox.Open(Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "held"));
        Assert.True(offered);
        Assert.False(dismissed);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicCancelsAlreadyQueuedCardOpen(bool noShell) => Run(null, shell =>
    {
        Assert.Null(MainShellWindow.Current);
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        var opened = false;
        StartupLadder.ModalPending = true;
        StartupLadder.PresentOrInbox(new() { Key = "held", Open = () => opened = true });
        PanicSurfaces.All.Single(s => s.Id == "upgrade-intro").Stop(noShell ? null : shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("lockdown", CoreSettings.Current.SeenFeatureIntros);
        Assert.False(StartupLadder.ModalPending);
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(TimeSpan.FromSeconds(5));
        Assert.False(opened);
        Assert.True(StartupLadder.Inbox.Contains("held"));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicCancelsOfferAlreadyPostedFromWorker(bool noShell) => Run(null, shell =>
    {
        Assert.Null(MainShellWindow.Current);
        var opened = false;
        Task.Run(() => StartupLadder.PresentOrInbox(new() { Key = "worker", Open = () => opened = true }))
            .GetAwaiter().GetResult();
        PanicSurfaces.All.Single(s => s.Id == "upgrade-intro").Stop(noShell ? null : shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(StartupLadder.ModalPending);
        Assert.Empty(((StartupLadder.TestClock)StartupLadder.Time).Timers);
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(TimeSpan.FromSeconds(5));
        StartupLadder.PumpHeld();
        Dispatcher.UIThread.RunJobs();
        Assert.False(opened);
        Assert.True(StartupLadder.Inbox.Contains("worker"));
        StartupLadder.Inbox.Open(Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "worker"));
        Assert.True(opened);
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicKeepsExistingInboxIntroAvailableWithoutDuplicatingIt() => Run(null, shell =>
    {
        MainShellWindow.IsStartupDialogShowing = true;
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        MainShellWindow.IsStartupDialogShowing = false;
        PanicSurfaces.All.Single(s => s.Id == "upgrade-intro").Stop(shell);
        FeatureIntroPopup.ShowWhenStartupSettles("lockdown", shell, "play");
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        var row = Assert.Single(StartupLadder.Inbox.Items, i => i.Key == "intro:lockdown");
        StartupLadder.Inbox.Open(row);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(shell.OwnedWindows.OfType<FeatureIntroPopup>());
        return Task.CompletedTask;
    });

    private static bool LadderBusy() => StartupLadder.World(StartupLadder.Time.GetUtcNow().UtcDateTime).LadderBusy;

    private static async Task Step()
    {
        Dispatcher.UIThread.RunJobs();
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
        for (var i = 0; i < 5; i++) await Task.Yield();
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Run(string? previous, Func<MainShellWindow, Task> body, bool detachStartup = true, bool desktop = false) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        LocalizationManager.Instance.SetLanguage("en");
        CoreReleaseContent.PatchNotesProvider = () => $"v{CoreReleaseContent.AppVersion} - Test notes";
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.PerformanceMode = true;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.LastSeenVersion = previous ?? CoreReleaseContent.AppVersion;
        s.SeenFeatureIntros.Clear();
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys.Where(k => k != "lockdown"));
        StartupLadder.ResetForTests();
        FeatureIntroPopup.ResetForTests();
        var shell = new MainShellWindow();
        if (detachStartup) shell.Opened -= shell.OnUpgradeShellOpened;
        StartupLadder.ModalWindowReady = () => shell.IsVisible;
        // The initialized headless app forbids the lifetime setter; restore its backing field below.
        var lifetimeField = typeof(Application).GetField("_applicationLifetime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var lifetime = lifetimeField.GetValue(Application.Current);
        if (desktop) lifetimeField.SetValue(Application.Current, new ClassicDesktopStyleApplicationLifetime { MainWindow = shell, ShutdownMode = ShutdownMode.OnExplicitShutdown });
        try { shell.Show(); await body(shell); }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            MainShellWindow.StopUpgradeIntroForPanic(shell);
            shell.Close();
            lifetimeField.SetValue(Application.Current, lifetime);
            MainShellWindow.IsStartupDialogShowing = false;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = old;
            LocalizationManager.Instance.SetLanguage(oldLanguage);
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
    });
}

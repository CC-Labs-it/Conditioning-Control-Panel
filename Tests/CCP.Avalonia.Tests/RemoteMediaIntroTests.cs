using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;
using HeadApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class RemoteMediaIntroTests
{
    private static Task Run(bool filtered, string source, Action<List<Window>> check, bool startupModal = false, bool updateModal = false, string? language = null, bool makeOffer = true, bool quiet = true) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (oldSource, min, max) = (s.MediaSource, s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds);
        var oldSeen = s.SeenFeatureIntros.ToList();
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        var overlay = MandatoryVideoOverlay.Instance;
        var oldScheduler = overlay.Scheduler;
        var owner = new Window();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        try
        {
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            HeadApp.ResetRemoteMediaOfferForTests();
            if (quiet) StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            s.MediaSource = source;
            if (language != null) LocalizationManager.Instance.Initialize(language);
            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (filtered ? 500 : 0, 0);
            s.SeenFeatureIntros.Remove("remotemedia");
            owner.Show();
            if (startupModal)
            {
                var present = typeof(WelcomeBackOffer).GetMethod("PresentOnShellAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                _ = (Task)present.Invoke(null, new object[] { new WelcomeBackSheetContent() })!;
                Assert.True(MainShellWindow.IsStartupDialogShowing);
            }
            if (updateModal)
            {
                var method = typeof(AppUpdater).GetMethod("OfferAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                _ = (Task)method.Invoke(null, new object[] { owner, new UpdateInfo { Version = "99.1.0" } })!;
                Assert.True(AppUpdater.IsUpdateDialogActive);
            }
            overlay.NoVideosOwnerForTests = owner;
            overlay.Scheduler = new MandatoryVideoScheduler(overlay,
                library: () => filtered ? new[] { "/v/short.mp4" } : Array.Empty<string>()) { DurationOf = _ => 20 };
            Assert.False(overlay.Scheduler.Trigger());
            Dispatcher.UIThread.RunJobs();
            var message = Assert.Single(opened.OfType<MessageDialog>());
            Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
            Assert.Empty(opened.OfType<FeatureIntroPopup>());
            message.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            // Empty video guidance must not advertise online video until this head can play it.
            Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
            Assert.Empty(opened.OfType<FeatureIntroPopup>());
            Assert.DoesNotContain("remotemedia", s.SeenFeatureIntros);
            // Exercise the retained offer service separately for a future supported producer.
            if (makeOffer && !filtered)
            {
                HeadApp.OfferRemoteMediaSource(owner);
                Dispatcher.UIThread.RunJobs();
            }
            check(opened);
        }
        finally
        {
            HeadApp.ResetRemoteMediaOfferForTests();
            foreach (var w in opened.Where(w => w != owner).ToArray()) w.Close();
            owner.Close();
            MainShellWindow.IsStartupDialogShowing = false;
            overlay.NoVideosOwnerForTests = null;
            overlay.Scheduler = oldScheduler;
            (s.MediaSource, s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (oldSource, min, max);
            s.SeenFeatureIntros.Clear();
            s.SeenFeatureIntros.AddRange(oldSeen);
            if (language != null) LocalizationManager.Instance.Initialize(oldLanguage);
            HeadApp.ResetRemoteMediaOfferForTests();
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task EmptyVideoGuidanceDoesNotOfferUnsupportedOnlineMedia(bool quiet) => Run(false, "local", opened =>
    {
        Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("remotemedia", CoreSettings.Current.SeenFeatureIntros);
    }, makeOffer: false, quiet: quiet);

    // The offer is invoked explicitly by Run until a producer can play remote videos.
    [Fact]
    public Task EmptyLocalLibraryOffersAfterDialogAndClaimsOnlyOnOpen() => Run(false, "local", opened =>
    {
        var row = Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "intro:remote-media"));
        Assert.DoesNotContain("remotemedia", CoreSettings.Current.SeenFeatureIntros);
        StartupLadder.Inbox.Open(row);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(opened.OfType<FeatureIntroPopup>());
        Assert.Contains("remotemedia", CoreSettings.Current.SeenFeatureIntros);
        // A second dead end can file a row, but cannot open a second card this launch.
        opened.OfType<FeatureIntroPopup>().Single().Close();
        Dispatcher.UIThread.RunJobs();
        FeatureIntroPopup.ResetForTests();
        CoreSettings.Current.SeenFeatureIntros.Remove("remotemedia");
        HeadApp.OfferRemoteMediaSource(opened.First());
        StartupLadder.Inbox.Open(Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "intro:remote-media")));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(opened.OfType<FeatureIntroPopup>());
    });

    [Fact]
    public Task StartupModalDoesNotOpenOrSpendTheOffer() => Run(false, "local", opened =>
    {
        Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
        Assert.DoesNotContain("remotemedia", CoreSettings.Current.SeenFeatureIntros);
        Assert.Single(opened.OfType<WelcomeBackSheet>()).Commit();
        Dispatcher.UIThread.RunJobs();
        Assert.False(MainShellWindow.IsStartupDialogShowing);
        Assert.True(StartupLadder.Inbox.Contains("intro:remote-media"));
        Assert.DoesNotContain("remotemedia", CoreSettings.Current.SeenFeatureIntros);
    }, startupModal: true);

    [Fact]
    public Task PendingOfferWaitsForBothModalsToClose() => Run(false, "local", opened =>
    {
        Assert.Single(opened.OfType<WelcomeBackSheet>()).Commit();
        Dispatcher.UIThread.RunJobs();
        Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
        AppUpdater.OpenDialog!.FindControl<Button>("BtnLater")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.False(AppUpdater.IsUpdateDialogActive);
        Assert.True(StartupLadder.Inbox.Contains("intro:remote-media"));
    }, startupModal: true, updateModal: true);

    [Fact]
    public Task PendingOfferRechecksTheSourceAfterDismissal() => Run(false, "local", opened =>
    {
        CoreSettings.Current.MediaSource = "mixed";
        Assert.Single(opened.OfType<WelcomeBackSheet>()).Commit();
        Dispatcher.UIThread.RunJobs();
        Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
        Assert.DoesNotContain("remotemedia", CoreSettings.Current.SeenFeatureIntros);
    }, startupModal: true);

    [Fact]
    public Task InboxUsesTheSelectedLanguage() => Run(false, "local", _ =>
    {
        var row = Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "intro:remote-media"));
        Assert.Equal("Contenido sin descargar", row.Title);
        Assert.Equal("Tus carpetas están vacías. Ella puede reproducir contenido en línea.", row.Summary);
    }, language: "es");

    [Theory]
    [InlineData(true, "local")]
    [InlineData(false, "mixed")]
    public Task FilteredClipsAndRemoteSourceDoNotOffer(bool filtered, string source) => Run(filtered, source, opened =>
    {
        Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
        Assert.Empty(opened.OfType<FeatureIntroPopup>());
        Assert.DoesNotContain("remotemedia", CoreSettings.Current.SeenFeatureIntros);
    });
}

using System;
using System.Collections.Generic;
using System.Linq;
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
using Xunit;
using HeadApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class RemoteMediaIntroTests
{
    private static Task Run(bool filtered, string source, Action<List<Window>> check, bool startupModal = false) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (oldSource, min, max) = (s.MediaSource, s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds);
        var oldSeen = s.SeenFeatureIntros.ToList();
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
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            s.MediaSource = source;
            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (filtered ? 500 : 0, 0);
            s.SeenFeatureIntros.Remove("remotemedia");
            owner.Show();
            overlay.NoVideosOwnerForTests = owner;
            overlay.Scheduler = new MandatoryVideoScheduler(overlay,
                library: () => filtered ? new[] { "/v/short.mp4" } : Array.Empty<string>()) { DurationOf = _ => 20 };
            Assert.False(overlay.Scheduler.Trigger());
            Dispatcher.UIThread.RunJobs();
            var message = Assert.Single(opened.OfType<MessageDialog>());
            Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
            Assert.Empty(opened.OfType<FeatureIntroPopup>());
            MainShellWindow.IsStartupDialogShowing = startupModal;
            message.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            check(opened);
        }
        finally
        {
            foreach (var w in opened.Where(w => w != owner).ToArray()) w.Close();
            owner.Close();
            MainShellWindow.IsStartupDialogShowing = false;
            overlay.NoVideosOwnerForTests = null;
            overlay.Scheduler = oldScheduler;
            (s.MediaSource, s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (oldSource, min, max);
            s.SeenFeatureIntros.Clear();
            s.SeenFeatureIntros.AddRange(oldSeen);
            HeadApp.ResetRemoteMediaOfferForTests();
            StartupLadder.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

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
        MainShellWindow.IsStartupDialogShowing = false;
        HeadApp.OfferRemoteMediaSource(opened.First());
        Assert.True(StartupLadder.Inbox.Contains("intro:remote-media"));
    }, startupModal: true);

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

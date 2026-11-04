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

namespace CCP.Avalonia.Tests;

/// <summary>WPF offers remote media after the empty-video guidance (VideoService.cs:2469) because it
/// can play online clips (:7973). The offer must wait until the guidance closes.</summary>
public sealed class RemoteMediaIntroTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task EmptyVideoGuidanceOffersOnlineMediaAfterClose(bool quiet) => AvaloniaTestDispatcher.RunAsync(() =>
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
            RemoteMediaOffer.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            if (quiet) StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            s.MediaSource = "local";
            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (0, 0);
            s.SeenFeatureIntros.Remove("remotemedia");
            owner.Show();
            overlay.NoVideosOwnerForTests = owner;
            overlay.Scheduler = new MandatoryVideoScheduler(overlay, library: () => Array.Empty<string>());
            Assert.False(overlay.Scheduler.Trigger());
            Dispatcher.UIThread.RunJobs();
            var message = Assert.Single(opened.OfType<MessageDialog>());
            Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
            Assert.Empty(opened.OfType<FeatureIntroPopup>());
            message.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            if (quiet)
            {
                Assert.True(StartupLadder.Inbox.Contains("intro:remote-media"));
                Assert.Empty(opened.OfType<FeatureIntroPopup>());
            }
            else Assert.Single(opened.OfType<FeatureIntroPopup>());
        }
        finally
        {
            foreach (var w in opened.Where(w => w != owner).ToArray()) w.Close();
            owner.Close();
            overlay.NoVideosOwnerForTests = null;
            overlay.Scheduler = oldScheduler;
            (s.MediaSource, s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (oldSource, min, max);
            s.SeenFeatureIntros.Clear();
            s.SeenFeatureIntros.AddRange(oldSeen);
            StartupLadder.ResetForTests();
            RemoteMediaOffer.ResetForTests();
            FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task LengthFilterDoesNotOfferAnotherSource() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (source, min, max) = (s.MediaSource, s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds);
        var overlay = MandatoryVideoOverlay.Instance;
        var oldScheduler = overlay.Scheduler;
        var owner = new Window();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        try
        {
            StartupLadder.ResetForTests(); RemoteMediaOffer.ResetForTests(); FeatureIntroPopup.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            s.MediaSource = "local"; s.VideoMinDurationSeconds = 10; s.VideoMaxDurationSeconds = 0;
            owner.Show(); overlay.NoVideosOwnerForTests = owner;
            overlay.Scheduler = new MandatoryVideoScheduler(overlay, library: () => new[] { "short.mp4" }) { DurationOf = _ => 2 };
            Assert.False(overlay.Scheduler.Trigger());
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<MessageDialog>()).FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
            Assert.Empty(opened.OfType<FeatureIntroPopup>());
        }
        finally
        {
            foreach (var w in opened.Where(w => w != owner).ToArray()) w.Close();
            owner.Close(); overlay.NoVideosOwnerForTests = null; overlay.Scheduler = oldScheduler;
            (s.MediaSource, s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (source, min, max);
            StartupLadder.ResetForTests(); RemoteMediaOffer.ResetForTests(); FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ModalDefersOfferAndOpeningClaimsLaunchBudget() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var source = s.MediaSource; var seen = s.SeenFeatureIntros.ToList();
        var owner = new Window(); var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        try
        {
            StartupLadder.ResetForTests(); RemoteMediaOffer.ResetForTests(); FeatureIntroPopup.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            s.MediaSource = "local"; s.SeenFeatureIntros.Remove("remotemedia"); owner.Show();
            MainShellWindow.IsStartupDialogShowing = true;
            RemoteMediaOffer.Offer(owner); RemoteMediaOffer.Offer(owner); RemoteMediaOffer.FlushPending();
            Assert.Empty(StartupLadder.Inbox.Items);
            MainShellWindow.IsStartupDialogShowing = false;
            RemoteMediaOffer.FlushPending();
            var item = Assert.Single(StartupLadder.Inbox.Items);
            Assert.DoesNotContain("remotemedia", s.SeenFeatureIntros);
            StartupLadder.Inbox.Open(item);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<FeatureIntroPopup>());
            RemoteMediaOffer.Offer(owner);
            Assert.Empty(StartupLadder.Inbox.Items);
        }
        finally
        {
            MainShellWindow.IsStartupDialogShowing = false;
            foreach (var w in opened.Where(w => w != owner).ToArray()) w.Close();
            owner.Close(); s.MediaSource = source; s.SeenFeatureIntros.Clear(); s.SeenFeatureIntros.AddRange(seen);
            StartupLadder.ResetForTests(); RemoteMediaOffer.ResetForTests(); FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

}

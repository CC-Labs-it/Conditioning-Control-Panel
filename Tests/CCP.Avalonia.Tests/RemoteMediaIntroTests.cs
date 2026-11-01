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
/// can play online clips (:7973). This head cannot yet, so the guidance must stand alone.</summary>
public sealed class RemoteMediaIntroTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task EmptyVideoGuidanceDoesNotOfferUnsupportedOnlineMedia(bool quiet) => AvaloniaTestDispatcher.RunAsync(() =>
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
            message.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(StartupLadder.Inbox.Contains("intro:remote-media"));
            Assert.Empty(opened.OfType<FeatureIntroPopup>());
            Assert.DoesNotContain("remotemedia", s.SeenFeatureIntros);
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
            FeatureIntroPopup.ResetForTests();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });
}

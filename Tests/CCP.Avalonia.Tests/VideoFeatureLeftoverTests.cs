using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>wire-feat-video-b: the Video card's mod-aware art plates, the Test button's error
/// dialog and the no-videos dialog's remote-media offer (WPF VideoFeatureControl.xaml.cs,
/// VideoService.cs:2486-2495, App.OfferRemoteMediaSource).</summary>
public sealed class VideoFeatureLeftoverTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task The_hero_and_side_plates_carry_the_feature_art()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var card = new VideoFeatureControl();
            var owner = new Window { Content = card };
            owner.Show();
            try
            {
                var hero = Assert.IsType<ImageBrush>(card.FindControl<Border>("HeroArt")!.Background);
                Assert.NotNull(hero.Source);
                Assert.Equal(0.9, hero.Opacity, 3);
                Assert.Equal(AlignmentX.Right, hero.AlignmentX);
                var side = Assert.IsType<ImageBrush>(card.FindControl<Border>("SideArt")!.Background);
                Assert.NotNull(side.Source);
            }
            finally { owner.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_Test_click_that_throws_tells_the_user()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var old = CoreEngine.Video;
            var scheduler = new MandatoryVideoScheduler(new Host(), library: () => throw new IOException("disk gone"));
            CoreEngine.Video = scheduler;
            var card = new VideoFeatureControl();
            var owner = new Window { Content = card };
            owner.Show();
            try
            {
                card.FindControl<Button>("BtnTestVideo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal("Error", dialog.Title);
                Assert.Equal("Error triggering video: disk gone", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
                dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await card.TestVideoRun;
            }
            finally { owner.Close(); CoreEngine.Video = old; }
        });
    }

    [Theory]
    [InlineData(false, 1)]   // empty library: offer the online source after the box closes
    [InlineData(true, 0)]    // the length filter emptied it: another source is the wrong fix
    public async Task The_no_videos_box_offers_remote_media_unless_the_length_filter_caused_it(bool filterEmptied, int offers)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (oldSource, oldMin, oldShow) = (s.MediaSource, s.VideoMinDurationSeconds, RemoteMediaOffer.ShowCard);
            var opened = 0;
            RemoteMediaOffer.ResetForTests();
            RemoteMediaOffer.ShowCard = _ => opened++;
            s.MediaSource = "local";
            s.VideoMinDurationSeconds = filterEmptied ? 100 : 0;
            IReadOnlyList<string> lib = filterEmptied ? new[] { "/x/clip.mp4" } : Array.Empty<string>();
            var scheduler = new MandatoryVideoScheduler(new Host(), library: () => lib) { DurationOf = _ => 5 };
            var owner = new Window();
            owner.Show();
            try
            {
                Assert.False(scheduler.Trigger());   // runs the funnel, nothing to play
                // Read at trigger time (WPF VideoService.cs:2474), then let the funnel flip before the box shows.
                var atTrigger = NoVideosReason.LengthFilterEmptied(scheduler.LastFunnelEnabled, scheduler.LastFunnelDuration);
                s.VideoMinDurationSeconds = filterEmptied ? 0 : 100;
                scheduler.Trigger();
                var run = MandatoryVideoOverlay.ShowNoVideosAsync(owner, scheduler, atTrigger);
                Dispatcher.UIThread.RunJobs();
                var dialog = Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal(0, opened);   // never before the box closes
                dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await run;
                Dispatcher.UIThread.RunJobs();
                if (StartupLadder.Inbox.Items.FirstOrDefault(i => i.Key == "intro:remote-media") is { } row)
                    StartupLadder.Inbox.Open(row);
                Assert.Equal(offers, opened);
            }
            finally
            {
                owner.Close();
                StartupLadder.Inbox.Remove("intro:remote-media");
                s.MediaSource = oldSource;
                s.VideoMinDurationSeconds = oldMin;
                RemoteMediaOffer.ShowCard = oldShow;
                RemoteMediaOffer.ResetForTests();
            }
        });
    }

    private sealed class Host : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }
}

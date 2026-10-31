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

/// <summary>WPF VideoService.cs:2448-2469: offer after guidance closes, except for a length-filter miss.</summary>
public sealed class NoVideosRemoteOfferTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
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

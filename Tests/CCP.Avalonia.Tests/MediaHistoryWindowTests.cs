using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using App = ConditioningControlPanel.Avalonia.App;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>win-media-history: the Media Log reads the real shared log (not placeholders), filters
/// audio, plans each preview like WPF, follows live entries and clears behind a confirm.</summary>
public sealed class MediaHistoryWindowTests
{
    [Fact]
    public Task MediaLog_shows_the_real_log_filters_previews_follows_and_clears() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-mhw-").FullName;
        var png = Path.Combine(dir, "still.png");
        using (var rt = new RenderTargetBitmap(new PixelSize(8, 8))) rt.Save(png);
        File.WriteAllBytes(Path.Combine(dir, "drain.mp3"), new byte[] { 0xFF, 0xFB });   // present: a missing clip is "file not found" (WPF order)
        var history = new MediaHistoryService(Path.Combine(dir, "media_history.json"));
        var before = App.MediaHistory;
        MediaHistoryWindow? w = null;
        try
        {
            App.MediaHistory = history;
            history.RecordImages(new[] { png });
            history.RecordVideo(Path.Combine(dir, "gone.mp4"));
            history.RecordAudio(Path.Combine(dir, "drain.mp3"));
            history.RecordImages(new[] { "https://cdn.example.com/x.jpg" });

            w = new MediaHistoryWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var list = w.FindControl<ListBox>("MediaList")!;
            MediaHistoryRow Row(int i) => (MediaHistoryRow)list.Items[i]!;
            string Text(string name) => w.FindControl<TextBlock>(name)!.Text ?? "";
            bool Shown(string name) => w.FindControl<Control>(name)!.IsVisible;
            void Click(string name) { w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

            Assert.Equal(4, list.ItemCount);                                  // the log, newest first
            Assert.Equal("https://cdn.example.com/x.jpg", Row(0).Entry.FilePath);
            Assert.Equal(Loc.GetF("label_media_entry_count", 4), Text("TxtCount"));

            Click("BtnFilterAudio");
            Assert.Equal(1, list.ItemCount);
            Assert.Equal(MediaType.Audio, Row(0).Entry.Type);
            Assert.Equal(Loc.GetF("label_media_entry_count_filtered", 1, 4), Text("TxtCount"));
            list.SelectedIndex = 0;
            Assert.Equal(Loc.Get("label_media_audio_preview"), Text("PreviewMissing"));   // never decoded as a picture

            Click("BtnFilterAll");
            list.SelectedIndex = 0;                                          // online entry
            Assert.Equal(Loc.Get("label_media_streamed_only"), Text("PreviewMissing"));
            Assert.True(Shown("BtnPreviewCopyLink"));
            Assert.True(Shown("BtnPreviewOpenSource"));
            Assert.False(Shown("BtnPreviewOpenFolder"));
            Assert.False(Row(0).FolderButtonVisible);

            list.SelectedIndex = 2;                                          // a video whose file is gone
            Assert.Equal(Loc.Get("label_file_not_found"), Text("PreviewMissing"));
            Assert.False(Shown("BtnPreviewCopyLink"));
            Assert.True(Shown("BtnPreviewOpenFolder"));

            list.SelectedIndex = 3;                                          // the real still
            Assert.True(Shown("PreviewImage"));
            Assert.NotNull(w.FindControl<Image>("PreviewImage")!.Source);
            Assert.NotNull(Row(3).Thumb);
            Assert.Null(Row(2).Thumb);                                       // videos: glyph, no decode

            history.RecordVideo(Path.Combine(dir, "live.mp4"));              // a clip plays while open
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, list.ItemCount);
            Assert.EndsWith("live.mp4", Row(0).Entry.FilePath);

            Click("BtnClear");
            var dialog = Assert.Single(w.OwnedWindows.OfType<MessageDialog>());
            dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, history.Count);
            Assert.True(Shown("TxtEmpty"));
            Assert.False(Shown("MediaList"));

            w.Close();
            Dispatcher.UIThread.RunJobs();
            history.RecordAudio(Path.Combine(dir, "after.mp3"));              // a closed window no longer listens
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, list.ItemCount);
        }
        finally
        {
            w?.Close();
            App.MediaHistory = before;
            history.Dispose();
            Dispatcher.UIThread.RunJobs();
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF ExplorerLauncher (the file selected; its folder when gone or when the desktop cannot
    /// select) and WPF Load preview (only with remote-media consent, only stills, retry after a failed draw).</summary>
    [Fact]
    public Task MediaLog_reveals_the_file_and_loads_an_online_still_only_with_consent() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-mhw-").FullName;
        var png = Path.Combine(dir, "still.png");
        using (var rt = new RenderTargetBitmap(new PixelSize(8, 8))) rt.Save(png);
        var history = new MediaHistoryService(Path.Combine(dir, "media_history.json"));
        var s = ConditioningControlPanel.CoreSettings.Current;
        var (before, source, consent) = (App.MediaHistory, s.MediaSource, s.RemoteMediaConsented);
        var (shell, select, fetch) = (ExternalOpener.Shell, ExternalOpener.SelectInFileManager, MediaHistoryWindow.FetchRemote);
        var opened = new System.Collections.Generic.List<string>();
        var selected = new System.Collections.Generic.List<string>();
        var selects = true;
        byte[]? served = null;
        MediaHistoryWindow? w = null;
        try
        {
            ExternalOpener.Shell = t => { opened.Add(t); return true; };
            ExternalOpener.SelectInFileManager = p => { selected.Add(p); return selects; };
            MediaHistoryWindow.FetchRemote = (_, _) => Task.FromResult(served);
            App.MediaHistory = history;
            history.RecordImages(new[] { png });
            history.RecordVideo(Path.Combine(dir, "gone.mp4"));
            history.RecordVideo("https://cdn.example.com/v.mp4");
            history.RecordImages(new[] { "https://cdn.example.com/x.jpg" });
            (s.MediaSource, s.RemoteMediaConsented) = ("mixed", false);

            w = new MediaHistoryWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var list = w.FindControl<ListBox>("MediaList")!;
            bool Shown(string name) => w.FindControl<Control>(name)!.IsVisible;
            string Missing() => w.FindControl<TextBlock>("PreviewMissing")!.Text ?? "";
            void Click(string name) { w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            void Press()                                                     // the user's click; pumps until the off-thread decode lands
            {
                Click("BtnPreviewLoad");
                var sw = System.Diagnostics.Stopwatch.StartNew();           // P73: bounded by time, not turns
                while (Shown("PreviewMissing") && Missing() == Loc.Get("label_media_loading_preview") && sw.Elapsed < System.TimeSpan.FromSeconds(10))
                    Dispatcher.UIThread.RunJobs();
            }

            list.SelectedIndex = 3;                                          // the still on disk: selected, not just its folder
            Click("BtnPreviewOpenFolder");
            Assert.Equal(new[] { png }, selected);
            Assert.Empty(opened);
            selects = false;                                                 // no FileManager1 on this desktop
            Click("BtnPreviewOpenFolder");
            Assert.Equal(new[] { dir }, opened);
            list.SelectedIndex = 2;                                          // the clip is gone: its folder (#998)
            Click("BtnPreviewOpenFolder");
            Assert.Equal(2, selected.Count);
            Assert.Equal(new[] { dir, dir }, opened);

            list.SelectedIndex = 0;                                          // online still, no consent: no fetch button
            Assert.False(Shown("BtnPreviewLoad"));
            s.RemoteMediaConsented = true;
            list.SelectedIndex = 1;                                          // an online clip never gets one
            Assert.False(Shown("BtnPreviewLoad"));
            list.SelectedIndex = 0;
            Assert.True(Shown("BtnPreviewLoad"));
            Assert.Equal(Loc.Get("label_media_streamed_only"), Missing());

            served = new byte[] { 1, 2, 3 };                                  // bytes came, none readable
            Press();
            Assert.Equal(Loc.Get("label_no_preview"), Missing());
            Assert.True(Shown("BtnPreviewLoad"));                            // the retry is back

            served = File.ReadAllBytes(png);
            Press();
            Assert.True(Shown("PreviewImage"));
            Assert.NotNull(w.FindControl<Image>("PreviewImage")!.Source);
            Assert.False(Shown("PreviewMissing"));
            Assert.False(Shown("BtnPreviewLoad"));

            s.RemoteMediaConsented = false;                                  // consent withdrawn: the press is refused too
            list.SelectedIndex = 1;
            list.SelectedIndex = 0;
            served = File.ReadAllBytes(png);
            Press();
            Assert.False(Shown("PreviewImage"));
        }
        finally
        {
            w?.Close();
            (ExternalOpener.Shell, ExternalOpener.SelectInFileManager, MediaHistoryWindow.FetchRemote) = (shell, select, fetch);
            (App.MediaHistory, s.MediaSource, s.RemoteMediaConsented) = (before, source, consent);
            history.Dispose();
            Dispatcher.UIThread.RunJobs();
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public async Task Online_still_fetch_follows_redirects_by_hand_and_stops_at_five_hops()
    {
        var hops = new System.Collections.Generic.List<string>();
        int redirects = 2;
        Task<System.Net.Http.HttpResponseMessage> Get(System.Uri u, System.Threading.CancellationToken _)
        {
            hops.Add(u.ToString());
            if (hops.Count <= redirects)
            {
                var r = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Found);
                r.Headers.Location = hops.Count == 1 ? new System.Uri("https://cdn2.example.com/a.jpg") : new System.Uri("/b.jpg", System.UriKind.Relative);
                return Task.FromResult(r);
            }
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.ByteArrayContent(new byte[] { 7, 8 }) });
        }
        var bytes = await MediaHistoryWindow.FetchFollowingRedirectsAsync(Get, "https://cdn.example.com/x.jpg", default);
        Assert.Equal(new byte[] { 7, 8 }, bytes);
        Assert.Equal(new[] { "https://cdn.example.com/x.jpg", "https://cdn2.example.com/a.jpg", "https://cdn2.example.com/b.jpg" }, hops);

        (hops, redirects) = (new(), 99);                                    // a redirect loop gives up, never spins
        Assert.Null(await MediaHistoryWindow.FetchFollowingRedirectsAsync(Get, "https://cdn.example.com/x.jpg", default));
        Assert.Equal(6, hops.Count);

        hops = new();                                                       // https -> http is refused, as .NET's auto-redirect does
        Task<System.Net.Http.HttpResponseMessage> Downgrade(System.Uri u, System.Threading.CancellationToken _)
        {
            hops.Add(u.ToString());
            var r = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.Found);
            r.Headers.Location = new System.Uri("http://cdn.example.com/x.jpg");
            return Task.FromResult(r);
        }
        Assert.Null(await MediaHistoryWindow.FetchFollowingRedirectsAsync(Downgrade, "https://cdn.example.com/x.jpg", default));
        Assert.Single(hops);
    }

    /// <summary>WPF MediaHistoryWindow.xaml.cs:35/192/394: "via Scrolller" sits on the preview of a Scrolller
    /// row, never on a local or other online row, and goes when nothing is selected.</summary>
    [Fact]
    public Task Scrolller_rows_carry_the_via_Scrolller_mark() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-mhw-").FullName;
        var history = new MediaHistoryService(Path.Combine(dir, "media_history.json"));
        var before = App.MediaHistory;
        MediaHistoryWindow? w = null;
        try
        {
            App.MediaHistory = history;
            history.RecordImages(new[] { Path.Combine(dir, "local.png") });
            history.RecordImages(new[] { "https://cdn.example.com/x.jpg" });
            history.RecordImages(new[] { "https://images.scrolller.com/abc.jpg" });
            w = new MediaHistoryWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var list = w.FindControl<ListBox>("MediaList")!;
            Assert.Same(w.FindControl<Image>("PreviewImage")!.Parent, w.Credit.Parent);   // on the preview, as WPF

            list.SelectedIndex = 0;                                          // the Scrolller picture (newest)
            Assert.True(w.Credit.Shown);
            list.SelectedIndex = 1;                                          // another site
            Assert.False(w.Credit.Shown);
            list.SelectedIndex = 0;
            list.SelectedIndex = 2;                                          // a local file
            Assert.False(w.Credit.Shown);
            list.SelectedIndex = 0;
            list.SelectedItem = null;
            Assert.False(w.Credit.Shown);
        }
        finally
        {
            w?.Close();
            App.MediaHistory = before;
            history.Dispose();
            Dispatcher.UIThread.RunJobs();
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using LibVLCSharp.Shared;
using MediaType = ConditioningControlPanel.Models.MediaType;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// "Media Log" recap window (opened from the Assets tab). Shows the app-lifetime history
    /// of flashed images, played videos and Brain Drain audio in a virtualized master list, with
    /// a single live preview pane on the right.
    ///
    /// PORTED from ConditioningControlPanel/Windows/MediaHistoryWindow.xaml.cs, reading the same
    /// <see cref="MediaHistoryService"/> (now in CCP.Core, <see cref="App.MediaHistory"/>) and the
    /// same <see cref="MediaHistoryPreviewRules"/>. Deviations:
    ///  - Video preview plays through LibVLC into an <c>Image</c> (<see cref="VlcFrameSink"/>), muted
    ///    and looping like WPF's MediaElement; a GIF animates from the spiral's bounded SkiaSharp decoder
    ///    (WPF XamlAnimatedGif), showing its first frame until the frames are ready.
    ///  - This head has no RemoteMediaCache, so an online entry is never "cached": it gets the
    ///    streamed-only card with Copy link / Open source, and (with remote-media consent) Load preview,
    ///    which fetches the still into memory instead of into the cache.
    ///  - LibVLC raises nothing for an unreadable clip, so the MediaFailed plate is decided up front
    ///    (<see cref="MediaHistoryPreviewRules.VideoLooksBroken"/>).
    /// </summary>
    public partial class MediaHistoryWindow : Window
    {
        private readonly List<MediaHistoryRow> _allRows = new();          // newest first, unfiltered
        private readonly ObservableCollection<MediaHistoryRow> _view = new();
        private string _filter = "all";       // all | image | video | audio
        private string _search = "";
        private MediaHistoryService? _history;

        private readonly ListBox _mediaList;
        private readonly TextBox _txtSearch;
        private readonly TextBlock _txtCount, _txtEmpty, _searchPlaceholder, _copyLinkText;
        private readonly TextBlock _previewHint, _previewMissing, _previewName, _previewPath;
        private readonly Image _previewImage;
        /// <summary>WPF MediaHistoryWindow.xaml.cs:35: "via Scrolller" on an online row's preview.</summary>
        internal readonly ScrolllerCreditBadge Credit = new() { Margin = new Thickness(0, 0, 8, 6) };
        private readonly Button _btnFilterAll, _btnFilterImages, _btnFilterVideos, _btnFilterAudio;
        private readonly Button _btnPreviewOpenFolder, _btnPreviewOpenFile, _btnPreviewCopyLink, _btnPreviewOpenSource, _btnPreviewLoad;
        private MediaPreviewPlan? _plan;   // the selected row's plan; what the action buttons obey

        private MediaPlayer? _player;
        private Media? _media;
        private VlcFrameSink? _sink;
        private int _copyGeneration, _previewGeneration;
        private DispatcherTimer? _gifTimer;
        private Bitmap? _still;
        private List<Bitmap> _gifFrames = new();

        public MediaHistoryWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _mediaList = this.FindControl<ListBox>("MediaList")!;
            _txtSearch = this.FindControl<TextBox>("TxtSearch")!;
            _txtCount = this.FindControl<TextBlock>("TxtCount")!;
            _txtEmpty = this.FindControl<TextBlock>("TxtEmpty")!;
            _searchPlaceholder = this.FindControl<TextBlock>("SearchPlaceholder")!;
            _copyLinkText = this.FindControl<TextBlock>("CopyLinkText")!;
            _previewHint = this.FindControl<TextBlock>("PreviewHint")!;
            _previewMissing = this.FindControl<TextBlock>("PreviewMissing")!;
            _previewName = this.FindControl<TextBlock>("PreviewName")!;
            _previewPath = this.FindControl<TextBlock>("PreviewPath")!;
            _previewImage = this.FindControl<Image>("PreviewImage")!;
            (_previewImage.Parent as Panel)?.Children.Add(Credit);
            _btnFilterAll = this.FindControl<Button>("BtnFilterAll")!;
            _btnFilterImages = this.FindControl<Button>("BtnFilterImages")!;
            _btnFilterVideos = this.FindControl<Button>("BtnFilterVideos")!;
            _btnFilterAudio = this.FindControl<Button>("BtnFilterAudio")!;
            _btnPreviewOpenFolder = this.FindControl<Button>("BtnPreviewOpenFolder")!;
            _btnPreviewOpenFile = this.FindControl<Button>("BtnPreviewOpenFile")!;
            _btnPreviewCopyLink = this.FindControl<Button>("BtnPreviewCopyLink")!;
            _btnPreviewOpenSource = this.FindControl<Button>("BtnPreviewOpenSource")!;
            _btnPreviewLoad = this.FindControl<Button>("BtnPreviewLoad")!;

            _mediaList.ItemsSource = _view;
            _mediaList.SelectionChanged += MediaList_SelectionChanged;
            _txtSearch.TextChanged += Search_TextChanged;

            _btnFilterAll.Click += Filter_Click;
            _btnFilterImages.Click += Filter_Click;
            _btnFilterVideos.Click += Filter_Click;
            _btnFilterAudio.Click += Filter_Click;
            this.FindControl<Button>("BtnClose")!.Click += (_, _) => Close();
            this.FindControl<Button>("BtnClear")!.Click += async (_, _) => await ClearAsync();
            _btnPreviewOpenFolder.Click += (_, _) => { if (_mediaList.SelectedItem is MediaHistoryRow r) RevealInExplorer(r.Entry.FilePath); };
            _btnPreviewOpenFile.Click += (_, _) => OpenSelectedFile();
            _btnPreviewCopyLink.Click += async (_, _) => await CopyLinkAsync();
            _btnPreviewOpenSource.Click += async (_, _) => await OpenSourceAsync();
            _btnPreviewLoad.Click += async (_, _) => await LoadPreviewAsync();

            // One handler on the list instead of one inside the DataTemplate; Click bubbles.
            _mediaList.AddHandler(Button.ClickEvent, OpenFolder_Click);
            this.FindControl<DockPanel>("HeaderBar")!.PointerPressed += Header_PointerPressed;

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            _history = App.MediaHistory;
            foreach (var entry in _history?.GetSnapshot() ?? new List<MediaLogEntry>())
                _allRows.Add(new MediaHistoryRow(entry));

            RebuildView();
            UpdateFilterButtons();

            if (_history != null)
            {
                _history.EntryAdded += OnEntryAdded;
                _history.Cleared += OnHistoryCleared;
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_history != null)
            {
                _history.EntryAdded -= OnEntryAdded;
                _history.Cleared -= OnHistoryCleared;
                _history = null;
            }
            StopPreview();
        }

        // ---- Live updates -------------------------------------------------

        private void OnEntryAdded(object? sender, MediaLogEntry entry)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnEntryAdded(sender, entry));
                return;
            }
            if (_history == null) return;   // closed while the post was queued
            var row = new MediaHistoryRow(entry);
            _allRows.Insert(0, row);
            if (_allRows.Count > MediaHistoryService.MaxEntries)
                _allRows.RemoveAt(_allRows.Count - 1);

            if (PassesFilter(row))
                _view.Insert(0, row);

            _txtEmpty.IsVisible = _view.Count == 0;
            _mediaList.IsVisible = _view.Count > 0;
            UpdateCount();
        }

        private void OnHistoryCleared(object? sender, EventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnHistoryCleared(sender, e));
                return;
            }
            _allRows.Clear();
            RebuildView();
            StopPreview();
            ShowPreviewNone();
        }

        // ---- Filtering / search ------------------------------------------

        private bool PassesFilter(MediaHistoryRow row)
        {
            if (_filter == "image" && row.Entry.Type != MediaType.Image) return false;
            if (_filter == "video" && row.Entry.Type != MediaType.Video) return false;
            if (_filter == "audio" && row.Entry.Type != MediaType.Audio) return false;
            if (!string.IsNullOrEmpty(_search) &&
                row.DisplayName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return true;
        }

        private void RebuildView()
        {
            _view.Clear();
            foreach (var row in _allRows)
                if (PassesFilter(row)) _view.Add(row);

            bool empty = _view.Count == 0;
            _txtEmpty.IsVisible = empty;
            _mediaList.IsVisible = !empty;
            UpdateCount();
        }

        private void UpdateCount()
        {
            int total = _allRows.Count;
            int shown = _view.Count;
            _txtCount.Text = shown == total
                ? Loc.GetF("label_media_entry_count", total)
                : Loc.GetF("label_media_entry_count_filtered", shown, total);
        }

        private void Filter_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Control fe && fe.Tag is string tag)
            {
                _filter = tag;
                RebuildView();
                UpdateFilterButtons();
            }
        }

        private void UpdateFilterButtons()
        {
            SetActive(_btnFilterAll, _filter == "all");
            SetActive(_btnFilterImages, _filter == "image");
            SetActive(_btnFilterVideos, _filter == "video");
            SetActive(_btnFilterAudio, _filter == "audio");
        }

        private void SetActive(Button btn, bool active)
        {
            btn.Background = active
                ? (this.TryFindResource("PinkBrush", out var pink) && pink is IBrush b
                    ? b
                    : new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)))
                : new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x40));
            btn.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xE8));
        }

        private void Search_TextChanged(object? sender, TextChangedEventArgs e)
        {
            _search = _txtSearch.Text?.Trim() ?? "";
            _searchPlaceholder.IsVisible = string.IsNullOrEmpty(_txtSearch.Text);
            RebuildView();
        }

        // ---- Preview ------------------------------------------------------

        private void MediaList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_mediaList.SelectedItem is MediaHistoryRow row)
                ShowPreview(row);
            else
                ShowPreviewNone();
        }

        private void ShowPreview(MediaHistoryRow row)
        {
            StopPreview();
            _previewHint.IsVisible = false;
            _previewName.Text = row.DisplayName;
            Credit.SetShown(ScrolllerCreditRules.IsScrolllerMedia(row.Entry.FilePath));

            // No RemoteMediaCache on this head: an online entry is never cached here.
            var plan = _plan = MediaHistoryPreviewRules.Plan(row.Entry.FilePath, row.Entry.Type, row.FileExists,
                remoteCached: false, remoteConsent: RemoteConsent());

            _previewPath.Text = plan.SourceText;
            _btnPreviewOpenFolder.IsEnabled = plan.CanOpenFolder;
            _btnPreviewOpenFile.IsEnabled = plan.CanOpenFile;
            _btnPreviewOpenFolder.IsVisible = !plan.IsRemote;
            _btnPreviewOpenFile.IsVisible = !plan.IsRemote;
            _btnPreviewCopyLink.IsVisible = plan.CanCopyLink;
            _btnPreviewOpenSource.IsVisible = plan.CanOpenSource;
            _btnPreviewLoad.IsVisible = _btnPreviewLoad.IsEnabled = plan.CanLoadPreview;

            if (plan.Kind == MediaPreviewKind.LocalMissing || plan.Kind == MediaPreviewKind.RemoteUncached)
            {
                ShowMissing(plan.IsRemote ? "label_media_streamed_only" : "label_file_not_found");
                return;
            }
            // An audio clip has no picture: never hand an mp3 to the image decoder (ccp-bugs #1098).
            if (row.Entry.Type == MediaType.Audio)
            {
                ShowMissing("label_media_audio_preview");
                return;
            }
            _previewMissing.IsVisible = false;

            bool gif = string.Equals(Path.GetExtension(row.Entry.FilePath), ".gif", StringComparison.OrdinalIgnoreCase);
            if (row.Entry.Type != MediaType.Video)
            {
                try
                {
                    using var stream = File.OpenRead(row.Entry.FilePath);
                    // WPF capped the single preview with DecodePixelWidth=720; never full-res.
                    _previewImage.Source = _still = Bitmap.DecodeToWidth(stream, 720);
                    _previewImage.IsVisible = true;
                }
                catch (Exception ex)
                {
                    Log.Debug("MediaHistoryWindow: preview failed for {Path}: {Error}", row.Entry.FilePath, ex.Message);
                    ShowMissing("label_file_not_found");
                    return;
                }
            }
            if (row.Entry.Type == MediaType.Video && MediaHistoryPreviewRules.VideoLooksBroken(row.Entry.FilePath))
                ShowMissing("label_file_not_found");   // WPF PreviewVideo_MediaFailed
            else if (row.Entry.Type == MediaType.Video) PlayLoop(row.Entry.FilePath);
            else if (gif) _ = AnimateGifAsync(row.Entry.FilePath, _previewGeneration);
        }

        /// <summary>WPF XamlAnimatedGif (RepeatBehavior Forever) on the spiral's bounded SkiaSharp
        /// decoder, off the UI thread; the still stays up until the frames are ready. The frame timer
        /// runs only while this preview is the selected one and the window is open.</summary>
        private async System.Threading.Tasks.Task AnimateGifAsync(string path, int generation)
        {
            var (frames, delay) = await System.Threading.Tasks.Task.Run(() => Views.Overlays.SpiralOverlay.Decode(path));
            if (generation != _previewGeneration || frames.Count < 2)
            {
                foreach (var f in frames) f.Dispose();
                return;
            }
            _gifFrames = frames;
            var i = 0;
            _previewImage.Source = frames[0];
            _gifTimer = new DispatcherTimer { Interval = delay };
            _gifTimer.Tick += (_, _) => { i = (i + 1) % frames.Count; _previewImage.Source = frames[i]; };
            _gifTimer.Start();
        }

        /// <summary>WPF MediaElement (muted, looped by MediaEnded) and XamlAnimatedGif (RepeatBehavior
        /// Forever): one LibVLC decoder feeding the preview Image.</summary>
        private void PlayLoop(string path)
        {
            var vlc = LibVlcAudio.Shared;
            if (vlc == null)
            {
                ShowMissing("label_file_not_found");
                return;
            }
            var player = new MediaPlayer(vlc) { EnableHardwareDecoding = true, Mute = true };
            _player = player;
            _sink = new VlcFrameSink(player, () => _media,
                bmp => { if (ReferenceEquals(player, _player)) { _previewImage.Source = bmp; _previewImage.IsVisible = true; } },
                () => { if (ReferenceEquals(player, _player)) _previewImage.InvalidateVisual(); });
            // WPF PreviewVideo_MediaFailed: the plate instead of a dead pane.
            player.EncounteredError += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(player, _player)) return;
                _previewImage.IsVisible = false;
                ShowMissing("label_file_not_found");
            });
            _media = new Media(vlc, path, FromType.FromPath);
            _media.AddOption(":input-repeat=65535");   // WPF PreviewVideo_MediaEnded: loop
            _media.AddOption(LibVlcSilence.NoAudioOption);
            player.Play(_media);
        }

        private void ShowMissing(string key)
        {
            _previewImage.IsVisible = false;
            _previewMissing.Text = Loc.Get(key);
            _previewMissing.IsVisible = true;
        }

        private void ShowPreviewNone()
        {
            Credit.SetShown(false);
            StopPreview();
            _plan = null;
            _previewHint.IsVisible = true;
            _previewImage.IsVisible = false;
            _previewMissing.IsVisible = false;
            _previewName.Text = "";
            _previewPath.Text = "";
            _btnPreviewOpenFolder.IsEnabled = false;
            _btnPreviewOpenFile.IsEnabled = false;
            _btnPreviewOpenFolder.IsVisible = true;
            _btnPreviewOpenFile.IsVisible = true;
            _btnPreviewCopyLink.IsVisible = false;
            _btnPreviewOpenSource.IsVisible = false;
            _btnPreviewLoad.IsVisible = false;
        }

        /// <summary>WPF RemoteConsent: the app-wide remote-media gate every other surface reads
        /// (MediaSource != "local" and HasRemoteMediaConsent). The media log is not a way around it.</summary>
        private static bool RemoteConsent()
        {
            var s = CoreSettings.Current;
            return s.MediaSource != "local" && s.HasRemoteMediaConsent;
        }

        private static readonly System.Net.Http.HttpClient Http = CreateClient();

        // WPF RemoteMediaCache.CreateClient's headers: CDNs that refuse a request without a User-Agent must not
        // drop this head to the streamed-only card.
        private static System.Net.Http.HttpClient CreateClient()
        {
            var c = new System.Net.Http.HttpClient(ConditioningControlPanel.Services.Deeper.UrlSafety.CreateGuardedHandler()) { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://scrolller.com/");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/webp,image/jpeg,image/png,video/*,*/*;q=0.8");
            return c;
        }

        /// <summary>Test seam: the bytes behind an online still (WPF RemoteMediaCache.OpenAsync), at most
        /// <see cref="WelcomeShow.FirstShowStageWindow.MaxPictureBytes"/>; null when the host refused or sent no image.</summary>
        internal static Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task<byte[]?>> FetchRemote =
            (url, token) => FetchFollowingRedirectsAsync(
                (u, t) => Http.GetAsync(u, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, t), url, token);

        /// <summary>WPF RemoteMediaCache follows redirects (CDNs answer 302). The guarded handler cannot, so each
        /// hop is followed here and re-runs the handler's connect-time private-IP guard; http(s) only, 5 hops.</summary>
        internal static async System.Threading.Tasks.Task<byte[]?> FetchFollowingRedirectsAsync(
            Func<Uri, System.Threading.CancellationToken, System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage>> get,
            string url, System.Threading.CancellationToken token)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var current)) return null;
            for (int hop = 0; ; hop++)
            {
                if (current.Scheme != Uri.UriSchemeHttps && current.Scheme != Uri.UriSchemeHttp) return null;
                using var response = await get(current, token);
                int code = (int)response.StatusCode;
                if (code >= 300 && code < 400 && response.Headers.Location is { } location)
                {
                    if (hop >= 5) return null;
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    if (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp) return null; // .NET's auto-redirect refuses this downgrade too
                    current = next;
                    continue;
                }
                return await ReadCappedAsync(response, token);
            }
        }

        private static async System.Threading.Tasks.Task<byte[]?> ReadCappedAsync(System.Net.Http.HttpResponseMessage response, System.Threading.CancellationToken token)
        {
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > WelcomeShow.FirstShowStageWindow.MaxPictureBytes) return null;
            await using var body = await response.Content.ReadAsStreamAsync(token);
            using var copy = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = await body.ReadAsync(buffer, token)) > 0)
            {
                if (copy.Length + n > WelcomeShow.FirstShowStageWindow.MaxPictureBytes) return null;
                copy.Write(buffer, 0, n);
            }
            return copy.ToArray();
        }

        /// <summary>WPF PreviewLoad_Click / ShowRemotePreview(fetch: true): the only way this window reaches out,
        /// and only on a press, behind consent. One still, 720 wide, decoded off the UI thread; the generation
        /// guard drops a result whose row was deselected meanwhile. A failed draw puts the retry back.</summary>
        private async System.Threading.Tasks.Task LoadPreviewAsync()
        {
            if (_mediaList.SelectedItem is not MediaHistoryRow row) return;
            var url = row.Entry.FilePath;
            if (!MediaHistoryPreviewRules.IsRemote(url) || !RemoteConsent()) return;   // the floor under the hidden button
            var generation = _previewGeneration;
            _btnPreviewLoad.IsEnabled = false;
            _previewMissing.Text = Loc.Get("label_media_loading_preview");
            _previewMissing.IsVisible = true;
            Bitmap? decoded = null;
            string failure = "label_media_streamed_only";
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
                var bytes = await FetchRemote(url, cts.Token);
                if (bytes != null)
                {
                    // The bytes were here and we could not read them: never say nothing was kept.
                    failure = "label_no_preview";
                    decoded = await System.Threading.Tasks.Task.Run(() =>
                    {
                        try { using var s = new MemoryStream(bytes); return Bitmap.DecodeToWidth(s, 720); }
                        catch (Exception) { return null; }   // swallow: an unreadable still is the "no preview" card
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("MediaHistoryWindow: online preview failed for {Host}: {Error}",
                    Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : "?", ex.Message);
            }
            if (generation != _previewGeneration) { decoded?.Dispose(); return; }
            if (decoded == null)
            {
                ShowMissing(failure);
                bool allowed = _plan?.IsRemote == true && RemoteConsent();
                _btnPreviewLoad.IsVisible = _btnPreviewLoad.IsEnabled = allowed;
                return;
            }
            _previewImage.Source = _still = decoded;
            _previewImage.IsVisible = true;
            _previewMissing.IsVisible = false;
            _btnPreviewLoad.IsVisible = false;
        }

        /// <summary>Stop joins the decoder thread, so after it no callback touches the frame buffer.</summary>
        private void StopPreview()
        {
            _previewGeneration++;   // retires a GIF decode still in flight
            _gifTimer?.Stop();
            _gifTimer = null;
            var player = _player;
            _player = null;
            if (player != null)
            {
                try { player.Stop(); } catch { }
                try { player.Dispose(); } catch { }
            }
            try { _media?.Dispose(); } catch { }
            _media = null;
            _previewImage.Source = null;
            _still?.Dispose();
            _still = null;
            foreach (var f in _gifFrames) f.Dispose();
            _gifFrames = new();
            _sink?.Free();
            _sink = null;
        }

        // ---- Open in the file manager -------------------------------------

        private void OpenFolder_Click(object? sender, RoutedEventArgs e)
        {
            if ((e.Source as Control)?.Tag is MediaHistoryRow row)
                RevealInExplorer(row.Entry.FilePath);
        }

        /// <summary>WPF ExplorerLauncher.RevealInExplorer: the file selected in the file manager, its folder
        /// when the file is gone (#998). Nothing to reveal for an online item.</summary>
        private static void RevealInExplorer(string path)
        {
            if (string.IsNullOrEmpty(path) || MediaHistoryPreviewRules.IsRemote(path)) return;
            ExternalOpener.Reveal(path);
        }

        private void OpenSelectedFile()
        {
            if (_mediaList.SelectedItem is not MediaHistoryRow row) return;
            if (!File.Exists(row.Entry.FilePath)) return;
            try { ExternalOpener.Open(row.Entry.FilePath); }
            catch (Exception ex) { Log.Warning(ex, "MediaHistoryWindow: failed to open {Path}", row.Entry.FilePath); }
        }

        // ---- Online source ------------------------------------------------

        private async System.Threading.Tasks.Task CopyLinkAsync()
        {
            if (_mediaList.SelectedItem is not MediaHistoryRow row) return;
            try
            {
                if (Clipboard is { } c) await c.SetTextAsync(row.Entry.FilePath);
                // WPF FlashCopyConfirmation: two seconds of "Copied" on the button, then back.
                var generation = ++_copyGeneration;
                _copyLinkText.Text = Loc.Get("btn_copied");
                DispatcherTimer.RunOnce(() =>
                {
                    if (generation == _copyGeneration) _copyLinkText.Text = Loc.Get("btn_copy_link");
                }, TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) { Log.Debug("MediaHistoryWindow: copy link failed: {Error}", ex.Message); }
        }

        /// <summary>WPF PreviewOpenSource_Click: the PARSED url re-derived from the selected row,
        /// never the logged text.</summary>
        private async System.Threading.Tasks.Task OpenSourceAsync()
        {
            if (_mediaList.SelectedItem is not MediaHistoryRow row) return;
            var url = MediaHistoryPreviewRules.BrowsableUrl(row.Entry.FilePath);
            if (url == null) return;
            // WPF BrowserLauncher.OpenUrlOrPrompt: on failure the link goes to the clipboard.
            if (await ExternalOpener.OpenAsync(this, url)) return;
            try { if (Clipboard is { } c) await c.SetTextAsync(url); } catch { }
        }

        // ---- Chrome -------------------------------------------------------

        /// <summary>WPF BtnClear_Click: confirm, then clear the shared log (the window empties
        /// through the Cleared event, as WPF's does).</summary>
        internal async System.Threading.Tasks.Task ClearAsync()
        {
            if (await Dialogs.MessageDialog.ConfirmAsync(this, Loc.Get("dialog_media_log"), Loc.Get("confirm_clear_media_log")))
                App.MediaHistory?.Clear();
        }

        private void Header_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                try { BeginMoveDrag(e); } catch { }
            }
        }
    }

    /// <summary>
    /// Lightweight view-model for one history row. All display fields are precomputed once (rows
    /// are immutable), keeping the virtualized list cheap. Top-level so <c>x:DataType</c> can name it.
    /// </summary>
    public sealed class MediaHistoryRow
    {
        private const int ThumbSize = 96, ThumbCacheCap = 128;   // WPF MediaThumbnailConverter
        private static readonly Dictionary<string, Bitmap?> ThumbCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> ThumbLru = new();

        public MediaLogEntry Entry { get; }
        public string DisplayName { get; }
        public string TimeText { get; }
        public string TypeBadge { get; }
        public IconKind PlaceholderGlyph { get; }
        public IBrush BadgeBrush { get; }
        public bool FileExists => SafeExists(Entry.FilePath);
        /// <summary>Hidden for online entries: there is no folder of theirs to reveal.</summary>
        public bool FolderButtonVisible { get; }

        public MediaHistoryRow(MediaLogEntry entry)
        {
            Entry = entry;
            DisplayName = string.IsNullOrEmpty(entry.DisplayName) ? SafeName(entry.FilePath) : entry.DisplayName;
            TimeText = FormatTime(entry.Timestamp);
            FolderButtonVisible = !MediaHistoryPreviewRules.IsRemote(entry.FilePath);

            if (entry.Type == MediaType.Video)
            {
                TypeBadge = Loc.Get("badge_video");
                PlaceholderGlyph = IconKind.Video;
                BadgeBrush = new SolidColorBrush(Color.FromRgb(0x4A, 0x6C, 0xD0));
            }
            else if (entry.Type == MediaType.Audio)
            {
                TypeBadge = Loc.Get("badge_audio");
                PlaceholderGlyph = IconKind.MusicNote2;
                BadgeBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x9A, 0x86));
            }
            else
            {
                TypeBadge = Loc.Get("badge_image");
                PlaceholderGlyph = IconKind.Image;
                BadgeBrush = new SolidColorBrush(Color.FromRgb(0xB0, 0x50, 0x9C));
            }
        }

        /// <summary>WPF MediaThumbnailConverter: images only, decoded small, in a bounded LRU; read only
        /// by realized (on-screen) rows, so a 500-entry log decodes what is visible.</summary>
        public Bitmap? Thumb
        {
            get
            {
                if (Entry.Type != MediaType.Image || string.IsNullOrEmpty(Entry.FilePath)) return null;
                var path = Entry.FilePath;
                if (ThumbCache.TryGetValue(path, out var cached)) { ThumbLru.Remove(path); ThumbLru.AddLast(path); return cached; }
                Bitmap? thumb = null;
                try
                {
                    if (File.Exists(path)) { using var s = File.OpenRead(path); thumb = Bitmap.DecodeToWidth(s, ThumbSize); }
                }
                catch (Exception ex) { Log.Debug("MediaHistoryRow: thumbnail failed: {Error}", ex.Message); }
                ThumbCache[path] = thumb;
                ThumbLru.AddLast(path);
                while (ThumbLru.Count > ThumbCacheCap)
                {
                    ThumbCache.Remove(ThumbLru.First!.Value);   // not disposed: a realized row may still show it
                    ThumbLru.RemoveFirst();
                }
                return thumb;
            }
        }

        private static string FormatTime(DateTime t)
        {
            var now = DateTime.Now;
            if (t.Date == now.Date) return t.ToString("HH:mm:ss");
            if (t.Date == now.Date.AddDays(-1)) return Loc.Get("label_yesterday") + " " + t.ToString("HH:mm");
            return t.ToString("MMM d, HH:mm");
        }

        private static string SafeName(string path)
        {
            try { return Path.GetFileName(path) ?? path; } catch { return path; }
        }

        private static bool SafeExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && File.Exists(path); } catch { return false; }
        }
    }
}

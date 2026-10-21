using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.IO.Compression;
using ConditioningControlPanel.Services.Packs;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Assets.cs (asset browser + presets) and MainWindow.AssetsFx.cs on the
/// Avalonia Assets tab, entered through the shell's Library door like a user.</summary>
public sealed class AssetsTabTests
{
    [Fact]
    public Task DoorBuildsTreeAndSpaceOnAFolderCheckboxDisablesItsFiles() => Run(async (shell, tab, s) =>
    {
        var images = tab.AssetTree.Single(n => n.Name == "images");
        Assert.Equal(new[] { "images", "videos" }, tab.AssetTree.Select(n => n.Name));
        Assert.Equal(2, images.FileCount);
        Assert.Equal(Loc.GetF("label_0_images_1_videos_active", 3, 2), tab.TxtAssetCounts.Text);

        var cb = await Realized(shell, () => tab.AssetTreeView.GetVisualDescendants().OfType<CheckBox>()
            .FirstOrDefault(c => c.DataContext == images));
        PressSpace(shell, cb);

        Assert.Contains("images/a.png", s.DisabledAssetPaths);
        Assert.Contains("images/sub/c.png", s.DisabledAssetPaths);
        Assert.Contains("images", s.DisabledAssetFolders);
        Assert.False(images.Children.Single().IsChecked);
        Assert.Equal(Loc.GetF("label_0_images_1_videos_active", 0, 2), tab.TxtAssetCounts.Text);
    });

    [Fact]
    public Task SelectingAFolderShowsDecodedThumbnailsAndAThumbnailToggleUnchecksTheFolder() => Run(async (shell, tab, s) =>
    {
        var sub = tab.AssetTree.Single(n => n.Name == "images").Children.Single();
        tab.AssetTreeView.SelectedItem = sub;
        Assert.Single(tab.CurrentFolderFiles);
        await tab.ThumbnailLoads;
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(tab.CurrentFolderFiles[0].Thumbnail);

        var cb = await Realized(shell, () => tab.ThumbnailsItemsControl.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault());
        PressSpace(shell, cb);
        Assert.Contains("images/sub/c.png", s.DisabledAssetPaths);
        Assert.False(sub.IsChecked);

        tab.BtnSelectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain("images/sub/c.png", s.DisabledAssetPaths);
        Assert.True(tab.CurrentFolderFiles[0].IsChecked);
    });

    [Fact]
    public Task PresetsSaveSwitchRefuseDefaultAndDelete() => Run(async (shell, tab, s) =>
    {
        var told = new List<string>();
        tab.AskText = (_, _, def) => Task.FromResult<string?>("Mine");
        tab.Inform = (title, _) => { told.Add(title); return Task.CompletedTask; };
        tab.Confirm = (_, _) => Task.FromResult(true);
        var resets = 0;
        var oldReset = AssetPresetService.OnlineChannelsReset;
        AssetPresetService.OnlineChannelsReset = () => resets++;
        try
        {
            // Everything off, saved as "Mine".
            tab.SelectFolder(tab.AssetTree.Single(n => n.Name == "images"));
            tab.BtnDeselectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tab.BtnSaveAssetPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => s.AssetPresets.Any(p => p.Name == "Mine"));
            var mine = s.AssetPresets.Single(p => p.Name == "Mine");
            Assert.Contains("images/a.png", mine.DisabledAssetPaths);
            Assert.Same(mine, tab.CmbAssetPresets.SelectedItem);
            mine.OnlineNiches = new List<string> { "niche-from-preset" };

            // The combo switches the live selection, and the online half re-deals the channels.
            tab.CmbAssetPresets.SelectedItem = s.AssetPresets.Single(p => p.IsDefault);
            Assert.Empty(s.DisabledAssetPaths);
            tab.CmbAssetPresets.SelectedItem = mine;
            Assert.Contains("images/a.png", s.DisabledAssetPaths);
            Assert.Equal(1, resets);

            // The default row refuses Update and Delete.
            tab.CmbAssetPresets.SelectedItem = s.AssetPresets.Single(p => p.IsDefault);
            tab.BtnUpdateAssetPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            tab.BtnDeleteAssetPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => told.Count >= 3);
            Assert.Equal(new[] { Loc.Get("title_preset_saved"), Loc.Get("title_cannot_update_default"), Loc.Get("title_cannot_delete_default") }, told);

            tab.CmbAssetPresets.SelectedItem = mine;
            tab.BtnDeleteAssetPreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => s.AssetPresets.All(p => p.Name != "Mine"));
            Assert.Equal(s.AssetPresets.Single(p => p.IsDefault).Id, s.CurrentAssetPresetId);
        }
        finally { AssetPresetService.OnlineChannelsReset = oldReset; }
    });

    [Fact]
    public Task HoveringATreeRowNudgesIt() => Run(async (shell, tab, s) =>
    {
        var cb = await Realized(shell, () => tab.AssetTreeView.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault());
        var row = (Control)cb.GetVisualParent()!;
        var on = cb.TranslatePoint(new Point(cb.Bounds.Width / 2, cb.Bounds.Height / 2), shell)!.Value;
        void Hover(Point p)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();   // hit testing reads the last rendered frame
            shell.MouseMove(p, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        // Motion on: a 130 ms slide on X toward 3 px (WPF AssetTreeRowNudgeMs / Px).
        Hover(on);
        var slide = Assert.IsType<TranslateTransform>(row.RenderTransform);
        var t = Assert.IsType<DoubleTransition>(Assert.Single(slide.Transitions!));
        Assert.Equal(TranslateTransform.XProperty, t.Property);
        Assert.Equal(TimeSpan.FromMilliseconds(130), t.Duration);
        Assert.InRange(slide.X, 0.0001, 3.0);
        Hover(new Point(1, 1));

        // Motion off: the same nudge lands at once, and leaving puts it back.
        var old = s.MotionLevel;
        s.MotionLevel = MotionLevel.Off;
        try
        {
            Hover(on);
            Assert.Null(slide.Transitions);
            Assert.Equal(3.0, slide.X);
            Hover(new Point(1, 1));
            Assert.Equal(0.0, slide.X);
        }
        finally { s.MotionLevel = old; }
    });

    [Fact]
    public Task MediaLogPulsesOnEntryWhenTheLogGrewAndStopsWhenTheTabHides() => Run(async (shell, tab, s) =>
    {
        var old = global::ConditioningControlPanel.Avalonia.App.MediaHistory;
        var dir = Directory.CreateTempSubdirectory("ccp-assets-log-").FullName;
        var history = new MediaHistoryService(Path.Combine(dir, "media_history.json"));
        global::ConditioningControlPanel.Avalonia.App.MediaHistory = history;
        try
        {
            shell.ShowTab("settings");
            shell.ShowTab("assets");
            Assert.False(tab.MediaLogPulsing);                  // nothing logged since last opened

            history.RecordImages(new[] { "/x/a.png" });
            shell.ShowTab("settings");
            // Freshen the shared animation clock (a running section crossfade would hand the
            // pulse the whole real-time gap since its last tick as one frame).
            global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            shell.ShowTab("assets");
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.MediaLogPulsing);

            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.MediaLogPulsing);
            Assert.Equal(1.0, tab.BtnMediaLog.Opacity);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.App.MediaHistory = old;
            history.Dispose();
            try { Directory.Delete(dir, true); } catch { }
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task UntickingVideosDropsTheDealtVideoAndBubbleQueues() => Run(async (shell, tab, s) =>
    {
        var overlay = global::ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance;
        var bubbles = BubbleCountHost.Instance;
        var (oldVideo, oldBubbles) = (overlay.Scheduler, bubbles.Scheduler);
        overlay.Scheduler = new MandatoryVideoScheduler(overlay);
        bubbles.Scheduler = new BubbleCountScheduler(bubbles);
        try
        {
            // Both deal a two-clip queue and play one; one clip is left queued.
            Assert.NotNull(overlay.Scheduler.PickNext());
            Assert.NotNull(bubbles.Scheduler.PickNext());

            var videos = tab.AssetTree.Single(n => n.Name == "videos");
            var cb = await Realized(shell, () => tab.AssetTreeView.GetVisualDescendants().OfType<CheckBox>()
                .FirstOrDefault(c => c.DataContext == videos));
            PressSpace(shell, cb);
            Assert.Contains("videos/v.mp4", s.DisabledAssetPaths);

            // WPF #130: the very next pick honours the untick instead of draining the old queue.
            Assert.Null(overlay.Scheduler.PickNext());
            Assert.Null(bubbles.Scheduler.PickNext());
        }
        finally
        {
            overlay.Scheduler = oldVideo;
            bubbles.Scheduler = oldBubbles;
        }
    });

    [Fact]
    public Task RefreshKeepsTheOpenFolderAndItsThumbnails() => Run(async (shell, tab, s) =>
    {
        tab.Inform = (_, _) => Task.CompletedTask;
        var sub = tab.AssetTree.Single(n => n.Name == "images").Children.Single();
        tab.AssetTreeView.SelectedItem = sub;
        tab.BtnRefreshAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(tab.CurrentFolderFiles);
        Assert.Same(tab.AssetTreeView.SelectedItem, tab.AssetTree.Single(n => n.Name == "images").Children.Single());
        Assert.False(tab.TxtThumbnailsEmpty.IsVisible);

        tab.BtnDeselectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Contains("images/sub/c.png", s.DisabledAssetPaths);
        Assert.DoesNotContain("images/a.png", s.DisabledAssetPaths);
        await tab.ThumbnailLoads;
    });

    [Fact]
    public Task ActivePackTreeOpensDecryptsTogglesAndKeepsSelectionOnRefresh() => Run(async (shell, tab, s) =>
    {
        var oldStore = ContentPackStore.Current;
        var root = ConditioningControlPanel.CorePaths.EffectiveAssets;
        var packDir = Path.Combine(root, ".packs", "test-pack");
        Directory.CreateDirectory(Path.Combine(packDir, "content"));
        var file = new PackFileEntry { OriginalName = "face.png", ObfuscatedName = "one.enc", Extension = ".png", FileType = "image" };
        File.WriteAllBytes(Path.Combine(packDir, "content", file.ObfuscatedName),
            PackEncryptionService.Encrypt(File.ReadAllBytes(Path.Combine(root, "images", "a.png"))));
        var manifest = new InstalledPackManifest { PackId = "p1", PackGuid = "test-pack", PackName = "Pack One", Files = new() { file } };
        PackEncryptionService.SaveEncryptedManifest(Newtonsoft.Json.JsonConvert.SerializeObject(manifest), Path.Combine(packDir, ".manifest.enc"));
        var store = new ContentPackStore(() => root, () => s, () => { });
        ContentPackStore.Current = store;
        try
        {
            tab.RefreshAssetTree();
            var packs = tab.AssetTree.Single(n => n.IsPackFolder);
            Assert.Equal("Content Packs", packs.Name);
            var pack = Assert.Single(packs.Children);
            Assert.Equal("Pack One", pack.Name);
            var images = Assert.Single(pack.Children);
            Assert.Equal("image", images.PackFileType);
            Assert.Equal(Loc.GetF("label_0_images_1_videos_active", 4, 2), tab.TxtAssetCounts.Text);
            tab.AssetTreeView.SelectedItem = images;
            await tab.ThumbnailLoads;
            Dispatcher.UIThread.RunJobs();
            var tile = Assert.Single(tab.CurrentFolderFiles);
            Assert.Equal("face.png", tile.Name);
            Assert.NotNull(tile.Thumbnail);
            Assert.Equal("pack:p1/face.png", tile.RelativePath);

            tab.BtnDeselectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(tile.RelativePath, s.DisabledAssetPaths);
            Assert.False(tile.IsChecked);
            Assert.Empty(store.GetAllActivePackImages());
            Assert.Equal(Loc.GetF("label_0_images_1_videos_active", 3, 2), tab.TxtAssetCounts.Text);
            tab.BtnSelectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(store.GetAllActivePackImages());
            tab.RefreshAssetTree();
            Assert.Equal("image", ((AssetTreeItem)tab.AssetTreeView.SelectedItem!).PackFileType);
            Assert.Single(tab.CurrentFolderFiles);
            await tab.ThumbnailLoads;

            // The same context-menu preview path as a disk tile, with a temporary decrypt.
            var tileControl = await Realized(shell, () => tab.ThumbnailsItemsControl.GetVisualDescendants()
                .OfType<Border>().FirstOrDefault(b => b.ContextMenu != null));
            tileControl.ContextMenu!.Open(tileControl);
            Dispatcher.UIThread.RunJobs();
            var menuItem = (MenuItem)tileControl.ContextMenu.Items[0]!;
            menuItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var player = Assert.Single(shell.OwnedWindows.OfType<MiniPlayerWindow>());
            Assert.True(player.IsVisible);
            Assert.Single(Directory.GetFiles(Path.Combine(root, ".temp")));
            PanicSurfaces.StopAll("asset-preview-test", shell);
            Assert.False(player.IsVisible);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, ".temp")));
        }
        finally { ContentPackStore.Current = oldStore; store.CleanupTempFiles(); }
    });

    [Fact]
    public Task PackRefreshUsesTheCatalogueAndActivationPreservesDownloadedFiles() => Run(async (shell, tab, s) =>
    {
        await WithPackService(tab, s, async (store, service) =>
        {
            tab.BtnRefreshPacks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => tab.PackCardsItemsControl.Items.Count == 2);
            var card = Assert.IsType<PackCardViewModel>(tab.PackCardsItemsControl.Items[0]);
            Assert.Equal("Basic Bimbo Starter Pack", card.Name);
            Assert.True(card.IsDownloaded);
            Assert.Equal("Deactivate", card.ActivateButtonText);
            var folder = store.GetPackFolder("basic-bimbo-starter");
            // The section ships hidden in WPF too. Reveal only in this sandbox to exercise its real buttons.
            tab.PacksSection.IsVisible = true;
            var activate = await Realized(shell, () => tab.PackCardsItemsControl.GetVisualDescendants()
                .OfType<Button>().FirstOrDefault(b => b.Tag == card && b.Content is TextBlock { Text: "Deactivate" }));
            activate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => !card.IsActive);
            Assert.Equal("Activate", card.ActivateButtonText);
            Assert.Empty(store.GetActivePackIds());
            Assert.True(Directory.Exists(folder));
            Assert.DoesNotContain(tab.AssetTree, n => n.IsPackFolder);
            activate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => card.IsActive);
            Assert.Single(store.GetActivePackIds());
            Assert.Contains(tab.AssetTree, n => n.IsPackFolder);
            var vm = (AssetsTabViewModel)tab.DataContext!;
            for (var i = 0; i < 8; i++) vm.Packs.Add(new PackCardViewModel(new ContentPack { Id = "wheel-" + i }));
            shell.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var point = tab.PacksScrollViewer.TranslatePoint(new Point(30, 30), shell)!.Value;
            shell.MouseWheel(point, new Vector(0, -1), RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(120, tab.PacksScrollViewer.Offset.X);
        });
    });

    [Fact]
    public Task DeleteDownloadedPacksRequiresConfirmationAndPreservesLooseAssets() => Run(async (shell, tab, s) =>
    {
        await WithPackService(tab, s, async (store, service) =>
        {
            var messages = new List<string>();
            tab.Inform = (_, message) => { messages.Add(message); return Task.CompletedTask; };
            tab.Confirm = (_, _) => Task.FromResult(false);
            tab.BtnDeleteDownloadedPacks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(store.IsPackInstalled("basic-bimbo-starter"));
            tab.Confirm = (_, _) => Task.FromResult(true);
            tab.BtnDeleteDownloadedPacks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => messages.Count == 1);
            Assert.Empty(s.InstalledPackIds);
            Assert.Empty(s.ActivePackIds);
            Assert.True(File.Exists(Path.Combine(ConditioningControlPanel.CorePaths.EffectiveAssets, "images", "a.png")));
            Assert.DoesNotContain(tab.AssetTree, n => n.IsPackFolder);
            tab.BtnDeleteDownloadedPacks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => messages.Count == 2);
            Assert.Equal(Loc.Get("msg_no_downloaded_packs_to_delete"), messages[1]);
        });
    });

    [Fact]
    public Task PackDownloadButtonInstallsThroughCoreAndResetsProgressOnAuthFailure() => Run(async (shell, tab, s) =>
    {
        await WithPackService(tab, s, async (store, service) =>
        {
            await tab.RefreshPacksAsync();
            var card = (PackCardViewModel)tab.PackCardsItemsControl.Items[1]!;
            var told = new List<string>();
            tab.Confirm = (_, _) => Task.FromResult(true);
            var installed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tab.Inform = (title, _) => { told.Add(title); installed.TrySetResult(); return Task.CompletedTask; };
            tab.PacksSection.IsVisible = true;
            var download = await Realized(shell, () => tab.PackCardsItemsControl.GetVisualDescendants()
                .OfType<Button>().FirstOrDefault(b => b.Tag == card && b.Content is TextBlock { Text: "Install" }));
            download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await installed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(store.IsPackInstalled(card.Id));
            Assert.True(card.IsActive);
            Assert.False(card.IsDownloading);
            Assert.Equal(0, card.DownloadProgress);
            Assert.Single(store.GetPackFiles(card.Id, "image"));
            Assert.Contains(Loc.Get("title_success"), told);
            // Uninstall uses the same button and asks before it removes encrypted content.
            tab.Confirm = (_, _) => Task.FromResult(false);
            await tab.ChangePackDownloadAsync(card);
            Assert.True(card.IsDownloaded);
            tab.Confirm = (_, _) => Task.FromResult(true);
            await tab.ChangePackDownloadAsync(card);
            Assert.False(store.IsPackInstalled(card.Id));
            Assert.False(card.IsDownloaded);

            var old = ContentPackService.Current;
            ContentPackService.Current = new ContentPackService(store, baseUrl: () => "https://packs.test",
                settings: () => s, save: () => { }, patreonToken: () => null);
            try
            {
                await tab.ChangePackDownloadAsync(card);
                Assert.Contains(Loc.Get("title_login_required"), told);
                Assert.False(card.IsDownloading);
                Assert.Equal(0, card.DownloadProgress);
                Assert.False(store.IsPackInstalled(card.Id));
            }
            finally { ContentPackService.Current = old; }
        });
    });

    [Fact]
    public Task ExternalPackDownloadsAllowOnlyHttpsAndCreatorUsesDiscordInvite() => Run(async (shell, tab, s) =>
    {
        var opened = new List<string>();
        tab.OpenPackLink = url => { opened.Add(url); return Task.CompletedTask; };
        var pack = new ContentPack { Id = "external", IsExternalFlag = true, ExternalUrl = "http://unsafe.test/" };
        var card = new PackCardViewModel(pack);
        await tab.ChangePackDownloadAsync(card);
        Assert.Empty(opened);
        pack.ExternalUrl = "https://safe.test/";
        await tab.ChangePackDownloadAsync(card);
        Assert.Equal(new[] { pack.ExternalUrl }, opened);
        tab.BtnCreatorDiscord.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(DiscordLinks.Invite, opened.Last());
    });

    [Fact]
    public Task PackRateLimitShowsTheServerRefusalAndClearsDownloadState() => Run(async (shell, tab, s) =>
    {
        await WithPackService(tab, s, async (store, service) =>
        {
            using var http = new HttpClient(new PackHttp { RateLimited = true });
            ContentPackService.Current = new ContentPackService(store, http, () => "https://packs.test", () => s, () => { }, () => "sandbox-token");
            await tab.RefreshPacksAsync();
            var card = (PackCardViewModel)tab.PackCardsItemsControl.Items[1]!;
            var messages = new List<(string Title, string Body)>();
            tab.Confirm = (_, _) => Task.FromResult(true);
            tab.Inform = (title, body) => { messages.Add((title, body)); return Task.CompletedTask; };
            await tab.ChangePackDownloadAsync(card);
            var message = Assert.Single(messages);
            Assert.Equal(Loc.Get("title_download_limit_reached"), message.Title);
            Assert.Contains("sandbox quota reached", message.Body);
            Assert.False(card.IsDownloading);
            Assert.Equal(0, card.DownloadProgress);
            Assert.False(store.IsPackInstalled(card.Id));
        });
    });

    [Fact]
    public Task PackProgressNotificationsReturnToTheUiThread() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var model = new ContentPack();
        var card = new PackCardViewModel(model);
        var threads = new List<bool>();
        card.PropertyChanged += (_, _) => threads.Add(Dispatcher.UIThread.CheckAccess());
        await Task.Run(() => { model.IsDownloading = true; model.DownloadProgress = 37; });
        Dispatcher.UIThread.RunJobs();
        Assert.NotEmpty(threads);
        Assert.All(threads, onUi => Assert.True(onUi));
        Assert.Equal("Downloading... 37%", card.DownloadButtonText);
        Assert.False(card.IsNotDownloading);
    });

    private static async Task WithPackService(AssetsTabView tab, AppSettings s,
        Func<ContentPackStore, ContentPackService, Task> action)
    {
        var root = ConditioningControlPanel.CorePaths.EffectiveAssets;
        var dir = Path.Combine(root, ".packs", "catalogue-test");
        Directory.CreateDirectory(Path.Combine(dir, "content"));
        var file = new PackFileEntry { OriginalName = "face.png", ObfuscatedName = "one.enc", Extension = ".png", FileType = "image" };
        File.WriteAllBytes(Path.Combine(dir, "content", file.ObfuscatedName),
            PackEncryptionService.Encrypt(File.ReadAllBytes(Path.Combine(root, "images", "a.png"))));
        PackEncryptionService.SaveEncryptedManifest(Newtonsoft.Json.JsonConvert.SerializeObject(new InstalledPackManifest
        {
            PackId = "basic-bimbo-starter", PackGuid = "catalogue-test", PackName = "Basic Bimbo Starter Pack", Files = new() { file }
        }), Path.Combine(dir, ".manifest.enc"));
        var oldStore = ContentPackStore.Current;
        var oldService = ContentPackService.Current;
        var store = new ContentPackStore(() => root, () => s, () => { });
        using var http = new HttpClient(new PackHttp());
        var service = new ContentPackService(store, http, () => "https://packs.test", () => s, () => { }, () => "sandbox-token");
        ContentPackStore.Current = store;
        ContentPackService.Current = service;
        try { await action(store, service); }
        finally { ContentPackStore.Current = oldStore; ContentPackService.Current = oldService; }
    }

    private sealed class PackHttp : HttpMessageHandler
    {
        internal bool RateLimited;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (RateLimited && request.RequestUri!.AbsolutePath == "/pack/download-url")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(
                    Newtonsoft.Json.JsonConvert.SerializeObject(new { message = "sandbox quota reached", resetTime = DateTime.UtcNow.AddHours(3) })) });
            if (request.RequestUri!.AbsolutePath == "/pack/download-url")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"success\":true,\"downloadUrl\":\"https://packs.test/clip.zip\"}", Encoding.UTF8, "application/json") });
            if (request.RequestUri.AbsolutePath != "/clip.zip")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); // real built-in fallback
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                using var entry = zip.CreateEntry("images/face.png", CompressionLevel.NoCompression).Open();
                entry.Write(new byte[2048]);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ms.ToArray()) });
        }
    }

    [Fact]
    public Task CatalogueRefreshReplacesScaffoldCards() => Run(async (shell, tab, s) =>
    {
        var oldStore = ContentPackStore.Current;
        var oldService = ContentPackService.Current;
        var store = new ContentPackStore(() => ConditioningControlPanel.CorePaths.EffectiveAssets, () => s, () => { });
        ContentPackStore.Current = store;
        ContentPackService.Current = new ContentPackService(store, baseUrl: () => null);
        try
        {
            tab.BtnRefreshPacks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Pump(() => tab.PackCardsItemsControl.Items.Count == 2);
            Assert.Equal("Basic Bimbo Starter Pack", ((PackCardViewModel)tab.PackCardsItemsControl.Items[0]!).Name);
        }
        finally { ContentPackStore.Current = oldStore; ContentPackService.Current = oldService; }
    });

    // ---- harness ----------------------------------------------------------------------

    private static void PressSpace(Window shell, Control target)
    {
        target.Focus();
        shell.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        shell.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<T> Realized<T>(Window shell, Func<T?> probe) where T : class
    {
        for (var i = 0; i < 50; i++)
        {
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();
            if (probe() is { } hit) return hit;
            await Task.Yield();
        }
        throw new InvalidOperationException("control never realized");
    }

    private static async Task Pump(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(done());
    }

    private static void Png(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bmp = new SkiaSharp.SKBitmap(8, 8);
        bmp.Erase(SkiaSharp.SKColors.HotPink);
        using var data = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static Task Run(Func<MainShellWindow, AssetsTabView, AppSettings, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var root = Directory.CreateTempSubdirectory("ccp-assets-tab-").FullName;
        var assets = Path.Combine(root, "assets");
        Png(Path.Combine(assets, "images", "a.png"));
        Png(Path.Combine(assets, "images", "b.png"));
        Png(Path.Combine(assets, "images", "sub", "c.png"));
        Directory.CreateDirectory(Path.Combine(assets, "videos"));
        File.WriteAllBytes(Path.Combine(assets, "videos", "v.mp4"), new byte[16]);
        File.WriteAllBytes(Path.Combine(assets, "videos", "w.mp4"), new byte[16]);
        var provider = ConditioningControlPanel.CorePaths.EffectiveAssetsProvider;
        var oldSettings = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var svc = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => svc;
        ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = () => assets;
        svc.Current.DisabledAssetPaths.Clear();
        svc.Current.DisabledAssetFolders.Clear();
        svc.Current.AssetPresets.Clear();
        svc.Current.CurrentAssetPresetId = null;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            shell.OpenNavSection("library");
            Dispatcher.UIThread.RunJobs();
            shell.PageStrip!.PillFor("assets")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<AssetsTabView>("AssetsTab")!;
            Assert.True(tab.IsVisible);
            await body(shell, tab, svc.Current);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            svc.SealForReset();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldSettings;
            ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = provider;
            try { Directory.Delete(root, true); } catch { }
        }
    });
}

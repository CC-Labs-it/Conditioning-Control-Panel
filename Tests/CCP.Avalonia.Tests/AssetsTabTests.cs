using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public Task SpiralPickerConfirmsAfterSavingAndCancelLeavesTheChoiceAlone() => Run(async (shell, tab, s) =>
    {
        var control = new ConditioningControlPanel.Avalonia.Views.Features.SpiralFeatureControl();
        var host = new Window { Content = control };
        host.Show(shell);
        var previous = s.SpiralPath;
        var notices = new List<string>();
        try
        {
            var path = Path.Combine(ConditioningControlPanel.CorePaths.EffectiveAssets, "images", "a.png");
            control.SpiralFilePicker = () => Task.FromResult<string?>(path);
            control.SpiralSelectionNotice = (title, message) =>
            {
                Assert.Equal(path, s.SpiralPath); // save/repaint precede the confirmation
                Assert.Equal("Spiral Selected", title);
                notices.Add(message);
                return Task.CompletedTask;
            };
            var button = control.FindControl<Button>("BtnSelectGif")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await control.SpiralSelectionTask;
            Assert.Equal(new[] { "Selected: a.png" }, notices);
            control.SpiralFilePicker = () => Task.FromResult<string?>(null);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await control.SpiralSelectionTask;
            Assert.Equal(path, s.SpiralPath);
            Assert.Single(notices);
        }
        finally { host.Close(); s.SpiralPath = previous; }
    });

    [Fact]
    public Task InstalledPacksHaveTypedFoldersCountsSelectionsAndPreviews() => Run(async (shell, tab, s) =>
    {
        var root = Directory.CreateTempSubdirectory("ccp-assets-pack-").FullName;
        var old = ContentPackStore.Current;
        var manifest = new InstalledPackManifest { PackId = "test-pack", PackName = "Test pack" };
        var packDir = Path.Combine(root, ".packs", "pack-folder");
        Directory.CreateDirectory(Path.Combine(packDir, "content"));
        Png(Path.Combine(packDir, "content", "a.enc"));
        Png(Path.Combine(packDir, "content", "z.enc"));
        File.WriteAllBytes(Path.Combine(packDir, "content", "v.enc"), new byte[16]);
        File.WriteAllText(Path.Combine(packDir, ContentPackStore.ManifestFileName), "fixture");
        foreach (var (name, obfuscated, type) in new[] { ("z.png", "z.enc", "image"), ("a.png", "a.enc", "image"), ("v.mp4", "v.enc", "video") })
            manifest.Files.Add(new PackFileEntry { OriginalName = name, ObfuscatedName = obfuscated, FileType = type, Extension = Path.GetExtension(name) });
        var store = new ContentPackStore(() => root, () => s, () => { },
            _ => Newtonsoft.Json.JsonConvert.SerializeObject(manifest), File.ReadAllBytes);
        ContentPackStore.Current = store;
        try
        {
            s.DisabledAssetPaths.Add("pack:test-pack/z.png");
            shell.ShowTab("settings");
            shell.ShowTab("assets");
            var packs = tab.AssetTree.Single(n => n.IsPackFolder);
            var pack = Assert.Single(packs.Children);
            Assert.Equal("test-pack", pack.Name); // WPF: unknown built-in name falls back to id
            Assert.Equal(new[] { "image", "video" }, pack.Children.Select(n => n.PackFileType));
            Assert.Equal(Loc.GetF("label_0_images_1_videos_active", 4, 3), tab.TxtAssetCounts.Text);
            tab.AssetTreeView.SelectedItem = pack.Children[0];
            await tab.ThumbnailLoads;
            Assert.Equal(new[] { "a.png", "z.png" }, tab.CurrentFolderFiles.Select(f => f.Name));
            Assert.All(tab.CurrentFolderFiles, f => Assert.NotNull(f.Thumbnail));
            Assert.False(tab.CurrentFolderFiles[1].IsChecked);
            Assert.False(Directory.Exists(Path.Combine(root, ".temp"))); // thumbnails decrypt to streams

            var cb = await Realized(shell, () => tab.ThumbnailsItemsControl.GetVisualDescendants().OfType<CheckBox>()
                .FirstOrDefault(c => c.DataContext == tab.CurrentFolderFiles[0]));
            PressSpace(shell, cb);
            Assert.Contains("pack:test-pack/a.png", s.DisabledAssetPaths);
            Assert.False(pack.Children[0].IsChecked);
            Assert.True(pack.IsChecked); // video remains enabled
            tab.BtnSelectAllAssets.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.DoesNotContain("pack:test-pack/z.png", s.DisabledAssetPaths);
            tab.SetFolderEnabled(packs, false);
            Assert.Equal(3, s.DisabledAssetPaths.Count(k => k.StartsWith("pack:test-pack/", StringComparison.Ordinal)));
            Assert.Empty(s.DisabledAssetFolders);
            tab.SetFolderEnabled(packs, true);
            tab.SetFolderEnabled(pack.Children[1], false);
            foreach (var file in tab.CurrentFolderFiles) tab.ToggleFile(file);
            Assert.False(pack.IsChecked); // aggregate counts must not keep an empty group checked
            Assert.False(packs.IsChecked);
            tab.SetFolderEnabled(packs, true);
            var preset = new AssetPreset { Name = "Pack selection", DisabledAssetPaths = new() { "pack:test-pack/z.png" } };
            s.AssetPresets.Add(preset);
            tab.InitializeAssetPresets();
            Assert.Equal(4, preset.EnabledImageCount);
            Assert.Equal(3, preset.EnabledVideoCount);
            Assert.Equal(Loc.GetF("label_0_images_1_videos_active", 5, 3), tab.TxtAssetCounts.Text);

            // Real context-menu handler opens the existing owned player from a decrypted temp.
            var menu = new MenuItem { DataContext = tab.CurrentFolderFiles[0] };
            typeof(AssetsTabView).GetMethod("ThumbnailItem_Preview_Click", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(tab, new object[] { menu, new RoutedEventArgs(MenuItem.ClickEvent) });
            var preview = Assert.Single(shell.OwnedWindows.OfType<MiniPlayerWindow>());
            var temp = Assert.Single(Directory.GetFiles(Path.Combine(root, ".temp")));
            Assert.True(File.Exists(temp));
            PanicSurfaces.All.Single(surface => surface.Id == "asset-preview").Stop(shell);
            Assert.False(preview.IsVisible);
            Assert.False(File.Exists(temp));

            // A refresh keeps the selected virtual folder; inactive packs disappear on the next visit.
            tab.RefreshAssetTree();
            await tab.ThumbnailLoads;
            Assert.Equal(2, tab.CurrentFolderFiles.Count);
            store.DeactivatePack("test-pack");
            tab.RefreshAssetTree();
            Assert.DoesNotContain(tab.AssetTree, n => n.IsPackFolder);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            ContentPackStore.Current = old;
            s.InstalledPackIds.Remove("test-pack");
            s.ActivePackIds.Remove("test-pack");
            s.PackGuidMap.Remove("test-pack");
            try { Directory.Delete(root, true); } catch { }
        }
    });

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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
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

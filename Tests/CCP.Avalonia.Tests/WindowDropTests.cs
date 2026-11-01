using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.SessionIO.cs Window_Drop / ImportDroppedFilesAsync on the Avalonia shell,
/// driven by a real headless drag-and-drop onto the window.</summary>
public sealed class WindowDropTests
{
    [Fact]
    public Task MediaDropConfirmedCopiesIntoTheLibraryAndKeepsTheOriginal() => Run(async (shell, root, assets) =>
    {
        var video = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(video, new byte[] { 1, 2, 3 });

        Drop(shell, video);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
        Assert.Equal(Loc.Get("dlg_media_drop_library"), ButtonText(ask, "BtnOk"));
        Click(ask, "BtnOk");

        var done = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault(d => d != ask));
        Assert.Equal("Imported 1 video", done.FindControl<TextBlock>("TxtMessage")!.Text);
        Assert.True(File.Exists(Path.Combine(assets, "videos", "clip.mp4")));
        Assert.True(File.Exists(video));   // copied, never moved
        Assert.Equal("assets", shell.CurrentTab);
        done.Close();
    });

    [Fact]
    public Task VideoImportRefreshesTheDealtQueueAndAssetTree() => Run(async (shell, root, assets) =>
    {
        var overlay = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance;
        var original = overlay.Scheduler;
        var available = new[] { "old-a.mp4", "old-b.mp4" };
        var scheduler = new MandatoryVideoScheduler(overlay, library: () => available);
        overlay.Scheduler = scheduler;
        try
        {
            Assert.NotNull(scheduler.PickNext()); // leaves one old clip in the dealt queue
            var video = Path.Combine(root, "new.mp4");
            File.WriteAllBytes(video, new byte[] { 1, 2, 3 });
            var imported = Path.Combine(assets, "videos", "new.mp4");
            available = new[] { imported };
            shell.ShowTab("assets"); // a drop while already on the library must refresh its tree

            Drop(shell, video);
            var ask = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
            Click(ask, "BtnOk");
            var done = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault(d => d != ask));

            Assert.Equal(imported, scheduler.PickNext());
            var tab = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.AssetsTabView>("AssetsTab")!;
            Assert.Contains(tab.AssetTree, node => node.Name == "videos" && node.FileCount == 1);
            done.Close();
        }
        finally { overlay.Scheduler = original; }
    });

    [Fact]
    public Task ImageImportDoesNotResetTheVideoQueue() => Run(async (shell, root, _) =>
    {
        var overlay = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance;
        var original = overlay.Scheduler;
        var available = new[] { "old-a.mp4", "old-b.mp4" };
        var scheduler = new MandatoryVideoScheduler(overlay, library: () => available);
        overlay.Scheduler = scheduler;
        try
        {
            var first = scheduler.PickNext();
            available = Array.Empty<string>();
            var image = Path.Combine(root, "image.png");
            File.WriteAllBytes(image, new byte[] { 1, 2, 3 });

            Drop(shell, image);
            var done = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
            var next = scheduler.PickNext();
            Assert.NotNull(next);
            Assert.NotEqual(first, next);
            done.Close();
        }
        finally { overlay.Scheduler = original; }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ImportResetsPackVideoShuffleOnlyForVideos(bool isVideo) => Run(async (shell, root, _) =>
    {
        // Plain test manifest and decrypt seam; each temp contains its pack clip's name.
        var pack = Path.Combine(root, "pack-assets", ".packs", "guid");
        var content = Directory.CreateDirectory(Path.Combine(pack, "content")).FullName;
        File.WriteAllText(Path.Combine(pack, ".manifest.enc"), """
            {"PackId":"p1","PackGuid":"guid","Files":[
              {"OriginalName":"a.mp4","ObfuscatedName":"a.enc","FileType":"video","Extension":".mp4"},
              {"OriginalName":"b.mp4","ObfuscatedName":"b.enc","FileType":"video","Extension":".mp4"},
              {"OriginalName":"c.mp4","ObfuscatedName":"c.enc","FileType":"video","Extension":".mp4"}]}
            """);
        foreach (var name in new[] { "a", "b", "c" }) File.WriteAllText(Path.Combine(content, name + ".enc"), name);
        var settings = new ConditioningControlPanel.Models.AppSettings();
        var store = new ContentPackStore(() => Path.Combine(root, "pack-assets"), () => settings, () => { },
            loadManifest: File.ReadAllText, decryptFile: File.ReadAllBytes, tempDir: () => Path.Combine(root, "temps"));
        var pool = new PackMediaPool(ContentPackStore.VideoType, new ZeroRandom(), () => store);
        var overlay = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance;
        var original = overlay.Scheduler;
        var scheduler = new MandatoryVideoScheduler(overlay, library: () => Array.Empty<string>()) { PackVideos = pool };
        overlay.Scheduler = scheduler;
        try
        {
            var firstPath = scheduler.PickNext()!;
            var first = File.ReadAllText(firstPath);
            var dropped = Path.Combine(root, isVideo ? "new.mp4" : "new.png");
            File.WriteAllBytes(dropped, new byte[] { 1, 2, 3 });
            Drop(shell, dropped);
            MessageDialog? ask = null;
            if (isVideo)
            {
                ask = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
                Click(ask, "BtnOk");
            }
            var done = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault(d => d != ask));

            var next = File.ReadAllText(scheduler.PickNext()!);
            if (isVideo) Assert.Equal(first, next); // a new shuffle can deal the first clip again
            else Assert.NotEqual(first, next);      // image import keeps the current video shuffle
            Assert.True(File.Exists(firstPath));    // refreshing does not delete a clip in use
            done.Close();
        }
        finally { overlay.Scheduler = original; pool.CleanupTemps(); }
    });

    private sealed class ZeroRandom : Random
    {
        public override int Next(int maxValue) => 0;
    }

    [Fact]
    public Task MediaDropCancelledImportsNothing() => Run(async (shell, root, assets) =>
    {
        var video = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(video, new byte[] { 1, 2, 3 });

        Drop(shell, video);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
        Click(ask, "BtnCancel");
        await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().Any() ? null : shell);

        Assert.False(File.Exists(Path.Combine(assets, "videos", "clip.mp4")));
        Assert.True(File.Exists(video));
    });

    [Fact]
    public Task EnhancementDropRunsTheRealImport() => Run(async (shell, root, _) =>
    {
        // Tagged but versionless: the shared import batch rejects it (WPF ImportEnhancementFiles), nothing is written.
        var enh = Path.Combine(root, "wave.ccpenh.json");
        File.WriteAllText(enh, "{\"$schema\":\"" + ConditioningControlPanel.Models.Deeper.Enhancement.SchemaTag + "\"}");
        Drop(shell, enh);
        var head = Loc.GetF("deeper_import_failed_fmt", 1);
        await WaitFor(() => shell.Named<Panel>("NotificationHost")!.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.StartsWith(head, StringComparison.Ordinal) == true && t.Text.Contains("wave.ccpenh.json")));
    });

    [Fact]
    public Task UnrecognisedDropToastsAndShowsNoOverlay() => Run(async (shell, root, _) =>
    {
        var txt = Path.Combine(root, "notes.txt");
        File.WriteAllText(txt, "hello");
        Drop(shell, txt, RawDragEventType.DragEnter);
        Assert.False(shell.Named<Border>("GlobalDropOverlay")!.IsVisible);
        Drop(shell, txt);
        await WaitFor(() => Toast(shell, Loc.Get("drop_not_recognised")));
    });

    [Fact]
    public Task AZipDragShowsTheExtractOverlayAndTheDropHidesIt() => Run((shell, root, _) =>
    {
        var zip = Path.Combine(root, "pack.zip");
        File.WriteAllBytes(zip, Array.Empty<byte>());
        Drop(shell, zip, RawDragEventType.DragEnter);
        Assert.True(shell.Named<Border>("GlobalDropOverlay")!.IsVisible);
        Assert.Equal(Loc.Get("label_drop_to_extract_assets"), shell.Named<TextBlock>("DropOverlayTitle")!.Text);
        Assert.Equal("pack.zip", shell.Named<TextBlock>("DropOverlaySubtitle")!.Text);
        Drop(shell, zip, RawDragEventType.DragLeave);
        Assert.False(shell.Named<Border>("GlobalDropOverlay")!.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task SessionDroppedOnTheWindowImportsIntoTheLibrary() => Run((shell, root, _) =>
    {
        var custom = Directory.CreateDirectory(Path.Combine(root, "custom")).FullName;
        var files = new SessionFileService(custom, Directory.CreateDirectory(Path.Combine(root, "built-in")).FullName);
        var manager = new SessionManager(files);
        manager.LoadAllSessions();
        shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.PresetsTabView>("PresetsTab")!.UseSessionManager(manager);
        var definition = ConditioningControlPanel.Models.SessionDefinition.FromSession(ConditioningControlPanel.Models.Session.MorningDrift);
        definition.Id = "window-dropped";
        definition.Name = "Window Dropped";
        var dropped = Path.Combine(root, "window.session.json");
        files.ExportSession(definition, dropped);

        Drop(shell, dropped);

        Assert.Single(Directory.GetFiles(custom));
        Assert.Contains(manager.AllSessions, x => x.Id == "window-dropped");
        return Task.CompletedTask;
    });

    [Fact]
    public Task PresetDroppedOnTheWindowImports() => Run((shell, root, _) =>
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var users = settings.UserPresets.ToList();
        var id = "window-drop-" + Guid.NewGuid().ToString("N");
        try
        {
            var preset = ConditioningControlPanel.Models.Preset.FromSettings(settings, "Window Drop");
            preset.Id = id;
            var file = Path.Combine(root, "window.preset.json");
            File.WriteAllText(file, new PresetFileService().SerializePreset(preset));

            Drop(shell, file);

            Assert.Contains(settings.UserPresets, p => p.Id == id);
            Assert.NotNull(Toast(shell, Loc.GetF("preset_drop_imported_fmt", "Window Drop")));
        }
        finally
        {
            settings.UserPresets.Clear();
            settings.UserPresets.AddRange(users);
            ConditioningControlPanel.CoreSettings.Save();
            foreach (var f in Directory.Exists(PresetFileService.CustomPresetsFolder)
                         ? Directory.GetFiles(PresetFileService.CustomPresetsFolder, id + "*") : Array.Empty<string>())
                File.Delete(f);
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF HandleModDropAsync (ModCatalogue.cs:84): Cancel installs nothing; OK installs and toasts.</summary>
    [Fact]
    public Task ModDropConfirmsByNameAndAuthorThenInstalls() => Run(async (shell, root, _) =>
    {
        var userData = ConditioningControlPanel.CorePaths.UserData;
        var mods = new CoreModsSnapshot();
        var oldSettings = ConditioningControlPanel.CoreSettings.ServiceProvider;
        SettingsService? svc = null;
        try
        {
            svc = new SettingsService();
            ConditioningControlPanel.CoreSettings.ServiceProvider = () => svc;
            global::ConditioningControlPanel.Avalonia.App.StartMods();
            var pkg = Path.Combine(root, "drop.ccpmod");
            using (var zip = System.IO.Compression.ZipFile.Open(pkg, System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("mod.json").Open()))
                w.Write("{\"Id\":\"window-drop-mod\",\"Name\":\"Drop Mod\",\"Version\":\"1.0.0\",\"Author\":\"Tester\"}");
            var installed = Path.Combine(userData, "mods", "window-drop-mod");

            Drop(shell, pkg);
            var ask = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
            Assert.Equal(Loc.GetF("msg_confirm_install_mod_fmt", "Drop Mod", "Tester"), ask.FindControl<TextBlock>("TxtMessage")!.Text);
            Click(ask, "BtnCancel");
            await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().Any() ? null : shell);
            Assert.False(Directory.Exists(installed));

            Drop(shell, pkg);
            Click(await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault()), "BtnOk");
            await WaitFor(() => Toast(shell, Loc.GetF("toast_mod_installed_fmt", "Drop Mod")));
            Assert.True(Directory.Exists(installed));
        }
        finally
        {
            svc?.SaveImmediate();
            svc?.SealForReset();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldSettings;
            mods.Dispose();
            global::ConditioningControlPanel.Avalonia.App.ResetReleaseContent();
            foreach (var f in Directory.GetFiles(userData, "settings*")) File.Delete(f);
            foreach (var dir in new[] { "mods", "builtin_mods" })
                if (Directory.Exists(Path.Combine(userData, dir))) Directory.Delete(Path.Combine(userData, dir), recursive: true);
        }
    });

    // ---- harness ----------------------------------------------------------------------

    private static Task Run(Func<MainShellWindow, string, string, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var root = Directory.CreateTempSubdirectory("ccp-window-drop-").FullName;
        var assets = Path.Combine(root, "assets");
        var provider = ConditioningControlPanel.CorePaths.EffectiveAssetsProvider;
        ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = () => assets;
        var shell = new MainShellWindow();
        var host = shell.Named<Panel>("NotificationHost")!;
        global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(host);
        shell.Show();
        try
        {
            await body(shell, root, assets);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = provider;
            try { Directory.Delete(root, true); } catch { }
        }
    });

    private static void Drop(MainShellWindow shell, string path, RawDragEventType type = RawDragEventType.Drop)
    {
        var file = shell.StorageProvider.TryGetFileFromPathAsync(path).GetAwaiter().GetResult()
                   ?? throw new InvalidOperationException("headless storage provider has no file for " + path);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file));
        // A real drag always enters before it drops; Avalonia delivers the drop to the entered target.
        if (type == RawDragEventType.Drop)
            shell.DragDrop(new Point(400, 300), RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        shell.DragDrop(new Point(400, 300), type, data, DragDropEffects.Copy, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock? Toast(MainShellWindow shell, string text) =>
        shell.Named<Panel>("NotificationHost")!.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == text);

    private static string? ButtonText(Window dialog, string name) =>
        dialog.FindControl<Button>(name)!.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text
        ?? dialog.FindControl<Button>(name)!.Content as string;

    private static void Click(Window dialog, string name) =>
        dialog.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Pumps the UI queue until the side effect exists (the import runs on the pool).</summary>
    private static async Task<T> WaitFor<T>(Func<T?> probe) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            if (probe() is { } hit) return hit;
            await Task.Yield();
        }
        throw new TimeoutException("side effect never appeared");
    }
}

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
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
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

        var oldVideo = ConditioningControlPanel.CoreEngine.Video;
        var stale = true;
        var scheduler = new MandatoryVideoScheduler(new SilentVideoHost(), library: () =>
            stale ? new[] { "stale1.mp4", "stale2.mp4" }
            : Directory.Exists(Path.Combine(assets, "videos")) ? Directory.GetFiles(Path.Combine(assets, "videos")) : Array.Empty<string>());
        // A stale queue must be discarded even when it still has playable entries.
        Assert.NotNull(scheduler.PickNext());
        stale = false;
        ConditioningControlPanel.CoreEngine.Video = scheduler;
        try
        {
        Drop(shell, video);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault());
        Assert.Equal(Loc.Get("dlg_media_drop_library"), ButtonText(ask, "BtnLibrary"));
        Click(ask, "BtnLibrary");

        var done = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
        Assert.Equal("Imported 1 video", done.FindControl<TextBlock>("TxtMessage")!.Text);
        Assert.True(File.Exists(Path.Combine(assets, "videos", "clip.mp4")));
        Assert.True(File.Exists(video));   // copied, never moved
        Assert.Equal("assets", shell.CurrentTab);
        var next = scheduler.PickNext();
        Assert.Equal(Path.Combine(assets, "videos", "clip.mp4"), next);
        done.Close();
        }
        finally { ConditioningControlPanel.CoreEngine.Video = oldVideo; }
    });

    [Fact]
    public Task MediaDropCancelledImportsNothing() => Run(async (shell, root, assets) =>
    {
        var video = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(video, new byte[] { 1, 2, 3 });

        Drop(shell, video);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault());
        Click(ask, "BtnCancel");
        await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().Any() ? null : shell);

        Assert.False(File.Exists(Path.Combine(assets, "videos", "clip.mp4")));
        Assert.True(File.Exists(video));
    });

    [Fact]
    public Task EnhancementDropImportsRevealsAndDeduplicates() => Run(async (shell, root, _) =>
    {
        var name = "drop-" + Guid.NewGuid().ToString("N");
        var enh = Path.Combine(root, "wave.ccpenh.json");
        var project = new Enhancement();
        project.Metadata.Name = name;
        File.WriteAllText(enh, EnhancementSerializer.Save(project));
        string? saved = null;
        var startTimer = MainShellWindow.StartDeeperRevealTimer;
        MainShellWindow.StartDeeperRevealTimer = _ => { };   // the test steps the 1800 ms expiry itself
        try
        {
            Drop(shell, enh);
            saved = Assert.Single(Directory.GetFiles(DeeperLocalLibrary.DefaultFolder, name + "*.ccpenh.json"));
            Assert.True(File.Exists(enh));
            var view = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.DeeperTabView>("DeeperTab")!;
            var model = Assert.IsType<ConditioningControlPanel.Avalonia.Views.Tabs.DeeperTabViewModel>(view.DataContext);
            Assert.True(Assert.Single(model.FilteredEntries.Where(r => r.Entry.FilePath == saved)).IsHighlighted);
            Assert.Equal(root, ConditioningControlPanel.CoreSettings.Current.DeeperLastDirectory);
            Drop(shell, enh);
            Assert.Single(Directory.GetFiles(DeeperLocalLibrary.DefaultFolder, name + "*.ccpenh.json"));
            await WaitFor(() => Toast(shell, Loc.Get("deeper_import_already_in_library")));
            Assert.True(Assert.Single(model.FilteredEntries.Where(r => r.Entry.FilePath == saved)).IsHighlighted);
            shell.PendingDeeperRevealExpiry!();
            Assert.Null(shell.PendingDeeperRevealExpiry);
            Assert.False(Assert.Single(model.FilteredEntries.Where(r => r.Entry.FilePath == saved)).IsHighlighted);
        }
        finally { MainShellWindow.StartDeeperRevealTimer = startTimer; if (saved != null) File.Delete(saved); }
    });

    [Theory]
    [InlineData("clip.mp4", "video")]
    [InlineData("clip.wav", "audio")]
    public Task MediaDropEditOpensAnUnsavedProject(string filename, string type) => Run(async (shell, root, assets) =>
    {
        var media = Path.Combine(root, filename);
        File.WriteAllBytes(media, Array.Empty<byte>());
        Drop(shell, media);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault());
        Assert.Equal(new[] { "BtnPlay", "BtnEdit", "BtnLibrary", "BtnCancel" },
            ask.GetVisualDescendants().OfType<Button>().Select(b => b.Name));
        Click(ask, "BtnEdit");
        var editor = await WaitFor(() => shell.OwnedWindows.OfType<DeeperEditorWindow>().SingleOrDefault());
        var project = editor.Project;
        Assert.Equal(type, project.MediaType);
        Assert.Equal(media, project.MediaSource);
        Assert.False(Directory.Exists(assets));
        editor.ForceClose();
    });

    [Fact]
    public Task MediaDropPlayUsesTheSharedPlayerAndPanicClosesIt() => Run(async (shell, root, _) =>
    {
        var media = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(media, Array.Empty<byte>());
        for (int i = 0; i < 2; i++)
        {
            Drop(shell, media);
            Click(await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault()), "BtnPlay");
            await WaitFor(() => shell.OwnedWindows.OfType<EnhancementPlayerWindow>().SingleOrDefault());
        }
        PanicSurfaces.All.Single(s => s.Id == "deeper-drop-player").Stop(shell);
        Assert.Empty(shell.OwnedWindows.OfType<EnhancementPlayerWindow>());
        Drop(shell, media);
        await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault());
        PanicSurfaces.All.Single(s => s.Id == "media-drop").Stop(shell);
        Assert.Empty(shell.OwnedWindows.OfType<MediaDropDialog>());
    });

    [Fact]
    public Task LauncherDropShowsThePanelAndImports() => Run(async (shell, root, _) =>
    {
        var custom = Directory.CreateDirectory(Path.Combine(root, "custom")).FullName;
        var files = new SessionFileService(custom, Directory.CreateDirectory(Path.Combine(root, "built-in")).FullName);
        var manager = new SessionManager(files);
        manager.LoadAllSessions();
        shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.PresetsTabView>("PresetsTab")!.UseSessionManager(manager);
        var session = ConditioningControlPanel.Models.SessionDefinition.FromSession(ConditioningControlPanel.Models.Session.MorningDrift);
        session.Id = "launcher-drop";
        var dropped = Path.Combine(root, "launcher.session.json");
        files.ExportSession(session, dropped);
        var launcher = LauncherWindow.OpenFor(shell);
        try
        {
            shell.Hide();
            Drop(launcher, dropped);
            await WaitFor(() => manager.AllSessions.FirstOrDefault(s => s.Id == session.Id));
            Assert.True(shell.IsVisible);
            Assert.False(launcher.IsVisible);
            Assert.Single(Directory.GetFiles(custom));
        }
        finally { launcher.Close(); }
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

    private static void Drop(Window shell, string path, RawDragEventType type = RawDragEventType.Drop)
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

    private sealed class SilentVideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }

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

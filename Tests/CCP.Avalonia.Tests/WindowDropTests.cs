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
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.SessionIO.cs Window_Drop / ImportDroppedFilesAsync on the Avalonia shell,
/// driven by a real headless drag-and-drop onto the window.</summary>
public sealed class WindowDropTests
{
    // WPF MainWindow.Presets.cs:822-925: button order, labels and dismiss result.
    [Theory]
    [InlineData("BtnPlay", "Play")]
    [InlineData("BtnEdit", "Edit")]
    [InlineData("BtnOk", "Library")]
    [InlineData("BtnCancel", "Cancel")]
    [InlineData(null, "Cancel")]
    public Task MediaPromptReturnsOnlyTheChosenAction(string? button, string result) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var owner = new Window();
        var dialog = new MediaDropDialog("/tmp/clip.mp4");
        try
        {
            owner.Show();
            var pending = dialog.ShowDialog<MediaDropChoice>(owner);
            Assert.Equal(460, dialog.Width);
            Assert.Equal("clip.mp4", dialog.FindControl<TextBlock>("TxtFileName")!.Text);
            var buttons = dialog.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Equal(new[] { "BtnPlay", "BtnEdit", "BtnOk", "BtnCancel" }, buttons.Select(b => b.Name));
            Assert.Equal(new[] { "play", "edit", "library", "cancel" }.Select(k => Loc.Get("dlg_media_drop_" + k)),
                buttons.Select(b => ButtonText(dialog, b.Name!)));
            if (button == null) dialog.Close(); else Click(dialog, button);
            Assert.Equal(Enum.Parse<MediaDropChoice>(result), await pending);
        }
        finally { dialog.Close(); owner.Close(); }
    });

    [Fact]
    public Task MediaDropConfirmedCopiesIntoTheLibraryAndKeepsTheOriginal() => Run(async (shell, root, assets) =>
    {
        var video = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(video, new byte[] { 1, 2, 3 });

        Drop(shell, video);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault());
        Assert.Equal(Loc.Get("dlg_media_drop_library"), ButtonText(ask, "BtnOk"));
        Click(ask, "BtnOk");

        var done = await WaitFor(() => shell.OwnedWindows.OfType<MessageDialog>().SingleOrDefault());
        Assert.Equal("Imported 1 video", done.FindControl<TextBlock>("TxtMessage")!.Text);
        Assert.True(File.Exists(Path.Combine(assets, "videos", "clip.mp4")));
        Assert.True(File.Exists(video));   // copied, never moved
        Assert.Equal("assets", shell.CurrentTab);
        done.Close();
    });

    // WPF MainWindow.SessionIO.cs:1481; DeeperTab.cs:604-608: reuse player and load media.
    [Fact]
    public Task MediaDropPlayOpensTheFileAndReusesThePlayer() => Run(async (shell, root, assets) =>
    {
        var path = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(path, new byte[16]);
        EnhancementPlayerWindow? first = null;
        for (var i = 0; i < 2; i++)
        {
            Drop(shell, path);
            var ask = await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault());
            Assert.Equal(Path.GetFileName(path), ask.FindControl<TextBlock>("TxtFileName")!.Text);
            Assert.Equal(Loc.Get("dlg_media_drop_play"), ButtonText(ask, "BtnPlay"));
            Click(ask, "BtnPlay");
            var player = await WaitFor(() => shell.OwnedWindows.OfType<EnhancementPlayerWindow>().SingleOrDefault());
            Assert.Equal(path, typeof(EnhancementPlayerWindow).GetField("_lastMediaPathForCreateNew",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(player));
            if (first != null) Assert.Same(first, player);
            first = player;
        }
        Assert.False(Directory.Exists(Path.Combine(assets, "videos")));
        Assert.True(File.Exists(path));
    });

    [Fact]
    public Task AudioDropWithholdsPlayUntilThePlayerHasAnAudioTransport() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dialog = new MediaDropDialog("/tmp/track.mp3");
        Assert.False(dialog.FindControl<Button>("BtnPlay")!.IsVisible);
        Assert.True(dialog.FindControl<Button>("BtnEdit")!.IsVisible);
        dialog.Close();
        return Task.CompletedTask;
    });

    // WPF MainWindow.DeeperTab.cs:649-660: media type, blank project and no saved path.
    [Theory]
    [InlineData("clip.mp4", MediaTypes.Video)]
    [InlineData("track.mp3", MediaTypes.Audio)]
    public Task MediaDropEditStartsAnUnsavedProject(string name, string mediaType) => Run(async (shell, root, assets) =>
    {
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[16]);
        var original = DeeperLocalAudio.Open;
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(null);
        try
        {
            Drop(shell, path);
            var ask = await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault());
            Assert.Equal(Loc.Get("dlg_media_drop_edit"), ButtonText(ask, "BtnEdit"));
            Click(ask, "BtnEdit");
            var editor = await WaitFor(() => shell.OwnedWindows.OfType<DeeperEditorWindow>().SingleOrDefault());
            Assert.Null(editor.LoadedFilePath);
            var project = (Enhancement)typeof(DeeperEditorWindow).GetField("_enhancement",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
            Assert.Equal(mediaType, project.MediaType);
            Assert.Equal(path, project.MediaSource);
            Assert.Empty(project.Metadata.Name ?? "");
            Assert.False(Directory.Exists(Path.Combine(assets, "videos")));
            Assert.True(File.Exists(path));
        }
        finally { DeeperLocalAudio.Open = original; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MediaDropCancelledImportsNothing(bool closeWindow) => Run(async (shell, root, assets) =>
    {
        var video = Path.Combine(root, "clip.mp4");
        File.WriteAllBytes(video, new byte[] { 1, 2, 3 });

        Drop(shell, video);
        var ask = await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().SingleOrDefault());
        if (closeWindow) ask.Close(); else Click(ask, "BtnCancel");
        await WaitFor(() => shell.OwnedWindows.OfType<MediaDropDialog>().Any() ? null : shell);

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
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
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

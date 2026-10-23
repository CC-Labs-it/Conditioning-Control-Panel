using System;
using System.Collections.Generic;
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
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Row shell-file-dialogs (wire-shell-file-dialogs-b): WPF ImportEnhancementFiles on the
/// Avalonia shell, from a real window drop and from the Deeper tab's Import button - copy into the
/// library, dedupe, skip non-enhancements, toast, remember the folder, flash the row.</summary>
public sealed class DeeperImportTests
{
    [Fact]
    public Task DroppedEnhancementIsCopiedOnceThenRecognisedAsADuplicate() => Run(async (shell, root, model) =>
    {
        var src = WriteEnhancement(root, "Drop Wave");
        Drop(shell, src);
        var saved = await WaitFor(() => model.FilteredEntries.FirstOrDefault(r => r.Name == "Drop Wave"));
        Assert.True(File.Exists(src));   // copied, never moved
        Assert.True(DeeperTabViewModel.PathsEqual(Path.GetDirectoryName(saved.Entry.FilePath), DeeperLocalLibrary.DefaultFolder));
        Assert.NotNull(Toast(shell, Loc.GetF("deeper_import_done_one_fmt", Path.GetFileName(saved.Entry.FilePath))));
        Assert.True(saved.IsHighlighted);
        Assert.Contains("highlighted", RowBorder(shell, saved.Entry.FilePath).Classes);
        Assert.Equal(root, CoreSettings.Current.DeeperLastDirectory);

        // The same content again: no "Name (2)" copy, the existing row is pointed at.
        Drop(shell, src);
        await WaitFor(() => Toast(shell, Loc.Get("deeper_import_already_in_library")));
        Assert.Single(model.FilteredEntries, r => r.Name == "Drop Wave");
    });

    [Fact]
    public Task DroppedJsonThatIsNotAnEnhancementIsSkippedQuietly() => Run(async (shell, root, model) =>
    {
        var src = Path.Combine(root, "settings.ccpenh.json");
        File.WriteAllText(src, "{\"volume\": 3}");
        var before = model.FilteredEntries.Count;
        Drop(shell, src);
        await WaitFor(() => Toast(shell, Loc.Get("deeper_import_skipped_not_enh")));
        Assert.Equal(before, model.FilteredEntries.Count);
    });

    [Fact]
    public Task ImportButtonOpensThePickerAtTheLastFolderAndImportsEveryPick() => Run(async (shell, root, model) =>
    {
        var a = WriteEnhancement(root, "Pick One");
        var b = WriteEnhancement(root, "Pick Two");
        string? startedAt = null;
        MainShellWindow.DeeperImportPicker = (_, start) =>
        {
            startedAt = start;
            return Task.FromResult<IReadOnlyList<string>?>(new[] { a, b });
        };
        CoreSettings.Current.DeeperLastDirectory = root;

        var view = shell.Named<DeeperTabView>("DeeperTab")!;
        view.FindControl<Button>("BtnDeeperImport")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => Toast(shell, Loc.GetF("deeper_import_done_many_fmt", 2)));
        Assert.Equal(root, startedAt);
        Assert.Contains(model.FilteredEntries, r => r.Name == "Pick One");
        Assert.Contains(model.FilteredEntries, r => r.Name == "Pick Two");
    });

    private static string WriteEnhancement(string dir, string name)
    {
        var e = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "/tmp/none-" + Guid.NewGuid().ToString("N") + ".mp3" };
        e.Metadata.Name = name;
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + EnhancementLibrary.FileSuffix);
        File.WriteAllText(path, EnhancementSerializer.Save(e));
        return path;
    }

    private static Task Run(Func<MainShellWindow, string, DeeperTabViewModel, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var root = Directory.CreateTempSubdirectory("ccp-deeper-import-").FullName;
        Directory.CreateDirectory(DeeperLocalLibrary.DefaultFolder);
        var libraryBefore = Directory.GetFiles(DeeperLocalLibrary.DefaultFolder).ToHashSet();
        var lastDir = CoreSettings.Current.DeeperLastDirectory;
        var picker = MainShellWindow.DeeperImportPicker;
        var shell = new MainShellWindow();
        global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(shell.Named<Panel>("NotificationHost")!);
        shell.Show();
        shell.ShowTab("deeper");
        Dispatcher.UIThread.RunJobs();
        try
        {
            await body(shell, root, (DeeperTabViewModel)shell.Named<DeeperTabView>("DeeperTab")!.DataContext!);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            MainShellWindow.DeeperImportPicker = picker;
            CoreSettings.Current.DeeperLastDirectory = lastDir;
            foreach (var f in Directory.GetFiles(DeeperLocalLibrary.DefaultFolder).Where(f => !libraryBefore.Contains(f)))
                File.Delete(f);
            try { Directory.Delete(root, true); } catch { }
        }
    });

    private static void Drop(MainShellWindow shell, string path)
    {
        var file = shell.StorageProvider.TryGetFileFromPathAsync(path).GetAwaiter().GetResult()
                   ?? throw new InvalidOperationException("headless storage provider has no file for " + path);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(file));
        shell.DragDrop(new Point(400, 300), RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        shell.DragDrop(new Point(400, 300), RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static Border RowBorder(MainShellWindow shell, string path) =>
        shell.Named<DeeperTabView>("DeeperTab")!.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("deeper-row") && b.DataContext is DeeperLibraryRowVm r
                        && DeeperTabViewModel.PathsEqual(r.Entry.FilePath, path));

    private static TextBlock? Toast(MainShellWindow shell, string text) =>
        shell.Named<Panel>("NotificationHost")!.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == text);

    /// <summary>Pumps the UI queue until the side effect exists, bounded by time (P73).</summary>
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

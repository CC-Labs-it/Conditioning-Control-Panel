using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

/// <summary>Rows shell-deeper-hub / views-tab-deeper / shell-deeper-submissions / shell-deeper-fx:
/// the library list driven from the shell's Deeper tab - keyboard selection, open in the editor
/// (one per file), play (one player), two-step delete with Undo and the trash, the submission
/// badge, the welcome card and the row hover lift. No server is touched.</summary>
public sealed class DeeperHubTests
{
    [Fact]
    public Task ImportRevealsDeduplicatesAndSurvivesReload() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var source = Directory.CreateTempSubdirectory("ccp-import-");
        MainShellWindow? shell = null;
        string? saved = null;
        var oldDir = CoreSettings.Current.DeeperLastDirectory;
        var oldDelay = MainShellWindow.DeeperRevealDelay;
        var reveals = new System.Collections.Generic.List<TaskCompletionSource>();
        try
        {
            MainShellWindow.DeeperRevealDelay = wait =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(1800), wait);
                var t = new TaskCompletionSource(); reveals.Add(t); return t.Task;
            };
            var enhancement = new Enhancement { MediaType = MediaTypes.Audio };
            enhancement.Metadata.Name = "Imported " + Guid.NewGuid().ToString("N");
            var path = Path.Combine(source.FullName, "input.ccpenh.json");
            File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            File.WriteAllText(Path.Combine(source.FullName, "settings.json"), "{}");
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("deeper");
            var view = shell.Named<DeeperTabView>("DeeperTab")!;
            var model = (DeeperTabViewModel)view.DataContext!;
            shell.ImportEnhancementFiles(new[] { path, Path.Combine(source.FullName, "settings.json") });
            var row = model.FilteredEntries.Single(r => r.Name == enhancement.Metadata.Name);
            saved = row.Entry.FilePath;
            Assert.True(File.Exists(saved));
            Assert.True(row.IsHighlighted);
            Assert.Equal(source.FullName, CoreSettings.Current.DeeperLastDirectory);
            model.ReloadLibrary();
            Assert.True(model.FilteredEntries.Single(r => r.Entry.FilePath == saved).IsHighlighted);
            await shell.ImportDroppedFilesAsync(new[] { path });
            Assert.Single(model.FilteredEntries.Where(r => r.Name == enhancement.Metadata.Name));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("highlighted", RowBorder(view, saved).Classes);
            Assert.NotEmpty(reveals);
            Assert.True(model.FilteredEntries.Single(r => r.Entry.FilePath == saved).IsHighlighted);
            foreach (var t in reveals) t.SetResult(); // WPF reveal expires after 1800 ms, including after reload.
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.FilteredEntries.Single(r => r.Entry.FilePath == saved).IsHighlighted);
            model.SetSearch("no matching row " + Guid.NewGuid());
            Assert.Equal(-1, model.Highlight(saved)); // filtered imports never clear the user's filters
        }
        finally
        {
            shell?.Close();
            if (saved != null) File.Delete(saved);
            source.Delete(true);
            CoreSettings.Current.DeeperLastDirectory = oldDir;
            MainShellWindow.DeeperRevealDelay = oldDelay;
        }
    });

    [Fact]
    public Task DurationBackfillIsLocalDeduplicatedCachedAndSorted() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var media = Path.GetTempFileName();
        var a = WriteEnhancement("duration-a");
        var b = WriteEnhancement("duration-b");
        var old = DeeperTabViewModel.DurationProbe;
        var calls = 0;
        var answer = new TaskCompletionSource<double?>();
        try
        {
            foreach (var path in new[] { a, b })
            {
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                enhancement.MediaSource = media;
                File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            }
            DeeperTabViewModel.DurationProbe = path =>
            {
                Assert.Equal(media, path); calls++; return answer.Task;
            };
            var model = new DeeperTabViewModel();
            model.SetSearch("HubTest duration-");
            model.ReloadLibrary();
            Assert.All(model.FilteredEntries, row => Assert.False(row.ShowDuration));
            answer.SetResult(125);
            await model.DurationBackfill.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, calls);
            Assert.All(model.FilteredEntries, row => Assert.Equal("2:05", row.DurationDisplay));
            Assert.True(MediaDurationCache.TryGetCached(media, out var seconds));
            Assert.Equal(125, seconds);
            model.ReloadLibrary();
            Assert.All(model.FilteredEntries, row => Assert.True(row.ShowDuration));
            Assert.Equal(1, calls);
        }
        finally { DeeperTabViewModel.DurationProbe = old; File.Delete(a); File.Delete(b); File.Delete(media); }
    });

    [Fact]
    public Task WatcherRefreshesTheOpenHubAndStopsOnClose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var shell = new MainShellWindow();
        string? path = null;
        try
        {
            shell.Show(); shell.ShowTab("deeper");
            var model = (DeeperTabViewModel)shell.Named<DeeperTabView>("DeeperTab")!.DataContext!;
            path = WriteEnhancement("watcher");
            await Until(model, () => model.FilteredEntries.Any(r => r.Entry.FilePath == path));
            Assert.Contains(model.FilteredEntries, r => r.Entry.FilePath == path);
            File.Delete(path);
            await Until(model, () => !model.FilteredEntries.Any(r => r.Entry.FilePath == path));
            Assert.DoesNotContain(model.FilteredEntries, r => r.Entry.FilePath == path);
        }
        finally { shell.Close(); if (path != null) File.Delete(path); }
    });

    [Fact]
    public Task ShellSeedsTheBundledWelcomeDemoOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        var seeded = settings.HasSeededDeeperDemos;
        var path = Path.Combine(DeeperLocalLibrary.DefaultFolder, "welcome.ccpenh.json");
        var prior = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            File.Delete(path);
            settings.HasSeededDeeperDemos = false;
            var shell = new MainShellWindow();
            try { shell.InitializeDeeperHub(); Assert.True(File.Exists(path)); Assert.True(settings.HasSeededDeeperDemos); }
            finally { shell.Close(); }
            File.WriteAllText(path, "user-edited demo");
            settings.HasSeededDeeperDemos = false;
            shell = new MainShellWindow();
            try { shell.InitializeDeeperHub(); Assert.Equal("user-edited demo", File.ReadAllText(path)); }
            finally { shell.Close(); }
            File.Delete(path);
            shell = new MainShellWindow();
            try { shell.InitializeDeeperHub(); Assert.False(File.Exists(path)); }
            finally { shell.Close(); }
        }
        finally
        {
            settings.HasSeededDeeperDemos = seeded;
            if (prior == null) File.Delete(path); else File.WriteAllBytes(path, prior);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SearchWaitsForTheWpfDebounceAndShowsMissingMediaTooltip() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var path = WriteEnhancement("search-debounce");
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("deeper");
            var view = shell.Named<DeeperTabView>("DeeperTab")!;
            var model = (DeeperTabViewModel)view.DataContext!;
            var row = model.FilteredEntries.Single(r => r.Entry.FilePath == path);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("deeper_hub_tip_media_missing"), row.MediaSourceTooltip);
            view.FindControl<TextBox>("TxtDeeperSearch")!.Text = "no match " + Guid.NewGuid();
            Dispatcher.UIThread.RunJobs();   // TextChanged is raised from the dispatcher queue
            Assert.Contains(model.FilteredEntries, r => r.Entry.FilePath == path);
            Assert.True(view.FlushSearchDebounce(), "search did not wait on the 150 ms debounce");
            Assert.Empty(model.FilteredEntries);
        }
        finally { shell.Close(); File.Delete(path); }
    });

    [Fact]
    public Task FailedDurationProbeIsNotRetriedAndRemoteMediaIsNeverProbed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var media = Path.GetTempFileName();
        var local = WriteEnhancement("failed-duration");
        var remote = WriteEnhancement("remote-duration");
        var old = DeeperTabViewModel.DurationProbe;
        var calls = 0;
        try
        {
            var enhancement = EnhancementSerializer.LoadFromFile(local);
            enhancement.MediaSource = media;
            File.WriteAllText(local, EnhancementSerializer.Save(enhancement));
            enhancement.MediaSource = "https://example.invalid/media.mp3";
            File.WriteAllText(remote, EnhancementSerializer.Save(enhancement));
            DeeperTabViewModel.DurationProbe = path =>
            {
                Assert.Equal(media, path); calls++; return Task.FromResult<double?>(null);
            };
            var model = new DeeperTabViewModel();
            model.SetSearch("HubTest");
            model.ReloadLibrary(); model.ReloadLibrary();
            Assert.Equal(1, calls);
            Assert.False(model.FilteredEntries.Single(r => r.Entry.FilePath == local).ShowDuration);
            Assert.False(model.FilteredEntries.Single(r => r.Entry.FilePath == remote).ShowDuration);
        }
        finally { DeeperTabViewModel.DurationProbe = old; File.Delete(local); File.Delete(remote); File.Delete(media); }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(TimeSpan by) => Now += by.Ticks;
    }

    /// <summary>Completion signal for the watcher's reload, bounded by time (P73), never a sleep.</summary>
    private static Task Until(DeeperTabViewModel model, Func<bool> done)
    {
        if (done()) return Task.CompletedTask;
        var tcs = new TaskCompletionSource();
        System.Collections.Specialized.NotifyCollectionChangedEventHandler? h = null;
        h = (_, _) => { if (done()) { model.FilteredEntries.CollectionChanged -= h; tcs.TrySetResult(); } };
        model.FilteredEntries.CollectionChanged += h;
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static string WriteEnhancement(string name)
    {
        Directory.CreateDirectory(DeeperLocalLibrary.DefaultFolder);
        var path = Path.Combine(DeeperLocalLibrary.DefaultFolder, $"hubtest-{name}-{Guid.NewGuid():N}.ccpenh.json");
        var e = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "/tmp/none.mp3" };
        e.Metadata.Name = "HubTest " + name;
        File.WriteAllText(path, EnhancementSerializer.Save(e));
        return path;
    }

    private static void Key(Control target, Key key, KeyModifiers mods = KeyModifiers.None)
    {
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target });
        Dispatcher.UIThread.RunJobs();
    }

    private static Border RowBorder(DeeperTabView view, string path) =>
        view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("deeper-row") && b.DataContext is DeeperLibraryRowVm r
                        && DeeperTabViewModel.PathsEqual(r.Entry.FilePath, path));

    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    [Fact]
    public Task LibraryRowsSelectOpenPlayAndDeleteWithUndo() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var a = WriteEnhancement("a");
        var b = WriteEnhancement("b");
        var trash = Directory.CreateTempSubdirectory("ccp-trash-").FullName;
        var clock = new SteppedClock();
        var oldConfirm = MainShellWindow.DeeperConfirm;
        MainShellWindow? w = null;
        try
        {
            ConditioningControlPanel.Avalonia.Platform.TrashBin.RootOverride = trash;
            MainShellWindow.DeeperDeleteTime = clock;
            MainShellWindow.DeeperConfirm = (_, _, _) => Task.FromResult(true);
            var toasts = new StackPanel();
            global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(toasts);

            w = new MainShellWindow();
            w.Show();
            w.ShowTab("deeper");                     // every door rescans (WPF TabNavigation.cs:358)
            Dispatcher.UIThread.RunJobs();
            var view = w.Named<DeeperTabView>("DeeperTab")!;
            var vm = (DeeperTabViewModel)view.DataContext!;
            view.FindControl<TextBox>("TxtDeeperSearch")!.Text = "HubTest";   // only this test's rows
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FlushSearchDebounce()); // WPF search debounce is 150 ms.
            Assert.Equal(2, vm.FilteredEntries.Count);

            // Ctrl+F focuses the search box; Down selects the first row and paints it.
            var list = view.FindControl<ItemsControl>("DeeperLibraryList")!;
            Key(view, global::Avalonia.Input.Key.F, KeyModifiers.Control);
            Assert.True(view.FindControl<TextBox>("TxtDeeperSearch")!.IsFocused, "Ctrl+F did not focus search");
            Key(list, global::Avalonia.Input.Key.Down);
            var first = vm.FilteredEntries[0].Entry.FilePath;
            Assert.True(vm.FilteredEntries[0].IsSelected);
            Assert.Contains("selected", RowBorder(view, first).Classes);

            // Enter opens the editor; a second Enter re-activates the same one.
            Key(list, global::Avalonia.Input.Key.Enter);
            Key(list, global::Avalonia.Input.Key.Enter);
            var editors = w.OwnedWindows.OfType<DeeperEditorWindow>()
                .Where(e => DeeperTabViewModel.PathsEqual(e.LoadedFilePath, first)).ToList();
            Assert.Single(editors);
            editors[0].Close();
            Dispatcher.UIThread.RunJobs();

            // The row's play button opens one player on that file; again only brings it forward.
            var row = RowBorder(view, first);
            var play = row.GetVisualDescendants().OfType<Button>().First(x => x.Content is IconGlyph { Kind: IconKind.Play });
            Click(play);
            Click(play);
            var players = w.OwnedWindows.OfType<EnhancementPlayerWindow>().ToList();
            Assert.Single(players);
            Assert.True(DeeperTabViewModel.PathsEqual(players[0].LoadedFilePath, first));
            players[0].Close();

            // Delete: the row goes at once, the file waits out the grace; Undo brings it back.
            var delete = row.GetVisualDescendants().OfType<Button>().First(x => x.Content is IconGlyph { Kind: IconKind.Delete });
            Click(delete);
            Assert.DoesNotContain(vm.FilteredEntries, r => DeeperTabViewModel.PathsEqual(r.Entry.FilePath, first));
            Assert.True(File.Exists(first));
            var undo = toasts.GetLogicalDescendants().OfType<Button>().Last(x => x.Name == "ToastAction");
            Click(undo);
            Assert.Contains(vm.FilteredEntries, r => DeeperTabViewModel.PathsEqual(r.Entry.FilePath, first));

            // Delete again and let the grace run out on the stepped clock: the file is trashed.
            Click(RowBorder(view, first).GetVisualDescendants().OfType<Button>().First(x => x.Content is IconGlyph { Kind: IconKind.Delete }));
            clock.Step(TimeSpan.FromSeconds(5));
            w.CommitDueDeeperDeletes();
            Assert.True(File.Exists(first), "committed before the 6 s grace");
            clock.Step(TimeSpan.FromSeconds(2));
            w.CommitDueDeeperDeletes();
            Assert.False(File.Exists(first));
            Assert.True(File.Exists(Path.Combine(trash, "files", Path.GetFileName(first))));
            Assert.True(File.Exists(Path.Combine(trash, "info", Path.GetFileName(first) + ".trashinfo")));
            Assert.Single(vm.FilteredEntries);
        }
        finally
        {
            w?.Close();
            MainShellWindow.DeeperConfirm = oldConfirm;
            MainShellWindow.DeeperDeleteTime = TimeProvider.System;
            ConditioningControlPanel.Avalonia.Platform.TrashBin.RootOverride = null;
            File.Delete(a);
            File.Delete(b);
            Directory.Delete(trash, true);
        }
    });

    [Fact]
    public Task SubmissionBadgeWelcomeCardAndRowLift() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var path = WriteEnhancement("badge");
        var s = CoreSettings.Current;
        var seenWelcome = s.HasSeenDeeperWelcome;
        var motion = s.MotionLevel;
        var key = MainShellWindow.CanonicalCataloguePathKey(path);
        MainShellWindow? w = null;
        try
        {
            s.HasSeenDeeperWelcome = false;
            s.MotionLevel = MotionLevel.Off;   // the lift snaps, so no transition clock is needed
            s.DeeperSubmissions[key] = new DeeperSubmissionRecord { CatalogueId = "x1", Status = "published" };

            w = new MainShellWindow();
            w.Show();
            w.ShowTab("deeper");
            Dispatcher.UIThread.RunJobs();
            var view = w.Named<DeeperTabView>("DeeperTab")!;
            var vm = (DeeperTabViewModel)view.DataContext!;
            var row = vm.FilteredEntries.Single(r => DeeperTabViewModel.PathsEqual(r.Entry.FilePath, path));
            Assert.True(row.ShowSubmissionBadge);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("deeper_submission_badge_published"), row.SubmissionBadgeLabel);
            Assert.Equal(IconKind.CheckmarkCircle, row.SubmissionBadgeGlyph);

            // Fluent icons L6: the sort-direction chevron flips with the order, and the header
            // wave (now an IconGlyph) is still the control the ambient drift attaches to.
            Assert.Equal(IconKind.ChevronDown, view.DeeperSortDirGlyph.Kind);
            Click(view.FindControl<Button>("BtnDeeperSortDir")!);
            Assert.Equal(IconKind.ChevronUp, view.DeeperSortDirGlyph.Kind);
            Click(view.FindControl<Button>("BtnDeeperSortDir")!);
            Assert.IsType<TranslateTransform>(view.DeeperWaveGlyph.RenderTransform);

            // Hover lift: the row rises 2px on enter and settles on leave.
            var border = RowBorder(view, path);
            border.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, border,
                new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), w, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(-2, ((TranslateTransform)border.RenderTransform!).Y);
            border.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, border,
                new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), w, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Assert.Equal(0, ((TranslateTransform)border.RenderTransform!).Y);

            // The welcome card's dismiss folds it and remembers it.
            var card = view.FindControl<Border>("DeeperWelcomeCard")!;
            Assert.True(card.IsVisible);
            Click(view.FindControl<Button>("BtnDeeperWelcomeDismiss")!);
            Assert.False(card.IsVisible);
            Assert.True(s.HasSeenDeeperWelcome);
        }
        finally
        {
            w?.Close();
            s.HasSeenDeeperWelcome = seenWelcome;
            s.MotionLevel = motion;
            s.DeeperSubmissions.Remove(key);
            File.Delete(path);
        }
        return Task.CompletedTask;
    });
}

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
            var authored = EnhancementSerializer.LoadFromFile(a);
            authored.Metadata.MediaDurationSeconds = 30;
            File.WriteAllText(a, EnhancementSerializer.Save(authored));
            model.ReloadLibrary();
            model.SetSort("duration");
            Assert.Equal(new[] { 125.0, 30.0 }, model.FilteredEntries.Select(r => r.Entry.DurationSeconds));
            model.ToggleSortDirection();
            Assert.Equal(new[] { 30.0, 125.0 }, model.FilteredEntries.Select(r => r.Entry.DurationSeconds));
            Assert.Equal("0:30", model.FilteredEntries.Single(r => r.Entry.FilePath == a).DurationDisplay);
        }
        finally { DeeperTabViewModel.DurationProbe = old; File.Delete(a); File.Delete(b); File.Delete(media); }
    });

    [Fact]
    public Task DurationProbeRefreshesAfterFiveResultsAndSweepsReloads() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var folder = Directory.CreateTempSubdirectory("hub-batches-");
        var paths = new System.Collections.Generic.List<string>();
        var old = DeeperTabViewModel.DurationProbe;
        var answers = Enumerable.Range(0, 6).Select(_ => new TaskCompletionSource<double?>()).ToArray();
        var entered = Enumerable.Range(0, 6).Select(_ => new TaskCompletionSource()).ToArray();
        var calls = 0;
        try
        {
            for (var i = 0; i < 6; i++)
            {
                var media = Path.Combine(folder.FullName, i + ".mp3");
                File.WriteAllText(media, "test media");
                var path = WriteEnhancement("batch-" + i);
                paths.Add(path);
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                enhancement.MediaSource = media;
                File.WriteAllText(path, EnhancementSerializer.Save(enhancement));
            }
            DeeperTabViewModel.DurationProbe = _ =>
            {
                var i = calls++;
                entered[i].SetResult();
                return answers[i].Task;
            };
            var model = new DeeperTabViewModel();
            model.SetSearch("HubTest batch-");
            model.ReloadLibrary();
            for (var i = 0; i < 5; i++)
            {
                await entered[i].Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(i + 1, calls); // sequential, never starts the next parse early
                Assert.All(model.FilteredEntries, r => Assert.False(r.ShowDuration));
                answers[i].SetResult(60 + i);
            }
            await entered[5].Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(5, model.FilteredEntries.Count(r => r.ShowDuration));
            // Reload during the last parse preserves cached results and the in-flight operation.
            model.ReloadLibrary();
            answers[5].SetResult(65);
            await model.DurationBackfill.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(6, calls);
            Assert.All(model.FilteredEntries, r => Assert.True(r.ShowDuration));
        }
        finally
        {
            foreach (var answer in answers) answer.TrySetResult(null);
            DeeperTabViewModel.DurationProbe = old;
            foreach (var path in paths) File.Delete(path);
            folder.Delete(true);
        }
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
            // WPF MainWindow.DeeperTab.cs:677: hidden tabs wait for the next door.
            shell.ShowTab("settings");
            path = WriteEnhancement("hidden-watcher");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MainShellWindow).GetMethod("OnDeeperLibraryChanged", flags)!.Invoke(shell, new object?[] { null, EventArgs.Empty });
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(model.FilteredEntries, r => r.Entry.FilePath == path);
            shell.ShowTab("deeper");
            Assert.Contains(model.FilteredEntries, r => r.Entry.FilePath == path);
            var library = typeof(MainShellWindow).GetField("_deeperLibrary", flags)!.GetValue(shell)!;
            shell.Close();
            Assert.True((bool)typeof(EnhancementLibrary).GetField("_disposed", flags)!.GetValue(library)!);
            // A queued event or re-entry after close must not revive the library.
            File.Delete(path);
            typeof(MainShellWindow).GetMethod("OnDeeperLibraryChanged", flags)!.Invoke(shell, new object?[] { null, EventArgs.Empty });
            shell.InitializeDeeperHub();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(model.FilteredEntries, r => r.Entry.FilePath == path);
        }
        finally { shell.Close(); if (path != null) File.Delete(path); }
    });

    [Fact]
    public Task ShellSeedsTheBundledWelcomeDemoOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new ConditioningControlPanel.Services.SettingsService
            { SaveDebounceDueTimeMilliseconds = System.Threading.Timeout.Infinite };
        CoreSettings.ServiceProvider = () => service;
        var settings = service.Current;
        var seeded = settings.HasSeededDeeperDemos;
        var path = Path.Combine(DeeperLocalLibrary.DefaultFolder, "welcome.ccpenh.json");
        var prior = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            Directory.CreateDirectory(DeeperLocalLibrary.DefaultFolder);
            File.Delete(path);
            settings.HasSeededDeeperDemos = false;
            var shell = new MainShellWindow();
            try
            {
                service.SaveImmediate(); // flush constructor work before measuring the seeding save
                shell.InitializeDeeperHub();
                Assert.True(File.Exists(path));
                Assert.True(settings.HasSeededDeeperDemos);
                Assert.True(service.HasPendingSave, "first-run seeding must schedule its settings save");
                service.SaveImmediate();
                Assert.True(new ConditioningControlPanel.Services.SettingsService().Current.HasSeededDeeperDemos);
            }
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
            CoreSettings.ServiceProvider = oldProvider;
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
            var search = view.FindControl<TextBox>("TxtDeeperSearch")!;
            search.Text = "no match " + Guid.NewGuid();
            // Deliver the routed event without draining slow layout/render work past 150 ms.
            search.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent));
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
        var probed = new System.Collections.Generic.List<string>();
        try
        {
            var enhancement = EnhancementSerializer.LoadFromFile(local);
            enhancement.MediaSource = media;
            File.WriteAllText(local, EnhancementSerializer.Save(enhancement));
            enhancement.MediaSource = "https://example.invalid/media.mp3";
            File.WriteAllText(remote, EnhancementSerializer.Save(enhancement));
            DeeperTabViewModel.DurationProbe = path =>
            {
                probed.Add(path); return Task.FromResult<double?>(null);
            };
            var model = new DeeperTabViewModel();
            model.SetSearch("HubTest");
            model.ReloadLibrary(); model.ReloadLibrary();
            Assert.Equal(new[] { media }, probed);
            Assert.False(model.FilteredEntries.Single(r => r.Entry.FilePath == local).ShowDuration);
            Assert.False(model.FilteredEntries.Single(r => r.Entry.FilePath == remote).ShowDuration);
        }
        finally { DeeperTabViewModel.DurationProbe = old; File.Delete(local); File.Delete(remote); File.Delete(media); }
        return Task.CompletedTask;
    });

    /// <summary>Completion signal for the watcher's reload, bounded by time (P73), never a sleep.</summary>
    private static Task Until(DeeperTabViewModel model, Func<bool> done)
    {
        if (done()) return Task.CompletedTask;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        System.Collections.Specialized.NotifyCollectionChangedEventHandler? h = null;
        h = (_, _) => { if (done()) { model.FilteredEntries.CollectionChanged -= h; tcs.TrySetResult(); } };
        model.FilteredEntries.CollectionChanged += h;
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(TimeSpan by) => Now += by.Ticks;
    }

    private static void Setup()
    {
        AmbientBeatLoopsTests.Setup(); // shared Skia + bundled-font setup for Linux headless
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
    public Task LibraryRowsSelectOpenPlayAndDeleteWithUndo() => AvaloniaTestDispatcher.RunAsync(() =>
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
            view.FlushSearchDebounce(); // if rendering already spent 150 ms, the timer applied it
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
        return Task.CompletedTask;
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

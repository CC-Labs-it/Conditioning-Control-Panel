using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class PresetsFocalFxTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ModRefreshKeepsSessionSelectionAndUpdatesCards(bool hidden) => WithPresets((host, view, clock) =>
    {
        var panel = view.FindControl<WrapPanel>("PresetCardsPanel")!;
        var rack = view.FindControl<StackPanel>("SessionRackPanel")!;
        var row = rack.Children.OfType<Border>().First();
        row.Focus(); host.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Settle(clock);
        var title = view.FindControl<TextBlock>("TxtDetailTitle")!.Text;
        var custom = Preset.FromSettings(CoreSettings.Current, "Test preset", "Test description");
        CoreSettings.Current.UserPresets.Add(custom);
        view.RefreshPresetsList();
        var first = panel.Children.OfType<Border>().Single(b => Equals(b.Tag, custom.Id));
        CoreMods.MakeModAwareProvider = text => "changed " + text;
        view.IsVisible = !hidden;
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true)); Settle(clock);
        view.IsVisible = true; Settle(clock);
        var refreshed = panel.Children.OfType<Border>().Single(b => Equals(b.Tag, custom.Id));
        Assert.NotSame(first, refreshed);
        Assert.StartsWith("changed ", ((StackPanel)refreshed.Child!).Children.OfType<TextBlock>().Single().Text);
        Assert.True(view.FindControl<ScrollViewer>("SessionDetailScroller")!.IsVisible);
        Assert.False(view.FindControl<ScrollViewer>("PresetDetailScroller")!.IsVisible);
        Assert.Equal(title, view.FindControl<TextBlock>("TxtDetailTitle")!.Text);
        Assert.NotSame(row, rack.Children.OfType<Border>().First());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ModRefreshUpdatesRackNameAndDescription(bool hidden) => WithPresets((host, view, clock) =>
    {
        var rack = view.FindControl<StackPanel>("SessionRackPanel")!;
        var row = rack.Children.OfType<Border>().First();
        var session = Assert.IsAssignableFrom<Session>(row.Tag);
        Assert.NotEqual($"session_{session.Id}_name", session.LocalizedName);
        Assert.NotEqual($"session_{session.Id}_desc", session.LocalizedDescription);
        // WPF MainWindow.SessionIO.cs:450,472: rack rows read the Core mode-aware copy (PresetNaming), so a
        // mod switch is what moves a built-in row (restack b173, #3937). Hidden or not, the row follows on show.
        var oldMod = CoreMods.ActiveModIdProvider;
        var oldText = CoreMods.MakeModAwareProvider;
        try
        {
            string modId = BuiltInMods.BambiSleepId;
            CoreMods.ActiveModIdProvider = () => modId;
            CoreMods.MakeModAwareProvider = text => text;
            var beforeName = session.GetModeAwareName();
            modId = BuiltInMods.DronificationId;
            Assert.NotEqual(beforeName, session.GetModeAwareName());
            view.IsVisible = !hidden;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true)); Settle(clock);
            view.IsVisible = true; Settle(clock);
            var refreshed = rack.Children.OfType<Border>().Single(r => (r.Tag as Session)?.Id == session.Id);
            var grid = Assert.IsType<Grid>(refreshed.Child);
            var name = grid.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 2);
            var description = grid.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 3);
            var blurb = session.GetModeAwareDescription();
            Assert.Equal(session.GetModeAwareName(), name.Text);
            Assert.Equal(blurb.Split('\n')[0].Trim(), description.Text);
            Assert.Equal(blurb, ToolTip.GetTip(description));
        }
        finally { CoreMods.ActiveModIdProvider = oldMod; CoreMods.MakeModAwareProvider = oldText; }
    });

    [Fact]
    public Task BuiltInPresetNameChangesWithModAndLanguage() => WithPresets((host, view, clock) =>
    {
        var panel = view.FindControl<WrapPanel>("PresetCardsPanel")!;
        var preset = Preset.GetDefaultPresets().Single(p => p.Id == "default-gentle");
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        var oldProvider = CoreMods.ActiveModIdProvider;
        var modId = BuiltInMods.CCPDefaultId;
        string ExpectedName() => Loc.Get(PresetNaming.PresetKey(preset.Id, modId, CoreSettings.Current.InstallDate)!);
        string? DisplayedName() => ((StackPanel)panel.Children.OfType<Border>()
            .Single(b => Equals(b.Tag, preset.Id)).Child!).Children.OfType<TextBlock>().Single().Text;
        try
        {
            CoreMods.ActiveModIdProvider = () => modId;
            LocalizationManager.Instance.SetLanguage("en"); Settle(clock);
            var original = ExpectedName();
            Assert.Equal(original, DisplayedName());
            modId = BuiltInMods.BambiSleepId;
            var modName = ExpectedName();
            Assert.NotEqual(original, modName);
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true)); Settle(clock);
            Assert.Equal(modName, DisplayedName());
            LocalizationManager.Instance.SetLanguage("fr"); Settle(clock);
            var translatedName = ExpectedName();
            Assert.NotEqual(modName, translatedName);
            Assert.Equal(translatedName, DisplayedName());
        }
        finally
        {
            CoreMods.ActiveModIdProvider = oldProvider;
            LocalizationManager.Instance.SetLanguage(oldLanguage); Settle(clock);
        }
    });

    [Fact]
    public Task LanguageAndModRefreshSelectedPresetDetails() => WithPresets((host, view, clock) =>
    {
        var panel = view.FindControl<WrapPanel>("PresetCardsPanel")!;
        var preset = Preset.FromSettings(CoreSettings.Current, "Custom name", "Custom description");
        CoreSettings.Current.UserPresets.Add(preset);
        view.RefreshPresetsList();
        typeof(PresetsTabView).GetMethod("SelectPreset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, new object[] { preset }); Settle(clock);
        CoreMods.MakeModAwareProvider = text => "changed " + text;
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true)); Settle(clock);
        Assert.Equal("changed Custom name", view.FindControl<TextBlock>("TxtDetailTitle")!.Text);
        Assert.Equal("changed Custom description", view.FindControl<TextBlock>("TxtDetailSubtitle")!.Text);
        var chip = panel.Children.OfType<Border>().Single(b => Equals(b.Tag, preset.Id));
        var name = ((StackPanel)chip.Child!).Children.OfType<TextBlock>().Single();
        Assert.Equal("changed Custom name", name.Text);
        Assert.Equal("changed Custom description", ToolTip.GetTip(name));
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage("de"); Settle(clock);
            Assert.NotSame(chip, panel.Children.OfType<Border>().Single(b => Equals(b.Tag, preset.Id)));
            Assert.Equal("changed Custom name", view.FindControl<TextBlock>("TxtDetailTitle")!.Text);
            Assert.True(view.FindControl<ScrollViewer>("PresetDetailScroller")!.IsVisible);
        }
        finally { LocalizationManager.Instance.SetLanguage(oldLanguage); Settle(clock); }
    });

    [Fact]
    public Task LanguageRefreshesUnselectedPresetCards() => WithPresets((host, view, clock) =>
    {
        var panel = view.FindControl<WrapPanel>("PresetCardsPanel")!;
        var chip = panel.Children.OfType<Border>().First(b => b.Tag is string);
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage("de"); Settle(clock);
            Assert.NotSame(chip, panel.Children.OfType<Border>().First(b => b.Tag is string));
        }
        finally { LocalizationManager.Instance.SetLanguage(oldLanguage); Settle(clock); }
    });

    [Fact]
    public Task SelectedCardOwnsOneSheenAndItFollowsSelectionAndGates() => WithPresets((host, view, animation) =>
    {
        var clock = new Clock(); var oldTime = FxAdorner.Time; FxAdorner.Time = clock;
        try
        {
            Assert.Empty(Sheens(host));
            var panel = view.FindControl<WrapPanel>("PresetCardsPanel")!;
            Click(host, panel.Children.OfType<Border>().First(b => b.Tag is string)); Settle(animation);
            var first = Assert.Single(Sheens(host));
            Assert.True(first.IsTicking);
            clock.Now += TimeSpan.TicksPerSecond / 2; first.Tick();
            Assert.True(first.BandOffset > 0);
            Click(host, panel.Children.OfType<Border>().Where(b => b.Tag is string).Skip(1).First()); Settle(animation);
            Assert.False(first.IsTicking);
            Assert.NotSame(first, Assert.Single(Sheens(host)));
            var row = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
            row.Focus(); host.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Settle(animation);
            var sessionSheen = Assert.Single(Sheens(host));
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true)); Settle(animation);
            Assert.False(sessionSheen.IsTicking);
            Assert.Single(Sheens(host));
            view.IsVisible = false; Settle(animation); Assert.Empty(Sheens(host));
            view.IsVisible = true; Settle(animation); Assert.Single(Sheens(host));
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); Assert.Empty(Sheens(host));
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); Assert.Single(Sheens(host));
            host.WindowState = WindowState.Minimized; Settle(animation); Assert.Empty(Sheens(host));
            host.WindowState = WindowState.Normal; Settle(animation); Assert.Single(Sheens(host));
            CoreSettings.Current.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); Assert.Empty(Sheens(host));
            CoreSettings.Current.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            var last = Assert.Single(Sheens(host));
            host.Content = null; Settle(animation); Assert.False(last.IsTicking); Assert.Empty(Sheens(host));
        }
        finally { FxAdorner.Time = oldTime; }
    });

    [Fact]
    public Task PointerLiftAndPrimaryButtonSquishMatchWpfSnapAtOff() => WithPresets((host, view, animation) =>
    {
        var entrances = Assert.IsType<System.Threading.CancellationTokenSource>(typeof(PresetsTabView).GetField("_entranceCancellation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view));
        animation.Step(1000); Settle(animation); // finish the entrance before probing hover/press geometry
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.True(entrances.IsCancellationRequested);   // OnMotionGateChanged: motion off clears the arrival in flight, as WPF does
        // Show ran the Full-motion arrival, which owns TranslateTransform.Y at animation priority for ~260 ms of
        // real time and masks the hover's -2. Re-arriving at Off cancels it and snaps, as a tab switch would.
        view.IsVisible = false; view.IsVisible = true; Settle(animation);
        var chip = view.FindControl<WrapPanel>("PresetCardsPanel")!.Children.OfType<Border>().First(b => b.Tag is string);
        host.MouseMove(Center(host, chip)); Settle(animation);
        var scale = Assert.IsType<TransformGroup>(chip.RenderTransform).Children.OfType<ScaleTransform>().Single();
        Assert.Equal(1.02, scale.ScaleX); Assert.Equal(1.02, scale.ScaleY);
        host.MouseMove(new Point(1, 1)); Settle(animation); Assert.Equal(1, scale.ScaleX);
        var row = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
        host.MouseMove(Center(host, row)); Settle(animation);
        var group = Assert.IsType<TransformGroup>(row.RenderTransform);
        Assert.Equal(-2, group.Children.OfType<TranslateTransform>().Single().Y);
        Assert.Equal(1, group.Children.OfType<ScaleTransform>().Single().ScaleX);
        host.MouseMove(new Point(1, 1)); Settle(animation); Assert.Equal(0, group.Children.OfType<TranslateTransform>().Single().Y);
        Click(host, chip); Settle(animation);
        var load = view.FindControl<Button>("BtnLoadPreset")!;
        host.MouseDown(Center(host, load), MouseButton.Left, RawInputModifiers.None); Settle(animation);
        var press = Assert.IsType<TransformGroup>(load.RenderTransform).Children.OfType<ScaleTransform>().Single();
        Assert.Equal(0.97, press.ScaleX);
        host.MouseMove(new Point(1, 1)); host.MouseUp(new Point(1, 1), MouseButton.Left, RawInputModifiers.None); Settle(animation);
        Assert.Equal(1, press.ScaleX);
    });

    [Fact]
    public Task TurningMotionOffCancelsAnInFlightArrivalBeforeHover() => WithPresets((host, view, animation) =>
    {
        view.IsVisible = false; Settle(animation);
        view.IsVisible = true; Settle(animation);
        var row = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
        Assert.True(row.Opacity < 1); // The entrance is actually in progress.
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Settle(animation);
        Assert.Equal(1, row.Opacity);
        var slide = Assert.IsType<TransformGroup>(row.RenderTransform).Children.OfType<TranslateTransform>().Single();
        Assert.Equal(0, slide.Y);
        host.MouseMove(Center(host, row)); Settle(animation);
        Assert.Equal(-2, slide.Y);
        Settle(animation);
        Assert.Equal(-2, slide.Y); // A later arrival frame must not override the hover.
    });

    [Fact] // WPF replaces only the hovered row's rise, from where it is; its fade and the other rows keep arriving.
    public Task HoverDuringArrivalStartsFromTheVisiblePosition() => WithPresets((host, view, animation) =>
    {
        view.IsVisible = false; Settle(animation);
        view.IsVisible = true; Settle(animation);
        var rows = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().ToArray();
        var row = rows[0];
        var slide = Assert.IsType<TransformGroup>(row.RenderTransform).Children.OfType<TranslateTransform>().Single();
        animation.Attach(slide); // a transform is not in the tree: give its hover transition the stepped clock
        animation.Step(50); Settle(animation);
        var from = slide.Y;
        Assert.True(from > 0, $"Expected a running arrival, got {from}.");
        host.MouseMove(Center(host, row)); Settle(animation);
        Assert.True(row.IsPointerOver);
        Assert.Equal(from, slide.Y, 5);
        Assert.True(row.Opacity < 1, "Hover must not cancel the row's fade.");
        if (rows.Length > 1)
            Assert.True(Assert.IsType<TransformGroup>(rows[1].RenderTransform).Children.OfType<TranslateTransform>().Single().Y > 0,
                "Hover must not cancel another row's arrival.");
        animation.Step(125); Settle(animation);
        Assert.Equal(from + (-2 - from) * 0.75, slide.Y, 4);
        animation.Step(200); Settle(animation);
        Assert.Equal(-2, slide.Y, 4);
    });

    [Fact]
    public Task ArrivalStaggersBothZonesAndSelectionDoesNotReplayTheRail() => WithPresets((host, view, animation) =>
    {
        view.IsVisible = false; Settle(animation);
        view.IsVisible = true; Settle(animation);
        var panel = view.FindControl<WrapPanel>("PresetCardsPanel")!;
        var rack = view.FindControl<StackPanel>("SessionRackPanel")!;
        Assert.Contains(panel.Children.OfType<Border>(), c => c.Opacity < 1);
        Assert.Contains(rack.Children.OfType<Border>(), c => c.Opacity < 1);
        var row = rack.Children.OfType<Border>().First();
        row.Focus(); host.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Settle(animation);
        // Rebuilt chips must not replay; BtnNewPreset keeps finishing its own arrival.
        Assert.All(panel.Children.OfType<Border>().Where(b => b.Tag is string), c => Assert.Equal(1, c.Opacity));
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        view.IsVisible = false; view.IsVisible = true; Settle(animation);
        Assert.All(rack.Children.OfType<Border>(), c =>
        {
            Assert.Equal(1, c.Opacity);
            Assert.Equal(0, Assert.IsType<TransformGroup>(c.RenderTransform).Children.OfType<TranslateTransform>().Single().Y);
        });
    });

    [Fact]
    public Task WeeklyProgressAnimatesChangesButSeatsResizesAndOffImmediately() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); var animation = new TestAnimationClock(); var settings = CoreSettings.Current; var old = settings.MotionLevel; var oldQuests = AvApp.Quests;
        var dir = Directory.CreateTempSubdirectory("ccp-weekly-fx-").FullName;
        var quests = new QuestService(null, dir);
        Window? host = null;
        try
        {
            settings.MotionLevel = MotionLevel.Full; AvApp.Quests = quests;
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            var view = new QuestsTabView(); host = new Window { Width = 1200, Height = 900, Content = view }; animation.Attach(host); host.Show(); Settle(animation);
            var track = view.FindControl<Border>("WeeklyProgressTrack")!; var fill = view.FindControl<Border>("WeeklyProgressFill")!;
            track.Width = 300; Settle(animation); // separate progress from the counter text's layout changes
            var target = QuestDefinition.WeeklyQuests.Single(d => d.Id == "flash_monsoon_w").TargetValue;
            quests.Progress.WeeklyQuest.CurrentProgress = target / 2;
            view.RefreshQuestUI(); Settle(animation); Assert.True(fill.Width < track.Bounds.Width * 0.5, $"fill {fill.Width}, track {track.Bounds.Width}, visible {fill.IsEffectivelyVisible}");
            track.Width = 200; Settle(animation); Assert.Equal(track.Bounds.Width * 0.5, fill.Width, 3);
            settings.MotionLevel = MotionLevel.Off;
            quests.Progress.WeeklyQuest.CurrentProgress = target;
            view.RefreshQuestUI(); Settle(animation); Assert.Equal(track.Bounds.Width, fill.Width, 3);
        }
        finally { host?.Close(); Settle(animation); AvApp.Quests = oldQuests; settings.MotionLevel = old; quests.Dispose(); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ArrivalSyncsDraftsAndRecentOrderBeforeTheStagger() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); var animation = new TestAnimationClock(); var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode, settings.SessionRackSort, settings.SessionRackSourceFilter);
        var root = Directory.CreateTempSubdirectory("ccp-presets-sync-").FullName;
        var custom = Path.Combine(root, "custom"); var builtIn = Path.Combine(root, "built-in");
        Directory.CreateDirectory(custom); Directory.CreateDirectory(builtIn);
        Window? host = null;
        try
        {
            (settings.MotionLevel, settings.PerformanceMode, settings.SessionRackSort, settings.SessionRackSourceFilter) =
                (MotionLevel.Full, false, "recent", "all");
            var files = new SessionFileService(custom, builtIn);
            string Write(string id, int minutesAgo)
            {
                var path = Path.Combine(custom, id + ".session.json");
                files.ExportSession(new SessionDefinition { Id = id, Name = id, DurationMinutes = 10, IsAvailable = true }, path);
                File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-minutesAgo));
                return path;
            }
            var a = Write("sync_a", 30); Write("sync_b", 20);
            var manager = new SessionManager(files); manager.LoadAllSessions();
            var added = 0; manager.SessionAdded += _ => added++;
            var view = new PresetsTabView(); view.UseSessionManager(manager);
            host = new Window { Width = 1200, Height = 900, Content = view }; animation.Attach(host); host.Show(); host.Activate(); Settle(animation);
            var rack = view.FindControl<StackPanel>("SessionRackPanel")!;
            string[] Ids() => rack.Children.OfType<Border>().Select(b => ((Session)b.Tag!).Id).Where(id => id.StartsWith("sync_")).ToArray();
            Assert.Equal(new[] { "sync_b", "sync_a" }, Ids());

            // Editing re-dates a file without adding one: the arrival repaint re-sorts "Recent".
            view.IsVisible = false; Settle(animation);
            File.SetLastWriteTime(a, DateTime.Now.AddMinutes(-5));
            view.IsVisible = true; Settle(animation);
            Assert.Equal(new[] { "sync_a", "sync_b" }, Ids());

            // The animation clock stays at its first frame while disk/layout work runs.
            // A draft written behind the manager's back joins on arrival, before the stagger runs.
            view.IsVisible = false; Settle(animation);
            Write("sync_c", 1);
            view.IsVisible = true; Settle(animation);
            Assert.Equal(new[] { "sync_c", "sync_a", "sync_b" }, Ids());
            Assert.Contains(manager.AllSessions, s => s.Id == "sync_c");
            Assert.Equal(0, added);
            // Reproduce a loaded runner: wall time must not choose the frame being asserted.
            System.Threading.Thread.Sleep(600); Settle(animation);
            animation.Step(0); animation.Step(100);
            Assert.Contains(rack.Children.OfType<Border>(), c => c.Opacity < 1);
            Assert.Contains(view.FindControl<StackPanel>("RackSourceChips")!.Children.OfType<ToggleButton>(),
                chip => chip.Content is TextBlock t && t.Text!.EndsWith("  3"));
            animation.Step(1000);
            Assert.All(rack.Children.OfType<Border>(), c => Assert.Equal(1, c.Opacity));
        }
        finally
        {
            host?.Close(); Settle(animation);
            (settings.MotionLevel, settings.PerformanceMode, settings.SessionRackSort, settings.SessionRackSourceFilter) = old;
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });

    private static Task WithPresets(Action<Window, PresetsTabView, TestAnimationClock> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); var animation = new TestAnimationClock(); var settings = CoreSettings.Current; var old = (settings.MotionLevel, settings.PerformanceMode);
        Window? host = null;
        try
        {
            (settings.MotionLevel, settings.PerformanceMode) = (MotionLevel.Full, false);
            var view = new PresetsTabView(); host = new Window { Width = 1200, Height = 900, Content = view };
            animation.Attach(host); host.Show(); host.Activate(); Settle(animation); body(host, view, animation);
        }
        finally { host?.Close(); Settle(animation); (settings.MotionLevel, settings.PerformanceMode) = old; }
        return Task.CompletedTask;
    });
    private static void Setup() { if (Application.Current is null) ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting(); }
    private static CardSheenAdorner[] Sheens(Window host) => host.GetVisualDescendants().OfType<CardSheenAdorner>().ToArray();
    private static Point Center(Window host, Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), host)!.Value;
    private static void Click(Window host, Control c) { var p = Center(host, c); host.MouseDown(p, MouseButton.Left, RawInputModifiers.None); host.MouseUp(p, MouseButton.Left, RawInputModifiers.None); }
    private static void Settle(TestAnimationClock animation) { Dispatcher.UIThread.RunJobs(); animation.Step(animation.Now); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private sealed class Clock : TimeProvider { public long Now; public override long TimestampFrequency => TimeSpan.TicksPerSecond; public override long GetTimestamp() => Now; }
}

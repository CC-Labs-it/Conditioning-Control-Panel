using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
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
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class PresetsFocalFxTests
{
    [Fact]
    public Task SelectedCardOwnsOneSheenAndItFollowsSelectionAndGates() => WithPresets((host, view) =>
    {
        var clock = new Clock(); var oldTime = FxAdorner.Time; FxAdorner.Time = clock;
        try
        {
            Assert.Empty(Sheens(host));
            var panel = view.FindControl<WrapPanel>("PresetCardsPanel")!;
            Click(host, panel.Children.OfType<Border>().First(b => b.Tag is string)); Settle();
            var first = Assert.Single(Sheens(host));
            Assert.True(first.IsTicking);
            clock.Now += TimeSpan.TicksPerSecond / 2; first.Tick();
            Assert.True(first.BandOffset > 0);
            Click(host, panel.Children.OfType<Border>().Where(b => b.Tag is string).Skip(1).First()); Settle();
            Assert.False(first.IsTicking);
            Assert.NotSame(first, Assert.Single(Sheens(host)));
            var row = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
            row.Focus(); host.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Settle();
            var sessionSheen = Assert.Single(Sheens(host));
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true)); Settle();
            Assert.False(sessionSheen.IsTicking);
            Assert.Single(Sheens(host));
            view.IsVisible = false; Settle(); Assert.Empty(Sheens(host));
            view.IsVisible = true; Settle(); Assert.Single(Sheens(host));
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); Assert.Empty(Sheens(host));
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); Assert.Single(Sheens(host));
            host.WindowState = WindowState.Minimized; Settle(); Assert.Empty(Sheens(host));
            host.WindowState = WindowState.Normal; Settle(); Assert.Single(Sheens(host));
            CoreSettings.Current.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged(); Assert.Empty(Sheens(host));
            CoreSettings.Current.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            var last = Assert.Single(Sheens(host));
            host.Content = null; Settle(); Assert.False(last.IsTicking); Assert.Empty(Sheens(host));
        }
        finally { FxAdorner.Time = oldTime; }
    });

    [Fact]
    public Task PointerLiftAndPrimaryButtonSquishMatchWpfSnapAtOff() => WithPresets((host, view) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        var chip = view.FindControl<WrapPanel>("PresetCardsPanel")!.Children.OfType<Border>().First(b => b.Tag is string);
        host.MouseMove(Center(host, chip)); Settle();
        var scale = Assert.IsType<TransformGroup>(chip.RenderTransform).Children.OfType<ScaleTransform>().Single();
        Assert.Equal(1.02, scale.ScaleX); Assert.Equal(1.02, scale.ScaleY);
        host.MouseMove(new Point(1, 1)); Settle(); Assert.Equal(1, scale.ScaleX);
        var row = view.FindControl<StackPanel>("SessionRackPanel")!.Children.OfType<Border>().First();
        host.MouseMove(Center(host, row)); Settle();
        var group = Assert.IsType<TransformGroup>(row.RenderTransform);
        Assert.Equal(-2, group.Children.OfType<TranslateTransform>().Single().Y);
        Assert.Equal(1, group.Children.OfType<ScaleTransform>().Single().ScaleX);
        host.MouseMove(new Point(1, 1)); Settle(); Assert.Equal(0, group.Children.OfType<TranslateTransform>().Single().Y);
        Click(host, chip); Settle();
        var load = view.FindControl<Button>("BtnLoadPreset")!;
        host.MouseDown(Center(host, load), MouseButton.Left, RawInputModifiers.None); Settle();
        var press = Assert.IsType<TransformGroup>(load.RenderTransform).Children.OfType<ScaleTransform>().Single();
        Assert.Equal(0.97, press.ScaleX);
        host.MouseMove(new Point(1, 1)); host.MouseUp(new Point(1, 1), MouseButton.Left, RawInputModifiers.None); Settle();
        Assert.Equal(1, press.ScaleX);
    });

    [Fact]
    public Task ArrivalStaggersBothZonesAndSelectionDoesNotReplayTheRail() => WithPresets((host, view) =>
    {
        view.IsVisible = false; Settle();
        view.IsVisible = true; Settle();
        StepClock.Step(view, 0);
        var panel = view.FindControl<WrapPanel>("PresetCardsPanel")!;
        var rack = view.FindControl<StackPanel>("SessionRackPanel")!;
        Assert.Contains(panel.Children.OfType<Border>(), c => c.Opacity < 1);
        Assert.Contains(rack.Children.OfType<Border>(), c => c.Opacity < 1);
        var row = rack.Children.OfType<Border>().First();
        row.Focus(); host.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, ""); Settle();
        // Rebuilt chips must not replay; BtnNewPreset keeps finishing its own arrival.
        Assert.All(panel.Children.OfType<Border>().Where(b => b.Tag is string), c => Assert.Equal(1, c.Opacity));
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        view.IsVisible = false; view.IsVisible = true; Settle();
        Assert.All(rack.Children.OfType<Border>(), c =>
        {
            Assert.Equal(1, c.Opacity);
            Assert.Equal(0, Assert.IsType<TransformGroup>(c.RenderTransform).Children.OfType<TranslateTransform>().Single().Y);
        });
    });

    [Fact]
    public Task WeeklyProgressAnimatesChangesButSeatsResizesAndOffImmediately() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); var settings = CoreSettings.Current; var old = settings.MotionLevel; var oldQuests = AvApp.Quests;
        var dir = Directory.CreateTempSubdirectory("ccp-weekly-fx-").FullName;
        var quests = new QuestService(null, dir);
        Window? host = null;
        try
        {
            settings.MotionLevel = MotionLevel.Full; AvApp.Quests = quests;
            quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            var view = new QuestsTabView(); host = new Window { Width = 1200, Height = 900, Content = view }; host.Show(); Settle();
            var track = view.FindControl<Border>("WeeklyProgressTrack")!; var fill = view.FindControl<Border>("WeeklyProgressFill")!;
            track.Width = 300; Settle(); // separate progress from the counter text's layout changes
            var target = QuestDefinition.WeeklyQuests.Single(d => d.Id == "flash_monsoon_w").TargetValue;
            quests.Progress.WeeklyQuest.CurrentProgress = target / 2;
            view.RefreshQuestUI(); Settle(); Assert.True(fill.Width < track.Bounds.Width * 0.5, $"fill {fill.Width}, track {track.Bounds.Width}, visible {fill.IsEffectivelyVisible}");
            track.Width = 200; Settle(); Assert.Equal(track.Bounds.Width * 0.5, fill.Width, 3);
            settings.MotionLevel = MotionLevel.Off;
            quests.Progress.WeeklyQuest.CurrentProgress = target;
            view.RefreshQuestUI(); Settle(); Assert.Equal(track.Bounds.Width, fill.Width, 3);
        }
        finally { host?.Close(); Settle(); AvApp.Quests = oldQuests; settings.MotionLevel = old; quests.Dispose(); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ArrivalSyncsDraftsAndRecentOrderBeforeTheStagger() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); var settings = CoreSettings.Current;
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
            var animationClock = new StepClock();
            var view = new PresetsTabView();
            view.SetValue(StepClock.Property, animationClock.Instance);
            view.UseSessionManager(manager);
            host = new Window { Width = 1200, Height = 900, Content = view }; host.Show(); host.Activate(); Settle();
            var rack = view.FindControl<StackPanel>("SessionRackPanel")!;
            string[] Ids() => rack.Children.OfType<Border>().Select(b => ((Session)b.Tag!).Id).Where(id => id.StartsWith("sync_")).ToArray();
            Assert.Equal(new[] { "sync_b", "sync_a" }, Ids());

            // Editing re-dates a file without adding one: the arrival repaint re-sorts "Recent".
            view.IsVisible = false; Settle();
            File.SetLastWriteTime(a, DateTime.Now.AddMinutes(-5));
            view.IsVisible = true; Settle();
            Assert.Equal(new[] { "sync_a", "sync_b" }, Ids());

            // A draft written behind the manager's back joins on arrival, before the stagger runs.
            view.IsVisible = false; Settle();
            Write("sync_c", 1);
            view.IsVisible = true; Settle();
            animationClock.Step(0);
            Assert.Equal(new[] { "sync_c", "sync_a", "sync_b" }, Ids());
            Assert.Contains(manager.AllSessions, s => s.Id == "sync_c");
            Assert.Equal(0, added);
            Assert.Contains(rack.Children.OfType<Border>(), c => c.Opacity < 1);
            Assert.Contains(view.FindControl<StackPanel>("RackSourceChips")!.Children.OfType<ToggleButton>(),
                chip => chip.Content is TextBlock t && t.Text!.EndsWith("  3"));
            // Layout and disk sync may take longer than the entire entrance on CI. Only
            // animation time should decide whether it is in flight or has finished.
            animationClock.Step(0);
            animationClock.Step(1000);
            Settle();
            Assert.All(rack.Children.OfType<Border>(), c => Assert.Equal(1, c.Opacity));
        }
        finally
        {
            host?.Close(); Settle();
            (settings.MotionLevel, settings.PerformanceMode, settings.SessionRackSort, settings.SessionRackSourceFilter) = old;
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });

    private static Task WithPresets(Action<Window, PresetsTabView> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); var settings = CoreSettings.Current; var old = (settings.MotionLevel, settings.PerformanceMode);
        Window? host = null;
        try
        {
            (settings.MotionLevel, settings.PerformanceMode) = (MotionLevel.Full, false);
            var view = new PresetsTabView();
            view.SetValue(StepClock.Property, new StepClock().Instance);
            host = new Window { Width = 1200, Height = 900, Content = view };
            host.Show(); host.Activate(); Settle(); body(host, view);
        }
        finally { host?.Close(); Settle(); (settings.MotionLevel, settings.PerformanceMode) = old; }
        return Task.CompletedTask;
    });
    private static void Setup() { if (Application.Current is null) ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting(); }
    private static CardSheenAdorner[] Sheens(Window host) => host.GetVisualDescendants().OfType<CardSheenAdorner>().ToArray();
    private static Point Center(Window host, Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), host)!.Value;
    private static void Click(Window host, Control c) { var p = Center(host, c); host.MouseDown(p, MouseButton.Left, RawInputModifiers.None); host.MouseUp(p, MouseButton.Left, RawInputModifiers.None); }
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    // Same clock seam as SharedDeeperThemeTests: Avalonia 12 keeps IClock internal.
    // Scope it to this view so layout/render ticks and other tests keep their own clocks.
    private sealed class StepClock
    {
        private const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        private static readonly Type Base = typeof(Animatable).Assembly.GetType("Avalonia.Animation.ClockBase", throwOnError: true)!;
        private static readonly System.Reflection.MethodInfo Pulse = Base.GetMethod("Pulse", Any | System.Reflection.BindingFlags.Instance)!;
        public static readonly AvaloniaProperty Property =
            (AvaloniaProperty)typeof(Animatable).GetField("ClockProperty", Any | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        public readonly object Instance = Activator.CreateInstance(Base, nonPublic: true)!;
        public void Step(double ms) => Pulse.Invoke(Instance, new object[] { TimeSpan.FromMilliseconds(ms) });
        public static void Step(Control view, double ms) => Pulse.Invoke(view.GetValue(Property), new object[] { TimeSpan.FromMilliseconds(ms) });
    }
    private sealed class Clock : TimeProvider { public long Now; public override long TimestampFrequency => TimeSpan.TicksPerSecond; public override long GetTimestamp() => Now; }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class HeaderQuestStampsTests
{
    private static Task Shell(Action<MainShellWindow, QuestService> check,
        MotionLevel motion = MotionLevel.Off, Action<QuestService>? beforeShow = null) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldQuests = AvApp.Quests;
        var oldSettings = CoreSettings.ServiceProvider;
        var dir = Directory.CreateTempSubdirectory("ccp-header-stamps-").FullName;
        var quests = new QuestService(null, dir);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.PerformanceMode = true;
        CoreSettings.Current.SuppressPerkNotifications = true;
        CoreSettings.Current.MotionLevel = motion;
        quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
        quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
        beforeShow?.Invoke(quests);
        AvApp.Quests = quests;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            check(shell, quests);
        }
        finally
        {
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            AvApp.Quests = oldQuests;
            CoreSettings.ServiceProvider = oldSettings;
            quests.Dispose();
            Directory.Delete(dir, recursive: true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HeaderShowsThreeDailyStampsAndTheLargerWeeklyStamp() => Shell((shell, _) =>
    {
        var host = shell.Named<Grid>("QuestStampHost")!;
        Assert.True(host.IsVisible);
        var cells = host.Children.OfType<Panel>().ToArray();
        Assert.Equal(new[] { "d0", "d1", "d2", "w" }, cells.Select(x => x.Tag));
        Assert.Equal(new[] { 20d, 20d, 20d, 23d }, cells.Select(x => x.Width));
        Assert.Equal(22, host.Height);
        Assert.All(cells, c => Assert.IsType<ImageBrush>(c.Children.OfType<Rectangle>().First().Fill));
    });
    private static Panel Cell(MainShellWindow shell, string key) =>
        shell.Named<Grid>("QuestStampHost")!.Children.OfType<Panel>().Single(c => Equals(c.Tag, key));

    private static Popup Hover(MainShellWindow shell, string key)
    {
        shell.MouseMove(new Point(500, 250));
        Dispatcher.UIThread.RunJobs();
        var point = Cell(shell, key).TranslatePoint(new Point(8, 8), shell)!.Value;
        shell.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        return shell.Named<Grid>("QuestStampHost")!.Children.OfType<Popup>().Single();
    }

    [Fact]
    public Task HoverTracksProgressCompletionAndPoseWithoutEffects() => Shell((shell, quests) =>
    {
        var definition = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
        var popup = Hover(shell, "d0");
        void AssertReward()
        {
            var expected = QuestService.ScaledQuestXp(definition.XPReward, CoreSettings.Current,
                quests.StreakPaidOn(definition.Type, CoreSettings.Current));
            Assert.Contains(popup.Child!.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Inlines?.OfType<Run>().Any(r => r.Text == $" {expected} XP") == true);
        }
        AssertReward();
        Assert.True(popup.IsOpen);
        Assert.Same(ConditioningControlPanel.Avalonia.Views.Tabs.QuestsTabView.GetQuestArt(definition),
            popup.Child!.GetVisualDescendants().OfType<Image>().Single().Source);
        var transforms = (TransformGroup)Cell(shell, "d0").RenderTransform!;
        Assert.Equal(1.5, ((ScaleTransform)transforms.Children[0]).ScaleX);
        Assert.Equal(-1, ((RotateTransform)transforms.Children[1]).Angle);
        quests.TrackBubblesPopped(1);
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        Assert.Contains(popup.Child.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == $"{definition.TargetValue - 1} to go");
        quests.TrackBubblesPopped(definition.TargetValue);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(Cell(shell, "d0").Children, c => c is IconGlyph { Kind: FluentIcons.Common.Symbol.Checkmark });
        Assert.Contains(popup.Child.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "done");
        var tickShadow = popup.Child.GetVisualDescendants().OfType<IconGlyph>().Single(g => Equals(g.Tag, "stamp-tick-shadow")); // WPF :674
        Assert.True(tickShadow.IsEffectivelyVisible);
        Assert.Equal(FluentIcons.Common.Symbol.Checkmark, tickShadow.Kind);
        Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(tickShadow.Foreground).Color);
        var tickOffset = Assert.IsType<TranslateTransform>(tickShadow.RenderTransform);
        Assert.Equal(Math.Sqrt(2), tickOffset.X, 6);
        Assert.Equal(Math.Sqrt(2), tickOffset.Y, 6);
        Assert.Same(tickShadow.Parent, popup.Child.GetVisualDescendants().OfType<IconGlyph>()
            .Single(g => g.Kind == FluentIcons.Common.Symbol.Checkmark && !Equals(g.Tag, "stamp-tick-shadow")).Parent);
        AssertReward();
        Assert.DoesNotContain(shell.Named<Grid>("QuestStampHost")!.GetVisualDescendants(), v => v.Effect != null);
        shell.MouseMove(new Point(500, 250));
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
        transforms = (TransformGroup)Cell(shell, "d0").RenderTransform!;
        Assert.Equal(1, ((ScaleTransform)transforms.Children[0]).ScaleX);
        Assert.Equal(-4, ((RotateTransform)transforms.Children[1]).Angle);
    });

    [Fact]
    public Task RefreshRerollLanguageModAndCloseKeepTheHeaderCurrent() => Shell((shell, quests) =>
    {
        var first = Cell(shell, "d1");
        shell.ShowTab("quests");
        var tab = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.QuestsTabView>("QuestsTab")!;
        var budget = quests.GetRemainingDailyRerolls();
        tab.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.DailyQuestCard>("DailyCard1")!
            .FindControl<Button>("BtnReroll")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(budget - 1, quests.GetRemainingDailyRerolls());
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(first, Cell(shell, "d1"));
        var oldArt = ((ImageBrush)Cell(shell, "d0").Children.OfType<Rectangle>().First().Fill!).Source;
        var popup = Hover(shell, "d0");
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen); // WPF RestoreStampHover repaints/reopens while the pointer remains over the cluster.
        Assert.NotSame(oldArt, ((ImageBrush)Cell(shell, "d0").Children.OfType<Rectangle>().First().Fill!).Source);
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var oldLanguage = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(oldLanguage == "de" ? "en" : "de");
            Dispatcher.UIThread.RunJobs();
            popup = Hover(shell, "d0");
            Assert.Contains(popup.Child!.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == (ConditioningControlPanel.Localization.Loc.Get("quest_daily") + " 1").ToUpperInvariant());
        }
        finally { loc.SetLanguage(oldLanguage); }
        shell.Close();
        var cells = shell.Named<Grid>("QuestStampHost")!.Children.ToArray();
        quests.TrackBubblesPopped(1);
        Dispatcher.UIThread.RunJobs();
        Assert.False(popup.IsOpen);
        Assert.Equal(cells, shell.Named<Grid>("QuestStampHost")!.Children.ToArray());
    });

    [Fact]
    public Task GhostsAndMissingServiceCollapseAndOnlyLeftClickNavigates() => Shell((shell, quests) =>
    {
        var before = shell.CurrentTab;
        var point = Cell(shell, "d0").TranslatePoint(new Point(8, 8), shell)!.Value;
        shell.MouseDown(point, MouseButton.Right);
        shell.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, shell.CurrentTab);
        shell.MouseDown(point, MouseButton.Left);
        shell.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("quests", shell.CurrentTab);
        quests.Progress.DailyQuests.Clear();
        quests.Progress.WeeklyQuest = null;
        shell.RefreshQuestStamps();
        Assert.Equal(4, shell.Named<Grid>("QuestStampHost")!.Children.OfType<Panel>().Count());
        Assert.All(shell.Named<Grid>("QuestStampHost")!.Children.OfType<Panel>(),
            c => Assert.NotEmpty(c.Children.OfType<Rectangle>().First().StrokeDashArray!));
        Assert.Contains(Hover(shell, "w").Child!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "No weekly quest yet");
        AvApp.Quests = null;
        shell.InitializeQuestStamps();
        Assert.False(shell.Named<Grid>("QuestStampHost")!.IsVisible);
    });

    private sealed class StampClock : TimeProvider
    {
        private long _now;
        public override long GetTimestamp() => _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(MainShellWindow shell, double ms)
        {
            _now += TimeSpan.FromMilliseconds(ms).Ticks;
            shell.StepStampAnimations();
        }
    }

    private static Task AnimatedShell(Action<MainShellWindow, QuestService, StampClock> check) => Shell((shell, quests) =>
    {
        var previous = MainShellWindow.StampTime;
        var clock = new StampClock();
        MainShellWindow.StampTime = clock;
        try { check(shell, quests, clock); }
        finally
        {
            shell.StopQuestStampsForPanic();
            MainShellWindow.StampTime = previous;
        }
    }, MotionLevel.Reduced); // interaction motion still runs when ambient loops are disabled

    private static ScaleTransform Scale(Panel cell) =>
        (ScaleTransform)((TransformGroup)cell.RenderTransform!).Children[0];

    [Theory]
    [InlineData("d0")]
    [InlineData("w")]
    public Task CompletionPopsOnceAndDetachesTheGoldFlashAt650Ms(string key) => AnimatedShell((shell, quests, clock) =>
    {
        Assert.False(shell.StampAnimationsRunning);
        if (key == "d0")
            quests.TrackBubblesPopped(QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!.TargetValue);
        else
        {
            quests.Progress.WeeklyQuest!.CurrentProgress = quests.GetCurrentWeeklyDefinition()!.TargetValue - 1;
            quests.TrackFlashImage(); // daily progress + weekly completion arrive in the same turn
        }
        Dispatcher.UIThread.RunJobs();
        var cell = Cell(shell, key);
        var flash = cell.Children.OfType<Border>().Single(b => Equals(b.Tag, "stamp-flash"));
        Assert.Equal(0.7, Scale(cell).ScaleX, 6);
        Assert.Equal(0.9, flash.Opacity, 6);
        Assert.Equal(16, flash.BoxShadow[0].Blur);
        Assert.True(shell.StampAnimationsRunning);
        clock.Advance(shell, 94.5);
        Assert.True(Scale(cell).ScaleX > 1.35); // BackEase amplitude 0.6 on the first segment
        clock.Advance(shell, 94.5);
        Assert.Equal(1.35, Scale(cell).ScaleX, 6); // 45% of 420 ms
        clock.Advance(shell, 231);
        Assert.Equal(1, Scale(cell).ScaleX, 6);
        Assert.Equal(0.9 * (1 - 420d / 650), flash.Opacity, 6);
        clock.Advance(shell, 230);
        Assert.DoesNotContain(flash, cell.Children);
        Assert.False(shell.StampAnimationsRunning);
        shell.RefreshQuestStamps();
        Assert.DoesNotContain(Cell(shell, key).Children, c => Equals(c.Tag, "stamp-flash"));
        Assert.False(shell.StampAnimationsRunning); // finished state on a later paint never celebrates again
    });

    [Fact]
    public Task AlreadyCompletedFirstPaintAndMotionOffNeverCelebrate() => Shell((shell, quests) =>
    {
        Assert.Equal(1, Scale(Cell(shell, "d0")).ScaleX);
        Assert.DoesNotContain(Cell(shell, "d0").Children, c => Equals(c.Tag, "stamp-flash"));
        Dispatcher.UIThread.RunJobs();
        var popup = Hover(shell, "d1");
        Assert.Equal(1, popup.Child!.Opacity);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(popup.Child.RenderTransform).Y);
        Assert.Equal(1.5, Scale(Cell(shell, "d1")).ScaleX);
        Assert.False(shell.StampAnimationsRunning);
        for (int i = 0; i < 40; i++) quests.TrackFlashImage();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(Cell(shell, "d1").Children, c => Equals(c.Tag, "stamp-flash"));
        Assert.False(shell.StampAnimationsRunning);
    }, beforeShow: quests => quests.Progress.DailyQuests[0].IsCompleted = true);

    [Fact]
    public Task HoverHostAndPopupFollowWpfTimingsAndSwitchingDoesNotReplayEntrance() => AnimatedShell((shell, _, clock) =>
    {
        var popup = Hover(shell, "d0");
        var visual = Assert.IsType<Panel>(popup.Child);
        var slide = Assert.IsType<TranslateTransform>(visual.RenderTransform);
        var cell = Cell(shell, "d0");
        var hostScale = Assert.IsType<ScaleTransform>(shell.Named<Grid>("QuestStampHost")!.RenderTransform);
        Assert.Equal(0, visual.Opacity);
        Assert.Equal(-7, slide.Y);
        Assert.Equal(1, Scale(cell).ScaleX);
        clock.Advance(shell, 75);
        Assert.Equal(0.5, visual.Opacity, 6);
        Assert.Equal(1.015, hostScale.ScaleX, 6); // quadratic ease-out, 150 ms
        Assert.Equal(1 + 0.5 * (1 - Math.Pow(1 - 75d / 170, 3)), Scale(cell).ScaleX, 6);
        clock.Advance(shell, 75);
        Assert.Equal(1, visual.Opacity);
        Assert.Equal(1.02, hostScale.ScaleX, 6);
        clock.Advance(shell, 20);
        Assert.Equal(1.5, Scale(cell).ScaleX, 6);
        Assert.Equal(-1, ((RotateTransform)((TransformGroup)cell.RenderTransform!).Children[1]).Angle, 6);
        clock.Advance(shell, 10);
        Assert.Equal(0, slide.Y, 6);
        Assert.False(shell.StampAnimationsRunning);
        var shadow = visual.Children.OfType<Border>().Single(b => Equals(b.Tag, "stamp-card-shadow"));
        Assert.Equal(20, shadow.BoxShadow[0].Blur);
        Assert.Equal(5 / Math.Sqrt(2), shadow.BoxShadow[0].OffsetX, 6);
        Assert.Equal(5 / Math.Sqrt(2), shadow.BoxShadow[0].OffsetY, 6); // WPF default direction 315 degrees
        Assert.Equal(140, shadow.BoxShadow[0].Color.A);
        Assert.All(visual.GetVisualDescendants(), v => Assert.Null(v.Effect));

        // Exit d0 and enter d1 in one input turn: the same popup stays open and settled.
        cell.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, cell,
            new Pointer(2, PointerType.Mouse, true), shell, default, 0, new PointerPointProperties(), KeyModifiers.None));
        var next = Cell(shell, "d1");
        next.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, next,
            new Pointer(2, PointerType.Mouse, true), shell, default, 0, new PointerPointProperties(), KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        Assert.Equal(1, visual.Opacity);
        Assert.Equal(0, slide.Y);
        clock.Advance(shell, 100);
        Assert.Equal(1.0625, Scale(cell).ScaleX, 6); // cubic ease-out to 1 over 200 ms
        clock.Advance(shell, 100);
        Assert.Equal(1, Scale(cell).ScaleX);
        Assert.False(shell.StampAnimationsRunning);
    });

    [Fact]
    public Task SwitchingStampsMidEntranceContinuesTheOriginalFadeAndSlide() => AnimatedShell((shell, _, clock) =>
    {
        var popup = Hover(shell, "d0");
        var visual = Assert.IsType<Panel>(popup.Child);
        var slide = Assert.IsType<TranslateTransform>(visual.RenderTransform);
        clock.Advance(shell, 75);
        double y = slide.Y;
        var next = Cell(shell, "d1");
        next.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, next,
            new Pointer(2, PointerType.Mouse, true), shell, default, 0, new PointerPointProperties(), KeyModifiers.None));
        Assert.True(popup.IsOpen);
        Assert.Equal(0.5, visual.Opacity, 6);
        Assert.Equal(y, slide.Y);
        clock.Advance(shell, 105);
        Assert.Equal(1, visual.Opacity);
        Assert.Equal(0, slide.Y, 6);
        clock.Advance(shell, 95);
        Assert.False(shell.StampAnimationsRunning);
    });

    [Fact]
    public Task RefreshCloseAndPanicCancelFiniteWorkAndDismissTheCard() => AnimatedShell((shell, quests, clock) =>
    {
        var def = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
        quests.TrackBubblesPopped(def.TargetValue);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.StampAnimationsRunning);
        shell.RefreshQuestStamps();
        Assert.False(shell.StampAnimationsRunning); // the discarded cell has no remaining clock
        var popup = Hover(shell, "d0");
        Assert.True(shell.StampAnimationsRunning);
        clock.Advance(shell, 100); // mid-zoom and mid-lift, so the resets below are not vacuous
        var hostScale = Assert.IsType<ScaleTransform>(shell.Named<Grid>("QuestStampHost")!.RenderTransform);
        Assert.True(Scale(Cell(shell, "d0")).ScaleX > 1);
        Assert.True(hostScale.ScaleX > 1);
        Assert.Equal(20, Cell(shell, "d0").ZIndex);
        var surface = PanicSurfaces.All.Single(s => s.Id == "quest-stamps");
        Assert.Null(surface.OwnsTheScreen);
        surface.Stop(shell);
        Assert.False(popup.IsOpen);
        Assert.False(shell.StampAnimationsRunning);
        Assert.Equal(1, Scale(Cell(shell, "d0")).ScaleX);
        Assert.Equal(1, hostScale.ScaleX);
        Assert.Equal(0, Cell(shell, "d0").ZIndex);
        popup = Hover(shell, "d1");
        shell.Close();
        Assert.False(popup.IsOpen);
        Assert.False(shell.StampAnimationsRunning);
        clock.Advance(shell, 1000);
        Assert.False(popup.IsOpen);
    });

    [Fact]
    public Task CachedRemoteArtWinsAndModOverridesPrecedeLegacySwaps() => Shell((_, _) =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-stamp-art-");
        var mod = System.IO.Path.Combine(dir.FullName, "mod.png");
        var remote = System.IO.Path.Combine(dir.FullName, "logo.png");
        using (var bmp = new WriteableBitmap(new PixelSize(5, 3), new Vector(96, 96))) bmp.Save(mod);
        using (var bmp = new WriteableBitmap(new PixelSize(7, 3), new Vector(96, 96))) bmp.Save(remote);
        var previous = CoreModArt.OverridePathProvider;
        var def = new QuestDefinition { ImagePath = "pack://application:,,,/Resources/logo.png" };
        try
        {
            CoreSettings.Current.ActiveModId = BuiltInMods.SissyHypnoId;
            CoreModArt.OverridePathProvider = resource => resource == "logo.png" ? mod : null;
            ConditioningControlPanel.Avalonia.Views.Tabs.QuestsTabView.ClearQuestArtCache();
            Assert.Equal(5, ConditioningControlPanel.Avalonia.Views.Tabs.QuestsTabView.GetQuestArt(def)!.PixelSize.Width);
            def.CachedImagePath = remote;
            var image = ConditioningControlPanel.Avalonia.Views.Tabs.QuestsTabView.GetQuestArt(def)!;
            Assert.Equal(7, image.PixelSize.Width);
            Assert.Same(image, ConditioningControlPanel.Avalonia.Views.Tabs.QuestsTabView.GetQuestArt(def));
        }
        finally
        {
            CoreModArt.OverridePathProvider = previous;
            ConditioningControlPanel.Avalonia.Views.Tabs.QuestsTabView.ClearQuestArtCache();
            dir.Delete(true);
        }
    });

}

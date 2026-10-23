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
    private static Task Shell(Action<MainShellWindow, QuestService> check) => AvaloniaTestDispatcher.RunAsync(() =>
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
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        quests.Progress.DailyQuests = new List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
        quests.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
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

    // WPF MainWindow.QuestStamps.cs ZoomStamp :576 (170 ms in), ShowStampPopup :889 (fade 150 / slide 180),
    // MotionFx.HoverLift (1.02), PlayStampStampedPop :540 (420 ms pop, 650 ms gold flash, then detached).
    [Fact]
    public Task StampMotionFollowsWpfTimingsOnASteppedClock() => Shell((shell, quests) =>
    {
        var clock = new SteppedClock();
        MainShellWindow.QuestStampFxTime = clock;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            static ScaleTransform Scale(MainShellWindow s) => (ScaleTransform)((TransformGroup)Cell(s, "d0").RenderTransform!).Children[0];
            void Step(int ms) { clock.Now += TimeSpan.FromMilliseconds(ms).Ticks; shell.StepQuestStampFx(); }
            var popup = Hover(shell, "d0");
            var card = (Border)popup.Child!;
            Assert.Equal(1, Scale(shell).ScaleX);
            Assert.Equal(0, card.Opacity);
            Assert.Equal(-7, ((TranslateTransform)card.RenderTransform!).Y);
            Assert.Equal(20, card.BoxShadow[0].Blur);
            Step(85);
            Assert.InRange(Scale(shell).ScaleX, 1.01, 1.49);
            Step(100);
            Assert.Equal(1.5, Scale(shell).ScaleX);
            Assert.Equal(1, card.Opacity);
            Assert.Equal(0, ((TranslateTransform)card.RenderTransform!).Y);
            Assert.Equal(1.02, ((ScaleTransform)shell.Named<Grid>("QuestStampHost")!.RenderTransform!).ScaleX, 3);

            shell.MouseMove(new Point(500, 250));
            Dispatcher.UIThread.RunJobs();
            Step(300);
            Assert.Equal(1, Scale(shell).ScaleX);
            Assert.Equal(0, shell.QuestStampTweens);

            var definition = QuestDefinition.DailyQuests.Find(d => d.Id == "pop_parade_d")!;
            quests.TrackBubblesPopped(definition.TargetValue);
            Dispatcher.UIThread.RunJobs();
            Border Flash() => Cell(shell, "d0").Children.OfType<Border>().SingleOrDefault(b => Equals(b.Tag, "stamp-flash"))!;
            Assert.Equal(0.7, Scale(shell).ScaleX, 3);
            Assert.Equal(0.9, Flash().Opacity, 3);
            Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0x00), Flash().BoxShadow[0].Color);
            Assert.Null(Cell(shell, "d1").Children.OfType<Border>().FirstOrDefault(b => Equals(b.Tag, "stamp-flash")));
            Step(189);
            Assert.Equal(1.35, Scale(shell).ScaleX, 2);
            Step(231);
            Assert.Equal(1, Scale(shell).ScaleX, 3);
            Assert.NotNull(Flash());
            Step(230);
            Assert.Null(Flash());
            Assert.Equal(0, shell.QuestStampTweens);
        }
        finally { MainShellWindow.QuestStampFxTime = TimeProvider.System; }
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}

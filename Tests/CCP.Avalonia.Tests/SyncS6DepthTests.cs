using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SyncS6DepthTests
{
    private static void Run(Action<Window> test) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
        {
            var fonts = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        }
        var host = new Window { Width = 1200, Height = 900 };
        try { test(host); }
        finally { host.Close(); }
    });

    private static T Part<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), c => c.Name == name);

    private static object Resource(string key) => Application.Current!.FindResource(key)!;

    [Fact]
    public void ProgramTasksFloatAboveTheirGapAndMiniBarsUseTheSharedGrooveAndGloss() => Run(host =>
    {
        var view = new ProgramsTabView();
        var list = view.FindControl<ItemsControl>("TodayTaskList")!;
        var item = new ProgramTaskItem { RowOpacity = 0.55, BarVisible = true };
        var card = (Control)list.ItemTemplate!.Build(item)!;
        card.DataContext = item;
        host.Content = card;
        host.Show();
        host.UpdateLayout();
        var band = Part<Border>(card, "TaskFloatBand");
        Assert.Same(Resource("DepthFloatBand"), band.Background);
        Assert.Equal(0.55, band.Opacity);
        Assert.Equal(-8, band.Margin.Bottom);
        Assert.Equal(10, card.Margin.Bottom);
        Assert.False(band.IsHitTestVisible);
        Assert.Same(Resource("DepthFloatRim"), Part<Border>(card, "TaskFloatRim").BorderBrush);
        Assert.Same(Resource("DepthWellTop"), Part<Border>(card, "TaskGrooveTop").Background);
        Assert.Equal(3, Part<Border>(card, "TaskGrooveTop").Height);
        Assert.Same(Resource("DepthTubeGloss"), Part<Border>(card, "TaskTubeGloss").Background);
    });

    [Fact]
    public void ProgramHeroTubeBeadIsHiddenAtZeroAndGlossIsShared() => Run(host =>
    {
        var view = new ProgramsTabView();
        var bar = new ProgressBar { Theme = (global::Avalonia.Styling.ControlTheme)view.Resources["ProgramHeroBar"]!, Width = 300 };
        host.Content = bar;
        host.Show();
        bar.ApplyTemplate();
        host.UpdateLayout();
        var bead = Part<Ellipse>(bar, "SessionTubeBead");
        Assert.False(bead.IsVisible);
        bar.Value = 50;
        Dispatcher.UIThread.RunJobs();
        Assert.True(bead.IsVisible);
        Assert.Equal(12, bead.Width);
        Assert.Same(Resource("DepthTubeBead"), bead.Fill);
        Assert.Same(Resource("DepthTubeGloss"), Part<Border>(bar, "SessionTubeGloss").Background);
        bar.Value = 0;
        Assert.False(bead.IsVisible);
    });

    [Fact]
    public void LeaderboardWellOverlaysDoNotInterceptInputAndOwnRowSinks() => Run(host =>
    {
        var view = new LeaderboardTabView();
        host.Content = view;
        host.Show();
        host.UpdateLayout();
        Assert.Same(Resource("DepthWellFloorBrush"), view.FindControl<Border>("LbWellFloor")!.Background);
        Assert.False(view.FindControl<Grid>("LbWellBands")!.IsHitTestVisible);
        Assert.Equal(9, view.FindControl<Rectangle>("LbWellTop")!.Height);
        Assert.Equal(6, view.FindControl<Rectangle>("LbWellLeft")!.Width);
        Assert.Equal(1, view.FindControl<Rectangle>("LbWellFoot")!.Height);
        var list = view.FindControl<ListBox>("LstLeaderboard")!;
        CoreAccount.UnifiedUserId = "self";
        list.ItemsSource = new[] { new LeaderboardRow { DisplayName = "other", UnifiedId = "other" },
            new LeaderboardRow { DisplayName = "self", UnifiedId = "self" } };
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var cards = list.GetVisualDescendants().OfType<Border>().Where(c => c.Name == "LbRowCard").ToArray();
        Assert.Equal(2, cards.Length);
        var mine = Assert.Single(cards, c => c.Classes.Contains("me"));
        Assert.Same(Resource("DepthPressedBevel"), mine.BorderBrush);
        Assert.Equal(1, Assert.IsType<TranslateTransform>(mine.RenderTransform).Y);
        var other = Assert.Single(cards, c => !c.Classes.Contains("me"));
        Assert.Same(Resource("DepthFloatRim"), other.BorderBrush);
        var bands = list.GetVisualDescendants().OfType<Rectangle>().Where(c => c.Name == "LbRowFloat").ToArray();
        Assert.False(Assert.Single(bands, c => c.Classes.Contains("me")).IsVisible);
        Assert.True(Assert.Single(bands, c => !c.Classes.Contains("me")).IsVisible);
    });
    [Fact]
    public void EdgeLiftUsesFourThinHueStripsAndMotionRefreshParksOrHidesThem() => Run(host =>
    {
        var shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            shell.Show();
            CoreSettings.Current.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Full;
            shell.ShowTab("presets");
            shell.OnMotionChrome();
            shell.PlaceEdgeLift(4.5);
            var lifts = shell.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Name?.StartsWith("SectionEdgeLift") == true).ToArray();
            Assert.Equal(4, lifts.Length);
            var right = Assert.Single(lifts, b => b.Name == "SectionEdgeLiftRight");
            Assert.Equal(3, right.Width);
            Assert.Equal(0.5, Assert.IsType<LinearGradientBrush>(right.OpacityMask).GradientStops[1].Offset);
            foreach (var lift in lifts) Assert.False(lift.IsHitTestVisible);
            CoreSettings.Current.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Reduced;
            shell.OnMotionChrome();
            var top = Assert.Single(lifts, b => b.Name == "SectionEdgeLiftTop");
            Assert.Equal(3, top.Height);
            Assert.Equal(0.5, Assert.IsType<LinearGradientBrush>(top.OpacityMask).GradientStops[1].Offset);
            CoreSettings.Current.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Off;
            shell.OnMotionChrome();
            Assert.All(lifts, lift => Assert.False(lift.IsVisible));
        }
        finally { shell.Close(); }
    });

    [Fact]
    public void DepthPaintIsBoundBeforeTextLayout() => Run(host =>
    {
        var programs = new ProgramsTabView();
        var item = new ProgramTaskItem { RowOpacity = 0.55, BarVisible = true };
        var card = (Control)programs.FindControl<ItemsControl>("TodayTaskList")!.ItemTemplate!.Build(item)!;
        card.DataContext = item;
        Assert.Same(Resource("DepthFloatBand"), Part<Border>(card, "TaskFloatBand").Background);
        Assert.Equal(0.55, Part<Border>(card, "TaskFloatBand").Opacity);
        Assert.Same(Resource("DepthFloatRim"), Part<Border>(card, "TaskFloatRim").BorderBrush);
        Assert.Same(Resource("DepthWellTop"), Part<Border>(card, "TaskGrooveTop").Background);
        Assert.Same(Resource("DepthTubeGloss"), Part<Border>(card, "TaskTubeGloss").Background);
    });

    [Fact]
    public void LeaderboardPaintIsBoundBeforeTextLayout() => Run(host =>
    {
        var board = new LeaderboardTabView();
        host.Content = board;
        host.ApplyTemplate();
        Assert.Same(Resource("DepthWellFloorBrush"), board.FindControl<Border>("LbWellFloor")!.Background);
        Assert.False(board.FindControl<Grid>("LbWellBands")!.IsHitTestVisible);
        CoreAccount.UnifiedUserId = "self";
        var me = new LeaderboardRow { UnifiedId = "self" };
        var list = board.FindControl<ListBox>("LstLeaderboard")!;
        var row = (Control)Assert.Single(list.DataTemplates, t => t.Match(me)).Build(me)!;
        row.DataContext = me;
        ((Panel)list.Parent!).Children.Add(row);
        host.Content = board;
        foreach (var control in row.GetVisualDescendants().OfType<Control>()) control.ApplyStyling();
        var face = Part<Border>(row, "LbRowCard");
        Assert.Same(Resource("DepthPressedBevel"), face.BorderBrush);
        Assert.Equal(1, Assert.IsType<TranslateTransform>(face.RenderTransform).Y);
        Assert.False(Part<Rectangle>(row, "LbRowFloat").IsVisible);
    });

}

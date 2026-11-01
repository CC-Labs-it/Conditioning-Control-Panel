using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class DeeperEditorTimelineTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Prepare(DeeperEditorWindow editor)
    {
        editor.Show();
        typeof(DeeperEditorWindow).GetField("_totalSeconds", Private)!.SetValue(editor, 100d);
        editor.UpdateLayout();
        typeof(DeeperEditorWindow).GetMethod("RebuildRegionVisuals", Private)!.Invoke(editor, null);
        Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();
    }

    private static Point BandPoint(DeeperEditorWindow editor, Region region, double fraction)
    {
        var rect = editor.TimelineCanvas.Children.OfType<Rectangle>().Single(r => ReferenceEquals(r.Tag, region));
        return rect.TranslatePoint(new Point(rect.Bounds.Width * fraction, rect.Bounds.Height / 2), editor)!.Value;
    }

    private static void Click(DeeperEditorWindow editor, Point p)
    {
        editor.MouseMove(p);
        editor.MouseDown(p, MouseButton.Left);
        editor.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [InlineData(50, 120)]
    [InlineData(600, 400)]
    public Task TimelineHeightRestoresWithWpfBounds(int stored, int expected) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.DeeperEditorTimelineHeight = stored;
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, editor.PreviewTimelineGrid.RowDefinitions[2].Height.Value);
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DraggingTimelineSplitterPersistsHeightForNextEditor() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.DeeperEditorTimelineHeight = 160;
        service.SaveImmediate();
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        DeeperEditorWindow? next = null;
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
            var splitter = editor.TimelineSplitter;
            var start = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, 2), editor)!.Value;
            var end = new Point(start.X, start.Y - 65);
            editor.MouseMove(start); editor.MouseDown(start, MouseButton.Left);
            editor.MouseMove(end); Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
            editor.MouseUp(end, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            var saved = CoreSettings.Current.DeeperEditorTimelineHeight;
            Assert.InRange(saved, 220, 230);
            Assert.True(service.HasPendingSave);
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (new ConditioningControlPanel.Services.SettingsService().Current.DeeperEditorTimelineHeight != saved)
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), "timeline height was not saved");
                await Task.Delay(50);
            }
            editor.ForceClose();
            next = new DeeperEditorWindow(new Enhancement(), null);
            next.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(saved, next.PreviewTimelineGrid.RowDefinitions[2].Height.Value);
        }
        finally
        {
            editor.ForceClose(); next?.ForceClose();
            service.SealForReset(); CoreSettings.ServiceProvider = oldProvider;
        }
    });

    [Fact]
    public Task RegionEndClampsWhileTypingAndNormalizesOnFocusLoss() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var region = new Region { Id = "band", Start = 12, End = 25 };
        var enhancement = new Enhancement(); enhancement.Regions.Add(region);
        var editor = new DeeperEditorWindow(enhancement, null);
        try
        {
            Prepare(editor);
            Click(editor, BandPoint(editor, region, 0.5));
            editor.TxtRegionEnd.Focus();
            editor.TxtRegionEnd.Text = "120"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, region.End);
            editor.TxtRegionEnd.Text = "1"; Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, region.End); // allow intermediate input until focus leaves
            editor.TxtRegionStart.Focus(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(12.05, region.End, 6);
            Assert.Equal("12.05", editor.TxtRegionEnd.Text);
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(0.99)]
    public Task DraggingRuleBandKeepsInspectorInSync(double hit) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var region = new Region { Id = "band", Start = 20, End = 50 };
        var enhancement = new Enhancement(); enhancement.Regions.Add(region);
        enhancement.Rules.Add(new EnhancementRule { RegionConstraint = region.Id });
        var editor = new DeeperEditorWindow(enhancement, null);
        try
        {
            Prepare(editor);
            var start = BandPoint(editor, region, hit);
            var end = new Point(start.X + 40, start.Y);
            editor.MouseMove(start); editor.MouseDown(start, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            editor.MouseMove(end); Dispatcher.UIThread.RunJobs();
            Assert.True(editor.RuleRegionDetails.IsVisible);
            if (hit < 0.1) { Assert.True(region.Start > 20); Assert.Equal(50, region.End); }
            else if (hit > 0.9) { Assert.Equal(20, region.Start); Assert.True(region.End > 50); }
            else { Assert.True(region.Start > 20); Assert.True(region.End > 50); }
            Assert.Equal(region.Start.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), editor.TxtRuleBandStart.Text);
            Assert.Equal(region.End.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), editor.TxtRuleBandEnd.Text);
            editor.MouseUp(end, MouseButton.Left);
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });
}

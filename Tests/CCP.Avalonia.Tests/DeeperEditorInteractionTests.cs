using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DeeperEditorInteractionTests
{
    private static object? Field(DeeperEditorWindow w, string name) =>
        typeof(DeeperEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w);
    private static void Call(DeeperEditorWindow w, string name, params object?[] args)
    {
        typeof(DeeperEditorWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Key(DeeperEditorWindow w, global::Avalonia.Input.Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private static Task Run(Action<DeeperEditorWindow, Enhancement> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var model = new Enhancement();
        var w = new DeeperEditorWindow(model, null);
        try { w.Show(); w.MetadataDrawerToggle.IsChecked = true; Dispatcher.UIThread.RunJobs(); w.BtnPlayPause.Focus(); test(w, model); }
        finally { w.ForceClose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RuleDeleteAndEscapeUseThePrimarySelection() => Run((w, model) =>
    {
        var rule = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 3 } };
        model.Rules.Add(rule);
        Call(w, "SelectRule", rule);
        Key(w, global::Avalonia.Input.Key.Escape);
        Assert.Null(Field(w, "_selectedRule"));
        Assert.Single(model.Rules);
        Call(w, "SelectRule", rule);
        Key(w, global::Avalonia.Input.Key.Delete);
        Assert.Empty(model.Rules);
        w.Undo();
        Assert.Single(((Enhancement)Field(w, "_enhancement")!).Rules);
    });

    [Fact]
    public Task ArrowsNudgeAndClampRegionsWithoutChangingLength() => Run((w, model) =>
    {
        var region = new Region { Id = "band", Start = 0.05, End = 2.05 };
        model.Regions.Add(region);
        Call(w, "SelectRegion", region);
        Key(w, global::Avalonia.Input.Key.Left);
        Assert.Equal(0, region.Start);
        Assert.Equal(2, region.End, 8);
        Key(w, global::Avalonia.Input.Key.Right, KeyModifiers.Shift);
        Assert.Equal(1, region.Start);
        Assert.Equal(3, region.End, 8);
    });

    [Fact]
    public Task DuplicateRuleCopiesItsBandAndLeavesOriginalUntouched() => Run((w, model) =>
    {
        model.Regions.Add(new Region { Id = "band", Start = 1, End = 3 });
        var rule = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 2 }, RegionConstraint = "band" };
        model.Rules.Add(rule);
        Call(w, "SelectRule", rule);
        Key(w, global::Avalonia.Input.Key.D, KeyModifiers.Control);
        Assert.Equal(2, model.Rules.Count);
        var copy = model.Rules[1];
        Assert.Equal(3, ((TimeReachedTrigger)copy.Trigger).Time);
        Assert.NotEqual("band", copy.RegionConstraint);
        var band = model.Regions.Single(r => r.Id == copy.RegionConstraint);
        Assert.Equal(3, band.Start);
        Assert.Equal(5, band.End);
        Assert.Equal(2, ((TimeReachedTrigger)rule.Trigger).Time);
    });

    [Fact]
    public Task InspectorTypingIsOneUndoEntryAndRegionEndNormalizesOnBlur() => Run((w, model) =>
    {
        var region = new Region { Id = "band", Start = 2, End = 4, Label = "before" };
        model.Regions.Add(region);
        Call(w, "SelectRegion", region);
        w.TxtRegionLabel.Focus();
        w.TxtRegionLabel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.A });
        w.TxtRegionLabel.Text = "first";
        w.TxtRegionLabel.Text = "second";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, ((System.Collections.ICollection)Field(w, "_undo")!).Count);
        w.Undo();
        var restored = ((Enhancement)Field(w, "_enhancement")!).Regions.Single();
        Assert.Equal("before", restored.Label);
        Call(w, "SelectRegion", restored);
        Set(w, "_totalSeconds", 30d);
        w.TxtRegionEnd.Text = "1";
        Dispatcher.UIThread.RunJobs();
        Call(w, "TxtRegionEnd_LostFocus", w.TxtRegionEnd, new RoutedEventArgs());
        Assert.Equal(2.05, restored.End, 8);
    });

    private static void Set(DeeperEditorWindow w, string name, object value) =>
        typeof(DeeperEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(w, value);

    [Fact]
    public Task BracketsJumpOverTheCurrentStartAndArrowsRespectTextFocus() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        Set(w, "_currentSeconds", 5d);
        model.Regions.Add(new Region { Id = "a", Start = 5, End = 6 });
        model.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Effect, Start = 9 });
        Key(w, global::Avalonia.Input.Key.OemCloseBrackets);
        Assert.Equal(9d, Field(w, "_currentSeconds"));
        Key(w, global::Avalonia.Input.Key.OemOpenBrackets);
        Assert.Equal(5d, Field(w, "_currentSeconds"));
        w.MetadataDrawerToggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.TxtMetaName.Focus());
        Key(w, global::Avalonia.Input.Key.Right);
        Assert.Equal(5d, Field(w, "_currentSeconds"));
        w.BtnPlayPause.Focus();
        Key(w, global::Avalonia.Input.Key.End);
        Assert.Equal(30d, Field(w, "_currentSeconds"));
    });

    [Fact]
    public Task ItemMenuDisablesRuleClipboardAndDeletesThroughUndo() => Run((w, model) =>
    {
        var rule = new EnhancementRule();
        model.Rules.Add(rule);
        Call(w, "SelectRule", rule);
        Set(w, "_ctxItem", rule);
        var menu = (ContextMenu)typeof(DeeperEditorWindow).GetMethod("CreateItemContextMenu",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, null)!;
        Assert.Equal(5, menu.Items.Count); // WPF has no separator before Jump
        Assert.Equal(KeyGesture.Parse("Ctrl+D"), ((MenuItem)menu.Items[0]!).InputGesture);
        Assert.Equal(KeyGesture.Parse("Delete"), ((MenuItem)menu.Items[3]!).InputGesture);
        Assert.NotNull(menu.BorderBrush);
        Assert.Same(w.FindResource("DeeperAccentBrush"), menu.BorderBrush);
        Assert.False(((MenuItem)menu.Items[1]!).IsEnabled);
        Assert.False(((MenuItem)menu.Items[2]!).IsEnabled);
        ((MenuItem)menu.Items[3]!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Empty(model.Rules);
        w.Undo();
        Assert.Single(((Enhancement)Field(w, "_enhancement")!).Rules);
    });

    [Fact]
    public Task HistoryPinsReferenceRestoredRules() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        var rule = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 3 } };
        model.Rules.Add(rule);
        w.PushUndoSnapshot();
        ((TimeReachedTrigger)rule.Trigger).Time = 8;
        Call(w, "RebuildRuleVisuals");
        w.Undo();
        var restored = ((Enhancement)Field(w, "_enhancement")!).Rules.Single();
        Assert.Contains(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, restored));
        Assert.DoesNotContain(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, rule));
    });

    [Fact]
    public Task ConstrainedBandDragRefreshesRuleInspector() => Run((w, model) =>
    {
        var region = new Region { Id = "band", Start = 1, End = 3 };
        model.Regions.Add(region);
        var rule = new EnhancementRule { RegionConstraint = "band" };
        model.Rules.Add(rule);
        Call(w, "SelectRule", rule);
        Set(w, "_draggedRegion", region);
        region.Start = 4; region.End = 6;
        Call(w, "RefreshInspectorForDraggedRegion");
        Assert.Equal("4", w.TxtRuleBandStart.Text);
        Assert.Equal("6", w.TxtRuleBandEnd.Text);
    });

    [Fact]
    public Task TimelineHeightRestoresAndClampsTheSavedSetting() => Run((w, _) =>
    {
        var old = ConditioningControlPanel.CoreSettings.Current.DeeperEditorTimelineHeight;
        try
        {
            ConditioningControlPanel.CoreSettings.Current.DeeperEditorTimelineHeight = 275;
            w.ApplyPersistedSidebarWidth();
            Assert.Equal(275, w.PreviewTimelineGrid.RowDefinitions[2].Height.Value);
            ConditioningControlPanel.CoreSettings.Current.DeeperEditorTimelineHeight = 800;
            w.ApplyPersistedSidebarWidth();
            Assert.Equal(400, w.PreviewTimelineGrid.RowDefinitions[2].Height.Value);
            Assert.Equal(120, w.PreviewTimelineGrid.RowDefinitions[0].MinHeight); // WPF xaml:684
        }
        finally { ConditioningControlPanel.CoreSettings.Current.DeeperEditorTimelineHeight = old; }
    });

    [Fact]
    public Task TimelineSplitterDragCompletedSavesTheTimelineHeight() => Run((w, _) =>
    {
        var old = ConditioningControlPanel.CoreSettings.Current.DeeperEditorTimelineHeight;
        try
        {
            w.PreviewTimelineGrid.RowDefinitions[2].Height = new GridLength(230);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(230, w.PreviewTimelineGrid.RowDefinitions[2].ActualHeight, 0);
            w.TimelineSplitter.RaiseEvent(new global::Avalonia.Input.VectorEventArgs
                { RoutedEvent = global::Avalonia.Controls.Primitives.Thumb.DragCompletedEvent });
            Assert.Equal(230, ConditioningControlPanel.CoreSettings.Current.DeeperEditorTimelineHeight);
        }
        finally { ConditioningControlPanel.CoreSettings.Current.DeeperEditorTimelineHeight = old; }
    });

    [Fact]
    public Task RightClickOnARulePinSelectsItAndOpensTheItemMenu() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        var rule = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 3 } };
        model.Rules.Add(rule);
        Call(w, "RebuildRuleVisuals");
        var pin = w.TimelineCanvas.Children.OfType<global::Avalonia.Controls.Shapes.Rectangle>()
            .Single(c => ReferenceEquals(c.Tag, rule));
        global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); // hit testing reads the last frame
        Dispatcher.UIThread.RunJobs();
        var at = pin.TranslatePoint(new Point(pin.Bounds.Width / 2, 4), w)!.Value;
        w.MouseDown(at, MouseButton.Right);
        w.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(rule, Field(w, "_selectedRule"));
        Assert.Same(rule, Field(w, "_ctxItem"));
        Assert.NotSame(w.TimelineCtxMenu, w.TimelineCanvas.ContextMenu);
        Assert.False(((MenuItem)w.TimelineCanvas.ContextMenu!.Items[1]!).IsEnabled);
        w.TimelineCanvas.ContextMenu.Close();
    });

    [Fact]
    public Task DuplicateItemsPreservesClipboardAndMenuCopyCutJumpUseThePrimary() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        var region = new Region { Id = "band", Start = 2, End = 4 };
        model.Regions.Add(region);
        var saved = DeeperEditorWindow.DeeperClipboard.Regions;
        var sentinel = new Region { Id = "clipboard" };
        DeeperEditorWindow.DeeperClipboard.Regions = new() { sentinel };
        try
        {
            Call(w, "SelectRegion", region);
            Key(w, global::Avalonia.Input.Key.D, KeyModifiers.Control);
            Assert.Same(sentinel, DeeperEditorWindow.DeeperClipboard.Regions.Single());
            var copy = model.Regions.Single(r => !ReferenceEquals(r, region));
            Assert.NotEqual(region.Id, copy.Id);
            Assert.Equal(4, copy.Start);
            Assert.Equal(6, copy.End);
            Call(w, "SelectRegion", region);
            Set(w, "_ctxItem", region);
            Call(w, "CtxItemJump_Click", w, new RoutedEventArgs());
            Assert.Equal(2d, Field(w, "_currentSeconds"));
            w._selectionSet.Clear();
            Call(w, "CtxItemCopy_Click", w, new RoutedEventArgs());
            Assert.Equal("band", DeeperEditorWindow.DeeperClipboard.Regions.Single().Id);
            Call(w, "CtxItemCut_Click", w, new RoutedEventArgs());
            Assert.DoesNotContain(region, model.Regions);
            Set(w, "_zoomFactor", 3d);
            Key(w, global::Avalonia.Input.Key.D0, KeyModifiers.Control);
            Assert.Equal(1d, Field(w, "_zoomFactor"));
        }
        finally { DeeperEditorWindow.DeeperClipboard.Regions = saved; }
    });

    [Fact]
    public Task MetadataTypingJoinsOneSidebarUndoSession() => Run((w, model) =>
    {
        var original = model.Metadata.Name;
        Assert.True(w.TxtMetaName.Focus());
        w.TxtMetaName.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
            Key = global::Avalonia.Input.Key.A });
        w.TxtMetaName.Text = "first";
        Dispatcher.UIThread.RunJobs();
        w.TxtMetaName.Text = "second";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("second", model.Metadata.Name);
        Assert.Equal(1, ((System.Collections.ICollection)Field(w, "_undo")!).Count);
        w.Undo();
        Assert.Equal(original, ((Enhancement)Field(w, "_enhancement")!).Metadata.Name);
    });
}

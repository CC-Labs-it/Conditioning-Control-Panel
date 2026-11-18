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
using ConditioningControlPanel.Avalonia.Platform;
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
    public Task HoverTimeFollowsPointerAndPanicClearsIt() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        var canvas = w.FindControl<Canvas>("TimelineCanvas")!;
        var tip = w.FindControl<Border>("HoverTimeTip")!;
        var label = w.FindControl<TextBlock>("TxtHoverTime")!;
        void Move(double x) => canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, canvas,
            new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), w, canvas.TranslatePoint(new Point(x, 1), w) ?? default,
            0, PointerPointProperties.None, KeyModifiers.None));
        Assert.True(canvas.Bounds.Width > 0);
        Move(canvas.Bounds.Width / 2);
        Assert.True(tip.IsVisible);
        Assert.Equal("0:15.00", label.Text);
        Move(canvas.Bounds.Width + 100);
        Assert.Equal("0:30.00", label.Text);
        canvas.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, canvas,
            new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), w, default, 0, PointerPointProperties.None, KeyModifiers.None));
        Assert.False(tip.IsVisible);
        Move(1);
        ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.StopAll("test");
        Assert.False(tip.IsVisible);
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
    public Task ParkedRulesAndTimeRulesOnBandsShowPins() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        var past = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 40 } };
        var gone = new EnhancementRule { Trigger = new GazeTargetTrigger(), RegionConstraint = "deleted" };
        var band = new Region { Id = "band", Start = 1, End = 5 };
        var onBand = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 3 }, RegionConstraint = band.Id };
        var pastOnBand = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 50 }, RegionConstraint = band.Id };
        var gazeOnBand = new EnhancementRule { Trigger = new GazeTargetTrigger(), RegionConstraint = band.Id };
        model.Regions.Add(band);
        model.Rules.Add(past);
        model.Rules.Add(gone);
        model.Rules.Add(onBand);
        model.Rules.Add(pastOnBand);
        model.Rules.Add(gazeOnBand);
        Call(w, "RebuildRuleVisuals");
        var pins = w.TimelineCanvas.Children.OfType<Control>().Where(c => c.Tag is EnhancementRule).ToArray();
        Assert.Equal(4, pins.Length);
        var pastPin = Assert.Single(pins, c => ReferenceEquals(c.Tag, past));
        var gonePin = Assert.Single(pins, c => ReferenceEquals(c.Tag, gone));
        Assert.Equal(16, pastPin.Height);
        Assert.Contains("past the end of the media", (string)ToolTip.GetTip(pastPin)!);
        Assert.Contains("its region is gone", (string)ToolTip.GetTip(gonePin)!);
        Assert.Contains(pins, c => ReferenceEquals(c.Tag, onBand));
        Assert.Contains("past the end of the media", (string)ToolTip.GetTip(Assert.Single(pins, c => ReferenceEquals(c.Tag, pastOnBand)))!);
        Assert.DoesNotContain(pins, c => ReferenceEquals(c.Tag, gazeOnBand));
    });

    [Fact]
    public Task RegionChoiceRefreshesParkedRulePin() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        model.Regions.Add(new Region { Id = "band", Start = 1, End = 5 });
        var rule = new EnhancementRule { Trigger = new GazeTargetTrigger() };
        model.Rules.Add(rule);
        Call(w, "RebuildRuleVisuals");
        Call(w, "SelectRule", rule);
        Assert.Contains(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, rule));
        w.CmbRuleRegion.SelectedIndex = 1;
        Assert.DoesNotContain(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, rule));
        w.CmbRuleRegion.SelectedIndex = 0;
        Assert.Contains(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, rule));
    });

    [Fact]
    public Task BulkRegionDeleteRefreshesParkedRulePin() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        var region = new Region { Id = "band", Start = 1, End = 5 };
        var rule = new EnhancementRule { Trigger = new GazeTargetTrigger(), RegionConstraint = region.Id };
        model.Regions.Add(region);
        model.Rules.Add(rule);
        Call(w, "RebuildRuleVisuals");
        Assert.DoesNotContain(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, rule));
        w._selectionSet.Add(region);
        w.DeleteSelection();
        Assert.Contains(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, rule));
    });

    [Fact]
    public Task SingleRegionDeleteDetachesRuleAndRefreshesPin() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 30d);
        var region = new Region { Id = "band", Start = 1, End = 5 };
        var rule = new EnhancementRule { Trigger = new GazeTargetTrigger(), RegionConstraint = region.Id };
        var otherRule = new EnhancementRule { Trigger = new GazeTargetTrigger(), RegionConstraint = "other" };
        model.Regions.Add(region);
        model.Regions.Add(new Region { Id = "other", Start = 6, End = 8 });
        model.Rules.Add(rule);
        model.Rules.Add(otherRule);
        Call(w, "SelectRegion", region);
        w.BtnDeleteRegion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain(region, model.Regions);
        Assert.Null(rule.RegionConstraint);
        Assert.Equal("other", otherRule.RegionConstraint);
        var pin = Assert.Single(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, rule));
        Assert.Contains("fires anywhere, no place on the ruler", (string)ToolTip.GetTip(pin)!);
        w.Undo();
        var restored = (Enhancement)Field(w, "_enhancement")!;
        Assert.Contains(restored.Regions, r => r.Id == "band");
        Assert.Contains(restored.Rules, r => r.RegionConstraint == "band");
    });

    [Fact]
    public Task SingleRegionDeleteRemovesOnlyItsProjectedRuleItem() => Run((w, model) =>
    {
        var region = new Region { Id = "band", Start = 1, End = 5 };
        var projected = new TimelineItem { Id = "band", Kind = TimelineItemKind.Rule };
        var effect = new TimelineItem { Id = "band", Kind = TimelineItemKind.Effect };
        var other = new TimelineItem { Id = "other", Kind = TimelineItemKind.Rule };
        model.Regions.Add(region);
        model.TimelineItems.Add(projected);
        model.TimelineItems.Add(effect);
        model.TimelineItems.Add(other);
        Call(w, "SelectRegion", region);
        w.BtnDeleteRegion.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain(projected, model.TimelineItems);
        Assert.Contains(effect, model.TimelineItems);
        Assert.Contains(other, model.TimelineItems);
        w.Undo();
        Assert.Contains(((Enhancement)Field(w, "_enhancement")!).TimelineItems, t => t.Id == "band" && t.Kind == TimelineItemKind.Rule);
    });

    [Fact]
    public Task ParkedRuleTooltipUsesSelectedLanguage() => Run((w, model) =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("it");
            Set(w, "_totalSeconds", 30d);
            var rule = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 40 } };
            model.Rules.Add(rule);
            Call(w, "RebuildRuleVisuals");
            var pin = Assert.Single(w.TimelineCanvas.Children.OfType<Control>(), c => ReferenceEquals(c.Tag, rule));
            Assert.Equal("Regola #1 · tempo 40s, oltre la fine del media", ToolTip.GetTip(pin));
        }
        finally { loc.SetLanguage(previous); }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RuleSelectionKeys(bool delete) => Run((w, model) =>
    {
        var rule = new EnhancementRule();
        model.Rules.Add(rule);
        Call(w, "SelectRule", rule);
        Key(w, delete ? global::Avalonia.Input.Key.Delete : global::Avalonia.Input.Key.Escape);
        Assert.Null(Field(w, "_selectedRule"));
        Assert.Equal(delete ? 0 : 1, model.Rules.Count);
    });

    [Theory]
    [InlineData(global::Avalonia.Input.Key.D0)]
    [InlineData(global::Avalonia.Input.Key.NumPad0)]
    public Task ControlZeroResetsTimelineZoom(global::Avalonia.Input.Key key) => Run((w, _) =>
    {
        Call(w, "SetZoom", 2d, null);
        Assert.Equal(2d, Field(w, "_zoomFactor"));
        Key(w, key, KeyModifiers.Control);
        Assert.Equal(1d, Field(w, "_zoomFactor"));
    });

    [Fact]
    public Task PreviewNavigationInstallsZoomShortcut() => Run((w, _) =>
    {
        string? script = null;
        w.BrowserPreview.ScriptInvoker = js => { script = js; return Task.FromResult<string?>(null); };
        w.BrowserPreview.OnNavigationCompleted(new Uri("https://example.com/video"));
        Assert.NotNull(script);
        Assert.Contains("ccp_editor_zoom_fit", script);
        Assert.Contains("keydown", script);
        Assert.Contains("ccp_zoom_out", script);
        Assert.Contains("wheel", script);
    });

    // Focus inside the preview (any frame, cross-origin included): the engine's native key signal is the route.
    [Fact]
    public Task PreviewNativeKeyResetsTimelineZoom() => Run((w, _) =>
    {
        var wants = w.BrowserPreview.NativeKeyFilter;
        Assert.NotNull(wants);
        Assert.True(wants!(global::Avalonia.Input.Key.D0, KeyModifiers.Control));
        Assert.True(wants(global::Avalonia.Input.Key.NumPad0, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(wants(global::Avalonia.Input.Key.D0, KeyModifiers.None));
        Assert.False(wants(global::Avalonia.Input.Key.D1, KeyModifiers.Control));
        Call(w, "SetZoom", 2d, null);
        w.BrowserPreview.OnNativeKeyDown(global::Avalonia.Input.Key.D0, KeyModifiers.Control);
        Assert.Equal(1d, Field(w, "_zoomFactor"));
    });

    [Fact]
    public void NativeKeySignalTranslatesEngineKeys()
    {
        Assert.Equal((global::Avalonia.Input.Key.D0, KeyModifiers.Control), WebKeySignal.FromGdk(0x030, 4));
        Assert.Equal((global::Avalonia.Input.Key.NumPad0, KeyModifiers.Control | KeyModifiers.Shift), WebKeySignal.FromGdk(0xffb0, 5));
        Assert.Equal((global::Avalonia.Input.Key.None, KeyModifiers.Alt), WebKeySignal.FromGdk(0x031, 8));
        Assert.Equal((global::Avalonia.Input.Key.D0, KeyModifiers.Control), WebKeySignal.FromVirtualKey(0x30, true, false, false));
        Assert.Equal((global::Avalonia.Input.Key.NumPad0, KeyModifiers.Shift), WebKeySignal.FromVirtualKey(0x60, false, true, false));
        Assert.Equal(global::Avalonia.Input.Key.None, WebKeySignal.FromVirtualKey(0x31, true, false, false).Item1);
    }

    [Fact]
    public Task PreviewZoomMessageResetsTimelineZoom() => Run((w, _) =>
    {
        Call(w, "SetZoom", 2d, null);
        w.BrowserPreview.OnWebMessage("unrelated");
        Assert.Equal(2d, Field(w, "_zoomFactor"));
        w.BrowserPreview.OnWebMessage("ccp_editor_zoom_fit");
        Assert.Equal(1d, Field(w, "_zoomFactor"));
    });

    [Fact]
    public Task PreviewWheelScriptIsInstalledForAllFrames() => Run((w, _) =>
    {
        var property = w.BrowserPreview.GetType().GetProperty("AllFramesDocumentStartScript", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var script = Assert.IsType<string>(property!.GetValue(w.BrowserPreview));
        Assert.Contains("ccp_zoom_out", script);
        Assert.Contains("window.top.postMessage", script);
    });

    [Fact]
    public Task PreviewPageZoomButtonsAndMessagesClamp() => Run((w, _) =>
    {
        double applied = 0;
        w.BrowserPreview.ZoomInvoker = (zoom, _) => applied = zoom;
        w.BrowserPreview.BrowserZoomAdapterReady();
        w.BtnPreviewZoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1.1, applied, 8);
        w.BrowserPreview.OnWebMessage("ccp_zoom_out");
        Assert.Equal(1.0, applied, 8);
        for (int i = 0; i < 100; i++) w.BrowserPreview.OnWebMessage("ccp_zoom_out");
        Assert.Equal(0.25, applied, 8);
        for (int i = 0; i < 100; i++) w.BrowserPreview.OnWebMessage("ccp_zoom_in");
        Assert.Equal(5.0, applied, 8);
        w.BtnPreviewZoomOut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(4.9, applied, 8);
    });

    [Fact]
    public Task EndSeeksToMediaEnd() => Run((w, _) =>
    {
        Set(w, "_totalSeconds", 30d);
        Key(w, global::Avalonia.Input.Key.End);
        Assert.Equal(30d, Field(w, "_currentSeconds"));
    });

    [Fact]
    public Task UnselectedArrowsSeekButTextKeepsItsArrows() => Run((w, _) =>
    {
        Set(w, "_totalSeconds", 30d);
        Key(w, global::Avalonia.Input.Key.Right);
        Assert.Equal(0.1d, (double)Field(w, "_currentSeconds")!, 8);
        Key(w, global::Avalonia.Input.Key.Right, KeyModifiers.Shift);
        Assert.Equal(1.1d, (double)Field(w, "_currentSeconds")!, 8);
        Assert.True(w.TxtMetaName.Focus());
        Key(w, global::Avalonia.Input.Key.Right);
        Assert.Equal(1.1d, (double)Field(w, "_currentSeconds")!, 8);
    });

    [Fact]
    public Task RuleNudgeMovesTimeAndBandAndSupportsUndo() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 10d);
        var band = new Region { Id = "band", Start = 7, End = 9 };
        model.Regions.Add(band);
        var rule = new EnhancementRule { RegionConstraint = "band", Trigger = new TimeReachedTrigger { Time = 9 } };
        model.Rules.Add(rule);
        Call(w, "SelectRule", rule);
        Key(w, global::Avalonia.Input.Key.Right, KeyModifiers.Shift);
        Assert.Equal(10, ((TimeReachedTrigger)rule.Trigger).Time);
        Assert.Equal(8, band.Start);
        Assert.Equal(10, band.End);
        w.Undo();
        Assert.Equal(9, ((TimeReachedTrigger)((Enhancement)Field(w, "_enhancement")!).Rules.Single().Trigger).Time);
    });

    [Fact]
    public Task MixedSelectionNudgesHapticsAndEffectsWithBounds() => Run((w, model) =>
    {
        Set(w, "_totalSeconds", 10d);
        var haptic = new HapticEvent { Start = 8, Duration = 2 };
        var effect = new TimelineItem { Kind = TimelineItemKind.Effect, Start = 8, Duration = 1 };
        var track = new HapticTrack();
        track.Events.Add(haptic);
        model.HapticTracks.Add(track);
        model.TimelineItems.Add(effect);
        w._selectionSet.Add(haptic);
        w._selectionSet.Add(effect);
        Key(w, global::Avalonia.Input.Key.Right, KeyModifiers.Shift);
        Assert.Equal(8, haptic.Start);
        Assert.Equal(9, effect.Start);
        Key(w, global::Avalonia.Input.Key.Left, KeyModifiers.Shift);
        Assert.Equal(7, haptic.Start);
        Assert.Equal(8, effect.Start);
    });

    [Fact]
    public Task EscapeClearsRulePinHighlightAndHeavyLine() => Run((w, model) =>
    {
        // WPF DeeperEditorWindow.xaml.cs:1928 and Unified.cs:1186,1212.
        Set(w, "_totalSeconds", 30d);
        var rule = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 3 } };
        model.Rules.Add(rule);
        Call(w, "SelectRule", rule);
        var selected = ((System.Collections.Generic.IEnumerable<Control>)Field(w, "_ruleVisuals")!).ToArray();
        var flag = selected.OfType<global::Avalonia.Controls.Shapes.Polygon>().Single();
        Assert.Same(global::Avalonia.Media.Brushes.White, flag.Stroke);
        Assert.Equal(1.5, flag.StrokeThickness);
        Assert.Equal(2.5, selected.OfType<global::Avalonia.Controls.Shapes.Line>().Single().StrokeThickness);

        Key(w, global::Avalonia.Input.Key.Escape);

        Assert.Null(Field(w, "_selectedRule"));
        Assert.Same(rule, Assert.Single(model.Rules));
        var cleared = ((System.Collections.Generic.IEnumerable<Control>)Field(w, "_ruleVisuals")!).ToArray();
        flag = cleared.OfType<global::Avalonia.Controls.Shapes.Polygon>().Single();
        Assert.Null(flag.Stroke);
        Assert.Equal(0, flag.StrokeThickness);
        Assert.Equal(1.5, cleared.OfType<global::Avalonia.Controls.Shapes.Line>().Single().StrokeThickness);
    });

}

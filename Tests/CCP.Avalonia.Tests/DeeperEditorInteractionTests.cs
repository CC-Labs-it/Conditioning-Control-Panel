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

}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class DeeperEditorParityTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static Enhancement Model(DeeperEditorWindow editor) =>
        (Enhancement)typeof(DeeperEditorWindow).GetField("_enhancement", Private)!.GetValue(editor)!;
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [Fact]
    public Task InspectorTypingIsOneUndoSessionAndFocusChangeStartsAnother() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var region = new Region { Id = "band", Label = "old", Start = 12, End = 25 };
        var enhancement = new Enhancement { MediaSource = "*" }; enhancement.Regions.Add(region);
        var editor = new DeeperEditorWindow(enhancement, null);
        try
        {
            editor.Show();
            typeof(DeeperEditorWindow).GetField("_totalSeconds", Private)!.SetValue(editor, 100d);
            editor.UpdateLayout();
            typeof(DeeperEditorWindow).GetMethod("RebuildRegionVisuals", Private)!.Invoke(editor, null);
            Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
            var rect = editor.TimelineCanvas.Children.OfType<Rectangle>().Single(r => ReferenceEquals(r.Tag, region));
            var point = rect.TranslatePoint(new Point(rect.Bounds.Width / 2, rect.Bounds.Height / 2), editor)!.Value;
            editor.MouseMove(point); editor.MouseDown(point, MouseButton.Left); editor.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            editor.TxtRegionLabel.Focus();
            editor.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, "");
            editor.KeyTextInput("a"); Dispatcher.UIThread.RunJobs();
            editor.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            editor.KeyTextInput("b"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("oldab", Model(editor).Regions[0].Label);
            editor.TxtRegionStart.Focus();
            editor.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, "");
            editor.KeyTextInput("3"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(123, Model(editor).Regions[0].Start);
            editor.Undo(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(12, Model(editor).Regions[0].Start);
            Assert.Equal("oldab", Model(editor).Regions[0].Label);
            editor.Undo(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("old", Model(editor).Regions[0].Label);
            editor.Redo(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("oldab", Model(editor).Regions[0].Label);
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task InspectorCheckboxClickCanBeUndone() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var rule = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 10 }, Enabled = true };
        var enhancement = new Enhancement { MediaSource = "*" }; enhancement.Rules.Add(rule);
        var editor = new DeeperEditorWindow(enhancement, null);
        try
        {
            editor.Show();
            typeof(DeeperEditorWindow).GetMethod("SelectRule", Private)!.Invoke(editor, new object[] { rule });
            Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
            var box = editor.ChkRuleEnabled;
            var point = box.TranslatePoint(new Point(8, box.Bounds.Height / 2), editor)!.Value;
            editor.MouseMove(point); editor.MouseDown(point, MouseButton.Left); editor.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(Model(editor).Rules[0].Enabled);
            editor.Undo(); Dispatcher.UIThread.RunJobs();
            Assert.True(Model(editor).Rules[0].Enabled);
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task UndoRuleEditRebuildsPinForRestoredRule() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var rule = new EnhancementRule { Trigger = new TimeReachedTrigger { Time = 10 }, CooldownMs = 1000 };
        var enhancement = new Enhancement { MediaSource = "*" }; enhancement.Rules.Add(rule);
        var editor = new DeeperEditorWindow(enhancement, null);
        try
        {
            editor.Show();
            typeof(DeeperEditorWindow).GetField("_totalSeconds", Private)!.SetValue(editor, 100d);
            editor.UpdateLayout();
            typeof(DeeperEditorWindow).GetMethod("SelectRule", Private)!.Invoke(editor, new object[] { rule });
            Dispatcher.UIThread.RunJobs();
            editor.TxtRuleCooldown.Focus();
            editor.PushUndoSnapshot();
            editor.KeyTextInput("2"); Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(1000, Model(editor).Rules[0].CooldownMs);
            editor.Undo(); Dispatcher.UIThread.RunJobs();
            var restored = Model(editor).Rules[0];
            Assert.Equal(1000, restored.CooldownMs);
            Assert.Contains(editor.TimelineCanvas.Children.OfType<Rectangle>(), r => ReferenceEquals(r.Tag, restored));
            Assert.DoesNotContain(editor.TimelineCanvas.Children.OfType<Rectangle>(), r => ReferenceEquals(r.Tag, rule));
        }
        finally { editor.ForceClose(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("load")]
    [InlineData("haptic")]
    [InlineData("effect")]
    [InlineData("rule")]
    [InlineData("save")]
    [InlineData("title")]
    public Task LoadAddAndSavePublishTutorialEventsAndDirtyTitle(string check) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var events = new List<string>();
        var oldSavedPath = CoreTutorialEvents.LastSavedEnhancementPath;
        string? savedPathAtEvent = null;
        void OnEvent(object? _, string name)
        {
            events.Add(name);
            if (name == "FileSaved") savedPathAtEvent = CoreTutorialEvents.LastSavedEnhancementPath;
        }
        var dir = Directory.CreateTempSubdirectory("ccp-editor-parity-").FullName;
        var path = System.IO.Path.Combine(dir, "project.ccpenh.json");
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, null);
        CoreTutorialEvents.Event += OnEvent;
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs();
            var cleanTitle = editor.Title;
            Click(editor.HeroEffectHaptic);
            var hapticCount = events.Count(e => e == "EffectAdded");
            Click(editor.HeroEffectFlash);
            var effectCount = events.Count(e => e == "EffectAdded");
            Click(editor.BtnAddRuleHero);
            var dirtyTitle = editor.Title;
            // Save a valid empty project so no confirmation dialog interrupts the path.
            Model(editor).HapticTracks.Clear(); Model(editor).TimelineItems.Clear(); Model(editor).Rules.Clear();
            await (Task)typeof(DeeperEditorWindow).GetMethod("SaveToAsync", Private)!.Invoke(editor, new object[] { path })!;
            Assert.True(File.Exists(path));
            switch (check)
            {
                case "load": Assert.Contains("WindowLoaded:DeeperEditorWindow", events); break;
                case "haptic": Assert.Equal(1, hapticCount); break;
                case "effect": Assert.Equal(2, effectCount); break;
                case "rule": Assert.Contains("RuleAdded", events); break;
                case "save": Assert.Equal(path, savedPathAtEvent); break;
                case "title":
                    Assert.False(cleanTitle!.EndsWith("*", StringComparison.Ordinal));
                    Assert.EndsWith("*", dirtyTitle);
                    Assert.False(editor.Title!.EndsWith("*", StringComparison.Ordinal));
                    break;
            }
        }
        finally
        {
            CoreTutorialEvents.Event -= OnEvent;
            CoreTutorialEvents.LastSavedEnhancementPath = oldSavedPath;
            editor.ForceClose(); Directory.Delete(dir, true);
        }
    });

}

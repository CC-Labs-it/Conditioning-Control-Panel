using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class NewEnhancementTutorialTests
{
    private static void Click(Window w, string name) => w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var performance = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true; // RunJobs must not drain ambient clocks forever
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        CoreTutorialEvents.PendingPart2Tutorial = null;
        try { await body(); }
        finally { TutorialHead.ResetForTests(); CoreTutorialEvents.PendingPart2Tutorial = null; CoreSettings.Current.PerformanceMode = performance; }
    });

    [Theory]
    [InlineData("BtnLocalVideoTutorial", "DeeperEditorInteractiveLocalVideo", "video")]
    [InlineData("BtnLocalAudioTutorial", "DeeperEditorInteractiveLocalAudio", "audio")]
    [InlineData("BtnTryHypnoTubeTutorial", "DeeperEditorInteractiveHT", "video")]
    public Task ButtonsStartToursAndOnlyValidatedCreateQueuesTheMatchingPart2(string button, string tour, string media) => Run(async () =>
    {
        var dialog = new NewEnhancementDialog();
        dialog.Show();
        try
        {
            Click(dialog, button);
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal(tour, TutorialHead.Service.CurrentTutorialType.ToString());
            Assert.Equal(media == "video", dialog.FindControl<RadioButton>("RbVideo")!.IsChecked);
            Assert.Null(CoreTutorialEvents.PendingPart2Tutorial);
            dialog.FindControl<TextBox>("TxtSource")!.Text = " ";
            Click(dialog, "BtnCreate");
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.IsVisible);
            Assert.Null(CoreTutorialEvents.PendingPart2Tutorial);
            var warning = Assert.Single(dialog.OwnedWindows.OfType<MessageDialog>());
            Click(warning, "BtnOk");
            await Task.Yield();
            dialog.FindControl<TextBox>("TxtSource")!.Text = "  sample-media  ";
            Click(dialog, "BtnCreate");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(tour + "Part2", CoreTutorialEvents.PendingPart2Tutorial);
            Assert.Equal("sample-media", dialog.SelectedSource);
            Assert.Equal(media, dialog.SelectedMediaType);
            Assert.False(dialog.IsVisible);
            Assert.False(CoreTutorial.IsActive);
        }
        finally { dialog.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CancelAndWindowCloseDetachTheTourAndNeverQueuePart2(bool cancel) => Run(() =>
    {
        var dialog = new NewEnhancementDialog();
        dialog.Show();
        Click(dialog, "BtnLocalAudioTutorial");
        Dispatcher.UIThread.RunJobs();
        Assert.True(CoreTutorial.IsActive);
        if (cancel) Click(dialog, "BtnCancel"); else dialog.Close();
        Assert.False(CoreTutorial.IsActive);
        Assert.Null(CoreTutorialEvents.PendingPart2Tutorial);
        CoreTutorialEvents.Emit("EffectAdded");
        Assert.False(CoreTutorial.IsActive);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("DeeperEditorInteractiveHTPart2", false, false)]
    [InlineData("DeeperEditorInteractiveLocalAudioPart2", false, false)]
    [InlineData("DeeperEditorInteractiveLocalVideoPart2", false, false)]
    [InlineData("DeeperEditorInteractiveHTPart2", true, false)]
    [InlineData("DeeperEditorInteractiveHTPart2", false, true)]
    public Task EditorConsumesPart2OnceAfterLayoutAndCloseOrPanicCancelsIt(string tour, bool close, bool panic) => Run(() =>
    {
        var clock = new ManualClock();
        DeeperEditorWindow.Part2Clock = clock;
        CoreTutorialEvents.PendingPart2Tutorial = tour;
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(CoreTutorialEvents.PendingPart2Tutorial);
            Assert.False(CoreTutorial.IsActive); // no immediate continuation
            var timer = Assert.Single(clock.Timers);
            Assert.Equal(TimeSpan.FromMilliseconds(800), timer.Due);
            if (close) editor.Close();
            if (panic) PanicSurfaces.StopAll("test", null);
            Assert.Equal(close || panic, timer.Disposed);
            timer.Fire(); // a tick already queued when close/panic stopped the timer must still do nothing
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(!close && !panic, CoreTutorial.IsActive);
            if (!close && !panic)
            {
                Assert.Equal(tour, TutorialHead.Service.CurrentTutorialType.ToString());
                Assert.True(editor.FindControl<Border>("MetadataDrawerContent")!.IsVisible);
                // The source card's prep opens the collapsed metadata drawer before spotlighting.
                CoreTutorial.Next();
                var step = CoreTutorial.CurrentStep!.Id;
                timer.Fire(); // Part 2 starts once: a second tick must not restart it
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(step, CoreTutorial.CurrentStep!.Id);
                editor.Close();
                Assert.False(CoreTutorial.IsActive);
            }
        }
        finally { editor.Close(); DeeperEditorWindow.Part2Clock = TimeProvider.System; }
        return Task.CompletedTask;
    });

    private sealed class ManualClock : TimeProvider
    {
        public readonly System.Collections.Generic.List<ManualTimer> Timers = new();
        public override System.Threading.ITimer CreateTimer(System.Threading.TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var t = new ManualTimer(() => callback(state), dueTime);
            Timers.Add(t);
            return t;
        }
    }

    private sealed class ManualTimer(Action fire, TimeSpan due) : System.Threading.ITimer
    {
        public TimeSpan Due { get; private set; } = due;
        public bool Disposed { get; private set; }
        public void Fire() => fire();
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return !Disposed; }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    [Fact]
    public Task InspectorFieldsAreNamedBeforeAttachmentAndTextEditsAdvanceTheRealCards() => Run(() =>
    {
        var editor = new DeeperEditorWindow(); // render sample selects a time / screen-shake rule
        editor.Show();
        TutorialOverlay? overlay = null;
        try
        {
            TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
            while (CoreTutorial.CurrentStep!.Id != "iht_ruletime") CoreTutorial.Next();
            overlay = new TutorialOverlay(editor);
            overlay.Show();
            Dispatcher.UIThread.RunJobs();
            var field = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "TutorialTriggerTimeField"));
            field.Text = "15";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("iht_ruleaction", CoreTutorial.CurrentStep!.Id);
            CoreTutorial.Next();
            var intensity = Assert.Single(editor.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "TutorialActionIntensityField"));
            intensity.Text = "0.7";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("iht_save", CoreTutorial.CurrentStep!.Id);
        }
        finally
        {
            overlay?.Close();
            typeof(DeeperEditorWindow).GetField("_isDirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(editor, false);
            editor.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditorAddingAnEffectAndRuleEmitsTheEventsThatAdvancePart2() => Run(() =>
    {
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        editor.Show();
        TutorialOverlay? overlay = null;
        try
        {
            TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
            while (CoreTutorial.CurrentStep!.Id != "iht_addeffect") CoreTutorial.Next();
            overlay = new TutorialOverlay(editor);
            overlay.Show();
            Dispatcher.UIThread.RunJobs();
            editor.FindControl<MenuItem>("HeroEffectHaptic")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("iht_intensity", CoreTutorial.CurrentStep!.Id);
            while (CoreTutorial.CurrentStep!.Id != "iht_addrule") CoreTutorial.Next();
            Click(editor, "BtnAddRuleHero");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("iht_ruletime", CoreTutorial.CurrentStep!.Id);
        }
        finally
        {
            overlay?.Close();
            typeof(DeeperEditorWindow).GetField("_isDirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(editor, false);
            editor.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SuccessfulSaveRecordsThePathAndAdvancesTheWaitingCard() => Run(async () =>
    {
        var root = System.IO.Directory.CreateTempSubdirectory("ccp-tutorial-save-");
        var path = System.IO.Path.Combine(root.FullName, "sample.ccpenh.json");
        var editor = new DeeperEditorWindow(new Enhancement(), path);
        editor.Show();
        TutorialOverlay? overlay = null;
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<string> onEvent = (_, name) => { if (name == "FileSaved") saved.TrySetResult(); };
        CoreTutorialEvents.Event += onEvent;
        try
        {
            TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
            while (CoreTutorial.CurrentStep!.AdvanceEventName != "FileSaved") CoreTutorial.Next();
            overlay = new TutorialOverlay(editor);
            overlay.Show();
            Dispatcher.UIThread.RunJobs();
            Click(editor, "BtnEditorSave"); // the toolbar Save the card points at; the path came with the editor
            await saved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.True(System.IO.File.Exists(path));
            Assert.Equal(path, CoreTutorialEvents.LastSavedEnhancementPath);
            Assert.Equal("iht_done", CoreTutorial.CurrentStep!.Id);
        }
        finally { CoreTutorialEvents.Event -= onEvent; overlay?.Close(); editor.Close(); root.Delete(true); CoreTutorialEvents.LastSavedEnhancementPath = null; }
    });

}

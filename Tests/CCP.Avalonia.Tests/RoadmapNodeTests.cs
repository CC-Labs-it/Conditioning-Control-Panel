using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>shell-roadmap: the node strip painted from RoadmapService (WPF MainWindow.Roadmap.cs:118),
/// opened from the keyboard (P17), and the completion chime (WPF :459 SystemSounds.Exclamation).</summary>
public sealed class RoadmapNodeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        global::ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
    }

    private static void Press(Control c, Key key) =>
        c.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    [Fact]
    public void RoadmapSubTabPaintsNodesAndEnterOpensTheGateOrTheStartDialog() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        var host = new Window { Width = 1200, Height = 900, Content = new QuestsTabView() };
        try
        {
            host.Show();
            var view = (QuestsTabView)host.Content!;
            view.FindControl<Button>("BtnQuestSubRoadmap")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var steps = RoadmapStepDefinition.GetStepsForTrack(RoadmapTrack.EmptyDoll).ToList();
            var nodes = view.FindControl<StackPanel>("RoadmapNodesPanel")!.Children.OfType<Border>().ToList();
            Assert.Equal(steps.Select(s => s.Id), nodes.Select(n => (string)n.Tag!));
            Assert.All(nodes, n => Assert.True(n.Focusable));

            var roadmap = MainShellWindow.Roadmap;
            var locked = nodes.First(n => !roadmap.IsStepCompleted((string)n.Tag!) && !roadmap.IsStepActive((string)n.Tag!));
            var resting = locked.BorderBrush;
            Assert.True(locked.Focus(NavigationMethod.Tab));        // keyboard focus is visible
            Assert.Same(global::Avalonia.Media.Brushes.White, locked.BorderBrush);
            Press(locked, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<MessageDialog>());          // "Step Locked", not the start dialog
            Assert.Empty(opened.OfType<RoadmapStartDialog>());

            var active = nodes.First(n => roadmap.IsStepActive((string)n.Tag!));
            active.Focus(NavigationMethod.Tab);
            Assert.Same(resting, locked.BorderBrush);                // and leaves with the focus
            Press(active, Key.Space);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened.OfType<RoadmapStartDialog>());
        }
        finally
        {
            foreach (var w in opened.ToArray()) w.Close();
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static T Opened<T>(List<Window> opened) where T : Window
    {
        Dispatcher.UIThread.RunJobs();
        return Assert.Single(opened.OfType<T>(), w => w.IsVisible);
    }

    private static void Click(Control w, string name) =>
        w.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void PhotoConfirmGatesSubmitAndTheNoteLandsOnTheRepaintedNode() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        var host = new Window { Width = 1200, Height = 900, Content = new QuestsTabView() };
        var photo = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"roadmap-{System.Guid.NewGuid():N}.png");
        var diary = MainShellWindow.Roadmap.DiaryFolderPath;
        var diaryBefore = System.IO.Directory.GetFiles(diary).ToHashSet();
        var progressFile = System.IO.Path.Combine(CorePaths.UserData, "roadmap.json");
        var progressBefore = System.IO.File.Exists(progressFile) ? System.IO.File.ReadAllText(progressFile) : null;
        string? stepId = null;
        try
        {
            host.Show();
            var view = (QuestsTabView)host.Content!;
            Click(view, "BtnQuestSubRoadmap");
            new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(4, 4)).Save(photo);

            var roadmap = MainShellWindow.Roadmap;
            stepId = RoadmapStepDefinition.GetStepsForTrack(RoadmapTrack.EmptyDoll).First(s => roadmap.IsStepActive(s.Id)).Id;
            var stepDef = RoadmapStepDefinition.GetById(stepId)!;
            int before = roadmap.Progress.TotalStepsCompleted;

            // WPF :416: "No" on the confirm dialog ends the flow, nothing is submitted and no note is asked.
            var declined = view.ShowPhotoConfirmation(stepId, stepDef, photo, host);
            Click(Opened<RoadmapConfirmDialog>(opened), "BtnNo");
            Dispatcher.UIThread.RunJobs();
            Assert.True(declined.IsCompleted);
            Assert.Empty(opened.OfType<InputDialog>());
            Assert.False(roadmap.IsStepCompleted(stepId));

            // "Yes", then the optional note (WPF :420-428) -> SubmitPhoto -> RefreshRoadmapUI (:429).
            var accepted = view.ShowPhotoConfirmation(stepId, stepDef, photo, host);
            Click(Opened<RoadmapConfirmDialog>(opened), "BtnYes");
            var note = Opened<InputDialog>(opened);
            note.FindControl<TextBox>("TxtInput")!.Text = "felt calm";
            Click(note, "BtnOK");
            Dispatcher.UIThread.RunJobs();
            Assert.True(accepted.IsCompleted);

            Assert.True(roadmap.IsStepCompleted(stepId));
            Assert.Equal("felt calm", roadmap.GetStepProgress(stepId)!.UserNote);
            Assert.Equal($"{before + 1} / 21", view.FindControl<TextBlock>("TxtRoadmapTotalSteps")!.Text);
            var node = view.FindControl<StackPanel>("RoadmapNodesPanel")!.Children.OfType<Border>()
                .Single(n => (string)n.Tag! == stepId);
            Assert.Contains(node.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "\"felt calm\"");
        }
        finally
        {
            foreach (var w in opened.ToArray()) w.Close();
            host.Close();
            Dispatcher.UIThread.RunJobs();
            System.IO.File.Delete(photo);
            // P02: SubmitPhoto completed a step on the static roadmap and copied the photo into
            // roadmap_diary/; neither is covered by TestIsolation, so undo both here.
            foreach (var f in System.IO.Directory.GetFiles(diary).Where(f => !diaryBefore.Contains(f)))
                System.IO.File.Delete(f);
            MainShellWindow.ResetRoadmapForTests();   // disposes (saves) first, so restore the file after it
            if (progressBefore is null) System.IO.File.Delete(progressFile);
            else System.IO.File.WriteAllText(progressFile, progressBefore);
        }
        Assert.Equal(diaryBefore, System.IO.Directory.GetFiles(diary).ToHashSet());
        Assert.False(MainShellWindow.Roadmap.IsStepCompleted(stepId!));   // the next read reloads the restored roadmap.json
    });

    [Fact]
    public void LockedTrackShowsTheOverlayAndItsReason() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var host = new Window { Width = 1200, Height = 900, Content = new QuestsTabView() };
        try
        {
            host.Show();
            var view = (QuestsTabView)host.Content!;
            Click(view, "BtnQuestSubRoadmap");
            Assert.False(MainShellWindow.Roadmap.IsTrackUnlocked(RoadmapTrack.SluttyBlowdoll));   // a fresh sandbox
            Click(view, "BtnTrack3");
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.FindControl<Border>("TrackLockedOverlay")!.IsVisible);
            Assert.False(view.FindControl<Control>("RoadmapScrollContainer")!.IsVisible);
            Assert.Equal("Complete Track 2 Boss to unlock", view.FindControl<TextBlock>("TxtLockReason")!.Text);
        }
        finally
        {
            host.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void StepCompletedPlaysTheChime() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var old = CoreAudio.PlayOneShotProvider;
        var tags = new List<string>();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        try
        {
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            var step = new RoadmapStepDefinition("t1", RoadmapTrack.EmptyDoll, 1, "Title", "Objective", "Photo");
            MainShellWindow.OnRoadmapStepCompleted(null,
                new RoadmapStepCompletedEventArgs(step, new RoadmapStepProgress("t1"), false, false));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "roadmap-step" }, tags);
        }
        finally
        {
            CoreAudio.PlayOneShotProvider = old;
            foreach (var w in opened.ToArray()) w.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}

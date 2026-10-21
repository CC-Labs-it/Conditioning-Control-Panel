using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class DeeperEditorTutorialTests
{
    private static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static TutorialOverlay? Overlay(DeeperEditorWindow editor) =>
        editor.OwnedWindows.Count == 0 ? null : Assert.IsType<TutorialOverlay>(editor.OwnedWindows[0]);

    private static void Setup()
    {
        // Production builder (app fonts registered), so the overlay lays out as in the app. Only the
        // first class to run in a testhost builds the app, so DeeperHub/DeeperRows/
        // DeeperOverlayOpacityLabel/TutorialTour use the same builder: otherwise this class's
        // result would depend on which class ran first in its shard.
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        CoreTutorialEvents.PendingPart2Tutorial = null;
    }

    [Fact]
    public Task HelpOpensOneEditorTourAndClosingEditorAbandonsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var seen = CoreSettings.Current.HasSeenDeeperEditorIntro;
        CoreSettings.Current.HasSeenDeeperEditorIntro = true;
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs();
            editor.BtnEditorHelp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var overlay = Overlay(editor);
            Assert.NotNull(overlay);
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal("de_intro", CoreTutorial.CurrentStep!.Id);
            editor.BtnEditorHelp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(overlay, Overlay(editor));
            CoreTutorial.Next();
            Assert.Equal(1, CoreTutorial.CurrentStepIndex);
            CoreTutorial.Next();
            CoreTutorial.Next();
            Assert.True(editor.MetadataDrawerContent.IsVisible);
            PanicSurfaces.All.First(s => s.Id == "tour").Stop(null!);
            Assert.False(CoreTutorial.IsActive);
            Assert.False(overlay.IsVisible);
            editor.BtnEditorHelp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.ForceClose();
            Assert.False(CoreTutorial.IsActive);
            Assert.False(overlay.IsVisible);
        }
        finally { editor.ForceClose(); TutorialHead.ResetForTests(); CoreSettings.Current.HasSeenDeeperEditorIntro = seen; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FirstRunStartsOnceAndLeavesAnActiveOrPendingTourAlone() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var seen = CoreSettings.Current.HasSeenDeeperEditorIntro;
        CoreSettings.Current.HasSeenDeeperEditorIntro = false;
        try
        {
            var first = new DeeperEditorWindow(new Enhancement(), null);
            try
            {
                first.Show(); Dispatcher.UIThread.RunJobs();
                Assert.NotNull(Overlay(first));
                Assert.True(CoreSettings.Current.HasSeenDeeperEditorIntro);
            }
            finally { first.ForceClose(); }
            var second = new DeeperEditorWindow(new Enhancement(), null);
            try { second.Show(); Dispatcher.UIThread.RunJobs(); Assert.Null(Overlay(second)); }
            finally { second.ForceClose(); }
            foreach (var pending in new[] { false, true })
            {
                CoreSettings.Current.HasSeenDeeperEditorIntro = false;
                if (pending) CoreTutorialEvents.PendingPart2Tutorial = "DeeperEditorInteractiveHTPart2";
                else CoreTutorial.Start("DeeperEditor");
                var guarded = new DeeperEditorWindow(new Enhancement(), null);
                try
                {
                    guarded.Show(); Dispatcher.UIThread.RunJobs();
                    Assert.Null(Overlay(guarded));
                    Assert.False(CoreSettings.Current.HasSeenDeeperEditorIntro);
                }
                finally { guarded.ForceClose(); CoreTutorial.Skip(); CoreTutorialEvents.PendingPart2Tutorial = null; }
            }
        }
        finally { TutorialHead.ResetForTests(); CoreSettings.Current.HasSeenDeeperEditorIntro = seen; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicCancelsCoachmarksWaitingForLayout() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var seen = CoreSettings.Current.HasSeenDeeperEditorIntro;
        CoreSettings.Current.HasSeenDeeperEditorIntro = true;
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.HasSeenDeeperEditorIntro = false;
            Invoke(editor, "DeeperEditorWindow_Loaded", null, new RoutedEventArgs());
            PanicSurfaces.All.First(s => s.Id == "deeper-editor-tour").Stop(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(Overlay(editor));
            Assert.False(CoreTutorial.IsActive);
        }
        finally { editor.ForceClose(); CoreSettings.Current.HasSeenDeeperEditorIntro = seen; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditorPublishesLoadedAddAndSuccessfulSaveMoments() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var seen = CoreSettings.Current.HasSeenDeeperEditorIntro;
        var savedBefore = CoreTutorialEvents.LastSavedEnhancementPath;
        CoreSettings.Current.HasSeenDeeperEditorIntro = true;
        var events = new List<string>();
        EventHandler<string> listener = (_, name) => events.Add(name);
        CoreTutorialEvents.Event += listener;
        var dir = Directory.CreateTempSubdirectory("ccp-editor-tour-");
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Contains("WindowLoaded:DeeperEditorWindow", events);
            Invoke(editor, "AddEffectAt", "Flash", 0d);
            Invoke(editor, "AddRuleAt", "TimeReached", 0d);
            Assert.Contains("EffectAdded", events);
            Assert.Contains("RuleAdded", events);
            // Save a valid empty project: invalid saves deliberately remain behind the prompt.
            editor.ForceClose();
            editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Video, MediaSource = "*" }, null);
            var path = Path.Combine(dir.FullName, "saved.ccpenh.json");
            await (Task)Invoke(editor, "SaveToAsync", path)!;
            Assert.True(File.Exists(path));
            Assert.Equal(path, CoreTutorialEvents.LastSavedEnhancementPath);
            Assert.Contains("FileSaved", events);
        }
        finally
        {
            editor.ForceClose(); CoreTutorialEvents.Event -= listener;
            CoreTutorialEvents.LastSavedEnhancementPath = savedBefore;
            CoreSettings.Current.HasSeenDeeperEditorIntro = seen; dir.Delete(true);
        }
    });
}

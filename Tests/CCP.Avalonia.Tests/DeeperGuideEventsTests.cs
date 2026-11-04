using System;
using System.IO;
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
public sealed class DeeperGuideEventsTests
{
    [Theory]
    [InlineData("DeeperEditorInteractiveHTPart2", "EffectAdded", "haptic")]
    [InlineData("DeeperEditorInteractiveLocalAudioPart2", "EffectAdded", "haptic")]
    [InlineData("DeeperEditorInteractiveLocalVideoPart2", "EffectAdded", "haptic")]
    [InlineData("DeeperEditorInteractiveHTPart2", "EffectAdded", "flash")]
    [InlineData("DeeperEditorInteractiveHTPart2", "RuleAdded", "rule")]
    [InlineData("DeeperEditorInteractiveLocalAudioPart2", "RuleAdded", "rule")]
    [InlineData("DeeperEditorInteractiveLocalVideoPart2", "RuleAdded", "rule")]
    [InlineData("DeeperEditorInteractiveHTPart2", "FileSaved", "save")]
    [InlineData("DeeperEditorInteractiveLocalAudioPart2", "FileSaved", "save")]
    [InlineData("DeeperEditorInteractiveLocalVideoPart2", "FileSaved", "save")]
    public Task EditorActionAdvancesMatchingGuide(string tour, string eventName, string action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var oldPath = CoreTutorialEvents.LastSavedEnhancementPath;
        var dir = Directory.CreateTempSubdirectory("ccp-guide-events-").FullName;
        var path = Path.Combine(dir, "guide.ccpenh.json");
        var enhancement = new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" };
        var editor = new DeeperEditorWindow(enhancement, path);
        TutorialOverlay? overlay = null;
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            TutorialHead.Service.Start(Enum.Parse<TutorialType>(tour));
            overlay = new TutorialOverlay(editor);
            overlay.Show();
            while (TutorialHead.Service.CurrentStep?.AdvanceEventName != eventName)
            {
                Assert.True(TutorialHead.Service.IsActive);
                TutorialHead.Service.Next();
            }
            Dispatcher.UIThread.RunJobs();
            var before = TutorialHead.Service.CurrentStepIndex;
            switch (action)
            {
                case "haptic": editor.HeroEffectHaptic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); break;
                case "flash": editor.HeroEffectFlash.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); break;
                case "rule": editor.BtnAddRuleHero.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
                case "save": editor.BtnEditorSave.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); break;
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before + 1, TutorialHead.Service.CurrentStepIndex);
            if (action == "haptic") Assert.NotEmpty(enhancement.HapticTracks);
            if (action == "flash") Assert.NotEmpty(enhancement.TimelineItems);
            if (action == "rule") Assert.NotEmpty(enhancement.Rules);
            if (action == "save")
            {
                Assert.True(File.Exists(path));
                Assert.Equal(path, CoreTutorialEvents.LastSavedEnhancementPath);
            }
        }
        finally
        {
            overlay?.Close();
            TutorialHead.ResetForTests();
            typeof(DeeperEditorWindow).GetField("_isDirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(editor, false);
            editor.Close();
            CoreTutorialEvents.LastSavedEnhancementPath = oldPath;
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });
}

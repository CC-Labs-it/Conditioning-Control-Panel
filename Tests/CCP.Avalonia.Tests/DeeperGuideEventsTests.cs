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
    // Re-saving an existing file emits FileSaved synchronously inside the Save click, before the
    // Save card's deferred advance. WPF TutorialOverlay.xaml.cs:298-341 checks the step current when
    // the event is handled, so the event must still reach the following FileSaved card.
    [Theory]
    [InlineData("DeeperEditorInteractiveHTPart2", "iht_save", "iht_done")]
    [InlineData("DeeperEditorInteractiveLocalAudioPart2", "iaud_save", "iaud_done")]
    [InlineData("DeeperEditorInteractiveLocalVideoPart2", "ivid_save", "ivid_done")]
    public Task SavingAnExistingFileAdvancesThroughBothSaveSteps(string tour, string saveStep, string doneStep) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var performance = CoreSettings.Current.PerformanceMode;
        CoreSettings.Current.PerformanceMode = true; // RunJobs must not drain ambient clocks forever
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var oldPath = CoreTutorialEvents.LastSavedEnhancementPath;
        var dir = Directory.CreateTempSubdirectory("ccp-guide-resave-").FullName;
        var path = Path.Combine(dir, "guide.ccpenh.json");
        File.WriteAllText(path, "previous file");
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, path);
        TutorialOverlay? overlay = null;
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            TutorialHead.Service.Start(Enum.Parse<TutorialType>(tour));
            overlay = new TutorialOverlay(editor);
            overlay.Show();
            while (TutorialHead.Service.CurrentStep?.Id != saveStep)
            {
                Assert.True(TutorialHead.Service.IsActive);
                TutorialHead.Service.Next();
            }
            Dispatcher.UIThread.RunJobs();
            editor.FindControl<Button>("BtnEditorSave")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(doneStep, TutorialHead.Service.CurrentStep?.Id);
            Assert.Equal(path, CoreTutorialEvents.LastSavedEnhancementPath);
            Assert.NotEqual("previous file", File.ReadAllText(path));
        }
        finally
        {
            overlay?.Close();
            TutorialHead.ResetForTests();
            editor.Close();
            CoreTutorialEvents.LastSavedEnhancementPath = oldPath;
            CoreSettings.Current.PerformanceMode = performance;
            Directory.Delete(dir, true);
        }
        return Task.CompletedTask;
    });
}

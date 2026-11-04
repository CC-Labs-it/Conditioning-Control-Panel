using System;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class NewEnhancementGuideTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        TutorialHead.Seed();
        CoreTutorialEvents.PendingPart2Tutorial = null;
    }

    private static void Click(Window window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Theory]
    [InlineData("BtnLocalVideoTutorial", "DeeperEditorInteractiveLocalVideo", "DeeperEditorInteractiveLocalVideoPart2")]
    [InlineData("BtnLocalAudioTutorial", "DeeperEditorInteractiveLocalAudio", "DeeperEditorInteractiveLocalAudioPart2")]
    [InlineData("BtnTryHypnoTubeTutorial", "DeeperEditorInteractiveHT", "DeeperEditorInteractiveHTPart2")]
    public Task GuideQueuesOnlyOnSuccessAndEditorStartsContinuation(string button, string part1, string part2) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dialog = new NewEnhancementDialog();
        dialog.Show();
        Click(dialog, button);
        Assert.True(CoreTutorial.IsActive);
        Assert.Equal(part1, TutorialHead.Service.CurrentTutorialType.ToString());
        dialog.FindControl<TextBox>("TxtSource")!.Text = " ";
        Click(dialog, "BtnCreate");
        Assert.Null(CoreTutorialEvents.PendingPart2Tutorial);
        dialog.FindControl<TextBox>("TxtSource")!.Text = "sample.mp4";
        Click(dialog, "BtnCreate");
        Assert.Equal(part2, CoreTutorialEvents.PendingPart2Tutorial);
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        editor.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(CoreTutorialEvents.PendingPart2Tutorial);
        await Task.Delay(1200);
        Dispatcher.UIThread.RunJobs();
        Assert.True(CoreTutorial.IsActive);
        Assert.Equal(part2, TutorialHead.Service.CurrentTutorialType.ToString());
        editor.Close();
        TutorialHead.ResetForTests();
    });

    [Theory]
    [InlineData("BtnLocalVideoTutorial")]
    [InlineData("BtnLocalAudioTutorial")]
    [InlineData("BtnTryHypnoTubeTutorial")]
    public Task CancelDoesNotQueueContinuation(string button) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dialog = new NewEnhancementDialog();
        dialog.Show();
        Click(dialog, button);
        Click(dialog, "BtnCancel");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(CoreTutorialEvents.PendingPart2Tutorial);
        Assert.False(CoreTutorial.IsActive);
        TutorialHead.ResetForTests();
        return Task.CompletedTask;
    });
}

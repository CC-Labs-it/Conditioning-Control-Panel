using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF PopQuizWindow.xaml.cs:44-48/:307-312 and QuizWindow.xaml.cs:151-155/:1511-1515: the
/// avatar is muted while a quiz is open and restored on close, without touching the stored
/// AvatarMuted setting (WPF SetMuteAvatar is in-memory only); an already-muted avatar stays muted.</summary>
public sealed class QuizAvatarMuteTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task QuizMutesTheAvatarWhileOpenAndRestoresOnClose(bool fullQuiz, bool alreadyMuted) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var wasMuted = CoreSettings.Current.AvatarMuted;
        CoreSettings.Current.AvatarMuted = alreadyMuted;
        var main = new Window { Width = 1000, Height = 700 };
        AvatarTubeWindow? tube = null;
        Window? quiz = null;
        try
        {
            main.Show();
            tube = new AvatarTubeWindow(main);
            tube.Show();
            AvatarTubeWindow.TestLive = tube;   // no desktop lifetime headless
            Dispatcher.UIThread.RunJobs();
            Assert.Same(tube, AvatarTubeWindow.Live);
            Assert.Equal(alreadyMuted, tube.IsMuted);

            quiz = fullQuiz ? new QuizWindow(false, false) : new PopQuizWindow(PopQuizScheduler.QuestionPool[0], isTest: true);
            quiz.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(tube.IsMuted);                                   // silenced while the quiz is open
            Assert.Equal(alreadyMuted, CoreSettings.Current.AvatarMuted); // never persisted

            quiz.Close();
            quiz = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(alreadyMuted, tube.IsMuted);                    // pre-quiz state back
            Assert.Equal(alreadyMuted, CoreSettings.Current.AvatarMuted);
        }
        finally
        {
            quiz?.Close();
            PopQuizWindow.ForceCloseAll();
            tube?.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
            AvatarTubeWindow.TestLive = null;
            CoreSettings.Current.AvatarMuted = wasMuted;
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}

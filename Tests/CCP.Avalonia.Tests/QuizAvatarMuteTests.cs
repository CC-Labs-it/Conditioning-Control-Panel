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
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, true)]
    public Task QuizMutesTheAvatarWhileOpenAndRestoresOnClose(bool fullQuiz, bool alreadyMuted, bool shellMuted, bool muteShellDuringQuiz) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
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
            tube.SetWindowMute(shellMuted);
            AvatarTubeWindow.TestLive = tube;   // no desktop lifetime headless
            Dispatcher.UIThread.RunJobs();
            Assert.Same(tube, AvatarTubeWindow.Live);
            Assert.Equal(alreadyMuted || shellMuted, tube.IsMuted);

            quiz = fullQuiz ? new QuizWindow(false, false) : new PopQuizWindow(PopQuizScheduler.QuestionPool[0], isTest: true);
            quiz.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(tube.IsMuted);                                   // silenced while the quiz is open
            Assert.Equal(alreadyMuted, CoreSettings.Current.AvatarMuted); // never persisted

            if (muteShellDuringQuiz) tube.SetWindowMute(true);
            quiz.Close();
            quiz = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(alreadyMuted || shellMuted || muteShellDuringQuiz, tube.IsMuted); // keep shell mute
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

    // WPF MainWindow.CompanionRoom.cs:99 and AvatarTubeWindow.ChatInput.cs:1325:
    // an explicit user choice replaces the temporary quiz mute.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExplicitMuteControlClearsTheQuizOverride(bool quickMenu) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options()).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.AvatarMuted = false;
        var shell = new MainShellWindow();
        var tube = new AvatarTubeWindow(shell);
        try
        {
            typeof(MainShellWindow).GetField("_avatarTubeWindow",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(shell, tube);
            tube.SetMuteAvatar(true);
            Assert.True(tube.IsMuted);
            Assert.False(service.Current.AvatarMuted);
            if (quickMenu) tube.OnMenuMute();
            else shell.SetAvatarMuted(false);
            Assert.False(tube.IsMuted);
            Assert.False(service.Current.AvatarMuted);
        }
        finally
        {
            tube.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

}

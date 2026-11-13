using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EngineStopTeardownTests
{
    [Fact]
    public void PlainStopClosesQuizAndFuseAndExitsPlayerFullscreen() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldHook = CoreEngine.StoppedHook;
        var oldDual = CoreSettings.Current.DualMonitorEnabled;
        var shell = new MainShellWindow();
        var quiz = new QuizWindow(false, false);
        var fuse = new DescentFuseWindow();
        var player = new EnhancementPlayerWindow(null, null);
        try
        {
            CoreSettings.Current.DualMonitorEnabled = false;
            shell.Show(); quiz.Show(); fuse.Show(); player.Show();
            Dispatcher.UIThread.RunJobs();
            player.FindControl<WebHost>("VideoBrowser")!.OnNativeFullscreenEntered();
            Assert.True(quiz.IsVisible);
            Assert.True(fuse.IsVisible);
            Assert.NotNull(player.VideoFullscreenWindow);
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            CoreEngine.Start();
            Assert.True(CoreEngine.IsRunning);
            MainShellWindow.StopEngine();
            Assert.False(quiz.IsVisible);
            Assert.False(fuse.IsVisible);
            Assert.Null(player.VideoFullscreenWindow);
        }
        finally
        {
            CoreEngine.StoppedHook = null;
            CoreEngine.Stop();
            CoreEngine.StoppedHook = oldHook;
            CoreSettings.Current.DualMonitorEnabled = oldDual;
            quiz.Close(); fuse.Close(); player.Close(); shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}

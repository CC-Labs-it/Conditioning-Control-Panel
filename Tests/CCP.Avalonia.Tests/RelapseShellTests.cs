using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class RelapseShellTests
{
    [Fact]
    public async Task ManualStopPanicAndStartOriginReachAchievements()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-relapse-shell-").FullName;
            var property = typeof(AvApp).GetProperty("Achievements", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var old = property.GetValue(null);
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            property.SetValue(null, engine);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                shell.StartEngine();
                shell.Named<Button>("BtnStart")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.NotNull(engine.Progress.LastPanicPressTime);
                shell.StartEngine(systemInitiated: true);
                Assert.False(engine.Progress.IsUnlocked("relapse"));
                MainShellWindow.StopEngine();
                engine.Progress.LastPanicPressTime = null;
                PanicSurfaces.StopAll("test", shell);   // idle: WPF MainWindow.xaml.cs:1904 tracks only a running stop
                Assert.Null(engine.Progress.LastPanicPressTime);
                shell.StartEngine();
                Assert.False(engine.Progress.IsUnlocked("relapse"));
                PanicSurfaces.StopAll("test", shell);
                Assert.NotNull(engine.Progress.LastPanicPressTime);
                Assert.False(CoreEngine.IsRunning);
                shell.StartEngine();
                Assert.True(engine.Progress.IsUnlocked("relapse"));
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                shell.Close();
                property.SetValue(null, old);
                Directory.Delete(dir, true);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]   // WPF SessionEngine.cs:478: any incomplete StopSession, so the Presets Stop too
    public async Task PresetsStopTracksPanic()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-relapse-presets-").FullName;
            var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            var oldProvider = CoreProgression.TrackPanicPressedProvider;
            CoreProgression.TrackPanicPressedProvider = engine.TrackPanicPressed;   // as App.axaml.cs seeds it
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                shell.Show();
                runner.Start(new Session { Id = "relapse", Name = "Relapse", DurationMinutes = 60 });
                shell.BtnStartSession_Click(null);
                var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Dispatcher.UIThread.Post(() => settled.SetResult(), DispatcherPriority.Background);
                await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(runner.IsRunning);
                Assert.NotNull(engine.Progress.LastPanicPressTime);
            }
            finally
            {
                foreach (var d in shell.OwnedWindows.ToArray()) d.Close();
                await Task.Yield();
                runner.Stop();
                CoreEngine.Stop();
                AvApp.Sessions = null;
                CoreSession.IsSessionRunningProvider = null;
                CoreProgression.TrackPanicPressedProvider = oldProvider;
                shell.Close();
                Directory.Delete(dir, true);
            }
        });
    }
}

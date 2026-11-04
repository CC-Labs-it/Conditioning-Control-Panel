using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
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
                PanicSurfaces.StopAll("test", shell);
                Assert.NotNull(engine.Progress.LastPanicPressTime);
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
}

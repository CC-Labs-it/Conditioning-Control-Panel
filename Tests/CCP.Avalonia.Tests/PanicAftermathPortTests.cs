using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class PanicAftermathPortTests
{
    [Fact]
    public void StopPassArmsQuietBeforeAnySurfaceEvenIfItThrows()
    {
        var saved = PanicSurfaces.All;
        PanicAftermath.EndQuiet();
        bool observed = false;
        try
        {
            PanicSurfaces.All = new PanicSurfaces.Surface[]
            {
                new("failure", _ => throw new InvalidOperationException()),
                new("probe", _ => observed = PanicAftermath.TriggersSilenced()),
            };
            PanicSurfaces.StopAll("test");
            Assert.True(observed);
            Assert.True(PanicAftermath.HoldsEngineRestart);
        }
        finally { PanicSurfaces.All = saved; PanicAftermath.EndQuiet(); }
    }

    [Fact]
    public async Task PaletteEscapeDoesNotArmQuietButActualPanicArmsItOnce()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            var surfaces = PanicSurfaces.All;
            var settings = CoreSettings.Current;
            var oldKey = settings.PanicKey;
            var oldEnabled = settings.PanicKeyEnabled;
            try
            {
                PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>();
                PanicAftermath.EndQuiet();
                settings.PanicKeyEnabled = true;
                settings.PanicKey = "Escape";
                shell.Show();
                SettingsPaletteWindow.Toggle(shell);
                Assert.True(SettingsPaletteWindow.IsOpen);
                shell.HandlePanicKeyPress(DateTime.Now);
                Assert.False(SettingsPaletteWindow.IsOpen);
                Assert.False(PanicAftermath.TriggersSilenced());
                settings.PanicKey = "F8";
                var count = PanicAftermath.PanicCount;
                shell.HandlePanicKeyPress(DateTime.Now);
                Assert.True(PanicAftermath.TriggersSilenced());
                Assert.Equal(count + 1, PanicAftermath.PanicCount);
            }
            finally
            {
                SettingsPaletteWindow.CloseIfOpen();
                PanicSurfaces.All = surfaces;
                settings.PanicKey = oldKey;
                settings.PanicKeyEnabled = oldEnabled;
                shell.Close();
                PanicAftermath.EndQuiet();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AutomaticStartKeepsQuietWhileUserStartAndResumeReleaseIt()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new ConditioningControlPanel.Services.SessionRunner(
                new ConditioningControlPanel.Services.SessionLogService());
            try
            {
                PanicAftermath.ArmQuiet();
                shell.StartEngine();
                Assert.True(PanicAftermath.TriggersSilenced());
                MainShellWindow.StopEngine();
                shell.StartEngineFromUser();
                Assert.False(PanicAftermath.TriggersSilenced());
                Assert.False(PanicAftermath.HoldsEngineRestart);
                MainShellWindow.StopEngine();
                PanicAftermath.ArmQuiet();
                shell.RandomizeAndStart(new Random(1));
                Assert.False(PanicAftermath.TriggersSilenced());
                runner.Start(new ConditioningControlPanel.Models.Session { Id = "panic-port", DurationMinutes = 1 });
                runner.Pause();
                PanicAftermath.ArmQuiet();
                shell.Named<Button>("BtnPauseSession")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(runner.IsPaused);
                Assert.False(PanicAftermath.TriggersSilenced());
            }
            finally
            {
                runner.Stop();
                AvApp.Sessions = null;
                CoreEngine.Stop();
                shell.Close();
                PanicAftermath.EndQuiet();
            }
            return Task.CompletedTask;
        });
    }
}

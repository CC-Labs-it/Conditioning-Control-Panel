using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class RemoteSessionPauseTests
{
    [Fact]
    public async Task RemotePauseStopsTheRunOnceAndUpdatesTheButton()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
            {
                var fonts = ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
                var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
                mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
                fonts.FontFamilyMappings = mappings;
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(fonts).UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            }
            var shell = new MainShellWindow();
            shell.Show();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
                shell.StartSession(new Session { Id = "remote_pause_test", Name = "Test", DurationMinutes = 1 });
                Assert.True(runner.IsRunning);
                Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
                Assert.True(runner.IsPaused);
                Assert.Equal(1, runner.PauseCount);
                Assert.Equal("\u25B6", shell.Named<TextBlock>("TxtPauseIcon")!.Text);
                Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
                Assert.Equal(1, runner.PauseCount);
                Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteControlTabView.ExecuteRemoteCommand("resume_session", null));
            }
            finally
            {
                runner.Stop();
                AvApp.Sessions = null;
                CoreEngine.Stop();
                shell.Close();
            }
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
            return Task.CompletedTask;
        });
    }
}

using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
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
            var oldRunning = CoreSession.IsSessionRunningProvider;
            var oldPaused = CoreSession.IsSessionPausedProvider;
            var oldSessions = AvApp.Sessions;
            MainShellWindow? shell = null;
            SessionRunner? runner = null;
            try
            {
                shell = new MainShellWindow();
                shell.Show();
                runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
                Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
                shell.StartSession(new Session { Id = "remote_pause_test", Name = "Test", DurationMinutes = 1 });
                Assert.True(runner.IsRunning);
                shell.VoiceCmds.Host.ActionFor("spiral_on")!(); shell.VoiceCmds.Host.ActionFor("pink_on")!();
                Assert.NotNull(SpiralOverlay.VoiceOpacity); Assert.NotNull(PinkFilterOverlay.VoiceOpacity);
                Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
                Assert.True(runner.IsPaused);
                // WPF SessionEngine.cs:536: the pause stops every overlay, voice holds included.
                Assert.Null(SpiralOverlay.VoiceOpacity); Assert.Null(PinkFilterOverlay.VoiceOpacity);
                Assert.Equal(1, runner.PauseCount);
                Assert.Equal("\u25B6", shell.Named<TextBlock>("TxtPauseIcon")!.Text);
                Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
                Assert.Equal(1, runner.PauseCount);
                Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteControlTabView.ExecuteRemoteCommand("resume_session", null));
            }
            finally
            {
                try
                {
                    runner?.Stop();
                    SpiralOverlay.Stop(); PinkFilterOverlay.Stop();
                    AvApp.Sessions = oldSessions;
                    CoreEngine.Stop();
                    shell?.Close();
                }
                finally
                {
                    CoreSession.IsSessionRunningProvider = oldRunning;
                    CoreSession.IsSessionPausedProvider = oldPaused;
                }
            }
            Assert.Same(oldRunning, CoreSession.IsSessionRunningProvider);
            Assert.Same(oldPaused, CoreSession.IsSessionPausedProvider);
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteControlTabView.ExecuteRemoteCommand("pause_session", null));
            return Task.CompletedTask;
        });
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.StartStop.cs:449 and :592-601: the shell's Start and Stop reach EMI's moment
/// bus with the start origin and the whole minutes run.</summary>
public sealed class EngineEmiMomentsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShellStartAndStopFireEngineMomentsOnTheDesk(bool systemInitiated)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var (oldAudioOnly, oldPanic, oldOffline, oldSessions) = (s.AudioOnlySession, s.PanicKeyEnabled, s.OfflineMode, s.TotalSessions);
            var oldHook = CoreEngine.MomentHook;
            var oldStopped = CoreEngine.StoppedHook;
            var oldClock = CoreEngine.UtcNow;
            var (oldLayers, oldAudio) = (ConditioningControlPanel.Avalonia.Platform.LayeredAudio.Instance, ConditioningControlPanel.Avalonia.Platform.LibVlcAudio.Instance);
            var clock = new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc);
            var heard = new List<(string Id, IReadOnlyDictionary<string, object?> Ctx)>();
            EventHandler<EmiMoment> listen = (_, m) =>
            {
                if (m.Id is "engineStarted" or "engineStopped") heard.Add((m.Id, EmiLineEngine.ToCtx(m.Context)));
            };
            var service = EmiDeskService.Instance;
            MainShellWindow? shell = null;
            try
            {
                s.AudioOnlySession = true;   // nothing visual starts, and no audio device either
                ConditioningControlPanel.Avalonia.Platform.LayeredAudio.Instance = null;
                ConditioningControlPanel.Avalonia.Platform.LibVlcAudio.Instance = null;
                s.PanicKeyEnabled = false;
                s.OfflineMode = true;
                CoreEngine.UtcNow = () => clock;
                EmiDeskService.HookEngine();
                service.MomentFired += listen;
                shell = new MainShellWindow();
                shell.Show();
                CoreEngine.StoppedHook = shell.OnEngineStopped;

                shell.StartEngine(systemInitiated: systemInitiated);
                Assert.True(CoreEngine.IsRunning);
                Assert.Equal("engineStarted", Assert.Single(heard).Id);
                Assert.Equal((object)systemInitiated, heard[0].Ctx["systemInitiated"]);

                clock = clock.AddMinutes(12).AddSeconds(30);
                MainShellWindow.StopEngine();
                Assert.Equal(2, heard.Count);
                Assert.Equal("engineStopped", heard[1].Id);
                Assert.Equal((object)12, heard[1].Ctx["minutes"]);
            }
            finally
            {
                service.MomentFired -= listen;
                CoreEngine.StoppedHook = oldStopped;
                CoreEngine.Stop();
                CoreEngine.MomentHook = oldHook;
                CoreEngine.UtcNow = oldClock;
                shell?.Close();
                (ConditioningControlPanel.Avalonia.Platform.LayeredAudio.Instance, ConditioningControlPanel.Avalonia.Platform.LibVlcAudio.Instance) = (oldLayers, oldAudio);
                (s.AudioOnlySession, s.PanicKeyEnabled, s.OfflineMode, s.TotalSessions) = (oldAudioOnly, oldPanic, oldOffline, oldSessions);
            }
            return Task.CompletedTask;
        });
    }
}

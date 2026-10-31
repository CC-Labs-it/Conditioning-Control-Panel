using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LockdownEmergencyExitTests
{
    [Theory]
    [InlineData("password", "completed", .33, false)]
    [InlineData("jigsaw", "completed", .99, false)]
    [InlineData("captcha", "completed", .5, false)]
    [InlineData("labyrinth", "completed", .99, true)]
    [InlineData("password", "completed", .329, true)]
    [InlineData("password", "failed", .99, true)]
    [InlineData("jigsaw", "failed", .99, true)]
    [InlineData("captcha", "failed", .99, true)]
    [InlineData("labyrinth", "failed", .99, true)]
    public async Task VerdictRestoresOrRestartsExactlyOnce(string game, string result, double roll, bool stays)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.StrictLockEnabled = false; s.PanicKeyEnabled = true;
            s.LockdownForceStrictLock = true; s.LockdownDisablePanicKey = true;
            using var ld = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(30));
            var w = new EmergencyExitWindow(ld, game) { Roll = () => roll };
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"game-finished\",\"result\":\"" + result + "\"}");
                Assert.Equal(stays, ld.IsActive);
                Assert.Equal(stays, s.StrictLockEnabled);
                Assert.Equal(!stays, s.PanicKeyEnabled);
                Assert.Equal(stays ? 1 : 0, ld.RestartCount);
                if (stays) Assert.True(ld.Remaining > TimeSpan.FromMinutes(29));
                w.HandleMessage("{\"type\":\"game-finished\",\"result\":\"failed\"}");
                Assert.Equal(stays ? 1 : 0, ld.RestartCount);
                Assert.True(w.IsVisible); // escape must leave the outro alive
                w.HandleMessage("{\"type\":\"outro-done\"}");
                Assert.False(w.IsVisible);
            }
            finally { w.Close(); ld.Deactivate(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AbandonPanicAndExternalEndCannotApplyALateVerdict()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            using var ld = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(30));
            try
            {
                EmergencyExitWindow.Open(null, ld);
                var w = EmergencyExitWindow.Current!;
                EmergencyExitWindow.Open(null, ld);
                Assert.Same(w, EmergencyExitWindow.Current);
                w.HandleMessage("invalid json");
                w.HandleMessage("{\"type\":\"quit\"}");
                Assert.True(ld.IsActive);
                Assert.Equal(0, ld.RestartCount);
                w.HandleMessage("{\"type\":\"game-finished\",\"result\":\"failed\"}");
                Assert.Equal(0, ld.RestartCount);
                EmergencyExitWindow.Open(null, ld);
                Assert.True(PanicSurfaces.AnyOwnsTheScreen());
                foreach (var surface in PanicSurfaces.All)
                    if (surface.Id == "emergency-exit") surface.Stop(null);
                Assert.Null(EmergencyExitWindow.Current);
                Assert.True(ld.IsActive); // closing a game is not a Lockdown bypass
                EmergencyExitWindow.Open(null, ld);
                ld.Deactivate();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(EmergencyExitWindow.Current);
            }
            finally { EmergencyExitWindow.Current?.Close(); ld.Deactivate(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task MissingOutroStillClosesTheWindow()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            using var ld = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(30));
            var w = new EmergencyExitWindow(ld, "password")
                { Roll = () => .9, OutroTimeout = TimeSpan.FromMilliseconds(10) };
            w.Show();
            try
            {
                w.HandleMessage("{\"type\":\"game-finished\",\"result\":\"completed\"}");
                await Task.Delay(100);
                Dispatcher.UIThread.RunJobs();
                Assert.False(w.IsVisible);
                Assert.False(ld.IsActive);
            }
            finally { w.Close(); ld.Deactivate(); }
        });
    }

    private static void EnsureApp()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}

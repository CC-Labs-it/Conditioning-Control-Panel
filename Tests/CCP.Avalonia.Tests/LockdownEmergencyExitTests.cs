using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using Newtonsoft.Json.Linq;
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

    [Fact]
    public async Task ReloadReadyThroughWebHostRefreshesInitWithoutReapplyingVerdict()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var now = DateTime.UtcNow;
            using var ld = new LockdownService { UtcNow = () => now };
            ld.Activate(TimeSpan.FromMinutes(30));
            try
            {
                EmergencyExitWindow.Open(null, ld);
                var window = EmergencyExitWindow.Current!;
                var web = Assert.IsType<WebHost>(window.Content);
                var frames = new List<JObject>();
                web.ScriptInvoker = script =>
                {
                    frames.Add(JObject.Parse(script[(script.LastIndexOf("})(", StringComparison.Ordinal) + 3)..^2]));
                    return Task.FromResult<string?>(null);
                };
                var page = web.Source!;
                web.OnNavigationCompleted(page);
                var first = Assert.Single(frames);
                Assert.Equal("init", (string?)first["type"]);
                Assert.Equal(1800, (int)first["remainingSec"]!);
                now = now.AddSeconds(70);
                // Reload the same document, then exercise the native message event, not HandleMessage.
                web.OnNavigationCompleted(page);
                frames.Clear();
                web.OnWebMessage("{\"type\":\"ready\"}");
                var reloaded = Assert.Single(frames);
                Assert.Equal("init", (string?)reloaded["type"]);
                Assert.Equal(1730, (int)reloaded["remainingSec"]!);
                Assert.Equal(first["game"], reloaded["game"]);
                Assert.Equal(first["attempt"], reloaded["attempt"]);
                Assert.Equal(0, ld.RestartCount);
                web.OnWebMessage("{\"type\":\"game-finished\",\"result\":\"failed\"}");
                Assert.Equal(1, ld.RestartCount);
                Assert.True(ld.IsActive);
                frames.Clear();
                web.OnNavigationCompleted(page);
                web.OnWebMessage("{\"type\":\"ready\"}");
                web.OnWebMessage("{\"type\":\"game-finished\",\"result\":\"failed\"}");
                Assert.Empty(frames);
                Assert.Equal(1, ld.RestartCount);
            }
            finally { EmergencyExitWindow.Current?.Close(); ld.Deactivate(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RenderConstructorDoesNotTouchLiveLockdown()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var previous = LockdownService.Current;
            using var live = LockdownService.Current = new LockdownService();
            live.Activate(TimeSpan.FromMinutes(30));
            var settings = CoreSettings.Current;
            var before = (settings.StrictLockEnabled, settings.PanicKeyEnabled);
            EmergencyExitWindow? window = null;
            try
            {
                window = Assert.IsType<EmergencyExitWindow>(Activator.CreateInstance(typeof(EmergencyExitWindow), nonPublic: true));
                Assert.IsType<WebHost>(window.Content);
                window.Show();
                window.HandleMessage("{\"type\":\"game-finished\",\"result\":\"completed\"}");
                window.Close();
                Assert.Same(live, LockdownService.Current);
                Assert.True(live.IsActive);
                Assert.Equal(0, live.RestartCount);
                Assert.Equal(before, (settings.StrictLockEnabled, settings.PanicKeyEnabled));
                Assert.Null(EmergencyExitWindow.Current);
            }
            finally { window?.Close(); live.Deactivate(); LockdownService.Current = previous; }
            return Task.CompletedTask;
        });
    }

    private static void EnsureApp()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}

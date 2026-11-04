using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using CCP.Tests.Shared;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class RemoteSessionReviewTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Remote_stop_force_unducks_all_references(bool session, bool engineStopped) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var saved = (AvApp.Sessions, LibVlcAudio.Instance, CoreAudio.DuckProvider, CoreEngine.StoppedHook, CoreSettings.Current.MasterVolume, CoreSession.IsSessionRunningProvider);
        var audio = new LibVlcAudio(_ => "[]");
        SessionRunner? runner = null;
        try
        {
            CoreEngine.StoppedHook = null;
            CoreSettings.Current.MasterVolume = 100;
            LibVlcAudio.Instance = audio;
            CoreAudio.DuckProvider = audio.Duck;
            if (session)
            {
                AvApp.Sessions = runner = new SessionRunner(new SessionLogService());
                runner.Start(new Session { Id = "duck-test", Name = "Duck test", DurationMinutes = 30, Settings = new SessionSettings() });
                if (engineStopped) { runner.Pause(); CoreEngine.Stop(); }
            }
            else { AvApp.Sessions = null; CoreEngine.Start(); }
            Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("duck_audio", null));
            Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("duck_audio", null));
            Assert.True(audio.IsDucked);
            Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("stop_session", null));
            Assert.False(audio.IsDucked);
            Assert.False(CoreEngine.IsRunning || runner?.IsRunning == true);
            // An old asynchronous unduck cannot release a new duck after the force.
            audio.Duck(80);
            audio.Unduck(0);
            Assert.True(audio.IsDucked);
        }
        finally
        {
            try { runner?.Stop(); CoreEngine.Stop(); audio.Shutdown(); }
            finally { (AvApp.Sessions, LibVlcAudio.Instance, CoreAudio.DuckProvider, CoreEngine.StoppedHook, CoreSettings.Current.MasterVolume, CoreSession.IsSessionRunningProvider) = saved; }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Production_relay_delivers_stop_from_a_loopback_controller() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var server = new FakeServer();
        var status = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Handle = async context =>
        {
            var path = context.Request.Url!.AbsolutePath;
            if (path == "/v2/remote/status")
            {
                using var reader = new StreamReader(context.Request.InputStream);
                status.TrySetResult(await reader.ReadToEndAsync());
            }
            var body = path switch
            {
                "/v2/remote/start" => "{\"code\":\"ABC123\"}",
                "/v2/remote/poll" => "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"stop_session\"}]}",
                _ => "{}"
            };
            await FakeServer.Send(context, Encoding.UTF8.GetBytes(body));
        };
        var oldUrl = Environment.GetEnvironmentVariable("CCP_REMOTE_BASE_URL");
        var oldSandbox = Environment.GetEnvironmentVariable("CCP_USERDATA_DIR");
        var saved = (RemoteControlTabView.Relay, AvApp.Sessions, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, CoreDispatch.InvokeProvider, CoreEngine.StoppedHook, CoreSession.IsSessionRunningProvider);
        var runner = new SessionRunner(new SessionLogService());
        RemoteRelay? relay = null;
        try
        {
            Environment.SetEnvironmentVariable("CCP_REMOTE_BASE_URL", server.Prefix);
            Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", TestUserDataProfile.Root);
            CoreSecrets.RetrieveProvider = _ => null;
            CoreAccount.UnifiedUserId = "loopback-test";
            CoreEngine.StoppedHook = null;
            CoreDispatch.InvokeProvider = (f, _) =>
            {
                object? result = null;
                AvaloniaTestDispatcher.Run(() => result = f());
                return (true, result);
            };
            AvApp.Sessions = runner;
            RemoteControlTabView.Relay = RemoteControlTabView.NewRelay();
            relay = RemoteControlTabView.Relay.Value;
            relay.AutoPoll = false;
            runner.Start(new Session { Id = "relay-stop", Name = "Relay stop", DurationMinutes = 30, Settings = new SessionSettings() });
            Assert.Equal("ABC123", await relay.StartAsync("full"));
            await relay.PollOnceAsync();
            var payload = JObject.Parse(await status.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("ok", (string?)payload["last_executed"]?["status"]);
            Assert.False(runner.IsRunning || CoreEngine.IsRunning);
        }
        finally
        {
            try
            {
                if (relay?.IsActive == true) await relay.StopAsync();
                relay?.Dispose();
                runner.Stop();
                CoreEngine.Stop();
            }
            finally
            {
                (RemoteControlTabView.Relay, AvApp.Sessions, CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, CoreDispatch.InvokeProvider, CoreEngine.StoppedHook, CoreSession.IsSessionRunningProvider) = saved;
                Environment.SetEnvironmentVariable("CCP_REMOTE_BASE_URL", oldUrl);
                Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", oldSandbox);
            }
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task A_remote_stop_cancels_a_queued_portal_start(bool session) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (!OperatingSystem.IsLinux()) return; // GlobalShortcuts is Linux-specific.
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var type = typeof(MainShellWindow);
        var allowed = type.GetField("_portalAllowed", flags)!;
        var failed = type.GetField("_portalFailed", flags)!;
        var binding = type.GetField("_portalBind", flags)!;
        var oldPortal = (allowed.GetValue(null), failed.GetValue(null), binding.GetValue(null));
        var oldDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var saved = (AvApp.Sessions, MainShellWindow.PostPortalStart, CoreSettings.Current.PanicKeyEnabled, CoreEngine.StoppedHook, CoreSession.IsSessionRunningProvider);
        var bind = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new SessionRunner(new SessionLogService());
        try
        {
            Assert.False(PortalPanicShortcut.IsBound);
            CoreEngine.StoppedHook = null;
            CoreEngine.Stop();
            AvApp.Sessions = runner;
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "ccp-test");
            CoreSettings.Current.PanicKeyEnabled = true;
            allowed.SetValue(null, true);
            failed.SetValue(null, false);
            binding.SetValue(null, bind.Task);
            MainShellWindow.PostPortalStart = action => posted.TrySetResult(action);
            var shell = (MainShellWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainShellWindow));
            if (session) shell.StartSession(new Session { Id = "portal-test", Name = "Portal test", DurationMinutes = 30, Settings = new SessionSettings() });
            else shell.StartEngine();
            Assert.False(CoreEngine.IsRunning || runner.IsRunning);
            Assert.Null(RemoteControlTabView.ExecuteRemoteCommand("stop_session", null));
            bind.SetResult();
            var queued = await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var error = Record.Exception(queued);
            Assert.False(CoreEngine.IsRunning || runner.IsRunning);
            Assert.Null(error);
        }
        finally
        {
            try { runner.Stop(); CoreEngine.Stop(); }
            finally
            {
                (AvApp.Sessions, MainShellWindow.PostPortalStart, CoreSettings.Current.PanicKeyEnabled, CoreEngine.StoppedHook, CoreSession.IsSessionRunningProvider) = saved;
                allowed.SetValue(null, oldPortal.Item1);
                failed.SetValue(null, oldPortal.Item2);
                binding.SetValue(null, oldPortal.Item3);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldDisplay);
            }
        }
    });
}

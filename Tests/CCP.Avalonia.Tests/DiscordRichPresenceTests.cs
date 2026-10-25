using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Discord Rich Presence (WPF Services/Account/DiscordRichPresenceService.cs + MainWindow.AccountShell.cs:279):
/// opt-in, never armed without a linked Discord or offline, both switches agree, and on Linux the client speaks the
/// discord-ipc handshake on $XDG_RUNTIME_DIR/discord-ipc-0 (a FAKE socket here; the real Discord is never reached).</summary>
public sealed class DiscordRichPresenceTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>Runs <paramref name="body"/> with XDG_RUNTIME_DIR on an empty temp dir, so no real Discord is reachable.</summary>
    private static async Task WithRuntimeDir(Func<string, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-rpc").FullName;
        var old = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var oldSandboxed = DiscordRichPresenceService.Sandboxed;
        var oldProvider = CoreSettings.ServiceProvider;   // each test swaps in its own SettingsService
        Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", dir);
        try { await body(dir); }
        finally
        {
            DiscordRichPresenceService.Sandboxed = oldSandboxed;
            CoreSettings.ServiceProvider = oldProvider;
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", old);
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public async Task SwitchArmsOnlyWithALinkedDiscordAndBothSurfacesAgree()
    {
        await WithRuntimeDir(_ => AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            DiscordRichPresenceService.Sandboxed = () => true;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            (s.DiscordRichPresenceEnabled, s.HasLinkedDiscord, s.OfflineMode) = (false, false, false);
            var oldRpc = AvApp.DiscordRpc;
            var rpc = AvApp.DiscordRpc = new DiscordRichPresenceService();
            SettingsTabView.SuppressDialogsForTest = true;
            var tab = new SettingsTabView();
            var panel = new ProfilePrivacyPanel();
            var w = new Window { Content = new StackPanel { Children = { tab, panel } } };
            try
            {
                w.Show();
                var quick = tab.FindControl<CheckBox>("ChkQuickDiscordRichPresence")!;
                var profile = panel.FindControl<CheckBox>("ChkDiscordTabRichPresence")!;

                // Not linked: refused, snapped back, nothing armed (WPF AccountShell.cs:290).
                quick.IsChecked = true;
                Assert.False(quick.IsChecked);
                Assert.False(s.DiscordRichPresenceEnabled);
                Assert.False(rpc.IsEnabled);
                profile.IsChecked = true;
                Assert.False(profile.IsChecked);
                Assert.False(rpc.IsEnabled);

                // Linked: armed, and the other surface repaints.
                s.HasLinkedDiscord = true;
                quick.IsChecked = true;
                Assert.True(s.DiscordRichPresenceEnabled);
                Assert.True(rpc.IsEnabled);
                Assert.True(profile.IsChecked);
                Assert.False(rpc.IsConnected);   // sandboxed: the real Discord is never reached

                profile.IsChecked = false;
                Assert.False(rpc.IsEnabled);
                Assert.False(quick.IsChecked);

                // Offline: never armed (WPF UiUpdates.cs:2818, App.xaml.cs:3127 + offline guard).
                (s.DiscordRichPresenceEnabled, s.OfflineMode) = (true, true);
                AvApp.ApplyRichPresence();
                Assert.False(rpc.IsEnabled);
            }
            finally
            {
                w.Close();
                rpc.Dispose();
                AvApp.DiscordRpc = oldRpc;
                SettingsTabView.SuppressDialogsForTest = false;
                (s.DiscordRichPresenceEnabled, s.HasLinkedDiscord, s.OfflineMode) = (false, false, false);
            }
            return Task.CompletedTask;
        }));
    }

    [Fact]
    public async Task StartupArmsOnlyWhenAskedAndLinked()
    {
        await WithRuntimeDir(_ => AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            DiscordRichPresenceService.Sandboxed = () => true;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            var (oldRpc, oldPost) = (AvApp.DiscordRpc, DiscordRichPresenceService.PostToUi);
            try
            {
                // WPF App.xaml.cs:3127: asked but no linked Discord -> built, not armed.
                (s.DiscordRichPresenceEnabled, s.HasLinkedDiscord, s.OfflineMode) = (true, false, false);
                AvApp.StartRichPresence();
                Assert.NotNull(AvApp.DiscordRpc);
                Assert.False(AvApp.DiscordRpc!.IsEnabled);
                AvApp.DiscordRpc.Dispose();

                // Asked and linked -> armed at startup.
                s.HasLinkedDiscord = true;
                AvApp.StartRichPresence();
                Assert.True(AvApp.DiscordRpc!.IsEnabled);
                AvApp.DiscordRpc.Dispose();
            }
            finally
            {
                (AvApp.DiscordRpc, DiscordRichPresenceService.PostToUi) = (oldRpc, oldPost);
                (s.DiscordRichPresenceEnabled, s.HasLinkedDiscord, s.OfflineMode) = (false, false, false);
            }
            return Task.CompletedTask;
        }));
    }

    [Fact]
    public async Task StartupSeedsCoreActivitiesWithoutEnablingTheRpc()
    {
        await WithRuntimeDir(_ => AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            (service.Current.DiscordRichPresenceEnabled, service.Current.HasLinkedDiscord) = (false, false);
            var (oldRpc, oldPost, oldBridge) = (AvApp.DiscordRpc, DiscordRichPresenceService.PostToUi, CoreDiscord.ServiceProvider);
            try
            {
                AvApp.StartRichPresence();
                Assert.Same(AvApp.DiscordRpc, CoreDiscord.Service);
                CoreFlash.Start();
                Assert.Equal("Conditioning", AvApp.DiscordRpc!.CurrentState);
                CoreFlash.Stop();
                Assert.Equal("Browsing", AvApp.DiscordRpc.CurrentState);
                Assert.False(AvApp.DiscordRpc.IsEnabled || AvApp.DiscordRpc.IsConnected);
            }
            finally
            {
                CoreFlash.Stop();
                AvApp.DiscordRpc?.Dispose();
                (AvApp.DiscordRpc, DiscordRichPresenceService.PostToUi, CoreDiscord.ServiceProvider) = (oldRpc, oldPost, oldBridge);
            }
            return Task.CompletedTask;
        }));
    }

    [Fact]
    public void MindWipeOwnsThePresenceWhileItRuns()
    {
        // WPF MindWipeService.cs:179/230: Start shows the mind-wipe activity, Stop of a running service goes back
        // to idle, Stop of a stopped one leaves whatever activity another feature set.
        var oldRpc = AvApp.DiscordRpc;
        var rpc = AvApp.DiscordRpc = new DiscordRichPresenceService();
        var player = new ConditioningControlPanel.Avalonia.Platform.MindWipePlayer((_, _, _, _) => null);
        try
        {
            player.Start(60, 0.5);
            Assert.Equal("Deep conditioning", rpc.CurrentState);
            player.Stop();
            Assert.Equal("Browsing", rpc.CurrentState);
            rpc.SetVideoActivity();
            var video = rpc.CurrentState;
            player.Stop();
            Assert.Equal(video, rpc.CurrentState);
        }
        finally
        {
            player.Stop();
            rpc.Dispose();
            AvApp.DiscordRpc = oldRpc;
        }
    }

    [Fact]
    public async Task OnLinuxTheClientHandshakesOnTheRuntimeDirSocket_AndNoDiscordNeverBlocks()
    {
        if (OperatingSystem.IsWindows()) return;
        await WithRuntimeDir(async dir =>
        {
            DiscordRichPresenceService.Sandboxed = () => false;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            CoreSettings.Current.OfflineMode = false;

            // No Discord running: arming returns at once and throws nothing.
            var none = new DiscordRichPresenceService();
            var sw = Stopwatch.StartNew();
            none.IsEnabled = true;
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1), $"arming blocked for {sw.Elapsed}");
            none.Dispose();

            // A fake Discord: the first frame is the handshake (opcode 0) carrying WPF's application id.
            var path = Path.Combine(dir, "discord-ipc-0");
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);
            var rpc = new DiscordRichPresenceService();
            try
            {
                rpc.IsEnabled = true;
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var conn = await listener.AcceptAsync(cts.Token);
                var header = new byte[8];
                await ReadExactly(conn, header, cts.Token);
                Assert.Equal(0, BitConverter.ToInt32(header, 0));
                var body = new byte[BitConverter.ToInt32(header, 4)];
                await ReadExactly(conn, body, cts.Token);
                Assert.Contains("\"client_id\":\"1461012135982403696\"", Encoding.UTF8.GetString(body));
            }
            finally { rpc.Dispose(); }
        });
    }

    private static async Task ReadExactly(Socket s, byte[] buf, System.Threading.CancellationToken ct)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await s.ReceiveAsync(buf.AsMemory(got), SocketFlags.None, ct);
            if (n == 0) throw new EndOfStreamException();
            got += n;
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChatHotkeyLinuxTests
{
    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinuxRoutesSavedChordAndRebinds(bool portal) => AvaloniaTestDispatcher.Run(() =>
    {
        if (!OperatingSystem.IsLinux()) return;
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current.CompanionPrompt;
        var old = (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal);
        var oldRoute = MainShellWindow.ChatUsesPortal;
        var oldX = MainShellWindow.RegisterChatX11;
        var oldPortal = MainShellWindow.RegisterChatPortal;
        var registrations = new System.Collections.Generic.List<Registration>();
        int calls = 0;
        IDisposable Register(ChordMods mods, string key)
        {
            calls++;
            Assert.Equal(calls == 1 ? ChordMods.Ctrl : ChordMods.Alt | ChordMods.Win, mods);
            Assert.Equal(calls == 1 ? "T" : "B", key);
            var r = new Registration(); registrations.Add(r); return r;
        }
        MainShellWindow.ChatUsesPortal = _ => portal;
        MainShellWindow.RegisterChatX11 = (id, mods, key, _) => { Assert.False(portal); Assert.Equal("chat", id); return Register(mods, key); };
        MainShellWindow.RegisterChatPortal = (mods, key, _) => { Assert.True(portal); return Register(mods, key); };
        var shell = new MainShellWindow();
        try
        {
            (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal) = ("T", "", true);
            shell.StartChatHotkey();
            Assert.Equal(1, calls);
            (s.ChatShortcutKey, s.ChatShortcutModifiers) = ("B", "Alt,Windows");
            shell.ApplyChatHotkey();
            Assert.True(registrations[0].Disposed);
            Assert.Equal(2, calls);
            s.ChatShortcutGlobal = false;
            shell.ApplyChatHotkey();
            Assert.True(registrations[1].Disposed);
            Assert.False(shell.ChatHotkeyArmed);
        }
        finally
        {
            shell.StopChatHotkey(); shell.Close();
            (s.ChatShortcutKey, s.ChatShortcutModifiers, s.ChatShortcutGlobal) = old;
            MainShellWindow.ChatUsesPortal = oldRoute;
            MainShellWindow.RegisterChatX11 = oldX;
            MainShellWindow.RegisterChatPortal = oldPortal;
        }
    });

    [Fact]
    public void PortalRoutesOnlyItsSessionAndChatId()
    {
        int presses = 0;
        using var portal = new PortalChatHotkey(() => presses++);
        portal.SetSessionForTests("/session");
        portal.Route("/other", "chat", true);
        portal.Route("/session", "camera", true);
        portal.Route("/session", "chat", true);
        portal.Route("/session", "chat", true);
        Assert.Equal(1, presses);
        portal.Route("/session", "chat", false);
        portal.Route("/session", "chat", true);
        Assert.Equal(2, presses);
    }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool down, ulong delay);

    // Run the grab in a child testhost: X11SummonChord owns a process-lifetime display and listener.
    [Fact]
    public async Task CapturedIsoChordGrabsThePhysicalKeyOnUkLayout()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (Environment.GetEnvironmentVariable("CCP_CHAT_ISO_CHILD") == "1")
        {
            var display = XOpenDisplay(null);
            Assert.NotEqual(IntPtr.Zero, display);
            try
            {
                foreach (var key in new[] { "OemBackslash", "Oem102" })
                {
                    using var fired = new ManualResetEventSlim();
                    using var grab = X11SummonChord.Register("chat", ChordMods.Ctrl, key, fired.Set);
                    Assert.NotNull(grab);
                    Assert.Null(X11SummonChord.Register("other", ChordMods.Ctrl, key, () => { }));
                    void Send(uint code, bool down) => XTestFakeKeyEvent(display, code, down, 0);
                    Send(37, true); // Ctrl
                    Send(59, true); Send(59, false); XSync(display, false); // UK comma produces less
                    Assert.False(fired.Wait(TimeSpan.FromMilliseconds(250)));
                    Send(94, true); Send(94, false); Send(37, false); XSync(display, false); // ISO LSGT
                    Assert.True(fired.Wait(TimeSpan.FromSeconds(5)), key);
                }
            }
            finally { XCloseDisplay(display); }
            File.WriteAllText(Environment.GetEnvironmentVariable("CCP_CHAT_ISO_MARKER")!, "ok");
            return;
        }

        var marker = Path.Combine(Path.GetTempPath(), "ccp-chat-iso-" + Guid.NewGuid().ToString("N"));
        using var server = Process.Start(new ProcessStartInfo("Xvfb", "-displayfd 1 -noreset -nolisten tcp")
        { RedirectStandardOutput = true, UseShellExecute = false })!;
        try
        {
            var number = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(int.TryParse(number, out _));
            var address = ":" + number;
            using var layout = Process.Start(new ProcessStartInfo("setxkbmap")
            { ArgumentList = { "-display", address, "-layout", "gb" }, UseShellExecute = false })!;
            await layout.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, layout.ExitCode);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, ".github/scripts/cloud-test-env.sh"))) root = root.Parent;
            Assert.NotNull(root);
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var start = new ProcessStartInfo("bash") { WorkingDirectory = root!.FullName, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { ".github/scripts/cloud-test-env.sh", "dotnet", "test", "Tests/CCP.Avalonia.Tests", "--no-build", "--no-restore", "-c", configuration, "--filter", "FullyQualifiedName~CapturedIsoChordGrabsThePhysicalKeyOnUkLayout" })
                start.ArgumentList.Add(arg);
            start.Environment["DISPLAY"] = address;
            start.Environment["CCP_CHAT_ISO_CHILD"] = "1";
            start.Environment["CCP_CHAT_ISO_MARKER"] = marker;
            using var child = Process.Start(start)!;
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
            Assert.True(child.ExitCode == 0, (await output) + "\n" + (await error));
            Assert.True(File.Exists(marker), "Child test did not complete the grab check");
        }
        finally
        {
            if (!server.HasExited) server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
            File.Delete(marker);
        }
    }

}

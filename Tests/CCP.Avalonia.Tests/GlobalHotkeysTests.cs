using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ApplyGlobalChatHotkey / ApplyGlobalCameraHotkey (MainWindow.SessionIO.cs:1160/:1324) and the
/// RegisterHotKey slot dispatch (GlobalHotkeyService.WndProc): each saved combo arms its own slot, the
/// Global toggle releases it, and an X key press reaches only the slot whose modifiers it carries.
/// The X grab is a seam.</summary>
public sealed class GlobalHotkeysTests
{
    private sealed record Grab(int Id, ChordMods Mods, string Key);

    private static void Run(Action<CompanionPromptSettings, List<Grab>, List<int>> body)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var (oldReg, oldUnreg) = (MainShellWindow.RegisterGlobalHotkey, MainShellWindow.UnregisterGlobalHotkey);
        var grabs = new List<Grab>();
        var releases = new List<int>();
        try
        {
            MainShellWindow.RegisterGlobalHotkey = (id, m, k, _) => { grabs.Add(new Grab(id, m, k)); return true; };
            MainShellWindow.UnregisterGlobalHotkey = releases.Add;
            body(CoreSettings.Current.CompanionPrompt, grabs, releases);
        }
        finally
        {
            (MainShellWindow.RegisterGlobalHotkey, MainShellWindow.UnregisterGlobalHotkey) = (oldReg, oldUnreg);
            CoreSettings.ServiceProvider = oldSettings;
        }
    }

    [Fact]
    public void ChatAndCameraArmTheirSavedCombosInTheirOwnSlots() => Run((p, grabs, releases) =>
    {
        p.ChatShortcutGlobal = true; p.ChatShortcutKey = "B"; p.ChatShortcutModifiers = "Windows,Shift";
        p.CameraShortcutGlobal = true; p.CameraShortcutKey = "K"; p.CameraShortcutModifiers = "Control,Alt";
        MainShellWindow.ApplyGlobalChatHotkey();
        MainShellWindow.ApplyGlobalCameraHotkey();
        Assert.Equal(new[]
        {
            new Grab(X11SummonChord.ChatId, ChordMods.Win | ChordMods.Shift, "B"),
            new Grab(X11SummonChord.CameraId, ChordMods.Ctrl | ChordMods.Alt, "K"),
        }, grabs);
        Assert.Empty(releases);
    });

    [Fact]
    public void GlobalOffReleasesTheSlotInsteadOfArmingIt() => Run((p, grabs, releases) =>
    {
        p.ChatShortcutGlobal = false;
        p.CameraShortcutGlobal = false;
        MainShellWindow.ApplyGlobalChatHotkey();
        MainShellWindow.ApplyGlobalCameraHotkey();
        Assert.Empty(grabs);
        Assert.Equal(new[] { X11SummonChord.ChatId, X11SummonChord.CameraId }, releases);
    });

    [Fact]
    public void UnparseableKeyOrNoModifiersFallBackToTheWpfDefaults()
    {
        Assert.Equal((ChordMods.Ctrl, "T"), MainShellWindow.ParseShortcut("NotAKey", "", "T", ChordMods.Ctrl));
        Assert.Equal((ChordMods.Ctrl | ChordMods.Alt, "K"), MainShellWindow.ParseShortcut("", "Bogus", "K", ChordMods.Ctrl | ChordMods.Alt));
        Assert.Equal((ChordMods.Alt, "F5"), MainShellWindow.ParseShortcut("f5", "Alt", "T", ChordMods.Ctrl));
    }

    [Fact]
    public void AKeyPressFiresOnlyTheSlotWithItsModifiersIgnoringCapsAndNumLock()
    {
        const uint shift = 1, caps = 2, ctrl = 4, alt = 8, numLock = 16;
        string? fired = null;
        var slots = new Dictionary<int, (int Code, uint Mask, Action OnPress)>
        {
            [X11SummonChord.ChatId] = (28, ctrl, () => fired = "chat"),
            [X11SummonChord.CameraId] = (28, ctrl | alt, () => fired = "camera"),
        };
        X11SummonChord.Match(slots, 28, ctrl | alt | caps | numLock)?.Invoke();
        Assert.Equal("camera", fired);
        X11SummonChord.Match(slots, 28, ctrl | numLock)?.Invoke();
        Assert.Equal("chat", fired);
        Assert.Null(X11SummonChord.Match(slots, 28, ctrl | shift));
        Assert.Null(X11SummonChord.Match(slots, 29, ctrl));
    }

    [Fact]
    public void AHeldChordFiresOnceUntilItsKeyIsReleased()
    {
        var fired = 0;
        var slots = new Dictionary<int, (int Code, uint Mask, Action OnPress)> { [X11SummonChord.CameraId] = (28, 12, () => fired++) };
        var held = new HashSet<int>();
        // Press, two X auto-repeat presses (no release between them), release, press again.
        foreach (var (press, code) in new[] { (true, 28), (true, 28), (true, 28), (false, 28), (true, 28) })
            X11SummonChord.OnKey(slots, held, press, code, 12)?.Invoke();
        Assert.Equal(2, fired);
        Assert.Null(X11SummonChord.OnKey(slots, held, true, 29, 12));   // another key is not a slot
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
    }

    /// <summary>A headless shell started the way App starts it (StartGlobalHotkeys), with the OS grab
    /// replaced by a map of the armed callbacks, so a test can "press" a slot.</summary>
    private static Task WithShell(Func<MainShellWindow, Dictionary<int, Action>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.AvatarEnabled = false;
        s.WebcamConsentGiven = false;
        var (oldReg, oldUnreg, oldTarget, oldFactory) = (MainShellWindow.RegisterGlobalHotkey,
            MainShellWindow.UnregisterGlobalHotkey, MainShellWindow.HotkeyTarget, WebcamTracker.SourceFactory);
        var armed = new Dictionary<int, Action>();
        var shell = new MainShellWindow();
        try
        {
            MainShellWindow.RegisterGlobalHotkey = (id, _, _, onPress) => { armed[id] = onPress; return true; };
            MainShellWindow.UnregisterGlobalHotkey = id => armed.Remove(id);
            MainShellWindow.HotkeyTarget = () => shell;
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.StartGlobalHotkeys();
            await body(shell, armed);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            (MainShellWindow.RegisterGlobalHotkey, MainShellWindow.UnregisterGlobalHotkey, MainShellWindow.HotkeyTarget,
                WebcamTracker.SourceFactory) = (oldReg, oldUnreg, oldTarget, oldFactory);
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Fact]
    public Task StartupArmsBothSlotsAndTheChatPressRaisesTheShell() => WithShell(async (shell, armed) =>
    {
        Assert.Equal(new[] { X11SummonChord.ChatId, X11SummonChord.CameraId }, armed.Keys.OrderBy(k => k));
        shell.WindowState = WindowState.Minimized;
        armed[X11SummonChord.ChatId]();
        await Until(() => shell.WindowState == WindowState.Normal);
        Assert.Equal(WindowState.Normal, shell.WindowState);
        Assert.Empty(shell.OwnedWindows.OfType<WebcamConsentDialog>());   // chat is not the camera slot
    });

    [Fact]
    public Task CameraPressAsksConsentAndARefusalNeverOpensTheCamera() => WithShell(async (shell, armed) =>
    {
        var opened = 0;
        WebcamTracker.SourceFactory = () => { opened++; return new BlinkTrainerSessionTests.BlackFrames(); };
        armed[X11SummonChord.CameraId]();
        await Until(() => shell.OwnedWindows.OfType<WebcamConsentDialog>().Any());
        shell.OwnedWindows.OfType<WebcamConsentDialog>().Single().Close();   // closed without consenting
        await Until(() => false);
        try
        {
            Assert.False(WebcamTracker.Instance.IsRunning);
            Assert.Equal(0, opened);
        }
        finally { await WebcamTracker.Instance.StopAsync(); }
    });
}

using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

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
}

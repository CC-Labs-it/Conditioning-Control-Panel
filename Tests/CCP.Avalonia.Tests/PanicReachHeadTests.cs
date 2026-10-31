using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Hard rule 6 on this head (owner 2026-10-22, WPF 769373c28 / 605c451ae): the spoken safe word reaches
/// exactly as far as the panic key, the panic key still runs the leash part when it cannot run itself
/// (WPF LeashPanicKeyWhilePanicOff), a preset loaded mid-Lockdown never changes the panic key, and the
/// Lockdown page's padlock never covers a running lockdown.</summary>
public sealed class PanicReachHeadTests
{
    private static void WithShell(Action<MainShellWindow, List<string>> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.PanicKeyEnabled, s.PanicKey, s.LockdownForceStrictLock, s.LockdownDisablePanicKey, s.StrictLockEnabled);
        var (oldAll, oldLd) = (PanicSurfaces.All, LockdownService.Current);
        var stops = new List<string>();
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stops.Add("stop")) };
        var shell = new MainShellWindow();
        shell.Show();
        try { body(shell, stops); }
        finally
        {
            PanicSurfaces.All = oldAll;
            if (LockdownService.Current is { IsActive: true } ld) ld.Deactivate();
            LockdownService.Current = oldLd;
            shell.Close();
            (s.PanicKeyEnabled, s.PanicKey, s.LockdownForceStrictLock, s.LockdownDisablePanicKey, s.StrictLockEnabled) = old;
        }
    });

    /// <summary>Lockdown alone (its own panic-off and strict safeties off), so only Lockdown refuses.</summary>
    private static LockdownService StartLockdown()
    {
        var s = CoreSettings.Current;
        (s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = (false, false);
        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        return ld;
    }

    [Fact]
    public void TheSafeWord_ReachesExactlyAsFarAsThePanicKey() => WithShell((shell, stops) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");
        Assert.True(shell.RunVoicePanic());
        Assert.Single(stops);

        stops.Clear();
        s.StrictLockEnabled = true;                     // Strict Lock alone leaves it whole, as it leaves the key
        Assert.True(shell.RunVoicePanic());
        Assert.Single(stops);
        s.StrictLockEnabled = false;

        stops.Clear();
        s.PanicKeyEnabled = false;                      // the player switched panic off
        Assert.False(shell.RunVoicePanic());
        shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
        Assert.Empty(stops);

        s.PanicKeyEnabled = true;
        var ld = StartLockdown();                       // Lockdown holds every key: one word never ends it
        Assert.True(s.PanicKeyEnabled);
        Assert.False(shell.RunVoicePanic());
        shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
        Assert.Empty(stops);
        Assert.True(ld.IsActive);
    });

    [Fact]
    public void ARefusedSafeWord_SaysNothing() => WithShell((shell, stops) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKeyEnabled, s.PanicKey) = (false, "F8");
        var commands = shell.VoiceCmds;
        Assert.Contains("safe word", commands.Grammar());
        Assert.Equal((Func<bool>)shell.RunVoicePanic, commands.Host.RunFor("panic"));
        Assert.Null(commands.Host.ActionFor("panic"));
        var said = new List<string>();
        var originalSay = commands.Host.Say;
        commands.Host.Say = (text, _) => said.Add(text);
        try
        {
            void SpeakSafeWord()
            {
                Assert.True(commands.TryHandleInlineCommand("hey bambi safe word", new[] { "hey bambi" }));
                Dispatcher.UIThread.RunJobs();
            }

            SpeakSafeWord(); // panic off: heard, refused, and silent
            Assert.Empty(said);
            Assert.Empty(stops);
            s.PanicKeyEnabled = true;
            var ld = StartLockdown();
            SpeakSafeWord(); // Lockdown: the same refusal must stay silent
            Assert.Empty(said);
            Assert.Empty(stops);
            ld.Deactivate();
            SpeakSafeWord(); // positive control: the real route runs and confirms
            Assert.Single(stops);
            Assert.Single(said);
        }
        finally { commands.Host.Say = originalSay; }
    });

    /// <summary>WPF LeashOnPanicPress(panicRuns: false): the runner parks AND stops its own session; with the
    /// panic running it only parks (the stop pass takes the rest down).</summary>
    [Theory]
    [InlineData(true, "park")]
    [InlineData(false, "park+session")]
    public void TheLeashPart_ParksTheTask(bool panicRuns, string expected) => WithShell((shell, _) =>
    {
        var old = LeashTaskHost.Runner;
        var runner = new FakeRunner();
        LeashTaskHost.Runner = runner;
        try
        {
            LeashTaskHost.OnPanicPress(shell, panicRuns);
            Assert.Equal(new[] { expected }, runner.Calls);
        }
        finally { LeashTaskHost.Runner = old; }
    });

    [Fact]
    public void ThePanicKey_RunsTheLeashPart_WhereThePanicCannotRun()
    {
        // Panic always works on a leash (WPF OnGlobalKeyPressed :882 / :967 LeashPanicKeyWhilePanicOff): the
        // refusal for Lockdown or panic-off hands a leashed press to the leash part before it returns.
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Windows", "MainShellWindow.PanicKey.cs"))
            .ReplaceLineEndings("\n");
        var at = src.IndexOf("internal void HandlePanicKeyPress(DateTime now)", StringComparison.Ordinal);
        var body = src.Substring(at, src.IndexOf("\n        }\n", at, StringComparison.Ordinal) - at);
        int gate = body.IndexOf("if (!VoicePanicReach.PanicKeyRuns(LockdownActive, s.PanicKeyEnabled))", StringComparison.Ordinal);
        int leash = body.IndexOf("if (LeashHoldLeashed) PanicKeyLeashOnly(fromKey: true);", StringComparison.Ordinal);
        Assert.True(gate > 0 && leash > gate);
        Assert.True(leash < body.IndexOf("TryGamePauseOnEscape(now)", StringComparison.Ordinal));
        Assert.Contains("LeashTaskHost.OnPanicPress(this, panicRuns: false, fromKey: fromKey)", src);
        // The key's own leash call is the only one that may stand aside for the palette.
        Assert.Contains("Platform.LeashTaskHost.OnPanicPress(this, fromKey: true);", body);
        // The voice's leash-only path never does ("red" is no Escape).
        var voice = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Windows", "MainShellWindow.VoiceCommands.cs"));
        Assert.Contains("PanicKeyLeashOnly();", voice);
        Assert.DoesNotContain("fromKey: true", voice);
    }

    /// <summary>Fix wave 2 #5: only a physical Escape the Ctrl+K palette takes holds the leash part back.</summary>
    [Theory]
    [InlineData(true, true, false, "Escape", true)]
    [InlineData(true, true, false, "escape", true)]
    [InlineData(false, true, false, "Escape", false)]   // voice, tray, leash gate, remote
    [InlineData(true, true, true, "Escape", false)]     // a lock card takes the press first
    [InlineData(true, false, false, "Escape", false)]   // no palette
    [InlineData(true, true, false, "F8", false)]        // a rebound key never closes the palette
    public void ThePalette_OnlyHoldsBackAPhysicalEscape(bool fromKey, bool paletteOpen, bool lockCard, string key, bool skips) =>
        Assert.Equal(skips, LeashTaskHost.PaletteTakesPress(fromKey, paletteOpen, lockCard, key));

    [Fact]
    public void APresetLoadedMidLockdown_NeverChangesThePanicKey() => WithShell((shell, _) =>
    {
        var s = CoreSettings.Current;
        var preset = Preset.GetDefaultPresets()[0];
        var tab = new PresetsTabView();
        StartLockdown();
        s.PanicKeyEnabled = false;                      // as a Lockdown with its no-panic safety leaves it
        Assert.True(tab.LoadPreset(preset));
        Assert.False(s.PanicKeyEnabled);                // every built-in preset writes true
        LockdownService.Current!.Deactivate();
        s.PanicKeyEnabled = false;
        Assert.True(tab.LoadPreset(preset));
        Assert.True(s.PanicKeyEnabled);                 // outside a Lockdown the preset wins
    });

    [Fact]
    public void TheRememberSlot_HoldsThePanicKeyToo()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "Views", "Windows", "MainShellWindow.Remember.cs"));
        int before = src.IndexOf("bool panicBefore = s.PanicKeyEnabled;", StringComparison.Ordinal);
        int apply = src.IndexOf("rc.Preset.ApplyTo(s);", StringComparison.Ordinal);
        int restore = src.IndexOf("LockdownPanicHold.RestoreAfterApply(s, panicBefore);", StringComparison.Ordinal);
        Assert.True(before > 0 && before < apply && apply < restore);
    }

    [Fact]
    public void TheLockdownPadlock_NeverCoversARunningLockdown() => WithShell((shell, _) =>
    {
        var (oldPremium, oldFree) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        CoreEntitlement.HasPremiumProvider = () => false;   // signed out mid-lockdown
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        try
        {
            var gate = shell.FindControl<global::Avalonia.Controls.Control>("LockdownTab")?
                .FindControl<global::Avalonia.Controls.Control>("LockdownGate");
            Assert.NotNull(gate);
            shell.RefreshLockdownGate();
            Assert.True(gate!.IsVisible);
            var ld = StartLockdown();
            shell.RefreshLockdownGate();
            Assert.False(gate.IsVisible);
            ld.Deactivate();
            shell.RefreshLockdownGate();
            Assert.True(gate.IsVisible);                // back as soon as it ends
        }
        finally { (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = (oldPremium, oldFree); }
    });

    private sealed class FakeRunner : ILeashTaskRunner
    {
        internal readonly List<string> Calls = new();
        public bool IsRunning => true;
        public string? RunningPid => "p1";
        public bool Start(Punishment punishment) => true;
        public void Cancel() => Calls.Add("cancel");
        public void Park() => Calls.Add("park");
        public void ParkAndStopItsSession() => Calls.Add("park+session");
        public event Action<string, int, int>? Progress { add { } remove { } }
        public event Action<string>? Completed { add { } remove { } }
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

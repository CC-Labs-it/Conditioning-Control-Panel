using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WaylandWebcamShortcutTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "x11", false)]
    [InlineData("wayland-0", null, true)]
    [InlineData(null, "Wayland", true)]
    public void BackendSelection(string? display, string? session, bool portal)
        => Assert.Equal(portal, LinuxWebcamShortcut.UsesPortal(display, session));

    [Theory]
    [InlineData(ChordMods.Ctrl | ChordMods.Alt, "G", "CTRL+ALT+g")]
    [InlineData(ChordMods.Shift | ChordMods.Win, "Escape", "SHIFT+LOGO+Escape")]
    public void PreferredTriggerUsesXkbNames(ChordMods mods, string key, string expected)
        => Assert.Equal(expected, LinuxWebcamShortcut.PreferredTrigger(mods, key));

    [Fact]
    public async Task PendingConsentIsNotArmed_AssignedTriggerIsTaught_ClosedSessionStopsCallbacks()
    {
        var answer = new TaskCompletionSource<string?>();
        Action? activate = null;
        int presses = 0, closes = 0;
        bool alive = true;
        using var registration = new LinuxWebcamShortcut("CTRL+ALT+G", "Quick Recalibrate", () => presses++,
            (trigger, description, callback) =>
            {
                Assert.Equal("CTRL+ALT+G", trigger);
                Assert.Equal("Quick Recalibrate", description);
                activate = callback;
                return answer.Task;
            }, () => { closes++; return Task.CompletedTask; }, () => alive);
        Assert.False(LinuxWebcamShortcut.Armed(registration));
        activate!();
        Assert.Equal(0, presses);
        answer.SetResult("Meta+G");
        await registration.Ready;
        Assert.True(LinuxWebcamShortcut.Armed(registration));
        Assert.Equal("Meta+G", LinuxWebcamShortcut.Hint(registration, "Ctrl+Alt+G"));
        activate();
        Assert.Equal(1, presses);
        alive = false;
        activate();
        Assert.False(registration.IsBound);
        Assert.Equal(1, presses);
        registration.Dispose();
        registration.Dispose();
        Assert.Equal(1, closes);
    }

    [Fact]
    public async Task CloseDuringConsentCannotResurrectBindingOrInvokeAction()
    {
        var answer = new TaskCompletionSource<string?>();
        Action? activate = null;
        int presses = 0, closes = 0;
        var registration = new LinuxWebcamShortcut("CTRL+ALT+G", "Quick Recalibrate", () => presses++,
            (_, _, callback) => { activate = callback; return answer.Task; },
            () => { closes++; return Task.CompletedTask; });
        registration.Dispose();
        answer.SetResult("Ctrl+Alt+G");
        await registration.Ready;
        activate!();
        Assert.False(registration.IsBound);
        Assert.Equal(0, presses);
        Assert.True(closes >= 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusalOrFailureDoesNotClaimArmed(bool throws)
    {
        using var registration = new LinuxWebcamShortcut("CTRL+ALT+K", "Camera start/stop", () => { },
            (_, _, _) => throws ? Task.FromException<string?>(new InvalidOperationException("synthetic refusal"))
                : Task.FromResult<string?>(null), () => Task.CompletedTask);
        await registration.Ready;
        Assert.False(registration.IsBound);
    }

    [Theory]
    [InlineData("session-a", "quick-recal", true)]
    [InlineData("session-b", "quick-recal", false)]
    [InlineData("session-a", "camera", false)]
    public void ActivationMustMatchSessionAndId(string session, string id, bool matches)
        => Assert.Equal(matches, PortalShortcut.Matches("session-a", "quick-recal", session, id));
}

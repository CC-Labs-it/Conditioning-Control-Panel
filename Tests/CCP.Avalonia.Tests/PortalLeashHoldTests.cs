using System;
using System.Reflection;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PortalLeashHoldTests
{
    [Fact]
    public async Task Signals_FilterSessionAndId_DeduplicatePress_ReleaseAndUnbindEndHold()
    {
        var field = typeof(PortalPanicShortcut).GetField("_session", BindingFlags.NonPublic | BindingFlags.Static)!;
        var old = field.GetValue(null);
        var releaseField = typeof(PortalPanicShortcut).GetField("_onReleased", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldRelease = releaseField.GetValue(null);
        var presses = 0;
        var releases = 0;
        Action down = () => presses++;
        Action up = () => releases++;
        try
        {
            releaseField.SetValue(null, up);
            field.SetValue(null, "/test/session"); // no D-Bus connection or compositor grab in tests
            Assert.False(PortalPanicShortcut.Route("/other/session", "panic", true, down, up));
            Assert.False(PortalPanicShortcut.Route("/test/session", "other", true, down, up));
            Assert.False(PortalPanicShortcut.IsHeld);
            Assert.True(PortalPanicShortcut.Route("/test/session", "panic", true, down, up));
            Assert.True(PortalPanicShortcut.IsHeld);
            Assert.True(MainShellWindow.KeepPortalBound);
            PortalPanicShortcut.Route("/test/session", "panic", true, down, up);
            Assert.Equal((1, 0), (presses, releases));
            PortalPanicShortcut.Route("/test/session", "panic", false, down, up);
            Assert.False(PortalPanicShortcut.IsHeld);
            Assert.Equal((1, 1), (presses, releases));
            Assert.False(MainShellWindow.KeepPortalBound);
            PortalPanicShortcut.Route("/test/session", "panic", false, down, up);
            Assert.Equal(1, releases); // a stale release must not end a different listener's hold
            PortalPanicShortcut.Route("/test/session", "panic", true, down, up);
            await PortalPanicShortcut.UnbindAsync();
            Assert.False(PortalPanicShortcut.IsHeld);
            Assert.Equal((2, 2), (presses, releases));
            Assert.False(PortalPanicShortcut.Route("/test/session", "panic", true, down, up));
        }
        finally { await PortalPanicShortcut.UnbindAsync(); field.SetValue(null, old); releaseField.SetValue(null, oldRelease); }
    }
}

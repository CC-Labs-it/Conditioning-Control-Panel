using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PortalLeashHoldTests
{
    private static readonly DateTime T0 = new(2030, 1, 1);

    [Fact]
    public async Task Signals_FilterSessionAndId_DeduplicatePress_ReleaseAndUnbindEndHold()
    {
        var presses = 0;
        var releases = 0;
        Action down = () => presses++;
        Action up = () => releases++;
        try
        {
            PortalPanicShortcut.SetSessionForTests("/test/session", up); // no D-Bus connection or compositor grab in tests
            Assert.False(PortalPanicShortcut.Route("/other/session", "panic", true, down, up, T0));
            Assert.False(PortalPanicShortcut.Route("/test/session", "other", true, down, up, T0));
            Assert.False(PortalPanicShortcut.IsHeld);
            Assert.False(PortalPanicShortcut.ReportsRelease);
            Assert.True(PortalPanicShortcut.Route("/test/session", "panic", true, down, up, T0));
            Assert.True(PortalPanicShortcut.IsHeld);
            PortalPanicShortcut.Route("/test/session", "panic", true, down, up, T0.AddMilliseconds(100)); // a repeat
            Assert.Equal((1, 0), (presses, releases));
            PortalPanicShortcut.Route("/test/session", "panic", false, down, up, T0.AddSeconds(1));
            Assert.False(PortalPanicShortcut.IsHeld);
            Assert.True(PortalPanicShortcut.ReportsRelease);
            Assert.Equal((1, 1), (presses, releases));
            PortalPanicShortcut.Route("/test/session", "panic", false, down, up, T0.AddSeconds(2));
            Assert.Equal(1, releases); // a stale release must not end a different listener's hold
            PortalPanicShortcut.Route("/test/session", "panic", true, down, up, T0.AddSeconds(3));
            await PortalPanicShortcut.UnbindAsync();
            Assert.False(PortalPanicShortcut.IsHeld);
            Assert.Equal((2, 2), (presses, releases));
            Assert.False(PortalPanicShortcut.Route("/test/session", "panic", true, down, up, T0.AddSeconds(4)));
        }
        finally { await PortalPanicShortcut.UnbindAsync(); PortalPanicShortcut.SetSessionForTests(null, null); }
    }

    [Fact]
    public async Task LostDeactivated_NextPressAfterRepeatGap_RunsPanicAgain()
    {
        var presses = 0;
        Action down = () => presses++;
        Action up = () => { };
        try
        {
            PortalPanicShortcut.SetSessionForTests("/test/session", up);
            PortalPanicShortcut.Route("/test/session", "panic", true, down, up, T0);
            // No Deactivated arrives. A press inside the gap is still a repeat ...
            PortalPanicShortcut.Route("/test/session", "panic", true, down, up, T0 + LeashHoldToCut.RepeatGap);
            Assert.Equal(1, presses);
            // ... but one after the gap is a new press: the panic key must not go dead.
            var later = T0 + LeashHoldToCut.RepeatGap * 2 + TimeSpan.FromMilliseconds(1);
            PortalPanicShortcut.Route("/test/session", "panic", true, down, up, later);
            Assert.Equal(2, presses);
        }
        finally { await PortalPanicShortcut.UnbindAsync(); PortalPanicShortcut.SetSessionForTests(null, null); }
    }
}

using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PortalPauseShortcutTests
{
    [Fact]
    public void SignalsAreScopedAndHeldPressesAreSuppressed()
    {
        int presses = 0, closed = 0;
        var binding = new PortalPauseShortcut("G", () => presses++, () => closed++);
        binding.SetSessionForTests("/pause");
        Assert.True(binding.Ready());
        var now = DateTime.UtcNow;
        binding.Route("/panic", "pause", true, now);
        binding.Route("/pause", "panic", true, now);
        binding.Route("/pause", "pause", true, now);
        binding.Route("/pause", "pause", true, now.AddMilliseconds(100));
        Assert.Equal(1, presses);
        binding.Route("/pause", "pause", false, now.AddMilliseconds(150));
        binding.Route("/pause", "pause", true, now.AddMilliseconds(200));
        Assert.Equal(2, presses);
        binding.Dispose();
        binding.Route("/pause", "pause", true, now.AddSeconds(3));
        Assert.Equal(2, presses);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void ReassignedShortcutClosesAndCannotBeReadied()
    {
        int closed = 0;
        var binding = new PortalPauseShortcut("G", () => { }, () => closed++);
        binding.SetSessionForTests("/pause");
        binding.ShortcutChanged("/other", "K");
        binding.ShortcutChanged("/pause", "g");
        Assert.True(binding.IsSafeTrigger("G"));
        Assert.False(binding.IsSafeTrigger("Ctrl+G"));
        binding.ShortcutChanged("/pause", "K");
        Assert.False(binding.Ready());
        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData("Space", "space")]
    [InlineData("D1", "1")]
    [InlineData("PageUp", "Prior")]
    public void TriggerUsesX11KeysymNames(string key, string trigger)
    {
        var binding = new PortalPauseShortcut(key, () => { }, () => { });
        Assert.True(binding.IsSafeTrigger(trigger));
        binding.Dispose();
    }
}

using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class Win32AwarenessForegroundProbeTests
{
    [Theory]
    [InlineData("CHROME.EXE", "chrome")]
    [InlineData("Game", "game")]
    public void NormalizesProcess(string input, string expected) =>
        Assert.Equal(expected, Win32AwarenessForegroundProbe.NormalizeProcessName(input));

    [Theory]
    [InlineData("progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("Shell_TrayWnd", true)]
    [InlineData("Windows.UI.Core.CoreWindow", true)]
    [InlineData("Other", false)]
    public void ExcludesShell(string name, bool expected) =>
        Assert.Equal(expected, Win32AwarenessForegroundProbe.IsShellClass(name));

    [Fact]
    public void MonitorCoverageAllowsTwoPixelsOnEveryEdge()
    {
        var screen = new Win32AwarenessForegroundProbe.Rect { Left = -100, Top = 10, Right = 100, Bottom = 110 };
        var window = new Win32AwarenessForegroundProbe.Rect { Left = -98, Top = 12, Right = 98, Bottom = 108 };
        Assert.True(Win32AwarenessForegroundProbe.Covers(window, screen));
        window.Left++;
        Assert.False(Win32AwarenessForegroundProbe.Covers(window, screen));
    }

    [Fact]
    public void NativeReadDoesNotThrow()
    {
        var sample = new Win32AwarenessForegroundProbe().Read();
        if (!OperatingSystem.IsWindows()) Assert.Null(sample);
        else if (sample != null) Assert.NotEqual(IntPtr.Zero, sample.Handle);
    }
}

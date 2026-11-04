using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AwarenessWin32ForegroundTests
{
    [Fact]
    public void ReadsTitleAndProcessFromTheSameHandleAndKeepsTitleIfProcessExits()
    {
        var handle = new IntPtr(42);
        var reads = 0;
        string Title(IntPtr h) { Assert.Equal(handle, h); return "League of Legends"; }
        string Process(IntPtr h) { Assert.Equal(handle, h); return "password-manager"; }
        IntPtr Foreground() { reads++; return handle; }
        Assert.Equal(("League of Legends", "password-manager"),
            Win32Windows.ReadAwarenessForeground(Foreground, Title, Process));
        Assert.Equal(1, reads);
        Assert.Equal(("League of Legends", ""),
            Win32Windows.ReadAwarenessForeground(() => handle, Title, _ => throw new InvalidOperationException()));
        Assert.Equal(("", ""), Win32Windows.ReadAwarenessForeground(() => IntPtr.Zero, Title, Process));
    }
}

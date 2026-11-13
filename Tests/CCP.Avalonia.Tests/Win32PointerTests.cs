using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class Win32PointerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ReadsPhysicalPositionAndEitherButton(int held)
    {
        var result = Win32Pointer.Read((out Win32Pointer.Point p) => { p = new() { X = -123, Y = 456 }; return true; },
            key => key == held ? unchecked((short)0x8000) : (short)1);
        Assert.NotNull(result);
        Assert.Equal(new PixelPoint(-123, 456), result.Value.At);
        Assert.True(result.Value.Pressed);
        Assert.False(result.Value.Escape);
    }

    [Fact]
    public void ReleasedButtonsAndLowBitsAreNotHeld()
    {
        var result = Win32Pointer.Read((out Win32Pointer.Point p) => { p = default; return true; }, _ => 1);
        Assert.NotNull(result);
        Assert.False(result.Value.Pressed);
    }

    [Fact]
    public void FailedCursorReadReturnsNull() => Assert.Null(Win32Pointer.Read(
        (out Win32Pointer.Point p) => { p = default; return false; }, _ => throw new System.Exception()));
}

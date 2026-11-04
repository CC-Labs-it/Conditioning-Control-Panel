using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosToyPointerTests
{
    [Theory]
    [InlineData(0, 0, 0, false, false)]
    [InlineData(-32768, 0, 0, true, false)]
    [InlineData(0, -32768, 0, true, false)]
    [InlineData(1, 1, 1, false, false)]
    [InlineData(0, 0, -32768, false, true)]
    public void Windows_sample_preserves_desktop_pixels_and_uses_held_bits(short left, short right, short escape, bool pressed, bool esc)
    {
        var sample = Win32Input.PointerSample(-1920, 1440, left, right, escape);
        Assert.Equal(new PixelPoint(-1920, 1440), sample.At);
        Assert.Equal(pressed, sample.Pressed);
        Assert.Equal(esc, sample.Escape);
    }

    [Fact]
    public void Windows_reader_is_unavailable_off_windows()
    {
        if (!System.OperatingSystem.IsWindows()) Assert.Null(Win32Input.ReadPointer());
    }
}

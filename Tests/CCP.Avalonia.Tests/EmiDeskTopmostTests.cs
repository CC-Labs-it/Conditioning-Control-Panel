using System;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiDeskTopmostTests
{
    [Theory]
    [InlineData(true, true, true, 0u, 1)]
    [InlineData(true, true, true, 8u, 0)]
    [InlineData(true, false, true, 0u, 0)]
    [InlineData(true, true, false, 0u, 0)]
    [InlineData(false, true, true, 0u, 0)]
    public void TickRepairsOnlyLostTopmost(bool windows, bool visible, bool topmost, uint style, int expected)
    {
        int reads = 0, repairs = 0;
        EmiDeskWindow.TopmostWatchTick(windows, new IntPtr(1), visible, topmost,
            _ => { reads++; return style; }, _ => { repairs++; return true; });
        Assert.Equal(expected, repairs);
        Assert.Equal(windows ? 1 : 0, reads);
    }

    [Fact]
    public void CloseStopsWatch() => AvaloniaTestDispatcher.Run(() =>
    {
        var desk = new EmiDeskWindow();
        desk.Show();
        desk.StartTopmostWatch(true);
        Assert.True(desk.TopmostWatchRunning);
        desk.Close();
        Assert.False(desk.TopmostWatchRunning);
    });
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.ScreenCapture;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ScreenFrameSourceTests
{
    [Fact]
    public async Task CapturesEachMonitorWithItsPhysicalOriginAndScale()
    {
        CaptureMonitor[] monitors = [new("left", new Rectangle(-2, 0, 2, 1), 1), new("right", new Rectangle(0, 0, 2, 1), 2)];
        var calls = new List<CaptureMonitor>();
        var source = new DesktopScreenFrames(m => { calls.Add(m); return new byte[m.Bounds.Width * m.Bounds.Height * 4]; },
            () => Task.FromResult<IReadOnlyList<CaptureMonitor>>(monitors));
        var result = await source.CaptureAsync(CancellationToken.None);
        Assert.Equal(ScreenFrameStatus.Available, result.Status);
        Assert.Equal(monitors, calls);
        Assert.Equal(new Rectangle(-1, 0, 1, 1), result.Frames[0].ToScreen(new Rectangle(1, 0, 1, 1)));
        Assert.Equal(new Rectangle(1, 0, 1, 1), result.Frames[1].ToScreen(new Rectangle(1, 0, 1, 1)));
        Assert.Equal(2, result.Frames[1].Scale);
    }

    [Fact]
    public async Task AFailedMonitorIsUnavailableRatherThanPartial()
    {
        var source = new DesktopScreenFrames(_ => null, () => Task.FromResult<IReadOnlyList<CaptureMonitor>>(
            [new("screen", new Rectangle(0, 0, 1, 1), 1)]));
        var result = await source.CaptureAsync(CancellationToken.None);
        Assert.Equal(ScreenFrameStatus.Unavailable, result.Status);
        Assert.Empty(result.Frames);
    }
}

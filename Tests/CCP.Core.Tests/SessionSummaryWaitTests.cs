using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SessionSummaryWaitTests
{
    // WPF MainWindow/MainWindow.Presets.cs:1756-1772.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(14, 14)]
    [InlineData(15, 15)]
    [InlineData(100, 15)]
    public async Task PollsEvery200msUntilVideoFinishesOrThreeSeconds(int finishesAt, int expected)
    {
        int retries = 0;
        await SessionSummaryPresentation.WaitForVideoAsync(() => retries < finishesAt, interval =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(200), interval);
            retries++;
            return Task.CompletedTask;
        }, () => false);
        Assert.Equal(expected, retries);
        Assert.Equal(finishesAt > 15 ? SessionSummaryPresentation.Mode.Passive : SessionSummaryPresentation.Mode.Modal,
            SessionSummaryPresentation.Decide(retries < finishesAt, false, false, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DoesNotOpenWhileWaitingAndStopsOnCompletionOrWindowClose(bool closeWindow)
    {
        bool playing = true, closed = false;
        int retries = 0;
        var tick = new TaskCompletionSource();
        var waiting = SessionSummaryPresentation.WaitForVideoAsync(() => playing, _ =>
        {
            retries++;
            return tick.Task;
        }, () => closed);
        Assert.False(waiting.IsCompleted);
        if (closeWindow) closed = true; else playing = false;
        tick.SetResult();
        await waiting;
        Assert.Equal(1, retries);
    }
}

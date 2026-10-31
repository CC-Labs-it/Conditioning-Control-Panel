using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class SessionRunnerBubbleTests : IDisposable
{
    private readonly SessionSettingsSnapshot _saved = SessionSettingsSnapshot.Capture(CoreSettings.Current);
    private readonly Action? _start = CoreBubbles.StartAction, _stop = CoreBubbles.StopAction,
        _refresh = CoreBubbles.RefreshFrequencyAction;
    private readonly SessionRunner _runner = new(new SessionLogService());
    private bool _running;
    private int _refreshes;

    public SessionRunnerBubbleTests()
    {
        CoreBubbles.StartAction = () => _running = true;
        CoreBubbles.StopAction = () => _running = false;
        CoreBubbles.RefreshFrequencyAction = () => _refreshes++;
    }

    private static Session Template(bool enabled = true, int minute = 0, bool intermittent = false) => new()
    {
        Id = "runner-bubbles", Name = "Bubbles", DurationMinutes = 20,
        Settings = new SessionSettings { BubblesEnabled = enabled, BubblesStartMinute = minute,
            BubblesIntermittent = intermittent, BubblesFrequency = 17, BubblesClickable = false }
    };

    [Fact]
    public void ImmediateFieldRetimesDashboardBubbles_PauseResumeAndStopRestoreCustody()
    {
        var s = CoreSettings.Current;
        s.BubblesEnabled = true;
        s.BubblesFrequency = 3;
        s.BubblesClickable = true;
        _runner.Start(Template());
        Assert.True(_running);
        Assert.Equal(1, _refreshes);
        Assert.Equal((true, 17, false), (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable));
        _runner.Pause();
        Assert.False(_running);
        _runner.Resume();
        Assert.True(_running);
        _runner.Stop();
        Assert.False(_running);
        Assert.Equal((true, 3, true), (s.BubblesEnabled, s.BubblesFrequency, s.BubblesClickable));
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 3, false)]
    [InlineData(true, 0, true)]
    public void SessionTakesDashboardFieldOffUntilItsOwnSchedule(bool enabled, int minute, bool intermittent)
    {
        CoreSettings.Current.BubblesEnabled = true;
        _runner.Start(Template(enabled, minute, intermittent));
        Assert.False(_running);
        Assert.False(CoreSettings.Current.BubblesEnabled);
        _runner.Tick(TimeSpan.FromMinutes(2));
        Assert.False(_running);
        _runner.Tick(TimeSpan.FromMinutes(3));
        var due = enabled && !intermittent;
        Assert.Equal(due, _running);
        Assert.Equal(due, CoreSettings.Current.BubblesEnabled);
        _runner.Tick(TimeSpan.FromMinutes(20));
        Assert.False(_running);
        Assert.True(CoreSettings.Current.BubblesEnabled);
    }

    [Fact]
    public void DelayedContinuousFrequencyStepsEveryFiveMinutes_ImmediateFrequencyStaysConstant()
    {
        _runner.Start(Template(minute: 3));
        _runner.Tick(TimeSpan.FromMinutes(7.99));
        Assert.Equal(17, CoreSettings.Current.BubblesFrequency);
        Assert.Equal(0, _refreshes);
        _runner.Tick(TimeSpan.FromMinutes(8));
        Assert.Equal(18, CoreSettings.Current.BubblesFrequency);
        Assert.Equal(1, _refreshes);
        _runner.Tick(TimeSpan.FromMinutes(8.5));
        Assert.Equal(1, _refreshes);
        _runner.Stop();
        _runner.Start(Template());
        _runner.Tick(TimeSpan.FromMinutes(15));
        Assert.Equal(17, CoreSettings.Current.BubblesFrequency);
        Assert.Equal(2, _refreshes);
    }

    [Fact]
    public void ResumeMatchesWpfTemplateEnabledRuleEvenBeforeDelayedMinute()
    {
        _runner.Start(Template(minute: 3));
        _runner.Pause();
        _runner.Resume(); // WPF SessionEngine.cs:570 does not consult the delayed minute
        Assert.True(_running);
        Assert.False(CoreSettings.Current.BubblesEnabled);
    }

    public void Dispose()
    {
        _runner.Stop();
        CoreEngine.Stop();
        _saved.RestoreTo(CoreSettings.Current);
        CoreBubbles.StartAction = _start;
        CoreBubbles.StopAction = _stop;
        CoreBubbles.RefreshFrequencyAction = _refresh;
        CoreSession.IsSessionRunningProvider = null;
    }
}

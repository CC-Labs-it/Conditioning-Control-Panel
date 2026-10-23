using System;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using Runner = ConditioningControlPanel.Controls.Leash.ILeashTaskRunner;

namespace CCP.Avalonia.Tests;

public sealed class LeashPanicSafetyTests
{
    private sealed class Probe : Runner
    {
        public bool IsRunning => true;
        public string? RunningPid => "p1";
        public bool Parked, OwnedSessionStopped;
        public bool Start(Punishment p) => false;
        public void Cancel() => throw new InvalidOperationException("Panic must preserve progress");
        public void Park() => Parked = true;
        public void ParkAndStopItsSession() { Park(); OwnedSessionStopped = true; }
        public event Action<string, int, int>? Progress { add { } remove { } }
        public event Action<string>? Completed { add { } remove { } }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanicOffUsesTheOwnedSessionStop_OrdinaryPanicOnlyParks(bool panicRuns)
    {
        var old = LeashTaskHost.Runner;
        var probe = new Probe(); LeashTaskHost.Runner = probe;
        try
        {
            LeashTaskHost.OnPanicPress(null, panicRuns);
            Assert.True(probe.Parked);
            Assert.Equal(!panicRuns, probe.OwnedSessionStopped);
        }
        finally { LeashTaskHost.Runner = old; }
    }
}

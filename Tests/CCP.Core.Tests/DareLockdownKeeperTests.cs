using System;
using ConditioningControlPanel.Services.Dares;
using Xunit;

namespace ConditioningControlPanel.Tests.Dares;

public class DareLockdownKeeperTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

    private sealed class Host : IDareHost
    {
        public bool Active;
        public bool Starts = true;
        public Action? BeforeStart;
        public TimeSpan Duration;
        public string? Account() => "a";
        public int TzMinutes() => 0;
        public bool ChasterReady() => false;
        public int BookLockSeconds(int seconds) => 0;
        public void AdoptSp(string account, int sp) { }
        public bool LockdownActive => Active;
        public bool StartLockdown(TimeSpan duration)
        {
            BeforeStart?.Invoke();
            Duration = duration;
            return Active = Starts;
        }
        public event Action<TimeSpan, TimeSpan>? LockdownEnded;
        public void End(TimeSpan ran) { Active = false; LockdownEnded?.Invoke(Duration, ran); }
    }

    [Fact]
    public void Saves_before_start_and_reports_early_end_until_delivered()
    {
        var store = new DareLockdownKeeper.MemoryStore();
        var host = new Host();
        host.BeforeStart = () => Assert.Equal("bet", store.Read()?.BetId);
        var keeper = new DareLockdownKeeper(host, store);
        DareServe? ended = null;
        keeper.Ended += s => ended = s;
        Assert.True(keeper.Begin("account", "bet", 10, Now));
        Assert.Equal(TimeSpan.FromMinutes(10), host.Duration);
        Assert.True(keeper.Serving);
        Assert.False(keeper.Begin("account", "other", 10, Now));
        host.End(TimeSpan.FromMinutes(2));
        Assert.False(keeper.Serving);
        Assert.Equal(DareLockdownRule.EndedEarly, ended?.Outcome);
        Assert.Equal(ended, store.Read());
        keeper.Delivered("other");
        Assert.NotNull(keeper.Pending);
        keeper.Delivered("bet");
        Assert.Null(store.Read());
    }

    [Fact]
    public void Interrupted_serve_is_recovered_and_a_full_serve_has_slack()
    {
        var store = new DareLockdownKeeper.MemoryStore();
        store.Write(new DareServe("account", "bet", 10, Now, null));
        var keeper = new DareLockdownKeeper(new Host(), store);
        Assert.Equal(DareLockdownRule.EndedEarly, keeper.Pending?.Outcome);
        Assert.Equal(DareLockdownRule.EndedEarly, store.Read()?.Outcome);
        Assert.Equal(DareLockdownRule.Served, DareLockdownRule.Outcome(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(595)));
        Assert.Equal(DareLockdownRule.EndedEarly, DareLockdownRule.Outcome(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(594)));
    }

    [Fact]
    public void Rejects_invalid_busy_or_failed_starts()
    {
        var store = new DareLockdownKeeper.MemoryStore();
        var host = new Host { Active = true };
        var keeper = new DareLockdownKeeper(host, store);
        Assert.False(keeper.Begin("a", "b", 0, Now));
        Assert.False(keeper.Begin("a", "b", 1, Now));
        host.Active = false;
        host.Starts = false;
        Assert.False(keeper.Begin("a", "b", 1, Now));
        Assert.Null(store.Read());
    }
}

using System;
using System.Net;
using System.Net.Http;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Core.Tests;

public class ServerClockTimeProviderTests
{
    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void FrozenLocalTimeKeepsTheServerOffsetAndResetRestoresTheSystemClock()
    {
        var local = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(local);
        try
        {
            ServerClock.ResetForTests();
            ServerClock.Clock = clock;
            Assert.True(ServerClock.Observe(local.AddMinutes(-2), local));
            Assert.Equal(local.AddMinutes(-2), ServerClock.UtcNow);
            var expiry = local.AddMinutes(-2).AddSeconds(295);
            Assert.Equal("4:55", LandingRules.Countdown(expiry, ServerClock.UtcNow));
            clock.Now = local.AddSeconds(31);
            Assert.Equal(local.AddMinutes(-2).AddSeconds(31), ServerClock.UtcNow);
            Assert.Equal("4:24", LandingRules.Countdown(expiry, ServerClock.UtcNow));
            using var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Date = clock.Now.AddMinutes(-3);
            Assert.True(ServerClock.Observe(response));
            Assert.Equal(clock.Now.AddMinutes(-3), ServerClock.UtcNow);
        }
        finally { ServerClock.ResetForTests(); }
        Assert.Equal(TimeSpan.Zero, ServerClock.Offset);
        Assert.Same(TimeProvider.System, ServerClock.Clock);
    }
}

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ServerClockCollection { public const string Name = "ServerClock statics"; }

/// <summary>
/// ccp-bugs#1654: a PC clock an hour off had every bug report refused with a bare "HTTP 401"
/// (proxy reason stale_timestamp, 300 s window). The fake proxy below keeps its own clock an hour
/// ahead and refuses exactly like the real one, Date header included.
/// </summary>
[Collection(ServerClockCollection.Name)]
public sealed class BugReportClockSkewTests
{
    private sealed class SkewedProxy : HttpMessageHandler
    {
        public DateTimeOffset ServerNow => DateTimeOffset.UtcNow + Skew;
        public TimeSpan Skew = TimeSpan.FromHours(1);
        public bool AlwaysRefuse;
        public int Count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Count);
            var ts = long.Parse(request.Headers.GetValues("X-Bug-Timestamp").Single());
            var fresh = Math.Abs(ts - ServerNow.ToUnixTimeSeconds()) <= 300;
            var res = fresh && !AlwaysRefuse
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"token\":\"BUG-1654\"}", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"stale_timestamp\"}", Encoding.UTF8, "application/json") };
            res.Headers.Date = ServerNow;
            return Task.FromResult(res);
        }
    }

    [Fact]
    public async Task ClockAnHourOff_LearnsServerClockFromThe401_AndTheRetryIsAccepted()
    {
        ServerClock.ResetForTests();
        try
        {
            var proxy = new SkewedProxy();
            var service = new BugReportService(proxy);
            var result = await service.SubmitAsync(service.CreateDraft("clock test", "", includeAppLog: false));

            Assert.Equal(BugReportService.SubmitOutcome.Success, result.Outcome);
            Assert.Equal("BUG-1654", result.Token);
            Assert.Equal(2, proxy.Count);
        }
        finally { ServerClock.ResetForTests(); }
    }

    [Fact]
    public async Task StillStaleAfterTheRetry_SaysTheClockLooksOff_NotHttp401()
    {
        ServerClock.ResetForTests();
        try
        {
            var proxy = new SkewedProxy { AlwaysRefuse = true };
            var service = new BugReportService(proxy);
            var result = await service.SubmitAsync(service.CreateDraft("clock test", "", includeAppLog: false));

            Assert.Equal(BugReportService.SubmitOutcome.ValidationFailed, result.Outcome);
            Assert.Equal(Loc.Get("bug_report_clock_off"), result.ErrorMessage);
            Assert.Contains("clock", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, proxy.Count);
        }
        finally { ServerClock.ResetForTests(); }
    }
}

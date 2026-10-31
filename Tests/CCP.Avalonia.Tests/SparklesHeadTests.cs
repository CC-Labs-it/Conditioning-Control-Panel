using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Sparkles;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SparklesHeadTests
{
    private sealed class Reply : HttpMessageHandler
    {
        public Action? OnSend;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            OnSend?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("""{"ok":true,"receipt":{"credited":10,"sp":110}}""") });
        }
    }

    private static SparklesClaim Claim() => SparklesClaim.Create("rt3d", "solo", SparklesResult.ForPlace(1, 8), 600000);

    [Theory]
    [InlineData(false, false, 100, 110, 1)]
    [InlineData(true, false, 100, 100, 0)]
    [InlineData(false, true, 100, 100, 0)]
    [InlineData(false, false, 120, 120, 0)]
    public async Task QueuedReceiptRechecksAccountPanicAndBalance(bool swap, bool panic, int before, int expected, int saves)
    {
        var settings = CoreSettings.Current;
        var old = settings.SkillPoints;
        var queued = new Queue<Action>();
        var account = "first";
        int saved = 0;
        SparklePointAward? award = null;
        void Award(object? sender, SparklePointAward value) => award = value;
        SparklePointRewards.Awarded += Award;
        try
        {
            settings.SkillPoints = before;
            var head = new SparklesHead(new HttpClient(new Reply()), () => (account, "test"),
                queued.Enqueue, () => saved++, baseUrl: "http://127.0.0.1:9");
            Assert.True((await head.ClaimAsync(Claim())).Ok);
            if (swap) account = "second";
            if (panic) head.Stop();
            Assert.Single(queued)();
            Assert.Equal(expected, settings.SkillPoints);
            Assert.Equal(saves, saved);
            if (saves == 0) Assert.Null(award);
            else Assert.Equal(new SparklePointAward(10, 110, SparklePointSource.Place), award);
            head.Stop();
        }
        finally { settings.SkillPoints = old; SparklePointRewards.Awarded -= Award; }
    }

    [Fact]
    public async Task PanicDuringSendRejectsLateReceiptAndNextClaimCanRun()
    {
        var handler = new Reply();
        var queued = new Queue<Action>();
        var head = new SparklesHead(new HttpClient(handler), () => ("first", "test"), queued.Enqueue, () => {}, baseUrl: "http://127.0.0.1:9");
        handler.OnSend = head.Stop;
        Assert.False((await head.ClaimAsync(Claim())).Ok);
        Assert.Empty(queued);
        handler.OnSend = null;
        Assert.True((await head.ClaimAsync(Claim())).Ok);
        Assert.Single(queued);
        head.Stop();
    }
}

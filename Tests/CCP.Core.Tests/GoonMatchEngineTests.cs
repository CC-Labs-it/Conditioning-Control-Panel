using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The match engine moved to Core with its UI-thread hops behind GoonDispatch. With no head
/// attached it runs headless: two engines over the loopback pair walk lobby -> consent -> draft, and
/// a head that reports shutdown gets no callbacks. One class, so the static seam is never shared
/// with a parallel test.</summary>
public class GoonMatchEngineTests
{
    private static async Task<bool> Until(Func<bool> done)
    {
        var sw = Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < 5000) await Task.Delay(20);
        return done();
    }

    [Fact]
    public async Task Two_engines_over_loopback_reach_the_draft_without_a_head()
    {
        using var pair = GoonLoopbackTransport.CreatePair(GoonLoopbackOptions.Instant());
        using var host = new GoonMatchService(pair.Host, isHost: true);
        using var guest = new GoonMatchService(pair.Guest, isHost: false);

        Assert.True(host.AdoptLobby());
        Assert.True(guest.AdoptLobby());
        Assert.True(await pair.ConnectAsync());

        Assert.True(await Until(() => host.Phase == GoonMatchPhase.Consent && guest.Phase == GoonMatchPhase.Consent),
            $"host {host.Phase}, guest {guest.Phase}");

        host.ConfirmConsent();
        guest.ConfirmConsent();
        Assert.True(await Until(() => host.Phase == GoonMatchPhase.Draft && guest.Phase == GoonMatchPhase.Draft),
            $"host {host.Phase}, guest {guest.Phase}");
    }

    [Fact]
    public void A_head_shutting_down_drops_goon_callbacks()
    {
        var ran = 0;
        try
        {
            GoonDispatch.ShutdownStarted = () => true;
            GoonDispatch.Post(() => ran++);
            Assert.Equal(0, ran);

            GoonDispatch.ShutdownStarted = null;   // unseeded: no head, run in place
            GoonDispatch.Post(() => ran++);
            Assert.Equal(1, ran);
        }
        finally { GoonDispatch.ShutdownStarted = null; }
    }
}

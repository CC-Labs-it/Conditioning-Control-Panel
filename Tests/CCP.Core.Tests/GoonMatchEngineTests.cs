using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The match engine moved to Core with its UI-thread hops behind GoonDispatch. With no head
/// attached it runs headless: two engines over the loopback pair walk lobby -> consent -> draft, and
/// a head that reports shutdown gets no callbacks. CoreDispatch and GoonDispatch are process-global,
/// hence the SessionStatics collection.</summary>
[Collection(SessionStatics.Name)]
public class GoonMatchEngineTests
{
    /// <summary>Completes when <paramref name="m"/> enters <paramref name="phase"/>. Subscribe before
    /// the step that causes it; PhaseChanged fires after Phase is set.</summary>
    private static Task Reaches(GoonMatchService m, GoonMatchPhase phase)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        m.PhaseChanged += (_, p) => { if (p == phase) tcs.TrySetResult(); };
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Two_engines_over_loopback_reach_the_draft_on_one_ui_thread()
    {
        // The engine is not thread-safe: like a head, every callback and every user step runs on
        // one "UI" thread, so the test never races the transport's background threads.
        var ui = new BlockingCollection<Action>();   // not disposed: the pump may still be draining
        var pump = new Thread(() =>
        {
            foreach (var a in ui.GetConsumingEnumerable()) { try { a(); } catch { } }
        }) { IsBackground = true };
        pump.Start();
        var saved = CoreDispatch.PostProvider;
        CoreDispatch.PostProvider = ui.Add;
        try
        {
            using var pair = GoonLoopbackTransport.CreatePair(GoonLoopbackOptions.Instant());
            using var host = new GoonMatchService(pair.Host, isHost: true);
            using var guest = new GoonMatchService(pair.Guest, isHost: false);
            var consent = Task.WhenAll(Reaches(host, GoonMatchPhase.Consent), Reaches(guest, GoonMatchPhase.Consent));
            var draft = Task.WhenAll(Reaches(host, GoonMatchPhase.Draft), Reaches(guest, GoonMatchPhase.Draft));

            Assert.True(host.AdoptLobby());
            Assert.True(guest.AdoptLobby());
            Assert.True(await pair.ConnectAsync());
            await consent;

            CoreDispatch.Post(() => { host.ConfirmConsent(); guest.ConfirmConsent(); });
            await draft;
        }
        finally
        {
            CoreDispatch.PostProvider = saved;
            ui.CompleteAdding();
        }
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

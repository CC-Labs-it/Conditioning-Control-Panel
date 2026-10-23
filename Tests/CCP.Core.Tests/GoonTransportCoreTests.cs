using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The Goon transports (MatchClock, base, loopback, signaling, relay) now live in Core and
/// reach the head only through CoreDispatch / CoreAccount / CoreReleaseContent / CoreSettings.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class GoonTransportCoreTests
{
    [Fact]
    public async Task LoopbackPair_SyncsClocks_AndRaisesMessagesThroughCoreDispatch()
    {
        var oldPost = CoreDispatch.PostProvider;
        var hops = 0;
        CoreDispatch.PostProvider = action => { Interlocked.Increment(ref hops); action(); };
        try
        {
            using var pair = GoonLoopbackTransport.CreatePair(GoonLoopbackOptions.Instant());
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            Assert.True(await pair.ConnectAsync(cts.Token));
            Assert.True(pair.Host.Clock.IsSynced);
            Assert.True(pair.Guest.Clock.IsSynced);

            var got = new TaskCompletionSource<GoonMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            pair.Guest.MessageReceived += (_, m) => got.TrySetResult(m);
            var hopsBefore = Volatile.Read(ref hops);
            await pair.Host.SendAsync(new HelloMsg { DisplayName = "Bambi" }, cts.Token);

            var hello = Assert.IsType<HelloMsg>(await got.Task.WaitAsync(cts.Token));
            Assert.Equal("Bambi", hello.DisplayName);
            Assert.True(Volatile.Read(ref hops) > hopsBefore, "MessageReceived must surface through the head's UI seam");
        }
        finally { CoreDispatch.PostProvider = oldPost; }
    }

    [Fact]
    public void GoonDispatch_ContainsAThrowingCallback_AndKeepsDelivering()
    {
        var oldPost = CoreDispatch.PostProvider;
        CoreDispatch.PostProvider = null;   // inline: no head attached
        try
        {
            var ran = false;
            GoonDispatch.Post(() => throw new InvalidOperationException("boom"));
            GoonDispatch.Post(() => ran = true);
            Assert.True(ran);
        }
        finally { CoreDispatch.PostProvider = oldPost; }
    }

    [Fact]
    public async Task Signaling_InviteBody_CarriesCoreIdentityAndVersion()
    {
        var oldId = CoreAccount.UnifiedUserId;
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        CoreAccount.UnifiedUserId = "u_goon";
        CoreReleaseContent.AppVersionProvider = () => "7.1.5";
        try
        {
            string? url = null, body = null;
            using var client = new GoonSignalingClient((u, b, _) =>
            {
                url = u; body = b;
                return Task.FromResult((HttpStatusCode.OK, "{\"ok\":true,\"code\":\"7QK4RM\",\"token\":\"tok\"}"));
            });

            var invite = await client.CreateInviteAsync("Bambi", preferRelay: true, CancellationToken.None);

            Assert.Equal("7QK4RM", invite!.Code);
            Assert.EndsWith("/v2/goon/invite", url);
            var json = JObject.Parse(body!);
            Assert.Equal("u_goon", (string?)json["unified_id"]);
            Assert.Equal("7.1.5", (string?)json["app_version"]);
            Assert.True((bool)json["prefer_relay"]!);
        }
        finally
        {
            CoreAccount.UnifiedUserId = oldId;
            CoreReleaseContent.AppVersionProvider = oldVersion;
        }
    }

    [Fact]
    public async Task RelayInvite_SendsTheSettingsDisplayName_AndReportsTheServerRefusal()
    {
        var oldName = CoreSettings.Current.UserDisplayName;
        CoreSettings.Current.UserDisplayName = "Circe";
        try
        {
            string? body = null;
            var signaling = new GoonSignalingClient((_, b, _) =>
            {
                body = b;
                return Task.FromResult((HttpStatusCode.Forbidden, "{\"error\":\"no_host_access\"}"));
            });
            using var relay = new GoonRelayTransport(isHost: true, signaling);

            Assert.Null(await relay.CreateInviteAsync(CancellationToken.None));
            Assert.Equal("Circe", (string?)JObject.Parse(body!)["display_name"]);
            Assert.Equal(GoonSignalingClient.ErrorNoHostAccess, relay.LastError);
            Assert.Equal(GoonTransportState.Disconnected, relay.State);
            signaling.Dispose();
        }
        finally { CoreSettings.Current.UserDisplayName = oldName; }
    }
}

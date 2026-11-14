using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Rain;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ProfileSyncService.ApplyValidatePrizes:585-587 and RainLookSync.AdoptOnThisPc on this head: a
/// validate prizes block for the signed-in account pulls Rain's look once, after its grants landed; the
/// adopted look is clamped to them and plays this PC's pack when one exists.</summary>
public sealed class RainLookSyncHeadTests
{
    private const string Dress = "rose-flare", Hair = "original-blonde", Grant = "cos.rain.dress.rose-flare";
    private static readonly string ServerLook = "{\"rev\":1,\"items\":{\"dress\":\"" + Dress + "\",\"hair\":\"" + Hair + "\"},\"colors\":{}}";

    private sealed class Wire : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Url, string? Token)> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.Method, r.RequestUri!.ToString(), r.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"equip\":{\"rain\":" + ServerLook + ",\"updatedAt\":\"2026-11-01T12:00:00Z\"}}"),
            });
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task A_validate_block_pulls_once_and_adopts_the_look_clamped_to_its_grants(bool owned) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc);
        var (client, root, secrets, grants) = (RainLookSyncHead.Client, RainLookSyncHead.PackRoot, CoreSecrets.RetrieveProvider, RainLookSyncHead.Grants);
        var account = CoreAccount.UnifiedUserId;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-rainhead-" + Guid.NewGuid().ToString("N"));
        try
        {
            RainLookSyncHead.ForgetPulled();
            (s.UnifiedId, s.OfflineMode) = ("rain-u1", false);
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok-1" : null;
            (s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc) = ("", "", false, null);
            // This test's own ownership service and feed: the app's shared grants are never touched.
            var ownership = new OwnershipService(() => "rain-u1", null, a => a());
            var sink = PrizeOwnership.SinkFor(new PrizesFeed(() => ownership, () => "rain-u1", a => a()));
            CoreAccount.UnifiedUserId = "rain-u1";   // the receipt pulls only for the signed-in account
            RainLookSyncHead.Grants = () => ownership.Grants;
            var wire = new Wire();
            RainLookSyncHead.Client = new RainLookSync(new HttpClient(wire), RainLookSyncHead.Hooks("https://proxy.test"));
            var expected = owned ? RainLookCodec.Normalize(ServerLook)
                : RainLookCodec.Normalize("{\"rev\":1,\"items\":{\"hair\":\"" + Hair + "\"},\"colors\":{}}");
            RainLookSyncHead.PackRoot = dir;
            WritePack(dir, RainLookHash.Compute(expected));

            sink("rain-u1", "other", new PrizesBlock { Revision = 1, Grants = new() }, "Discord validate");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(wire.Seen);   // a block answered for another record is no readiness

            sink("rain-u1", "rain-u1",
                new PrizesBlock { Revision = 2, Grants = owned ? new() { Grant } : new() }, "Patreon validate");
            for (var i = 0; i < 200 && s.RainLookAgreedUtc == null; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }

            var get = Assert.Single(wire.Seen);
            Assert.Equal(HttpMethod.Get, get.Method);
            Assert.Equal("https://proxy.test/v2/equip?unified_id=rain-u1", get.Url);
            Assert.Equal("tok-1", get.Token);
            Assert.Equal(expected, s.RainLook);
            Assert.Equal(RainLookHash.Compute(expected), s.RainPackHash);
            Assert.False(s.RainLookPushOwed);
            Assert.Equal(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), s.RainLookAgreedUtc);

            sink("rain-u1", "rain-u1", null, "Discord validate");
            Dispatcher.UIThread.RunJobs();
            Assert.Single(wire.Seen);   // once per account per run
        }
        finally
        {
            RainLookSyncHead.ForgetPulled();
            RainLookSyncHead.Client = client;
            RainLookSyncHead.PackRoot = root;
            RainLookSyncHead.Grants = grants;
            CoreAccount.UnifiedUserId = account;
            CoreSecrets.RetrieveProvider = secrets;
            (s.UnifiedId, s.OfflineMode, s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc) = old;
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    });

    private sealed class NoPrizesWire : HttpMessageHandler
    {
        internal readonly List<HttpMethod> Methods = new();
        internal const string Look = "{\"rev\":1,\"items\":{\"hair\":\"original-blonde\"},\"colors\":{}}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Methods.Add(request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true,\"equip\":{\"rain\":" + Look + ",\"updatedAt\":\"2026-11-01T12:00:00Z\"}}"),
            });
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Matching_validation_without_a_snapshot_syncs_once(bool owed, bool emptyBlock) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc);
        var (client, root, secrets, grants, account) = (RainLookSyncHead.Client, RainLookSyncHead.PackRoot,
            CoreSecrets.RetrieveProvider, RainLookSyncHead.Grants, CoreAccount.UnifiedUserId);
        using var wire = new NoPrizesWire();
        try
        {
            RainLookSyncHead.ForgetPulled();
            (s.UnifiedId, s.OfflineMode) = ("rain-u1", false);
            CoreAccount.UnifiedUserId = "rain-u1";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok-1" : null;
            (s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc) =
                (owed ? RainLookCodec.Normalize(NoPrizesWire.Look) : "", "", owed, null);
            var ownership = new OwnershipService(() => CoreAccount.UnifiedUserId, null, a => a());
            var sink = PrizeOwnership.SinkFor(new PrizesFeed(() => ownership, () => CoreAccount.UnifiedUserId, a => a()));
            RainLookSyncHead.Grants = () => ownership.Grants;
            RainLookSyncHead.PackRoot = Path.Combine(Path.GetTempPath(), "ccp-rainhead-absent-" + Guid.NewGuid().ToString("N"));
            RainLookSyncHead.Client = new RainLookSync(new HttpClient(wire), RainLookSyncHead.Hooks("https://proxy.test"));
            var prizes = emptyBlock ? new PrizesBlock() : null;
            sink("rain-u1", "other", prizes, "wrong answer");
            sink("other", "other", prizes, "wrong account");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(wire.Methods);
            Assert.Equal(0, ownership.Revision);

            sink("rain-u1", "rain-u1", prizes, "validate");
            for (var i = 0; i < 200 && s.RainLookAgreedUtc == null; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
            Assert.Equal(owed ? HttpMethod.Put : HttpMethod.Get, Assert.Single(wire.Methods));
            Assert.Equal(RainLookCodec.Normalize(NoPrizesWire.Look), s.RainLook);
            Assert.False(s.RainLookPushOwed);
            Assert.Equal(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), s.RainLookAgreedUtc);
            Assert.Equal(0, ownership.Revision); // readiness does not invent an ownership snapshot
            sink("rain-u1", "rain-u1", prizes, "validate again");
            Dispatcher.UIThread.RunJobs();
            Assert.Single(wire.Methods);
        }
        finally
        {
            RainLookSyncHead.ForgetPulled();
            (RainLookSyncHead.Client, RainLookSyncHead.PackRoot, RainLookSyncHead.Grants) = (client, root, grants);
            CoreAccount.UnifiedUserId = account;
            CoreSecrets.RetrieveProvider = secrets;
            (s.UnifiedId, s.OfflineMode, s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc) = old;
        }
    });

    [Fact]
    public void An_adopted_look_without_a_pack_here_plays_the_embedded_clips()
    {
        var s = CoreSettings.Current;
        var old = (s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc);
        var (root, grants) = (RainLookSyncHead.PackRoot, RainLookSyncHead.Grants);
        try
        {
            RainLookSyncHead.Grants = () => Array.Empty<string>();
            RainLookSyncHead.PackRoot = Path.Combine(Path.GetTempPath(), "ccp-rainhead-missing-" + Guid.NewGuid().ToString("N"));
            (s.RainPackHash, s.RainLookPushOwed) = ("stale", true);
            RainLookSyncHead.Adopt("{\"rev\":1,\"items\":{\"hair\":\"" + Hair + "\"},\"colors\":{}}", null);
            Assert.Equal("", s.RainPackHash);
            Assert.False(s.RainLookPushOwed);
            Assert.Contains(Hair, s.RainLook);
        }
        finally
        {
            (RainLookSyncHead.PackRoot, RainLookSyncHead.Grants) = (root, grants);
            (s.RainLook, s.RainPackHash, s.RainLookPushOwed, s.RainLookAgreedUtc) = old;
        }
    }

    private static void WritePack(string root, string hash)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, hash)).FullName;
        File.WriteAllText(Path.Combine(folder, "idle.webp"), "x");
        File.WriteAllText(Path.Combine(folder, "emotes.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "clip_timing.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "manifest.json"), new JObject
        {
            ["rev"] = 1, ["lookHash"] = hash,
            ["clips"] = new JObject { ["idle"] = new JObject { ["file"] = "idle.webp", ["frames"] = 1, ["fps"] = 1, ["w"] = 1, ["h"] = 1 } },
        }.ToString());
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public sealed class SpiralMigrationSyncTests
{
    [Fact]
    public void RestoreRewritesLedgerAndQueuesOnlyServerStages()
    {
        var s = new AppSettings { PlayerLevel = 45, PlayerXP = 100, HighestLevelEver = 90,
            DescentMigrationOffered = true, LastConfirmedServerXp = 999999, LastConfirmedServerXpAccount = "a" };
        var offer = new DescentMigrationOffer { TotalXpEarned = 16000, RestoreBasisXp = 70000, DevotionDays = 30 };
        var expected = DescentMigration.Resolve(DescentMigrationChoices.Restore, offer);
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(DescentMigration.ApplyRestore(s, offer, new DescentStage { N = 4, Thresholds = new[] { 1, 10, 40 } }, now));
        Assert.Equal((expected.Level, expected.XpIntoLevel, DescentEpochs.AccountDescent), (s.PlayerLevel, s.PlayerXP, s.DescentEpoch));
        Assert.Equal((45, expected.LifetimeXp, 90), (s.DescentPreMigrationLevel, s.DescentPreMigrationLifetimeXp, s.HighestLevelEver));
        Assert.Equal(new[] { 1, 2 }, s.DescentPendingStageCeremonies);
        Assert.Equal(DescentMigration.CycleXpBonus, s.DescentCycleXpBonus);
        Assert.True(s.DescentVeteranArchive);
        Assert.Equal(now, s.DescentAnchorUtc);
        Assert.Null(s.DescentLastStageDripDate);
        Assert.Equal(0, s.LastConfirmedServerXp);
        Assert.False(s.DescentMigrationOffered);
        Assert.Equal(DescentMigrationChoices.Restore, s.PendingDescentMigrationChoice);
        Assert.False(s.DescentMigrationCompleted);
        Assert.False(DescentMigration.ApplyRestore(s, offer, null, now.AddDays(1)));
        Assert.Equal(now, s.DescentAnchorUtc);
        var noLadder = new AppSettings();
        Assert.True(DescentMigration.ApplyRestore(noLadder, offer, null, now));
        Assert.Empty(noLadder.DescentPendingStageCeremonies);
        var fallback = new AppSettings();
        Assert.True(DescentMigration.ApplyRestore(fallback, offer, new DescentStage { N = 3 }, now));
        Assert.Equal(new[] { 1, 2, 3 }, fallback.DescentPendingStageCeremonies);
    }

    [Fact]
    public void StageQueueWaitsForAckAndReleasesOncePerLocalDay()
    {
        var s = new AppSettings { DescentPendingStageCeremonies = new List<int> { 1, 2 } };
        var today = new DateTime(2026, 10, 20, 23, 59, 0);
        Assert.Null(DescentMigration.ReleaseStageCeremony(s, today));
        s.DescentMigrationCompleted = true;
        Assert.Equal(1, DescentMigration.ReleaseStageCeremony(s, today));
        Assert.Equal("2026-10-20", s.DescentLastStageDripDate);
        Assert.Null(DescentMigration.ReleaseStageCeremony(s, today));
        Assert.Equal(new[] { 2 }, s.DescentPendingStageCeremonies);
        Assert.Equal(2, DescentMigration.ReleaseStageCeremony(s, today.AddMinutes(1)));
        Assert.Null(DescentMigration.ReleaseStageCeremony(s, today.AddDays(1)));
    }

    [Fact]
    public async Task OfferWithholdsThenRestoresAndSubmitsUntilAck()
    {
        var old = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreDispatch.PostProvider);
        var settings = new SettingsService();
        var queued = new List<Action>();
        var handler = new FakeSync();
        var reader = new DescentService();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler)
            { DescentReader = reader, PostMigration = queued.Add };
        var now = DateTime.UtcNow;
        sync.UtcNow = () => now;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreDispatch.PostProvider = null;
            var s = settings.Current;
            s.UnifiedId = "migration-test";
            s.OfflineMode = false;
            s.PlayerLevel = 40;
            s.LastConfirmedServerXp = 0;
            s.PendingDescentMigrationChoice = null;
            s.DescentMigrationCompleted = false;
            s.DescentMigrationOffered = false;
            var notifications = 0;
            reader.BlockChanged += (_, _) => notifications++;
            sync.MarkLoaded(null);
            Assert.True(await sync.PushAsync("offer"));
            Assert.True(handler.Bodies[0].Value<bool>("descent_auto"));
            Assert.Null(handler.Bodies[0]["descent_migration"]);
            Assert.True(sync.HasLiveMigrationOffer(s));
            Assert.True(SpiralRoom.SpiralWithheldFor(s, sync.HasLiveMigrationOffer(s), false));
            Assert.Single(queued);
            Assert.Equal(1, notifications);
            queued[0]();
            Assert.Equal(2, notifications);
            Assert.Equal(DescentMigrationChoices.Restore, s.PendingDescentMigrationChoice);
            Assert.False(SpiralRoom.SpiralWithheldFor(s, sync.HasLiveMigrationOffer(s), false));
            var restored = (s.PlayerLevel, s.PlayerXP, s.DescentAnchorUtc);
            s.LastConfirmedServerXp = 100000000;
            s.LastConfirmedServerXpAccount = s.UnifiedId;
            s.LastConfirmedServerXpSeason = s.CurrentSeason;
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("submit"));
            Assert.Equal("restore", (string?)handler.Bodies[1]["descent_migration"]?["choice"]);
            Assert.Equal(s.PlayerLevel, (int)handler.Bodies[1]["level"]!);
            Assert.Equal((int)ProfileAdopt.TotalXp(s), (int)handler.Bodies[1]["xp"]!);
            Assert.Single(queued); // repeated offer while pending is ignored
            Assert.Equal(restored, (s.PlayerLevel, s.PlayerXP, s.DescentAnchorUtc));
            var stages = new List<int>();
            sync.StageCeremonyDue = stages.Add;
            s.DescentPendingStageCeremonies = new List<int> { 2, 3 };
            s.DescentLastStageDripDate = null;
            handler.Response = """{"descent_migration":{"completed":true,"required":true,"choice":"restore"}}""";
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("ack"));
            Assert.True(s.DescentMigrationCompleted);
            Assert.Equal(new[] { 2 }, stages);
            Assert.Equal(new[] { 3 }, s.DescentPendingStageCeremonies);
            Assert.Null(s.PendingDescentMigrationChoice);
            Assert.Single(queued); // ack wins over an offer in the same response
            s.DescentLastStageDripDate = null;
            s.LastConfirmedServerXp = 0;
            handler.Response = "{"; // an unreadable response cannot spend tomorrow's stage
            now += SyncPush.Cooldown + TimeSpan.FromSeconds(1);
            Assert.True(await sync.PushAsync("malformed"));
            Assert.Equal(new[] { 2 }, stages);
            Assert.Equal(new[] { 3 }, s.DescentPendingStageCeremonies);
        }
        finally
        {
            sync.Reset();
            settings.SealForReset();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreDispatch.PostProvider) = old;
        }
    }

    [Theory]
    [InlineData("cleared")]
    [InlineData("zero")]
    [InlineData("late")]
    public async Task ArmedFuseHoldsOfferUntilRelease(string release)
    {
        var old = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreDispatch.PostProvider);
        var settings = new SettingsService();
        var queued = new List<Action>();
        using var fuse = new DescentCountdownService();
        var handler = new FakeSync();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, handler)
            { Countdown = fuse, PostMigration = queued.Add };
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreDispatch.PostProvider = null;
            var s = settings.Current;
            s.UnifiedId = "held-offer";
            s.OfflineMode = false;
            s.LastConfirmedServerXp = 0;
            s.PendingDescentMigrationChoice = null;
            s.DescentMigrationCompleted = false;
            s.DescentCeremonyAtUtc = null;
            var at = DateTime.UtcNow.AddMinutes(30).ToString("O");
            fuse.ApplyCeremonyAt(at);
            var response = JObject.Parse(handler.Response);
            response["descent_countdown"] = new JObject { ["ceremony_at"] = at };
            handler.Response = response.ToString();
            sync.MarkLoaded(null);
            Assert.True(await sync.PushAsync("held offer"));
            Assert.Single(queued)();
            Assert.Null(s.PendingDescentMigrationChoice);
            Assert.True(sync.HasLiveMigrationOffer(s));
            if (release == "cleared") fuse.ApplyCeremonyAt(null);
            else
            {
                s.DescentCeremonyAtUtc = DateTime.UtcNow.AddMinutes(release == "late" ? -10 : -0.01).ToString("O");
                fuse.OnTimerTick();
            }
            Assert.Equal(2, queued.Count);
            queued[1]();
            Assert.Equal(DescentMigrationChoices.Restore, s.PendingDescentMigrationChoice);
        }
        finally
        {
            sync.Reset();
            sync.Countdown = null;
            settings.SealForReset();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreDispatch.PostProvider) = old;
        }
    }

    [Fact]
    public async Task QueuedOfferCannotRewriteAnotherAccountAfterResetOrSettingsReplacement()
    {
        var old = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider);
        var settings = new SettingsService();
        var queued = new List<Action>();
        var sync = new SyncPush(() => Array.Empty<string>(), () => false, new FakeSync()) { PostMigration = queued.Add };
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreAccount.IsLoggedInProvider = () => true;
            settings.Current.UnifiedId = "first";
            settings.Current.OfflineMode = false;
            settings.Current.LastConfirmedServerXp = 0;
            settings.Current.PendingDescentMigrationChoice = null;
            settings.Current.DescentMigrationCompleted = false;
            sync.MarkLoaded(null);
            Assert.True(await sync.PushAsync("offer"));
            var first = settings.Current;
            sync.Reset();
            sync.MarkLoaded(null); // even the same account must not inherit a callback from a previous session
            queued[0]();
            Assert.Null(first.PendingDescentMigrationChoice);
            Assert.True(await sync.PushAsync("new session"));
            Assert.Equal(2, queued.Count);
            settings.RestoreFrom(new AppSettings { UnifiedId = "second", PlayerLevel = 7 });
            queued[1]();
            Assert.Equal(7, settings.Current.PlayerLevel);
            Assert.Null(first.PendingDescentMigrationChoice);
            sync.Reset();
            Assert.False(sync.HasLiveMigrationOffer(first));
            queued[0]();
            Assert.Null(first.PendingDescentMigrationChoice);
        }
        finally
        {
            sync.Reset();
            settings.SealForReset();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider) = old;
        }
    }

    private sealed class FakeSync : HttpMessageHandler
    {
        public string Response = """{"descent_migration":{"required":true,"total_xp_earned":16000,"devotion_days":30,"restore_basis_xp":70000}}""";
        public List<JObject> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Response) };
        }
    }
}

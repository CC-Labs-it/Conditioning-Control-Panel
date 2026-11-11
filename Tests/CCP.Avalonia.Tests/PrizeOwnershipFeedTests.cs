using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed partial class AccountSeedTests
{
    [Fact]
    public async Task Sync_and_provider_validate_share_ownership_and_logout_drops_every_grant() => await AvaloniaTestDispatcher.RunAsync(() => Fresh(async () =>
    {
        PrizeOwnership.ResetHeld();
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        wire.SyncReply = new JObject
        {
            ["success"] = true,
            ["prizes"] = new JObject { ["revision"] = 4, ["grants"] = new JArray(PrizeGrants.All) },
        }.ToString();
        try
        {
            Assert.True(await AccountSeed.LoadProfileAsync());
            Dispatcher.UIThread.RunJobs();
            Assert.All(PrizeGrants.All, grant => Assert.True(PrizeGrants.IsGranted(grant), grant));
            Assert.All(wire.Syncs, body => Assert.DoesNotContain("prizes", body.Properties().Select(p => p.Name)));
            ProviderSubscription.PrizesSink!("u1", "other", new PrizesBlock { Revision = 5, Grants = new() }, "Discord validate");
            Dispatcher.UIThread.RunJobs();
            Assert.True(PrizeGrants.IsGranted(PrizeGrants.FlashPendulum));
            ProviderSubscription.PrizesSink!("u1", "u1", null, "Patreon validate");
            Dispatcher.UIThread.RunJobs();
            Assert.True(PrizeGrants.IsGranted(PrizeGrants.BubbleRain));
            sync.UtcNow = () => T0.AddMinutes(1);
            wire.SyncReply = "{\"prizes\":{\"revision\":5,\"grants\":[]},\"user\":{\"xp\":{}}}";
            Assert.True(await sync.PushAsync("revoke"));
            Dispatcher.UIThread.RunJobs();
            Assert.All(PrizeGrants.All, grant => Assert.False(PrizeGrants.IsGranted(grant)));
            ProviderSubscription.PrizesSink!("u1", "u1", new PrizesBlock { Revision = 6, Grants = new() { PrizeGrants.RacingTrack(10) } }, "SubscribeStar validate");
            Dispatcher.UIThread.RunJobs();
            Assert.True(PrizeGrants.IsGranted(PrizeGrants.RacingTrack(10)));
            sync.UtcNow = () => T0.AddMinutes(2);
            wire.SyncReply = "{\"success\":true}";
            Assert.True(await sync.PushAsync("missing block"));
            Dispatcher.UIThread.RunJobs();
            Assert.True(PrizeGrants.IsGranted(PrizeGrants.RacingTrack(10)));
            await AccountSeed.Logout(accountDeleted: true);
            Dispatcher.UIThread.RunJobs();
            CoreAccount.UnifiedUserId = "u1"; // Reusing an id cannot resurrect the previous run's grants.
            Assert.False(PrizeGrants.IsGranted(PrizeGrants.RacingTrack(10)));
        }
        finally { PrizeOwnership.ResetHeld(); }
    }));
}

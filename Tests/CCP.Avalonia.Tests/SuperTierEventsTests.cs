using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Fact]
    public void ProviderAndIdentityChangesRefreshEveryEffect()
    {
        var restore = AccountSeed.CaptureSuperTierEvents();
        var oldId = CoreAccount.UnifiedUserId;
        var seen = new List<SuperEffect>();
        void Changed(SuperEffect effect) => seen.Add(effect);
        try
        {
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            SuperAccess.Changed += Changed;
            void Check(Action change)
            {
                seen.Clear();
                change();
                Assert.Equal(Enum.GetValues<SuperEffect>().OrderBy(x => x), seen.OrderBy(x => x));
            }
            Check(() => AccountSeed.Patreon!.NotifyEntitlementRaised(PatreonTier.Level1));
            Check(() => AccountSeed.SubscribeStar!.NotifyEntitlementRaised(PatreonTier.Level1));
            Check(() => CoreAccount.UnifiedUserId = Guid.NewGuid().ToString());
        }
        finally
        {
            SuperAccess.Changed -= Changed;
            CoreAccount.UnifiedUserId = oldId;
            restore();
        }
    }
    [Fact]
    public void SnapshotRestoresSubscriptionsAndLatchAcrossReseeds()
    {
        var restoreOriginal = AccountSeed.CaptureSuperTierEvents();
        var oldId = CoreAccount.UnifiedUserId;
        var seen = new List<SuperEffect>();
        void Changed(SuperEffect effect) => seen.Add(effect);
        try
        {
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            var first = AccountSeed.Patreon!;
            var snapshot = ProcessStateSnapshot.Take(false);
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            var second = AccountSeed.Patreon!;
            SuperAccess.Changed += Changed;
            first.NotifyEntitlementRaised(PatreonTier.Level1);
            Assert.Empty(seen);
            snapshot.Restore();
            seen.Clear();
            second.NotifyEntitlementRaised(PatreonTier.Level1);
            Assert.Empty(seen);
            first.NotifyEntitlementRaised(PatreonTier.Level1);
            Assert.Equal(Enum.GetValues<SuperEffect>().Length, seen.Count);
            seen.Clear();
            CoreAccount.UnifiedUserId = Guid.NewGuid().ToString();
            Assert.Equal(Enum.GetValues<SuperEffect>().Length, seen.Count);
            SuperAccess.HookTierEvents();
            seen.Clear();
            first.NotifyEntitlementRaised(PatreonTier.Level1);
            Assert.Equal(Enum.GetValues<SuperEffect>().Length, seen.Count);
            restoreOriginal();
            seen.Clear();
            first.NotifyEntitlementRaised(PatreonTier.Level1);
            Assert.Empty(seen);

            // Also restore an unhooked latch: the next seed must be able to subscribe.
            SuperAccess.TierEventsHooked = false;
            SuperAccess.TierEventsHook = null;
            var restoreUnhooked = AccountSeed.CaptureSuperTierEvents();
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            var third = AccountSeed.Patreon!;
            restoreUnhooked();
            Assert.False(SuperAccess.TierEventsHooked);
            Assert.Null(SuperAccess.TierEventsHook);
            seen.Clear();
            third.NotifyEntitlementRaised(PatreonTier.Level1);
            Assert.Empty(seen);
        }
        finally
        {
            SuperAccess.Changed -= Changed;
            CoreAccount.UnifiedUserId = oldId;
            restoreOriginal();
        }
    }

}

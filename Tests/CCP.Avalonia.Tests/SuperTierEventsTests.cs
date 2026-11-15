using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        var field = typeof(SuperAccess).GetField("_tierHooked", BindingFlags.NonPublic | BindingFlags.Static)!;
        var oldHooked = (bool)field.GetValue(null)!;
        var oldHook = SuperAccess.TierEventsHook;
        var oldId = CoreAccount.UnifiedUserId;
        var seen = new List<SuperEffect>();
        void Changed(SuperEffect effect) => seen.Add(effect);
        try
        {
            field.SetValue(null, false);
            Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
            SuperAccess.Changed += Changed;
            void Check(Action change)
            {
                seen.Clear();
                change();
                Assert.Equal(Enum.GetValues<SuperEffect>().OrderBy(x => x), seen.Distinct().OrderBy(x => x));
            }
            Check(() => AccountSeed.Patreon!.NotifyEntitlementRaised(PatreonTier.Level1));
            Check(() => AccountSeed.SubscribeStar!.NotifyEntitlementRaised(PatreonTier.Level1));
            Check(() => CoreAccount.UnifiedUserId = Guid.NewGuid().ToString());
        }
        finally
        {
            SuperAccess.Changed -= Changed;
            CoreAccount.UnifiedUserId = oldId;
            SuperAccess.TierEventsHook = oldHook;
            field.SetValue(null, oldHooked);
        }
    }
}

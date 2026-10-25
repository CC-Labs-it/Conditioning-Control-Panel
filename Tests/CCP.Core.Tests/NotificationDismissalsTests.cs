using System.Collections.Generic;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class NotificationDismissalsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidKeysNeverShowOrEnterSettings(string? key)
    {
        var dismissed = new List<string>();
        Assert.False(NotificationDismissals.CanShow(key, dismissed));
        Assert.False(NotificationDismissals.Remember(dismissed, key!));
        Assert.Empty(dismissed);
    }

    [Fact]
    public void UserDismissalIsRememberedOnceWithWpfCaseSensitiveKeys()
    {
        var dismissed = new List<string>();
        Assert.True(NotificationDismissals.CanShow("accepted_1", dismissed));
        Assert.True(NotificationDismissals.Remember(dismissed, "accepted_1"));
        Assert.False(NotificationDismissals.CanShow("accepted_1", dismissed));
        Assert.False(NotificationDismissals.Remember(dismissed, "accepted_1"));
        Assert.True(NotificationDismissals.CanShow("ACCEPTED_1", dismissed));
        Assert.Single(dismissed);
    }
}

using System;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The presence switch's one door (Core FriendsPresenceSetting) without a friends
/// service; the through-the-service half stays with FriendsServiceTests in the WPF suite.</summary>
public class FriendsPresenceSettingTests
{
    [Fact]
    public void Setting_row_without_a_service_writes_the_setting()
    {
        bool saved = false, asked = false;
        FriendsPresenceSetting.Write(true, null, v => saved = v, () => asked = true);
        Assert.True(saved);
        Assert.True(asked);
        Assert.True(FriendsPresenceSetting.Read(null, () => true));
        Assert.False(FriendsPresenceSetting.Read(null, () => throw new InvalidOperationException()));
    }

    [Fact]
    public void A_failing_save_still_spends_the_ask()
    {
        var asked = false;
        FriendsPresenceSetting.Write(false, null, _ => throw new InvalidOperationException(), () => asked = true);
        Assert.True(asked);
    }
}

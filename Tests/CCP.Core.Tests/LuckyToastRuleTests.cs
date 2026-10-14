using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>ccp-bugs #1312 (lucky toast hang in the Back Room). Moved from the WPF
/// RenderStallGuardTests with the rule; the handler source scan stays there.</summary>
public class LuckyToastRuleTests
{
    [Fact]
    public void LuckyToast_SkipsWhileAGameOwnsTheScreen()
    {
        Assert.Equal(LuckyToastRule.Decision.Skip,
            LuckyToastRule.Decide(notificationsSuppressed: false, gameOwnsScreen: true, toastVisible: false));
        Assert.Equal(LuckyToastRule.Decision.Skip,
            LuckyToastRule.Decide(notificationsSuppressed: false, gameOwnsScreen: true, toastVisible: true));
    }

    [Fact]
    public void LuckyToast_SkipsWhenPerkNoticesAreOff()
    {
        Assert.Equal(LuckyToastRule.Decision.Skip,
            LuckyToastRule.Decide(notificationsSuppressed: true, gameOwnsScreen: false, toastVisible: false));
    }

    [Fact]
    public void LuckyToast_ASecondProcRefreshesInsteadOfShowingAgain()
    {
        Assert.Equal(LuckyToastRule.Decision.Show,
            LuckyToastRule.Decide(notificationsSuppressed: false, gameOwnsScreen: false, toastVisible: false));
        Assert.Equal(LuckyToastRule.Decision.Refresh,
            LuckyToastRule.Decide(notificationsSuppressed: false, gameOwnsScreen: false, toastVisible: true));
    }
}

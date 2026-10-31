using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class NeedsDisplayNameRuleTests
{
    [Fact]
    public void FirstNameSkipsBlankCandidatesAndTrimsTheFirstName()
    {
        Assert.Equal("Echo", NeedsDisplayNameRule.FirstName("", "  ", null, " Echo ", "Later"));
        Assert.Null(NeedsDisplayNameRule.FirstName("", null, "   "));
    }
}

using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Companion;
using Xunit;
namespace CCP.Core.Tests;
public sealed class RainSharedContractTests
{
    [Theory]
    [InlineData(typeof(RainLookCodec))]
    [InlineData(typeof(RainItemManifest))]
    [InlineData(typeof(RainItemAccess))]
    [InlineData(typeof(RainLookClamp))]
    [InlineData(typeof(RainWardrobeCatalog))]
    public void RulesUseTheOriginalContractFromCore(Type rule)
    {
        Assert.Same(typeof(CoreSettings).Assembly, rule.Assembly);
        Assert.Equal("ConditioningControlPanel.Services.Companion", rule.Namespace);
        Assert.Null(rule.Assembly.GetType("ConditioningControlPanel.Services.Companion.Rain." + rule.Name));
    }
}

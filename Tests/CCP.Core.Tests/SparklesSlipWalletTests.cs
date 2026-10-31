using ConditioningControlPanel.Services.GameRewards;
using ConditioningControlPanel.Services.Sparkles;
using Xunit;
namespace CCP.Core.Tests;
public class SparklesSlipWalletTests
{
    [Theory]
    [InlineData(4400, 4400)]
    [InlineData(4000, 4310)]
    [InlineData(null, 4310)]
    public void ChipUsesHigherWallet(int? local, int shown)
    {
        var outcome = new SparklesClaimOutcome(new SparklesReceipt { Sp = 4310, Credited = 20 }, null);
        var msg = SparklesSlip.WithWallet(SparklesSlip.Build(outcome, false, key => key), local);
        Assert.Equal(shown, (int)msg!["sp"]!);
        Assert.Null(SparklesSlip.WithWallet(null, local));
    }
}

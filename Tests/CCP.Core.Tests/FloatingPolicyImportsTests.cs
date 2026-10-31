using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.Services.Video.Floating;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FloatingPolicyImportsTests
{
    [Fact]
    public void WpfPolicyRemainsUnambiguousWithCoreVideoImports()
    {
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, 0, 0, 3999));
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(false, 0, 0, 4000));
    }
}

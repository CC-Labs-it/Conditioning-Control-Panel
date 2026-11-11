using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SparklesHeadTests
{
    [Fact]
    public void SandboxNeedsLoopbackAndCreditsRecheckAccountAndOnlyRaise()
    {
        Assert.Null(SparklesHead.Create("sandbox", null));
        using var http = new System.Net.Http.HttpClient();
        Assert.NotNull(SparklesHead.Create("sandbox", "http://127.0.0.1:9", http));
        var s = CoreSettings.Current;
        s.OfflineMode = false;
        s.UnifiedId = "u_me";
        CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "test-token" : null;
        s.SkillPoints = 5000;
        SparklePointAward? award = null;
        void OnAward(object? sender, SparklePointAward a) => award = a;
        SparklePointRewards.Awarded += OnAward;
        try
        {
            SparklesHead.AdoptSp("u_other", 6000);
            SparklesHead.AdoptSp("u_me", 4310);
            Assert.Equal(5000, s.SkillPoints);
            Assert.Null(award);
            SparklesHead.AdoptSp("u_me", 5100);
            Assert.Equal(5100, s.SkillPoints);
            Assert.Equal(new SparklePointAward(100, 5100, SparklePointSource.Place), award);
        }
        finally { SparklePointRewards.Awarded -= OnAward; }
    }
}

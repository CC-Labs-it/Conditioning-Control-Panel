using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

public class DeeperPlayerPositionSettingsTests
{
    [Fact]
    public void PhysicalHintSurvivesSettingsRoundTripAndWpfWritesInvalidateIt()
    {
        var original = new AppSettings { DeeperPlayerWindowLeft = 1500, DeeperPlayerWindowTop = 100,
            DeeperPlayerWindowPixelLeft = 1500, DeeperPlayerWindowPixelTop = 100 };
        var restored = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(original))!;
        Assert.Equal(1500, restored.DeeperPlayerWindowPixelLeft);
        Assert.Equal(100, restored.DeeperPlayerWindowPixelTop);
        // WPF writes both settings on close, even if the window did not move.
        restored.DeeperPlayerWindowLeft = restored.DeeperPlayerWindowLeft;
        Assert.Null(restored.DeeperPlayerWindowPixelLeft);
        Assert.Null(restored.DeeperPlayerWindowPixelTop);
        restored.DeeperPlayerWindowPixelLeft = 1500;
        restored.DeeperPlayerWindowPixelTop = 100;
        restored.DeeperPlayerWindowTop = restored.DeeperPlayerWindowTop;
        Assert.Null(restored.DeeperPlayerWindowPixelLeft);
        Assert.Null(restored.DeeperPlayerWindowPixelTop);
    }
}

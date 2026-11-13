using System.Linq;
using ConditioningControlPanel.Avalonia.Tours;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NavSyncSixTests
{
    [Theory]
    [InlineData(TutorialType.GettingStarted, "gs_assets")]
    [InlineData(TutorialType.ShortWalk, "sw-assets")]
    public void ContentStepsPointAtTheLiveLibraryRow(TutorialType tour, string id)
    {
        var step = new TutorialService().StepsFor(tour).Single(s => s.Id == id);
        Assert.Equal("DoorLibrary", step.TargetElementName);
        Assert.Equal("settings", step.RequiresTab);
    }
}

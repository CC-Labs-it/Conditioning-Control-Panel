using System.IO;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmergencyExitGifTests
{
    [Fact]
    public void OnlyTickedGifsAreEligibleBeforeSampling()
    {
        var root = Path.Combine(Path.GetTempPath(), "exit-assets");
        var files = new[] { Path.Combine(root, "images", "a.gif"),
            Path.Combine(root, "images", "sub", "b.gif"), Path.Combine(root, "images", "c.gif") };
        var active = EmergencyExitWindow.ActiveGifs(files, root, new[] { "IMAGES\\A.GIF", "images/sub/b.gif" }).ToArray();
        Assert.Equal(new[] { files[2] }, active);
        Assert.Empty(EmergencyExitWindow.ActiveGifs(files.Take(1), root, new[] { "images/a.gif" }));
        Assert.Equal(files, EmergencyExitWindow.ActiveGifs(files, root, null));
    }
}

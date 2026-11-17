using System;
using System.IO;
using System.Windows.Automation.Peers;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Sparkles are one star picture (owner, 2026-11-17). The three wallet surfaces draw the shared
/// <see cref="SparkleStar"/>, never the old gold diamond Path, and the star resolves by pack URI.
/// </summary>
public class SparkleStarTests
{
    // The old Sparkles diamond, as all three surfaces drew it before the star.
    private const string OldDiamond = "M0,8 L6,0 12,8 6,16 Z";

    [Theory]
    [InlineData("Windows/Launcher/LauncherWindow.xaml")]
    [InlineData("Controls/SparkleWallet.xaml")]
    [InlineData("Controls/V2GetItRow.xaml")]
    public void WalletSurface_DrawsTheSharedStar_NotTheDiamond(string file)
    {
        var xaml = Read(file.Split('/'));
        Assert.Contains(":SparkleStar ", xaml);
        Assert.DoesNotContain(OldDiamond, xaml);
    }

    [Fact]
    public void StarArt_ShipsAsAPackResource()
    {
        var root = RepoRoot();
        var png = Path.Combine(root, "ConditioningControlPanel", "Resources", "features", "sparkle_star.png");
        Assert.True(File.Exists(png), "sparkle_star.png is missing");
        var head = new byte[4];
        using (var f = File.OpenRead(png)) Assert.Equal(4, f.Read(head, 0, 4));
        Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, head);

        var csproj = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "ConditioningControlPanel.csproj"));
        Assert.Contains("<Resource Include=\"Resources\\features\\*.png\" />", csproj);
        Assert.Contains("/Resources/features/sparkle_star.png", SparkleStar.PackUri);
    }

    [Fact]
    public void Star_ResolvesAndIsDecorative()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        WpfRenderHarness.OnStaThread(() =>
        {
            var star = new SparkleStar { Height = 12 };
            var bmp = Assert.IsAssignableFrom<BitmapSource>(star.Source);
            Assert.True(bmp.PixelWidth > 0 && bmp.PixelWidth == bmp.PixelHeight, "the star is a square picture");
            Assert.False(star.IsHitTestVisible);
            Assert.False(star.Focusable);
            Assert.Null(UIElementAutomationPeer.CreatePeerForElement(star));
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return dir!.FullName;
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", Path.Combine(parts)));
}

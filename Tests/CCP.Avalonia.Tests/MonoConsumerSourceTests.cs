using System.Runtime.CompilerServices;
using System.IO;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MonoConsumerSourceTests
{
    [Fact]
    public void WpfGuardedConsumersDoNotKeepPrivateMonoChains()
    {
        var root = new DirectoryInfo(RepoRoot());
        Assert.True(File.Exists(Path.Combine(root.FullName, "CCP.Avalonia/CCP.Avalonia.csproj")));
        var files = new[]
        {
            "Controls/DescentFuseRailChip.cs", "Controls/CircesMoodMeter.cs",
            "Controls/ChasterBookedFlash.cs", "Controls/ChasterRailChip.cs",
            "Views/Windows/MainShellWindow.DescentFuse.cs", "Views/Controls/Invites/InvitePanel.cs",
        };
        foreach (var file in files)
        {
            var source = File.ReadAllText(Path.Combine(root.FullName, "CCP.Avalonia", file));
            Assert.DoesNotContain("Cascadia", source);
            Assert.DoesNotContain("Consolas", source);
            Assert.DoesNotContain("FriendsDrawer.Mono", source);
            Assert.Contains("Font.Mono", source);
        }
        var startup = File.ReadAllText(Path.Combine(root.FullName, "CCP.Avalonia/App.axaml.cs"));
        Assert.Contains("Platform.FontGuard.Startup.Verify(Resources, OperatingSystem.IsWindows())", startup);
        var shell = File.ReadAllText(Path.Combine(root.FullName, "CCP.Avalonia/Views/Windows/MainShellWindow.axaml"));
        var player = File.ReadAllText(Path.Combine(root.FullName, "CCP.Avalonia/Views/Deeper/EnhancementPlayerWindow.axaml"));
        Assert.Matches("x:Name=\"FuseCornerDigits\"[\\s\\S]*?FontFamily=\"\\{DynamicResource Font.Mono\\}\"", shell);
        Assert.Matches("x:Name=\"TxtMiniHoverTime\"[\\s\\S]*?FontFamily=\"\\{DynamicResource Font.Mono\\}\"", player);
    }
    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

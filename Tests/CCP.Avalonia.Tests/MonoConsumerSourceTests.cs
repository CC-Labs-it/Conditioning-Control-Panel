using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            var source = Code(root, "CCP.Avalonia/" + file);
            Assert.DoesNotContain("Cascadia", source);
            Assert.DoesNotContain("Consolas", source);
            Assert.DoesNotContain("FriendsDrawer.Mono", source);
            Assert.Contains("Font.Mono", source);
        }
        var startup = Code(root, "CCP.Avalonia/App.axaml.cs");
        Assert.Contains("Platform.FontGuard.Startup.Verify(Resources, OperatingSystem.IsWindows())", startup);
        var shell = Code(root, "CCP.Avalonia/Views/Windows/MainShellWindow.axaml");
        var player = Code(root, "CCP.Avalonia/Views/Deeper/EnhancementPlayerWindow.axaml");
        Assert.Matches("x:Name=\"FuseCornerDigits\"[\\s\\S]*?FontFamily=\"\\{DynamicResource Font.Mono\\}\"", shell);
        Assert.Matches("x:Name=\"TxtMiniHoverTime\"[\\s\\S]*?FontFamily=\"\\{DynamicResource Font.Mono\\}\"", player);
    }
    [Fact]
    public void EmiFaceChainsGoThroughTheGuardLikeWpfEmiFace()
    {
        // WPF EmiFace.cs:172,191,232,251 passes FaceFont/PixelFont through FontGuard.Sanitize/Family.
        var root = new DirectoryInfo(RepoRoot());
        foreach (var file in new[] { "EmiBookWindow.axaml.cs", "EmiRingWindow.axaml.cs" })
        {
            var lines = Code(root, "CCP.Avalonia/Views/Windows/EmiDesk/" + file).Split('\n').Where(l => l.Contains("Consolas")).ToArray();
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.Contains("FontGuard.Startup.Family(", l));
        }
        Assert.DoesNotContain("Consolas", Code(root, "CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.axaml"));
        Assert.Contains("_faceView.FontFamily = EmiBookWindow.FaceFont;", Code(root, "CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.axaml.cs"));
    }

    // P23: comments are removed (string literals kept: they are what is matched), so a
    // commented-out line can neither satisfy nor break a check.
    private static string Code(DirectoryInfo root, string path) =>
        Regex.Replace(File.ReadAllText(Path.Combine(root.FullName, path)),
            "\"(?:\\\\.|[^\"\\\\\n])*\"|<!--[\\s\\S]*?-->|//[^\\n]*|/\\*[\\s\\S]*?\\*/",
            m => m.Value.StartsWith('"') ? m.Value : " ");

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

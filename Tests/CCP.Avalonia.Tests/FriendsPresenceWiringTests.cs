using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsPresenceWiringTests
{
    // Complements the runtime lifecycle test: its explicit binding cannot prove App startup binds it.
    // WPF Services/Session/SessionEngine.cs:396 clears friends activity on every session exit.
    [Fact]
    public void StartupSubscribesFriendsToTheProductionSessionRunner()
    {
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "App.axaml.cs"));
        code = Regex.Replace(code, @"/\*.*?\*/|//[^\r\n]*", "", RegexOptions.Singleline);
        var create = code.IndexOf("Sessions = new SessionRunner(", System.StringComparison.Ordinal);
        Assert.True(create >= 0, "Startup must construct the production session runner");
        var next = code.IndexOf("MediaHistory =", create, System.StringComparison.Ordinal);
        Assert.True(next > create, "Find the startup block after session construction");
        Assert.Matches(@"Platform\.FriendsHead\.WatchSessionStop\(\s*Sessions\s*\)\s*;", code[create..next]);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

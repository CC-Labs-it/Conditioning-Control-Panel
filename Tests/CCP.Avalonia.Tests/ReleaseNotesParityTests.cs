using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Avalonia head shows <see cref="ReleaseNotes.Current"/> where WPF shows
/// UpdateService.CurrentPatchNotes (welcome-back sheet, WPF App.xaml.cs:5035-5052). WPF's literal stays the
/// source on main, so a catch-up that bumps the notes must bring this copy along.
/// </summary>
public sealed class ReleaseNotesParityTests
{
    [Fact]
    public void CoreNotesMatchTheWpfPatchNotesWordForWord()
    {
        var wpf = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Update", "UpdateService.cs"));
        var match = Regex.Match(wpf, "public const string CurrentPatchNotes = @\"((?:[^\"]|\"\")*)\";");
        Assert.True(match.Success, "WPF UpdateService.CurrentPatchNotes literal not found");
        var expected = match.Groups[1].Value.Replace("\"\"", "\"").Replace("\r\n", "\n");
        Assert.Equal(expected, ReleaseNotes.Current.Replace("\r\n", "\n"));
    }

    [Fact]
    public void CoreNotesLeadWithTheSharedVersion()
    {
        var props = File.ReadAllText(Path.Combine(RepoRoot(), "Version.props"));
        var version = Regex.Match(props, "<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.StartsWith("v" + version + " ", ReleaseNotes.Current);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

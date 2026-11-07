using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Both heads show ReleaseNotes.Current as the new release (WPF UpdateService.CurrentPatchNotes aliases it).
/// Restores the version check c9f61d8643 dropped with the WPF word-for-word test: a Version.props bump with stale
/// notes must go red.</summary>
public sealed class ReleaseNotesVersionTests
{
    [Fact]
    public void CoreNotesLeadWithTheSharedVersion()
    {
        var props = XDocument.Load(Path.Combine(RepoRoot(), "Version.props"));
        var version = props.Descendants("Version").Single().Value;
        Assert.StartsWith("v" + version + " ", ReleaseNotes.Current);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}

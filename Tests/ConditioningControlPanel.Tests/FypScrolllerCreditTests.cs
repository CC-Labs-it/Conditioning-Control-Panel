using System;
using System.IO;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// For You credits Scrolller under every online clip (owner, 2026-10-17): the caption's folder line
/// reads "r/&lt;sub&gt; · via Scrolller" for online entries and stays the plain folder for library
/// files. The page code is plain JS with no node harness, so these pin the source.
/// </summary>
public class FypScrolllerCreditTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Page(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "web", "fyp", file));

    [Fact]
    public void Caption_reports_online_from_the_entry_origin()
    {
        Assert.Equal("online", RemoteMediaFormats.OriginOnline);
        Assert.Contains("online: t.cut.asset.origin === 'online'", Page("surfaces.js"));
    }

    [Fact]
    public void Online_caption_carries_the_credit_and_library_captions_do_not()
    {
        var main = Page("main.js");
        Assert.Contains("cap?.online", main);
        Assert.Contains("'via Scrolller'", main);
        Assert.Contains(": (cap?.folder ?? '')", main);
    }
}

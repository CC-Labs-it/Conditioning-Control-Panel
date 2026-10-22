using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The licence credit on the calm surfaces (2026-10-21): the Assets card shows a linked
/// "Media from Scrolller" and the "kept for a day" line on every online meter card, official or
/// not, and the wallpaper card (a wallpaper cannot carry a mark) carries the linked credit. The
/// day line exists in all ten languages, without mojibake, dashes or exclamation marks.
/// </summary>
public class ScrolllerCreditCardsTests
{
    private static readonly string[] Languages = { "de", "en", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    [Fact]
    public void The_assets_card_links_the_credit_and_says_how_long_copies_stay()
    {
        var xaml = File.ReadAllText(Path.Combine(App(), "Views", "Tabs", "AssetsTabView.xaml"));
        int at = xaml.IndexOf("x:Name=\"TxtScrolllerCredit\"", StringComparison.Ordinal);
        Assert.True(at > 0);
        var credit = xaml.Substring(at, xaml.IndexOf("</TextBlock>", at, StringComparison.Ordinal) - at);
        Assert.Contains("Click=\"ScrolllerLink_Click\"", credit);
        Assert.Contains(">Media from Scrolller</Hyperlink>", credit);
        int day = xaml.IndexOf("x:Name=\"TxtOnlineCacheDay\"", StringComparison.Ordinal);
        Assert.True(day > at, "the day line sits under the credit");
        Assert.Contains("{loc:Str online_cache_day_line}", xaml.Substring(day, 120));

        // The credit is never hidden once the licence is official (the stamped line is not it).
        var meter = File.ReadAllText(Path.Combine(App(), "MainWindow", "MainWindow.OnlineMeter.cs"));
        Assert.Contains("tab.TxtScrolllerCredit.Visibility = Visibility.Visible;", meter);
        Assert.DoesNotContain("TxtOnlineCacheDay.Visibility", meter);
    }

    [Fact]
    public void The_wallpaper_card_carries_the_linked_credit()
    {
        var xaml = File.ReadAllText(Path.Combine(App(), "Views", "Tabs", "BambiTakeoverTabView.xaml"));
        int at = xaml.IndexOf("x:Name=\"TxtWallpaperCredit\"", StringComparison.Ordinal);
        Assert.True(at > 0);
        var credit = xaml.Substring(at, xaml.IndexOf("</TextBlock>", at, StringComparison.Ordinal) - at);
        Assert.Contains("Click=\"ScrolllerLink_Click\"", credit);
        Assert.Contains(">Media from Scrolller</Hyperlink>", credit);
        var code = File.ReadAllText(Path.Combine(App(), "Views", "Tabs", "BambiTakeoverTabView.xaml.cs"));
        Assert.Contains("ScrolllerCredit.OpenHome()", code);

        // Only while online media may reach the wallpaper: hidden by default, shown by the same
        // rule the wallpaper service fills its online pool by, re-read on every show.
        Assert.Contains("Visibility=\"Collapsed\"", xaml.Substring(at, 140));
        Assert.Contains("WallpaperService.RemoteMediaEnabled(s.MediaSource, s.HasRemoteMediaConsent)", code);
        Assert.Contains("RefreshWallpaperCredit(); } };", code);
        Assert.Contains("tab.RefreshWallpaperCredit();", File.ReadAllText(Path.Combine(App(), "MainWindow", "MainWindow.Autonomy.cs")));
    }

    [Fact]
    public void The_day_line_is_in_all_ten_languages_cleanly()
    {
        foreach (var lang in Languages)
        {
            var path = Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json");
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, lang + " carries a BOM");
            var line = File.ReadAllLines(path).SingleOrDefault(l => l.TrimStart().StartsWith("\"online_cache_day_line\"", StringComparison.Ordinal));
            Assert.True(line != null, lang + " has no online_cache_day_line");
            Assert.DoesNotContain("Ã", line);          // UTF-8 read as Latin-1
            Assert.DoesNotContain("�", line);
            Assert.DoesNotContain("—", line);
            Assert.DoesNotContain("–", line);
            Assert.DoesNotContain("!", line);
        }
    }

    private static string App()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}

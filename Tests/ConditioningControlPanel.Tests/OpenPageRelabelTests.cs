using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Views.Controls.AppSettingsSections;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 review (wave 6, item 3, second desk run): the page that is OPEN during a language switch kept
/// its breadcrumb ("Einstellungen › Allgemein" under a Japanese Settings page) and the Startup Video
/// "(random)" line in the old language until the next navigation: both are written from code. The
/// strip now forgets what it built and MainWindow shows it again on LanguageChanged; the General page
/// rewrites its startup-video line.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class OpenPageRelabelTests
{
    private static SectionTabStrip Strip(string section, string tab)
    {
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off, Width = 1480 };
        strip.Show(section, tab);
        strip.Measure(new Size(1480, 200));
        strip.Arrange(new Rect(0, 0, 1480, strip.DesiredSize.Height));
        return strip;
    }

    private static string Section(string key) => Loc.Get(NavSections.Find(key)!.LabelKey);

    private static string PillText(SectionTabStrip strip)
        => string.Join("|", strip.PillKeys.Select(k => strip.PillFor(k))
            .SelectMany(p => RowsRender.Descendants(p!).OfType<TextBlock>()).Select(t => t.Text));

    [Fact]
    public void A_language_switch_relabels_the_open_pages_crumb_and_pills()
    {
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage("en", () =>
        {
            var section = NavSections.Order.First(s => s.Key != NavSections.Home && NavStripRules.ShowsHeader(s.Key)).Key;
            var tab = NavSections.DefaultTab(section)!;
            var strip = Strip(section, tab);
            string english = strip.CrumbText;
            string englishPills = PillText(strip);
            Assert.StartsWith(Section(section), english);

            RowsRender.InLanguage("de", () =>
            {
                // Nothing calls Show on a language switch, so the crumb stays as built (the desk bug);
                // and Show alone keeps the pills of the section it built them for.
                Assert.Equal(english, strip.CrumbText);
                strip.Show(section, tab);
                Assert.Equal(englishPills, PillText(strip));

                strip.Relabel();
                strip.Show(section, tab);
                Assert.StartsWith(Section(section), strip.CrumbText);
                Assert.NotEqual(english, strip.CrumbText);
                Assert.NotEqual(englishPills, PillText(strip));
                return 0;
            });
            return 0;
        }));
    }

    [Fact]
    public void The_startup_video_line_follows_the_language()
    {
        RowsRender.InLanguage("de", () =>
        {
            Assert.Equal(Loc.Get("label_random"), GeneralSettingsSection.StartupVideoText(null));
            Assert.Equal(Loc.Get("label_random"), GeneralSettingsSection.StartupVideoText(@"C:\no\such\clip.mp4"));
            return 0;
        });
        var file = Path.GetTempFileName();
        try { Assert.Equal(Path.GetFileName(file), GeneralSettingsSection.StartupVideoText(file)); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void MainWindow_relabels_the_open_page_on_a_language_switch()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var app = Path.Combine(dir!.FullName, "ConditioningControlPanel");
        var chrome = File.ReadAllText(Path.Combine(app, "MainWindow", "MainWindow.SectionChrome.cs"));
        Assert.Contains("LanguageChanged += (_, _) =>", chrome);
        Assert.Contains("Dispatcher.BeginInvoke(new Action(RelabelSectionChrome))", chrome);
        var general = File.ReadAllText(Path.Combine(app, "Views", "Controls", "AppSettings", "GeneralSettingsSection.xaml.cs"));
        Assert.Contains("Dispatcher.BeginInvoke(new Action(RelabelStartupVideo))", general);
    }
}

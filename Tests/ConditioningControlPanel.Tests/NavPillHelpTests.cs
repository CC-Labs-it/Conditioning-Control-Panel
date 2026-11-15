using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner, 2026-10-07: "add a ? button over each chip so people can hover before clicking to know
/// what it does", and "a prompt on Just Drop that asks if they want to open it, since it opens in
/// a new window". Every visible pill carries a "what is this" line in every language, the badge
/// never grows the pill, and a Window pill only opens after "Open it".
/// </summary>
[Collection(SectionTabStripCollection.Name)]
public class NavPillHelpTests
{
    private static string LanguagesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Localization")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Localization", "Languages");
    }

    private static IEnumerable<(string Section, NavTab Tab)> VisiblePills() =>
        NavSections.Order.Where(s => NavStripRules.ShowsPills(s.Key))
            .SelectMany(s => s.Tabs.Where(t => !t.Hidden).Select(t => (s.Key, t)));

    private static readonly string[] ConfirmKeys =
        { "nav_open_window_title", "nav_open_window_body", "nav_open_window_yes", "nav_open_window_no" };

    [Fact]
    public void EveryVisiblePillHasAHelpLineInEveryLanguage()
    {
        var files = Directory.GetFiles(LanguagesDir(), "*.json");
        Assert.True(files.Length >= 9);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Path.GetFileName(file) + " has a BOM");
            using var doc = JsonDocument.Parse(bytes);
            var keys = VisiblePills().Select(p => NavStripRules.HelpKey(p.Tab)).Concat(ConfirmKeys);
            foreach (var key in keys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var v), $"{Path.GetFileName(file)} lacks {key}");
                var text = v.GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(text), $"{Path.GetFileName(file)} {key} is empty");
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
                Assert.DoesNotContain("!", text);
            }
            Assert.Contains("{0}", doc.RootElement.GetProperty("nav_open_window_title").GetString());
        }
    }

    [Fact]
    public void TheEnglishHelpNeverGendersTheCompanion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(LanguagesDir(), "en.json")));
        foreach (var (_, tab) in VisiblePills())
        {
            var words = (doc.RootElement.GetProperty(NavStripRules.HelpKey(tab)).GetString() ?? "")
                .ToLowerInvariant().Split(' ', '.', ',', ':');
            Assert.DoesNotContain("she", words);
            Assert.DoesNotContain("her", words);
        }
    }

    [Fact]
    public void OnlyWindowPillsAskBeforeOpening()
    {
        var asking = VisiblePills().Where(p => NavStripRules.AsksBeforeOpening(p.Tab)).Select(p => p.Tab.Key).ToArray();
        Assert.Equal(new[] { "justdrop" }, asking);
    }

    private static SectionTabStrip Laid(string section, string tab, double width = 1480)
    {
        var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off, Width = width };
        strip.Show(section, tab);
        strip.Measure(new Size(width, 200));
        strip.Arrange(new Rect(0, 0, width, strip.DesiredSize.Height));
        strip.UpdateLayout();
        return strip;
    }

    // Declutter wave 1 (2026-11-15): the per-pill "?" badges became ONE help button at the right
    // end of the strip, showing the help line of the pill that is lit.
    [Fact]
    public void OneHelpButtonShowsTheLitPillsHelp()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(x => NavStripRules.ShowsPills(x.Key)))
            {
                var strip = Laid(s.Key, s.DefaultTab);
                var help = strip.HelpButtonForTests;
                foreach (var key in strip.PillKeys)
                {
                    // No pill carries a badge of its own: its host holds the pill and nothing else.
                    var pill = strip.PillFor(key)!;
                    var host = (System.Windows.Controls.Panel)pill.Parent;
                    Assert.Single(host.Children);
                    Assert.Equal(pill.ActualWidth, host.ActualWidth, 2);
                    Assert.False(help.IsDescendantOf(pill));
                }
                // The one button sits right of the last pill, inside the strip.
                Assert.Equal(Visibility.Visible, help.Visibility);
                var last = strip.PillFor(strip.PillKeys[^1])!;
                double lastRight = last.TranslatePoint(new Point(last.ActualWidth, 0), strip).X;
                double at = help.TranslatePoint(new Point(0, 0), strip).X;
                Assert.True(at >= lastRight, $"{s.Key}: the help button overlaps the pills");
                Assert.True(at + help.ActualWidth <= strip.ActualWidth + 0.5, $"{s.Key}: the help button is clipped");
                // At the default window's strip width (1469) the tray still ends before the button:
                // Companion's bar is the full one, and nothing of it may be squeezed.
                var tight = Laid(s.Key, s.DefaultTab, 1469);
                var tray = tight.TrayHostForTests;
                double trayRight = tray.TranslatePoint(new Point(tray.ActualWidth, 0), tight).X;
                double helpLeft = tight.HelpButtonForTests.TranslatePoint(new Point(0, 0), tight).X;
                Assert.True(trayRight <= helpLeft + 0.5, $"{s.Key}: the tray ({trayRight:0.#}) runs under the help button ({helpLeft:0.#})");
                Assert.True(helpLeft + tight.HelpButtonForTests.ActualWidth <= 1469.5, $"{s.Key}: the help button leaves the strip");

                // It follows the lit pill.
                foreach (var tab in s.Tabs.Where(t => !t.Hidden && t.Kind is NavTabKind.Tab or NavTabKind.Zone))
                {
                    strip.Show(s.Key, tab.Key);
                    if (strip.ActivePillKey != tab.Key) continue;
                    Assert.Equal(tab.Key, strip.HelpShownFor);
                    Assert.Equal(SectionTabStrip.HelpText(tab), strip.HelpLineShown);
                    Assert.NotNull(help.ToolTip);
                }
            }
            // Settings draws no pills, so there is nothing to explain: hidden, the row keeps its width.
            var settings = Laid(NavSections.Settings, NavSections.Find(NavSections.Settings)!.DefaultTab);
            Assert.Equal(Visibility.Hidden, settings.HelpButtonForTests.Visibility);
            Assert.Null(settings.HelpShownFor);
        });
    }

    [Fact]
    public void RestingPillsShowNoFaceUntilThePointerAsks()
    {
        Assert.True(NavStripRules.ShowsFace(active: true, hovered: false, pressed: false));
        Assert.True(NavStripRules.ShowsFace(active: false, hovered: true, pressed: false));
        Assert.True(NavStripRules.ShowsFace(active: false, hovered: false, pressed: true));
        Assert.False(NavStripRules.ShowsFace(active: false, hovered: false, pressed: false));
    }

    [Fact]
    public void TheCountBadgeSeamFeedsAPillAndStaysQuietWithoutAProvider()
    {
        var before = NavStripRules.BadgeProvider;
        try
        {
            NavStripRules.BadgeProvider = null;
            Assert.Null(NavStripRules.BadgeText("studio"));
            NavStripRules.BadgeProvider = k => k == "studio" ? "  5 on " : k == "presets" ? "   " : k == "library" ? "123456789012" : null;
            Assert.Equal("5 on", NavStripRules.BadgeText("studio"));
            Assert.Null(NavStripRules.BadgeText("presets"));
            Assert.Null(NavStripRules.BadgeText("justdrop"));
            Assert.Null(NavStripRules.BadgeText(null));
            Assert.Equal(NavStripRules.BadgeTextMax, NavStripRules.BadgeText("library")!.Length);

            WpfRenderHarness.OnStaThread(() =>
            {
                var strip = Laid(NavSections.Studio, "studio");
                Assert.Equal("5 on", strip.PillBadge("studio"));
                Assert.Null(strip.PillBadge("presets"));
                // The number moves: the strip asks again, and the pill keeps its 38 px.
                NavStripRules.BadgeProvider = k => k == "studio" ? "6 on" : null;
                strip.RefreshBadges();
                Assert.Equal("6 on", strip.PillBadge("studio"));
                Assert.Equal(NavStripRules.PillHeight, strip.PillFor("studio")!.ActualHeight, 1);
                // A count that moves on a worker thread never touches the pills there: the strip's
                // handler hops to its own dispatcher and repaints when that runs.
                NavStripRules.BadgeProvider = k => k == "studio" ? "7 on" : null;
                System.Threading.Tasks.Task.Run(strip.OnBadgesChanged).Wait();
                Assert.Equal("6 on", strip.PillBadge("studio"));
                var frame = new System.Windows.Threading.DispatcherFrame();
                strip.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                Assert.Equal("7 on", strip.PillBadge("studio"));
                NavStripRules.BadgeProvider = _ => throw new InvalidOperationException("page gone");
                strip.RefreshBadges();
                Assert.Null(strip.PillBadge("studio"));
            });
        }
        finally { NavStripRules.BadgeProvider = before; }
    }

    [Fact]
    public void TheSectionWordIsTheRoomTitle()
    {
        Assert.True(NavStripRules.CrumbTitleSize >= NavStripRules.CrumbPageSize + 4);
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var s in NavSections.Order.Where(x => NavStripRules.ShowsHeader(x.Key)))
            {
                var strip = Laid(s.Key, s.DefaultTab);
                var word = Assert.IsType<System.Windows.Media.SolidColorBrush>(strip.CrumbWordBrush);
                Assert.Equal(NavStripRules.Accent(s.Key), word.Color);
                Assert.Equal(NavStripRules.CrumbTitleSize, strip.CrumbTitleSizeForTests);
            }
        });
    }

    [Fact]
    public void JustDropOpensOnlyAfterOpenIt()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var strip = Laid(NavSections.Studio, "studio");
            var asked = new List<string>();
            strip.TabRequested += t => asked.Add(t.Key);

            strip.ChooseForTests("justdrop");
            Assert.Empty(asked);
            Assert.Equal("justdrop", strip.ConfirmingKey);
            strip.AnswerConfirmForTests(open: false);
            Assert.Empty(asked);
            Assert.Null(strip.ConfirmingKey);

            strip.ChooseForTests("justdrop");
            strip.AnswerConfirmForTests(open: true);
            Assert.Equal(new[] { "justdrop" }, asked);

            // A plain page still goes at once, and Just Drop never lights as the page on screen.
            strip.ChooseForTests("presets");
            Assert.Equal(new[] { "justdrop", "presets" }, asked);
            Assert.Equal("presets", strip.ActivePillKey);
        });
    }
}

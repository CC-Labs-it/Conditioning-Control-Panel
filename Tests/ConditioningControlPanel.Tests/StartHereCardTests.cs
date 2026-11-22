using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The first-run "Start here" card (2026-11-17): offered once after the first-run show, every
/// row lands on something that exists, every language file carries its text, Help can replay it,
/// and the card realizes offscreen.
/// </summary>
public class StartHereCardTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null);
        return dir!.FullName;
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static readonly string[] Keys =
    {
        "starthere_title", "starthere_intro", "starthere_row_packs", "starthere_row_first",
        "starthere_row_settings", "starthere_row_panic", "starthere_show", "starthere_open",
        "starthere_done", "help_starthere",
    };

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void Offered_once_and_only_after_the_age_check(bool shown, bool age, bool expected)
        => Assert.Equal(expected, StartHerePlan.ShouldOffer(shown, age));

    [Theory]
    [InlineData("Escape", "Esc")]
    [InlineData(null, "Esc")]
    [InlineData("F12", "F12")]
    public void Panic_row_names_the_bound_key(string? stored, string expected)
        => Assert.Equal(expected, StartHerePlan.PanicKeyName(stored));

    [Fact]
    public void Every_row_lands_somewhere_real()
    {
        var rows = StartHerePlan.Rows;
        Assert.Equal(4, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());

        foreach (var row in rows)
        {
            if (row.Link != null)
            {
                Assert.Equal(Services.DiscordLinks.PackCatalogue, row.Link);
                Assert.StartsWith("https://discord.com/", row.Link);
                continue;
            }
            Assert.NotNull(row.Tab);
            if (row.SettingsSection != null)
            {
                Assert.Equal("appsettings", row.Tab);
                Assert.Contains(NavSections.Find(NavSections.Settings)!.Tabs, t => t.Key == row.SettingsSection);
            }
            else
            {
                Assert.NotNull(NavSections.SectionForTab(row.Tab!));
            }
        }

        // The panic key really is in Settings > Devices, and Spiral Overlay really is a Studio module.
        Assert.Contains("BtnPanicKey_Click", Source("Views", "Controls", "AppSettings", "DevicesSettingsSection.xaml"));
        Assert.Contains("Add(\"spiral\"", Source("Views", "Tabs", "StudioTabView.xaml.cs"));
        Assert.Contains("_spiralOpacity = 10;", Source("Models", "AppSettings.cs"));
    }

    [Fact]
    public void Every_language_file_carries_the_text()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(f))!;
            foreach (var k in Keys)
            {
                Assert.True(map.ContainsKey(k), k + " missing in " + Path.GetFileName(f));
                Assert.DoesNotContain("!", map[k]);
                Assert.DoesNotContain("—", map[k]);
                Assert.DoesNotContain("–", map[k]);
            }
            Assert.Contains("{0}", map["starthere_row_panic"]);
        }
    }

    [Fact]
    public void Follows_the_first_run_show_through_the_passive_ladder_and_help_can_replay_it()
    {
        var code = Source("MainWindow", "MainWindow.StartHere.cs");
        Assert.Contains("PresentOrInbox(", code);
        Assert.DoesNotContain("ShowDialog(", code);

        var ctor = Source("MainWindow", "MainWindow.xaml.cs");
        Assert.Contains("onEnded: OfferStartHereIfNeeded", ctor);

        var xaml = Source("MainWindow", "MainWindow.xaml");
        Assert.Contains("Click=\"BtnStartHereReplay_Click\"", xaml);
        Assert.Contains("{loc:Str help_starthere}", xaml);
    }

    [Fact]
    public void Realizes_offscreen()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var card = new StartHereCard(null);
            var root = (FrameworkElement)card.Content;
            card.Content = null;
            var host = new System.Windows.Controls.Grid { Width = 540 };
            host.Children.Add(root);
            host.Measure(new Size(540, double.PositiveInfinity));
            host.Arrange(new Rect(host.DesiredSize));
            host.UpdateLayout();
            Assert.True(host.ActualHeight > 200, "card collapsed to " + host.ActualHeight);

            // Desk look: CCP_RENDER_OUT=<dir> writes the card as PNG.
            var outDir = Environment.GetEnvironmentVariable("CCP_RENDER_OUT");
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
                var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
                                                 96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(outDir, "starthere-card.png"));
                enc.Save(fs);
            }
        });
    }
}

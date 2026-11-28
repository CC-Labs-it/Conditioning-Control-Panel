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
/// The assets nudge (2026-11-25): three cards with a moving arrow on the Library source bar, the
/// Folders zone and the Start button. Offered once per profile, never in Lockdown or Strict Lock,
/// upgrades only with an empty library; still arrow under reduced motion; Help replays it; every
/// language file carries its words; each card realizes offscreen.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AssetsNudgeCardTests
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
        "help_assetsnudge", "assetsnudge_inbox_title", "assetsnudge_inbox_summary",
        "assetsnudge_source_title", "assetsnudge_source_text", "assetsnudge_add_title", "assetsnudge_add_text",
        "assetsnudge_enable_title", "assetsnudge_enable_text", "assetsnudge_next", "assetsnudge_skip", "assetsnudge_done",
    };

    [Theory]
    // shown, age, fresh, empty, lockdown, strict -> offered
    [InlineData(false, true, true, false, false, false, true)]    // fresh install, even on Both
    [InlineData(false, true, false, true, false, false, true)]    // upgrade, empty library
    [InlineData(false, true, false, false, false, false, false)]  // upgrade with assets: knows the way
    [InlineData(true, true, true, true, false, false, false)]     // once per profile
    [InlineData(false, false, true, true, false, false, false)]   // never before the age check
    [InlineData(false, true, true, true, true, false, false)]     // never in Lockdown
    [InlineData(false, true, true, true, false, true, false)]     // never in Strict Lock
    public void Offered_once_only_where_it_helps(bool shown, bool age, bool fresh, bool empty,
                                                 bool lockdown, bool strict, bool expected)
        => Assert.Equal(expected, AssetsNudgePlan.ShouldOffer(shown, age, fresh, empty, lockdown, strict));

    [Theory]
    [InlineData(Models.MotionLevel.Full, true, true)]
    [InlineData(Models.MotionLevel.Reduced, true, false)]
    [InlineData(Models.MotionLevel.Off, true, false)]
    [InlineData(Models.MotionLevel.Full, false, false)]
    public void Arrow_holds_still_under_reduced_motion(Models.MotionLevel level, bool system, bool moves)
        => Assert.Equal(moves, AssetsNudgePlan.ArrowMoves(level, system));

    [Fact]
    public void Three_cards_each_pointing_at_a_real_control()
    {
        var steps = AssetsNudgePlan.Steps;
        Assert.Equal(new[] { "source", "add", "enable" }, steps.Select(s => s.Id).ToArray());
        foreach (var s in steps) Assert.NotNull(NavSections.SectionForTab(s.Tab));

        // The controls the arrows point at exist where the resolver looks for them.
        var assets = Source("Views", "Tabs", "AssetsTabView.xaml");
        Assert.Contains("x:Name=\"RemoteSourceChips\" x:FieldModifier=\"internal\"", assets);
        Assert.Contains("x:Name=\"ZoneFolders\" x:FieldModifier=\"internal\"", assets);
        Assert.Contains("x:Name=\"BtnStart\"", Source("MainWindow", "MainWindow.xaml"));
        var nudge = Source("MainWindow", "MainWindow.AssetsNudge.cs");
        Assert.Contains("AssetsTab?.RemoteSourceChips", nudge);
        Assert.Contains("AssetsTab?.ZoneFolders", nudge);
        Assert.Contains("=> BtnStart", nudge);
        // The source bar offers the three sources the first card names.
        Assert.Contains("\"My own assets\"", Source("MainWindow", "MainWindow.Assets.cs"));
    }

    [Fact]
    public void Every_language_file_carries_the_text()
    {
        var dir = SourceRoots.LanguagesDirectory;
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
            Assert.Contains("Scrolller", map["assetsnudge_source_text"]);
        }
    }

    [Fact]
    public void Never_blocks_and_help_can_replay_it()
    {
        var code = Source("MainWindow", "MainWindow.AssetsNudge.cs");
        Assert.Contains("PresentOrInbox(", code);
        Assert.DoesNotContain("ShowDialog(", code);
        Assert.Contains("App.Lockdown?.IsActive == true", code);
        Assert.Contains("ShowActivated=\"False\"", Source("Windows", "AssetsNudgeCard.xaml"));

        Assert.Contains("OfferAssetsNudgeIfNeeded(freshInstall: true)", Source("MainWindow", "MainWindow.StartHere.cs"));
        Assert.Contains("OfferAssetsNudgeIfNeeded(freshInstall: false)", Source("MainWindow", "MainWindow.xaml.cs"));

        var xaml = Source("MainWindow", "MainWindow.xaml");
        Assert.Contains("Click=\"BtnAssetsNudgeReplay_Click\"", xaml);
        Assert.Contains("{loc:Str help_assetsnudge}", xaml);
    }

    [Fact]
    public void Each_card_realizes_offscreen()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            for (int i = 0; i < AssetsNudgePlan.Steps.Count; i++)
            {
                var card = new AssetsNudgeCard(AssetsNudgePlan.Steps, _ => null, arrowMoves: false);
                card.FillStep(i);
                card.ShowArrow(up: i < 2);
                var root = (FrameworkElement)card.Content;
                card.Content = null;
                var host = new System.Windows.Controls.Grid { Width = 400 };
                host.Children.Add(root);
                host.Measure(new Size(400, double.PositiveInfinity));
                host.Arrange(new Rect(host.DesiredSize));
                host.UpdateLayout();
                Assert.True(host.ActualHeight > 100, "card " + i + " collapsed to " + host.ActualHeight);

                // Desk look: CCP_RENDER_OUT=<dir> writes each card as PNG.
                var outDir = Environment.GetEnvironmentVariable("CCP_RENDER_OUT");
                if (!string.IsNullOrEmpty(outDir))
                {
                    Directory.CreateDirectory(outDir);
                    var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight),
                                                     96, 96, PixelFormats.Pbgra32);
                    bmp.Render(host);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = File.Create(Path.Combine(outDir, $"assets-nudge-{i + 1}-{AssetsNudgePlan.Steps[i].Id}.png"));
                    enc.Save(fs);
                }
            }
        });
    }
}

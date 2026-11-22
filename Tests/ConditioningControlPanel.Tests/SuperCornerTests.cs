using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.HelpLoops;
using ConditioningControlPanel.Features;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The V2 door on the dashboard (owner, 2026-11-15): a cut corner on every tile whose feature has a
/// V2, a "See V2" help card for each, and the tile titles stepping aside so no name is covered.
///
/// Set CCP_SUPER_TILE_DUMP to a folder to also write the tiles and four frames of every V2 loop as PNGs.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public sealed class SuperCornerTests
{
    private static readonly (string Help, SuperEffect Effect)[] Doors =
    {
        ("FlashImages", SuperEffect.FlickerDeck), ("Video", SuperEffect.LightsDown),
        ("Subliminals", SuperEffect.Afterglow), ("BouncingText", SuperEffect.Scrawl),
        ("SpiralOverlay", SuperEffect.Vortex), ("PinkFilter", SuperEffect.Creep),
        ("BrainDrain", SuperEffect.Undertow), ("BubblePop", SuperEffect.InnerBloom),
    };

    [Fact]
    public void EveryEffect_HasADoorAndAHelpLoop()
    {
        foreach (SuperEffect e in Enum.GetValues(typeof(SuperEffect)))
        {
            Assert.Contains(Doors, d => d.Effect == e);
            Assert.True(HelpLoopRegistry.Has(SuperDoor.HelpId(e)), "no V2 help loop for " + e);
        }
        foreach (var (help, effect) in Doors) Assert.Equal(effect, SuperDoor.ForHelp(help));
        Assert.Null(SuperDoor.ForHelp("LockCard"));
        Assert.Null(SuperDoor.ForHelp(null));
    }

    [Fact]
    public void TheDashboardWall_NamesTheSameEffectsAsTheDoor()
    {
        var xaml = File.ReadAllText(Path.Combine(AppDir(), "Views", "Tabs", "SettingsTabView.xaml"));
        foreach (var (help, effect) in Doors)
        {
            bool single = xaml.Contains($"HelpSectionId=\"{help}\" Super=\"{effect}\"");
            bool splitA = xaml.Contains($"HelpSectionIdA=\"{help}\"") && xaml.Contains($"SuperA=\"{effect}\"");
            bool splitB = xaml.Contains($"HelpSectionIdB=\"{help}\"") && xaml.Contains($"SuperB=\"{effect}\"");
            Assert.True(single || splitA || splitB, $"the {help} tile does not carry Super {effect}");
        }
    }

    [Fact]
    public void TheCorner_IsOnlyItsTriangle()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tl = new SuperCorner(SuperEffect.Scrawl, bottomRight: false);
            var br = new SuperCorner(SuperEffect.Creep, bottomRight: true);
            foreach (var c in new[] { tl, br })
            {
                c.Measure(new Size(SuperCorner.Leg, SuperCorner.Leg));
                c.Arrange(new Rect(0, 0, SuperCorner.Leg, SuperCorner.Leg));
            }
            double near = 4, far = SuperCorner.Leg - 4;
            Assert.NotNull(VisualTreeHelper.HitTest(tl, new Point(near, near)));
            Assert.Null(VisualTreeHelper.HitTest(tl, new Point(far, far)));
            Assert.NotNull(VisualTreeHelper.HitTest(br, new Point(far, far)));
            Assert.Null(VisualTreeHelper.HitTest(br, new Point(near, near)));
        });
    }

    [Fact]
    public void TilesWithACorner_KeepTheirNamesClear()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var split = new SplitFeatureCard
            {
                TitleA = "Mandatory Video", TitleB = "Brain Drain", SuperA = "LightsDown", SuperB = "Undertow",
                IsActiveA = true,
            };
            var single = new FeatureCard { Title = "Bouncing Text", Super = "Scrawl", DimWhenInactive = true, IsActive = true };
            var plain = new SplitFeatureCard { TitleA = "Mind Wipe", TitleB = "Bubble Count" };
            foreach (var card in new FrameworkElement[] { split, single, plain }) Dress(card);

            var host = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(20, 16, 34)) };
            foreach (var card in new FrameworkElement[] { split, single, plain })
            {
                card.Width = 190; card.Height = 190;
                host.Children.Add(card);
            }
            host.Measure(new Size(570, 190));
            host.Arrange(new Rect(0, 0, 570, 190));
            host.UpdateLayout();

            var pillA = (Border)split.FindName("TitlePillA");
            var pillB = (Border)split.FindName("TitlePillB");
            // The pills step in past the corner's short side, and the name still fits unclipped.
            Assert.True(pillA.Margin.Left >= SuperCorner.Leg - 4);
            Assert.True(pillB.Margin.Right >= SuperCorner.Leg - 4);
            var textA = (TextBlock)split.FindName("TxtTitleA");
            textA.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(textA.MaxWidth >= textA.DesiredSize.Width - 0.5 || textA.MaxWidth >= 100,
                $"'Mandatory Video' gets only {textA.MaxWidth:0} px");
            // A tile with no V2 is left as it was.
            Assert.Equal(8, ((Border)plain.FindName("TitlePillA")).Margin.Left);
            Assert.Equal(1, Descendants(single).OfType<SuperCorner>().Count());
            Assert.Equal(2, Descendants(split).OfType<SuperCorner>().Count());
            Assert.Empty(Descendants(plain).OfType<SuperCorner>());

            Dump(host, 570, 190, "tiles");
        });
    }

    [Fact]
    public void V2Loops_DumpFrames()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_SUPER_TILE_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (SuperEffect e in Enum.GetValues(typeof(SuperEffect)))
            {
                Assert.True(HelpLoopRegistry.TryGet(SuperDoor.HelpId(e), out var scene));
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                for (int i = 0; i < 4; i++)
                {
                    Assert.True(HelpLoopRegistry.TryGet(SuperDoor.HelpId(e), out var s));
                    var view = new HelpLoopView(s) { Width = 480, Height = 270 };
                    row.Children.Add(view);
                    row.Measure(new Size(1920, 270));
                    row.Arrange(new Rect(0, 0, 1920, 270));
                    double a = scene.Steps[Math.Min(i, 2)].StartMs, b = scene.Steps[Math.Min(i, 2)].EndMs;
                    view.RenderAt(i < 3 ? a + (b - a) * 0.6 : b - 150);
                }
                row.Measure(new Size(1920, 270));
                row.Arrange(new Rect(0, 0, 1920, 270));
                Dump(row, 1920, 270, "loop-" + e);
            }
        }, timeoutSeconds: 120);
    }

    private static void Dress(FrameworkElement card)
    {
        card.Resources["ElevatedSurfaceBrush"] = new SolidColorBrush(Color.FromRgb(40, 34, 64));
        card.Resources["GlassBorderBrush"] = new SolidColorBrush(Color.FromRgb(60, 50, 90));
        card.Resources["FxGlowBrush"] = new SolidColorBrush(Color.FromRgb(255, 105, 180));
        card.Resources["FxGlowColor"] = Color.FromRgb(255, 105, 180);
        card.Resources["PinkBrush"] = new SolidColorBrush(Color.FromRgb(255, 105, 180));
        card.Resources["TransparentPinkBrush"] = new SolidColorBrush(Color.FromArgb(40, 255, 105, 180));
        card.Resources["HelpButtonStyle"] = new Style(typeof(Button));
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static void Dump(Visual visual, int w, int h, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_SUPER_TILE_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var rtb = new RenderTargetBitmap(w * 2, h * 2, 192, 192, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(fs);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}

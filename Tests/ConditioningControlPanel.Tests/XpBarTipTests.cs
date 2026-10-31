using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The owner, 2026-10-31: "fix this obnoxious white dot at the end of the progress lv exp bar".
/// Two white lights rode the tip of the header fill: the 8 px bead (XPTubeBead) and the 10 px
/// pulsing meniscus (XPMeniscus). Both are gone, with the meniscus's slide and pulse clocks. Polish
/// wave 11 had aligned them on the tip; this replaces those tests.
///
/// <para>The rendered test realises the REAL XPBarTrack block out of MainWindow.xaml (loose parse,
/// handlers stripped, the HeaderLevelChipRenderTests recipe), renders at 4x and reads the tube's
/// centre row: nothing past the fill's edge may differ from the empty groove, and the end of the
/// fill may be no brighter than its body. The fill's edge moves by the same layout whether it is at
/// rest or mid-tween, so this holds through an XP gain.</para>
/// </summary>
public class XpBarTipTests
{
    private const double TrackWidth = 600;
    private const double Scale = 4;
    private const double Gutter = 20;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }

    private static string Xaml() =>
        File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "MainWindow", "MainWindow.xaml"))
            .Replace("\r\n", "\n");

    private static string TrackBlock(string xaml)
    {
        var start = xaml.IndexOf("<Border x:Name=\"XPBarTrack\"", StringComparison.Ordinal);
        Assert.True(start > 0, "XPBarTrack is gone");
        var txt = xaml.IndexOf("<TextBlock x:Name=\"TxtXP\"", start, StringComparison.Ordinal);
        var stop = xaml.LastIndexOf("</Border>", txt, StringComparison.Ordinal);
        Assert.True(stop > start, "lost the end of XPBarTrack");
        return xaml.Substring(start, stop + "</Border>".Length - start);
    }

    private static Border BuildTrack()
    {
        var xaml = Xaml();
        var head = Regex.Match(xaml, @"<Window\b(.*?)>", RegexOptions.Singleline).Groups[1].Value;
        var ns = string.Join(" ", Regex.Matches(head, @"xmlns(:\w+)?=""[^""]*""").Select(m => m.Value));
        ns = Regex.Replace(ns, @"clr-namespace:(ConditioningControlPanel[\w.]*)""", "clr-namespace:$1;assembly=ConditioningControlPanel\"");
        var block = TrackBlock(xaml);
        block = Regex.Replace(block, @"\s(ToolTipOpening|SizeChanged|Loaded)=""[A-Za-z_]\w*""", string.Empty);
        block = Regex.Replace(block, @"\sGrid\.Column=""\d+""", string.Empty);
        block = "<Border " + ns + block.Substring("<Border".Length);
        var track = (Border)XamlReader.Parse(block);
        track.HorizontalAlignment = HorizontalAlignment.Left;
        track.Margin = new Thickness(Gutter, 0, 0, 0);
        track.VerticalAlignment = VerticalAlignment.Center;
        return track;
    }

    /// <summary>The track's centre row (BGRA) with the fill at <paramref name="fraction"/>.</summary>
    private static byte[] Row(Border track, double fraction)
    {
        ((Border)track.FindName("XPBar")).Width = fraction * TrackWidth;

        var host = new Grid { Width = TrackWidth + 2 * Gutter, Height = 20, Background = new SolidColorBrush(Color.FromRgb(0x12, 0x0B, 0x1A)) };
        host.Children.Add(track);
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        host.UpdateLayout();

        int pw = (int)(host.Width * Scale), ph = (int)(host.Height * Scale);
        var rtb = new RenderTargetBitmap(pw, ph, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        rtb.Render(host);
        host.Children.Clear();

        var row = new byte[pw * 4];
        rtb.CopyPixels(new Int32Rect(0, ph / 2, pw, 1), row, pw * 4, 0);
        return row;
    }

    private static double Lum(byte[] row, int x) => 0.114 * row[x * 4] + 0.587 * row[x * 4 + 1] + 0.299 * row[x * 4 + 2];

    private static double Diff(byte[] a, byte[] b, int x) =>
        Math.Abs(a[x * 4] - b[x * 4]) + Math.Abs(a[x * 4 + 1] - b[x * 4 + 1]) + Math.Abs(a[x * 4 + 2] - b[x * 4 + 2]);

    [Fact]
    public void NothingLightsTheTipOfTheFill()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var f in new[] { 0.05, 0.5, 0.83 })
            {
                var track = BuildTrack();
                var empty = Row(track, 0);
                var filled = Row(track, f);
                int tip = (int)((Gutter + f * TrackWidth) * Scale);

                // the fill really painted: the body differs from the groove
                int body = tip - (int)(12 * Scale);
                Assert.True(Diff(filled, empty, body) > 24, $"no fill at {f:P0}");

                // past the fill's edge (1 px of antialiasing allowed), the groove is untouched
                for (int x = tip + (int)Scale; x < filled.Length / 4; x++)
                    Assert.True(Diff(filled, empty, x) <= 6, $"at {f:P0} something lights {(x / Scale) - Gutter:F2} px, past the tip");

                // the end of the fill is no brighter than its body
                double bodyLum = Lum(filled, body);
                for (int x = body; x < tip; x++)
                    Assert.True(Lum(filled, x) <= bodyLum + 2, $"at {f:P0} a hot spot at {(x / Scale) - Gutter:F2} px");
            }
        });
    }

    [Fact]
    public void TheTrackCarriesNoRoundLight()
    {
        var block = TrackBlock(Xaml());
        Assert.DoesNotContain("<Ellipse", block);
        Assert.DoesNotContain("RadialGradientBrush", block);
        Assert.DoesNotContain("DepthTubeBead", block);
        Assert.DoesNotContain("Meniscus", block);
        // what stays: the fill, its gloss, the sheen sweep, the level flash
        foreach (var kept in new[] { "x:Name=\"XPBar\"", "x:Name=\"XPTubeGloss\"", "x:Name=\"XPBarSheen\"", "x:Name=\"XPBarFlashOverlay\"" })
            Assert.Contains(kept, block);
    }
}

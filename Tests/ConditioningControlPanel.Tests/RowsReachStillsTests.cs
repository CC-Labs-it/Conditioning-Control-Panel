using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readable Rows reach (7.4.1 round, lane L3, 2026-11-25): stills of the door lists that took the
/// row label look (the Studio rack, the Settings mini-rail), rendered off-screen at the panel's
/// real content size. Does nothing without env <c>CCP_REACH_DUMP=&lt;dir&gt;</c>; with it, writes
/// <c>&lt;dir&gt;/&lt;name&gt;.png</c> (whole tab at 96 dpi) and <c>&lt;name&gt;-rail.png</c> (the
/// door list alone at 144 dpi). Env <c>CCP_ROWS_LANG</c> renders in another language. Silent: no
/// page here builds a media player (<see cref="RowsRender.AssertSilent"/>).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class RowsReachStillsTests
{
    /// <summary>MainWindow's fixed canvas (1489 x 901) less the content column's margins.</summary>
    internal const double TabWidth = 1469, TabHeight = 891;

    internal static string? DumpDir => Environment.GetEnvironmentVariable("CCP_REACH_DUMP") is { Length: > 0 } d ? d : null;

    [Theory]
    [InlineData("studio")]
    [InlineData("appsettings")]
    public void Door_list_still_when_asked(string tab)
    {
        if (DumpDir is not { } dir) return;
        string? lang = Environment.GetEnvironmentVariable("CCP_ROWS_LANG");
        string name = tab + (string.IsNullOrWhiteSpace(lang) ? "" : "-" + lang);
        WpfRenderHarness.OnStaThread(() => RowsRender.InLanguage(lang, () =>
        {
            FrameworkElement page = tab == "studio" ? new StudioTabView() : new AppSettingsTabView();
            var host = new Grid { Width = TabWidth, Height = TabHeight, Background = new SolidColorBrush(RowsRender.PageBack) };
            host.Children.Add(page);
            host.Measure(new Size(TabWidth, TabHeight));
            host.Arrange(new Rect(0, 0, TabWidth, TabHeight));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            host.UpdateLayout();
            Assert.NotNull(RowsRender.Dump(host, name, 96, dir));
            var rail = (FrameworkElement)page.FindName(tab == "studio" ? "RackList" : "SectionPillGeneral")!;
            // The rail's card: the Border two (Studio: Border > Grid > ScrollViewer > StackPanel)
            // or three steps up from the named element.
            var card = CardOf(rail);
            Region(host, card, name + "-rail", 144, dir);
            return 0;
        }));
        RowsRender.AssertSilent(tab);
    }

    /// <summary>
    /// ccp-bugs #1742 stills: the same real pages with the sticker edge white (shipped), dark and
    /// dark inside a light rim, on the panel's dark page colour, at 100% and 150%. Writes
    /// <c>edge-&lt;page&gt;-&lt;edge&gt;-&lt;dpi&gt;.png</c> (whole page) and
    /// <c>edge-compare-&lt;page&gt;.png</c> (the top of the page, the three edges side by side at 150%).
    /// </summary>
    [Theory]
    [InlineData("flash")]
    [InlineData("bubbles")]
    [InlineData("settings-audio")]
    public void Edge_stills_when_asked(string page)
    {
        if (DumpDir is not { } dir) return;
        var edges = new[] { ConditioningControlPanel.Emblems.EmblemEdge.White, ConditioningControlPanel.Emblems.EmblemEdge.Dark, ConditioningControlPanel.Emblems.EmblemEdge.DarkRim };
        WpfRenderHarness.OnStaThread(() =>
        {
            var crops = new System.Collections.Generic.List<BitmapSource>();
            var zooms = new System.Collections.Generic.List<BitmapSource>();
            foreach (var edge in edges)
            {
                var el = page == "flash" ? new ConditioningControlPanel.Features.FlashFeatureControl() : RowsPageStillsTests.Make(page);
                ConditioningControlPanel.Controls.Rows.RowHue.SetEdge(el, edge);
                var host = RowsRender.Host(el);
                foreach (var dpi in new[] { 96.0, 144.0 })
                    RowsRender.Dump(host, $"edge-{page}-{edge}-{dpi:0}", dpi, dir);
                crops.Add(Crop(host, new Rect(0, 0, Math.Min(560, host.ActualWidth), Math.Min(760, host.ActualHeight)), 144));
                // The emblem column close up (300%), where the edge is actually judged.
                zooms.Add(Crop(host, new Rect(0, 100, Math.Min(230, host.ActualWidth), Math.Min(420, host.ActualHeight - 100)), 288));
            }
            SideBySide(crops, Path.Combine(dir, $"edge-compare-{page}.png"));
            SideBySide(zooms, Path.Combine(dir, $"edge-zoom-{page}.png"));
        });
        RowsRender.AssertSilent(page);
    }

    private static BitmapSource Crop(FrameworkElement host, Rect box, double dpi)
    {
        double k = dpi / 96;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * k), (int)Math.Ceiling(host.ActualHeight * k), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(host);
        var px = new Int32Rect((int)(box.X * k), (int)(box.Y * k), (int)Math.Min(box.Width * k, bmp.PixelWidth), (int)Math.Min(box.Height * k, bmp.PixelHeight));
        var crop = new CroppedBitmap(bmp, px);
        crop.Freeze();
        return crop;
    }

    /// <summary>Lays the crops left to right with a 12 px gutter in the page colour.</summary>
    private static void SideBySide(System.Collections.Generic.IReadOnlyList<BitmapSource> crops, string path)
    {
        const int gap = 12;
        int w = 0, h = 0;
        foreach (var c in crops) { w += c.PixelWidth + gap; h = Math.Max(h, c.PixelHeight); }
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(RowsRender.PageBack), null, new Rect(0, 0, w, h));
            double x = 0;
            foreach (var c in crops)
            {
                dc.DrawImage(c, new Rect(x, 0, c.PixelWidth, c.PixelHeight));
                x += c.PixelWidth + gap;
            }
        }
        var rt = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rt.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rt));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>The nearest ancestor Border with rounded corners (the rail's card).</summary>
    internal static FrameworkElement CardOf(FrameworkElement el)
    {
        for (DependencyObject? n = el; n != null; n = VisualTreeHelper.GetParent(n) ?? LogicalTreeHelper.GetParent(n))
            if (n is Border b && b.CornerRadius.TopLeft >= 10) return b;
        return el;
    }

    /// <summary>Saves the part of <paramref name="host"/> under <paramref name="part"/> at <paramref name="dpi"/>.</summary>
    internal static string Region(FrameworkElement host, FrameworkElement part, string name, double dpi, string dir)
    {
        Directory.CreateDirectory(dir);
        double k = dpi / 96;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth * k), (int)Math.Ceiling(host.ActualHeight * k), dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(host);
        var box = part.TransformToAncestor(host).TransformBounds(new Rect(part.RenderSize));
        box.Inflate(6, 6);
        box.Intersect(new Rect(0, 0, host.ActualWidth, host.ActualHeight));
        var px = new Int32Rect((int)(box.X * k), (int)(box.Y * k), (int)(box.Width * k), (int)(box.Height * k));
        px.Width = Math.Min(px.Width, bmp.PixelWidth - px.X);
        px.Height = Math.Min(px.Height, bmp.PixelHeight - px.Y);
        var crop = new CroppedBitmap(bmp, px);
        string path = Path.Combine(dir, name + ".png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(crop));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }
}

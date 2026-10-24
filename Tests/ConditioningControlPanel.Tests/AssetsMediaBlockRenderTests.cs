using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Nav polish wave 3, PICKER lane: Library › Assets "Where your media comes from" is calm.
/// Closed it is three things (a segmented source bar, six flavour cards, one Fine-tune row)
/// and fits under 200 px; open, the catalog niches and your own communities are list rows
/// with switches in two clean columns. The cards, rows and switches are the REAL ones
/// (MainWindow's static builders). Set <c>CCP_NAV_PNG_DIR</c> to also write offscreen renders
/// of the block in "Both" with Pink chosen (closed) and with two niches + one own community (open).
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class AssetsMediaBlockRenderTests
{
    private static Grid Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)) };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        return host;
    }

    private static void MaybeRender(FrameworkElement el, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(el.ActualWidth);
        var h = (int)Math.Ceiling(el.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h));
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    /// <summary>Builds the block the way MainWindow does, in Both, with this selection.</summary>
    private static (AssetsTabView page, System.Collections.Generic.List<Button> tiles,
        System.Collections.Generic.List<ToggleButton> rows) Build(string[] niches, string[] customs)
    {
        var page = new AssetsTabView();
        Assert.Null(page.FindName("RemoteMediaScroll"));        // no scroll cap
        Assert.Null(page.FindName("RemoteNicheSubs"));          // no disclosure lines per niche

        var segStyle = page.RemoteSourceChips.TryFindResource("RemoteSourceSegment") as Style;
        Assert.NotNull(segStyle);
        foreach (var (label, picked) in new[] { ("My own assets", false), ("Scrolller", false), ("Both", true) })
            page.RemoteSourceChips.Children.Add(new ToggleButton { Style = segStyle, Content = label, IsChecked = picked });
        page.RemoteRatioRow.Visibility = Visibility.Visible;
        page.RemoteMediaDetails.Visibility = Visibility.Visible;

        var tileStyle = page.RemoteFlavourTiles.TryFindResource("RemoteFlavourTile") as Style;
        Assert.NotNull(tileStyle);
        var tiles = FlavourPresets.All.Append(FlavourPresets.Mine)
            .Select(f => MainWindow.CreateRemoteFlavourTile(f, tileStyle)).ToList();
        tiles.ForEach(t => page.RemoteFlavourTiles.Children.Add(t));

        var rowStyle = page.RemoteNicheChips.TryFindResource("RemoteNichePill") as Style;
        Assert.NotNull(rowStyle);
        var rows = FypOnlineCoordinator.Catalog.Select(n =>
        {
            var chip = MainWindow.CreateRemoteNicheChip(n, rowStyle);
            chip.IsChecked = niches.Contains(n.Id);
            page.RemoteNicheChips.Children.Add(chip);
            return chip;
        }).ToList();

        foreach (var c in customs)
            page.RemoteCustomSubChips.Children.Add(MainWindow.BuildRemoteSubRow(page, "r/" + c,
                "Verified · 412 clips on Reddit", on: true, verified: true, onToggle: () => { }, onRemove: () => { }));
        page.RemoteCustomSubChips.Children.Add(MainWindow.BuildRemoteSubRow(page, "r/GoonCaves",
            null, on: false, verified: false, onToggle: () => { }, onRemove: () => { }));

        MainWindow.PaintRemoteFlavourTiles(tiles, niches, customs);
        page.TxtRemoteSummary.Text = MainWindow.RemoteSummaryText(niches, customs);
        return (page, tiles, rows);
    }

    [Fact]
    public void ClosedTheBlockIsSourceFlavourAndOneLine() => WpfRenderHarness.OnStaThread(() =>
    {
        var pink = FlavourPresets.All.Single(f => f.Id == "pink");
        var sel = FlavourPresets.Resolve(pink, FypOnlineCoordinator.Catalog);
        var (page, tiles, _) = Build(sel.NicheIds.ToArray(), sel.CustomSubs.ToArray());
        Assert.Equal(Visibility.Collapsed, page.RemoteFineTuneScroll.Visibility);   // closed by default

        Realize(page, 1469, 891);
        MaybeRender(page.RemoteMediaBlock, "picker-closed.png");

        // Pink is lit: tint border 1.5 px and its check showing; every other card is neutral.
        var lit = tiles.Single(t => (string)t.Tag == "pink");
        Assert.Equal(1.5, lit.BorderThickness.Left);
        Assert.All(tiles.Where(t => t != lit), t => Assert.Equal(1, t.BorderThickness.Left));
        Assert.Contains(((Grid)lit.Content).Children.OfType<TextBlock>(),
            t => (string)t.Tag == "check" && t.Visibility == Visibility.Visible);

        // Six cards, one row, equal width, 72 px tall.
        Assert.Equal(6, page.RemoteFlavourTiles.Children.Count);
        Assert.All(tiles, t => Assert.Equal(72, t.ActualHeight));
        Assert.Single(tiles.Select(t => Math.Round(t.ActualWidth)).Distinct());

        Assert.StartsWith("Fine-tune", page.TxtRemoteSummary.Text);
        Assert.True(page.RemoteMediaBlock.ActualHeight < 200,
            $"closed media block is {page.RemoteMediaBlock.ActualHeight:0} px, owner wants one glance");
    });

    [Fact]
    public void OpenTheRowsSitInTwoCleanColumns() => WpfRenderHarness.OnStaThread(() =>
    {
        var on = new[] { "hypno", "bimbo" };
        var customs = new[] { "Bimbos" };
        var (page, tiles, rows) = Build(on, customs);
        page.RemoteFineTuneScroll.Visibility = Visibility.Visible;
        page.TxtRemoteSubError.Text = "r/Bimbos is already in your pool.";
        page.TxtRemoteSubError.Visibility = Visibility.Visible;

        Realize(page, 1469, 1400);
        MaybeRender(page.RemoteMediaBlock, "picker-open.png");

        // 7 + 3 subs from Hypno and Bimbo plus r/Bimbos = 8 distinct.
        Assert.Contains("2 niches", page.TxtRemoteSummary.Text);
        Assert.Contains("8 communities", page.TxtRemoteSummary.Text);

        // Mine is lit (hypno + bimbo + r/Bimbos is nobody's preset).
        var mine = tiles.Single(t => (string)t.Tag == FlavourPresets.MineId);
        Assert.Equal(1.5, mine.BorderThickness.Left);

        // Rows: two x positions only, every row the same width, none wrapped past 60 px.
        var xs = rows.Select(r => Math.Round(r.TranslatePoint(new Point(0, 0), page.RemoteNicheChips).X)).Distinct().ToList();
        Assert.Equal(2, xs.Count);
        Assert.Single(rows.Select(r => Math.Round(r.ActualWidth)).Distinct());
        Assert.All(rows, r => Assert.InRange(r.ActualHeight, 44, 60));

        // A switch is on in the niche's tint when its row is on, grey when off.
        var hypno = rows.Single(r => (string)r.Tag == "hypno");
        var track = (Border)((Grid)hypno.Content).Children[0];
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), ((SolidColorBrush)track.Background).Color);   // on = Pink, never a pale tint
        var offRow = rows.First(r => r.IsChecked != true);
        var offTrack = (Border)((Grid)offRow.Content).Children[0];
        Assert.True(((SolidColorBrush)offTrack.Background).Color.A < 0x40);
    });

    /// <summary>Online media today (2026-10-19): the meter card is two short lines (three once the
    /// licence is official), so the closed block stays a glance and the asset browser keeps its
    /// floor on the real ~650 px page. The longest English copy (Free plan) is used.</summary>
    [Fact]
    public void TheOnlineMeterCardIsShortAndTheBrowserKeepsItsFloor() => WpfRenderHarness.OnStaThread(() =>
    {
        var pink = FlavourPresets.All.Single(f => f.Id == "pink");
        var sel = FlavourPresets.Resolve(pink, FypOnlineCoordinator.Catalog);
        var (page, _, _) = Build(sel.NicheIds.ToArray(), sel.CustomSubs.ToArray());
        page.OnlineMeterCard.Visibility = Visibility.Visible;
        page.OnlineMeterStamped.Visibility = Visibility.Visible;
        page.TxtOnlineMeterPct.Text = "42%";
        page.OnlineMeterBarOn.Width = new GridLength(42, GridUnitType.Star);
        page.OnlineMeterBarOff.Width = new GridLength(58, GridUnitType.Star);
        page.TxtOnlineMeterLine.Text = "Resets at midnight. Pictures, GIFs and clips all count. Anything you've already seen replays free."
            + "   Free plan. Basic gets about 7 times more a day, Prime about 20.   See plans";

        var host = Realize(page, 1469, 650);
        MaybeRender(page.RemoteMediaBlock, "picker-meter.png");

        Assert.True(page.OnlineMeterCard.ActualHeight < 90, $"meter card is {page.OnlineMeterCard.ActualHeight:0} px");
        Assert.True(page.RemoteMediaBlock.ActualHeight < 300, $"closed block with the meter is {page.RemoteMediaBlock.ActualHeight:0} px");
        Assert.True(page.AssetBrowserSection.ActualHeight >= 260);
        AssertBrowserEndsOnThePage(page, 650);

        // The one-time official note sits at the top of the block, above the source bar, and on
        // the SAME real ~650 px page (no scroll) it still leaves the browser its floor, unclipped.
        page.OnlineOfficialNote.Visibility = Visibility.Visible;
        page.TxtOnlineOfficialPlan.Text = "Everyone gets a daily share, with extra on your first day. Anything you've already seen replays free.";
        host.Measure(new Size(1469, 650));
        host.Arrange(new Rect(0, 0, 1469, 650));
        host.UpdateLayout();
        MaybeRender(page, "picker-official.png");
        Assert.True(page.OnlineOfficialNote.TranslatePoint(new Point(0, 0), page.RemoteMediaBlock).Y
            < page.RemoteSourceChips.TranslatePoint(new Point(0, 0), page.RemoteMediaBlock).Y);
        Assert.True(page.OnlineOfficialNote.ActualHeight < 80, $"official note is {page.OnlineOfficialNote.ActualHeight:0} px");
        // The browser row keeps its 260 px floor (the section inside it carries an 8 px margin).
        Assert.True(page.RowAssetBrowser.ActualHeight >= 260,
            $"browser row is {page.RowAssetBrowser.ActualHeight:0} px with the official note up "
            + $"(note {page.OnlineOfficialNote.ActualHeight:0}, block {page.RemoteMediaBlock.ActualHeight:0})");
        AssertBrowserEndsOnThePage(page, 650);
    });

    /// <summary>The official note, the meter (Free, the longest lines) and the stamped line all up at
    /// once, in every language: the real ~650 px page still ends with the whole browser on it.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-CN")]
    public void TheOfficialNoteFitsThePageInEveryLanguage(string lang) => WpfRenderHarness.OnStaThread(() =>
    {
        var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(
            Path.Combine(OnlineMeterCopyTests.LanguageDir(), lang + ".json")));
        string L(string key) => (string)o[key]!;

        var pink = FlavourPresets.All.Single(f => f.Id == "pink");
        var sel = FlavourPresets.Resolve(pink, FypOnlineCoordinator.Catalog);
        var (page, _, _) = Build(sel.NicheIds.ToArray(), sel.CustomSubs.ToArray());
        page.OnlineMeterCard.Visibility = Visibility.Visible;
        page.OnlineMeterStamped.Visibility = Visibility.Visible;
        page.TxtOnlineMeterPct.Text = "42%";
        page.TxtOnlineMeterLine.Text = L("online_meter_under_free") + "   " + L("online_meter_upsell_free")
            + "   " + L("online_see_plans");
        page.OnlineOfficialNote.Visibility = Visibility.Visible;
        var runs = page.TxtOnlineOfficialText.Inlines.OfType<System.Windows.Documents.Run>().ToList();
        Assert.Equal(5, runs.Count);   // title, gap, body, space, plan line
        runs[0].Text = L("online_official_title");
        runs[2].Text = L("online_official_body");
        page.TxtOnlineOfficialPlan.Text = L("online_official_plan_free");

        Realize(page, 1469, 650);
        MaybeRender(page, $"picker-official-{lang}.png");
        Assert.True(page.RowAssetBrowser.ActualHeight >= 260,
            $"{lang}: browser row is {page.RowAssetBrowser.ActualHeight:0} px "
            + $"(note {page.OnlineOfficialNote.ActualHeight:0}, block {page.RemoteMediaBlock.ActualHeight:0})");
        AssertBrowserEndsOnThePage(page, 650);
    });

    /// <summary>The page is a fixed-height grid with no scroll: whatever row 1 grows by comes out of
    /// the browser, and past its 260 px floor the browser is pushed off the bottom (clipped).</summary>
    private static void AssertBrowserEndsOnThePage(AssetsTabView page, double pageHeight)
    {
        var bottom = page.AssetBrowserSection.TranslatePoint(new Point(0, page.AssetBrowserSection.ActualHeight), page).Y;
        Assert.True(bottom <= pageHeight + 0.5, $"asset browser ends at {bottom:0} on a {pageHeight:0} px page");
    }

    /// <summary>#1410: open, the list used to be capped at a fixed 430 px while the collapsed
    /// browser row still held its 260 px floor, so on the real page (about 650 px tall) the
    /// last rows and the r/ add field ran under the Start bar. Now the list takes what the
    /// page has left and scrolls inside it, at any page height.</summary>
    [Theory]
    [InlineData(650)]
    [InlineData(520)]
    [InlineData(900)]
    public void OpenTheListStaysOnThePageAndTheAddFieldIsReachable(double pageHeight) => WpfRenderHarness.OnStaThread(() =>
    {
        var customs = Enumerable.Range(1, 9).Select(i => "Own" + i).ToArray();
        var (page, _, _) = Build(new[] { "hypno", "bimbo" }, customs);
        page.ApplyFineTuneLayout(open: true, remoteInPlay: true);
        page.TxtRemoteSubError.Text = "r/Own1 is already in your pool.";
        page.TxtRemoteSubError.Visibility = Visibility.Visible;

        var host = Realize(page, 1469, pageHeight);
        MaybeRender(page, $"picker-open-fit-{pageHeight:0}.png");

        double Bottom(FrameworkElement el) =>
            el.TranslatePoint(new Point(0, el.ActualHeight), page).Y;

        Assert.Equal(Visibility.Collapsed, page.AssetBrowserSection.Visibility);
        Assert.True(Bottom(page.RemoteMediaBlock) <= pageHeight + 0.5,
            $"media block ends at {Bottom(page.RemoteMediaBlock):0} on a {pageHeight:0} px page");
        Assert.True(Bottom(page.RemoteFineTuneScroll) <= pageHeight + 0.5);

        // The list is longer than its room on the real page, so it has to scroll.
        if (pageHeight <= 650)
            Assert.True(page.RemoteFineTuneScroll.ScrollableHeight > 0);

        page.RemoteFineTuneScroll.ScrollToBottom();
        host.UpdateLayout();
        foreach (var el in new FrameworkElement[] { page.TxtRemoteCustomSub, page.BtnRemoteAddSub, page.TxtRemoteSubError })
        {
            var top = el.TranslatePoint(new Point(0, 0), page).Y;
            var scrollTop = page.RemoteFineTuneScroll.TranslatePoint(new Point(0, 0), page).Y;
            Assert.True(top >= scrollTop - 0.5, $"{el.Name} starts above the list at {top:0}");
            Assert.True(Bottom(el) <= Bottom(page.RemoteFineTuneScroll) + 0.5,
                $"{el.Name} ends at {Bottom(el):0}, past the list's {Bottom(page.RemoteFineTuneScroll):0}");
        }
    });

    /// <summary>#1409: Both, open Fine-tune, back to "My own assets". The Fine-tune row is gone
    /// on local, so a left-open list must not keep the Asset Browser collapsed.</summary>
    [Fact]
    public void OnMyOwnAssetsAnOpenListNeverHidesTheBrowser() => WpfRenderHarness.OnStaThread(() =>
    {
        Assert.False(AssetsTabView.FineTuneShows(open: true, remoteInPlay: false));
        Assert.True(AssetsTabView.FineTuneShows(open: true, remoteInPlay: true));
        Assert.False(AssetsTabView.FineTuneShows(open: false, remoteInPlay: true));

        var (page, _, _) = Build(new[] { "hypno" }, Array.Empty<string>());
        page.ApplyFineTuneLayout(open: true, remoteInPlay: true);
        Assert.Equal(Visibility.Collapsed, page.AssetBrowserSection.Visibility);

        page.RemoteMediaDetails.Visibility = Visibility.Collapsed;     // source -> My own assets
        page.ApplyFineTuneLayout(open: true, remoteInPlay: false);
        Assert.Equal(Visibility.Visible, page.AssetBrowserSection.Visibility);
        Assert.Equal(Visibility.Collapsed, page.RemoteFineTuneScroll.Visibility);
        Assert.True(page.RowAssetBrowser.Height.IsStar);
        Assert.Equal(260, page.RowAssetBrowser.MinHeight);
        Assert.True(page.RowMediaSource.Height.IsAuto);

        Realize(page, 1469, 650);
        Assert.True(page.AssetBrowserSection.ActualHeight >= 260);
    });

    [Fact]
    public void EveryNicheWearsATintAndTheFlavourOwnedOnesWearTheirFlavour()
    {
        Assert.Equal(Color.FromRgb(0xb9, 0x9c, 0xff), MainWindow.RemoteNicheTintOf("hypno"));    // Trance
        Assert.Equal(Color.FromRgb(0xff, 0x87, 0xc7), MainWindow.RemoteNicheTintOf("bimbo"));    // Pink
        Assert.Equal(Color.FromRgb(0xff, 0xb3, 0xd9), MainWindow.RemoteNicheTintOf("sissy"));    // Frills
        Assert.Equal(Color.FromRgb(0xFF, 0x69, 0xB4), MainWindow.RemoteNicheTintOf("hentai"));   // nobody's: Pink
    }

    [Fact]
    public void CommunitiesAndNichesKnowTheirSingular()
    {
        Assert.Equal("1 community", MainWindow.Plural("{0} community|{0} communities", 1));
        Assert.Equal("3 communities", MainWindow.Plural("{0} community|{0} communities", 3));
    }
}

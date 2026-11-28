using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Studio look pickers (owner notes 2026-11-17): the wardrobe seam's rules, the preview round trip with
/// the shop's own backend, the rows on the four options pages, and offscreen stills of every row
/// (CCP_LOOK_DUMP=&lt;dir&gt; writes PNGs).</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LookWardrobeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A store a test fills by hand.</summary>
    private sealed class FakeStore : ILookStore
    {
        public readonly HashSet<string> Owned = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string?> Worn = new(StringComparer.Ordinal);
        public readonly List<(string, string?)> Equips = new();
        public bool Owns(string itemId) => Owned.Contains(itemId);
        string? ILookStore.Worn(string slot) => Worn.TryGetValue(slot, out var v) ? v : null;
        public bool Equip(string slot, string? itemId) { Equips.Add((slot, itemId)); Worn[slot] = itemId; return true; }
    }

    private static T With<T>(ILookStore store, Func<T> body)
    {
        var was = LookWardrobe.Store;
        LookWardrobe.Store = store;
        try { return body(); } finally { LookWardrobe.Store = was; }
    }

    private static void With(ILookStore store, Action body) => With(store, () => { body(); return 0; });

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void Outside_the_preview_only_the_free_rows_are_owned_and_worn_and_nothing_else_can_be_worn()
    {
        With(new FreeOnlyLookStore(), () =>
        {
            foreach (var slot in new[] { "bubble", "card", "behind", "word" })
            {
                var c = LookWardrobe.Choices(slot);
                Assert.NotEmpty(c);
                Assert.Equal(WornLooks.DefaultOf(slot), c[0].Id);
                Assert.True(c[0].Owned && c[0].Worn);
                Assert.All(c.Skip(1), x => Assert.False(x.Owned || x.Worn));
                Assert.False(LookWardrobe.Wear(slot, c[^1].Id));
                Assert.True(LookWardrobe.Wear(slot, c[0].Id));
            }
            Assert.False(LookWardrobe.TwoToneOwned);
            LookWardrobe.TwoToneOn = true;
            Assert.False(LookWardrobe.TwoToneOn);
        });
        // no network in the seam itself: the real account equip is its own store, the default outside the preview
        var src = File.ReadAllText(Path.Combine(Root(), "ConditioningControlPanel", "Services", "Prizes", "LookWardrobe.cs"));
        Assert.DoesNotContain("HttpClient", src);
        Assert.DoesNotContain("BackRoomApi", src);
        Assert.Contains("return AccountLookStore.Shared;", src);
    }

    [Fact]
    public void Owned_looks_wear_unowned_and_todo_and_other_slots_are_refused()
    {
        var s = new FakeStore();
        s.Owned.Add("fx.bubble.heart");
        With(s, () =>
        {
            var c = LookWardrobe.Choices("bubble");
            Assert.DoesNotContain(c, x => x.Id is "fx.bubble.gum" or "fx.bubble.latex" or "fx.bubble.oil");   // not out yet
            Assert.Equal(new[] { "fx.bubble.soap", "fx.bubble.marble", "fx.bubble.heart", "fx.bubble.champagne", "fx.bubble.spiral", "fx.bubble.disco", "fx.bubble.frost" }, c.Select(x => x.Id));
            Assert.True(LookWardrobe.Wear("bubble", "fx.bubble.heart"));
            Assert.Equal("fx.bubble.heart", LookWardrobe.Worn("bubble"));
            Assert.False(LookWardrobe.Wear("bubble", "fx.bubble.disco"));     // not owned
            Assert.False(LookWardrobe.Wear("card", "fx.bubble.heart"));       // another slot
            Assert.False(LookWardrobe.Wear("bubble", "fx.bubble.gum"));       // not out yet
            Assert.True(LookWardrobe.Wear("bubble", "fx.bubble.soap"));
            Assert.Equal(("bubble", (string?)null), s.Equips[^1]);           // the free row = take off
            // a worn row the account no longer owns reads as the free row
            s.Worn["card"] = "fx.card.lace";
            Assert.Equal("fx.card.plain", LookWardrobe.Worn("card"));
            // two-tone: refused until owned, then the switch and palette colours only
            LookWardrobe.SecondFogColour = SuperCore.FogLooks.Sky;
            Assert.Equal(SuperCore.FogLooks.DefaultSecond, LookWardrobe.SecondFogColour);
            s.Owned.Add(SuperCore.FogLooks.Twotone);
            LookWardrobe.TwoToneOn = true;
            Assert.True(LookWardrobe.TwoToneOn);
            LookWardrobe.SecondFogColour = SuperCore.FogLooks.Peach;
            Assert.Equal(SuperCore.FogLooks.Peach, LookWardrobe.SecondFogColour);
            LookWardrobe.SecondFogColour = "fx.fog.teal";
            Assert.Equal(SuperCore.FogLooks.Peach, LookWardrobe.SecondFogColour);
        });
    }

#if DEBUG
    [Fact]
    public void In_the_preview_Studio_and_the_shop_share_one_state_and_fog2_takes_every_palette_colour()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-look-wardrobe-" + Guid.NewGuid().ToString("N"), ShopPreviewBackend.FileName);
        try
        {
            var b = new ShopPreviewBackend(path, null, () => Now);
            b.Credit(2000, "play");
            Assert.True((bool)b.Handle("shop-buy", new JObject { ["itemId"] = "fx.bubble.heart" }, "i1")["ok"]!);
            Assert.True((bool)b.Handle("shop-buy", new JObject { ["itemId"] = SuperCore.FogLooks.Twotone }, "i2")["ok"]!);
            With(new PreviewLookStore(b), () =>
            {
                Assert.True(LookWardrobe.Choices("bubble").Single(c => c.Id == "fx.bubble.heart").Owned);
                Assert.True(LookWardrobe.Wear("bubble", "fx.bubble.heart"));
                LookWardrobe.TwoToneOn = true;
                foreach (var (id, _) in SuperCore.FogLooks.Palette)
                {
                    LookWardrobe.SecondFogColour = id;
                    Assert.Equal(id, LookWardrobe.SecondFogColour);
                }
            });
            // the shop page's next shop-state shows what Studio wore
            var state = b.Handle("shop-state", null, null);
            Assert.Equal("fx.bubble.heart", (string?)state["equip"]!["bubble"]);
            Assert.True((bool)state["equip"]!["mix"]!);
            Assert.Equal(SuperCore.FogLooks.Peach, (string?)state["equip"]!["fog2"]);
            // and the other way round: the page wears sky through shop-equip fog2 (a palette value, no shop row)
            Assert.True((bool)b.Handle("shop-equip", new JObject { ["slot"] = "fog2", ["itemId"] = SuperCore.FogLooks.Sky }, null)["ok"]!);
            var again = new ShopPreviewBackend(path, null, () => Now);   // and it survives a restart
            Assert.Equal(SuperCore.FogLooks.Sky, With(new PreviewLookStore(again), () => LookWardrobe.SecondFogColour));
            Assert.Equal(SuperCore.FogLooks.Sky, again.Worn("fog2"));
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }
#endif

    [Fact]
    public void Each_options_page_hosts_its_look_row()
    {
        string Xaml(string name) => File.ReadAllText(Path.Combine(Root(), "ConditioningControlPanel", "Features", name));
        Assert.Contains("<ctl:LookRow Slot=\"bubble\"/>", Xaml("BubblePopFeatureControl.xaml"));
        Assert.Contains("<ctl:LookRow Slot=\"word\"/>", Xaml("SubliminalFeatureControl.xaml"));
        var flash = Xaml("FlashFeatureControl.xaml");
        int deck = flash.IndexOf("x:Name=\"DeckRows\"", StringComparison.Ordinal), end = flash.IndexOf("</ctl:SuperBox>", deck, StringComparison.Ordinal);
        foreach (var slot in new[] { "card", "behind" })
        {
            int at = flash.IndexOf($"<ctl:LookRow Slot=\"{slot}\"/>", StringComparison.Ordinal);
            Assert.True(at > deck && at < end, $"{slot} sits in the Deck's option rows");
        }
        var pink = Xaml("PinkFilterFeatureControl.xaml");
        Assert.True(pink.IndexOf("<ctl:FogTwoToneBlock", StringComparison.Ordinal) > pink.IndexOf("x:Name=\"BtnResetColor\"", StringComparison.Ordinal), "Two-tone sits beside the filter colour");
    }

    [Fact]
    public void Rows_show_locked_looks_open_the_shop_and_render_legibly()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        var dump = Environment.GetEnvironmentVariable("CCP_LOOK_DUMP");
        var opened = new List<string>();
        var was = LookRow.OpenShopOn;
        LookRow.OpenShopOn = opened.Add;
        try
        {
            WpfRenderHarness.OnStaThread(() =>
            {
                var none = new FreeOnlyLookStore();
                var some = new FakeStore();
                foreach (var id in new[] { "fx.bubble.heart", "fx.bubble.champagne", "fx.card.ink", "fx.word.chalk", "fx.behind.whisper" }) some.Owned.Add(id);
                some.Worn["bubble"] = "fx.bubble.heart"; some.Worn["card"] = "fx.card.ink"; some.Worn["word"] = "fx.word.chalk";
                var all = new FakeStore();
                foreach (var e in LookWardrobe.Catalogue) all.Owned.Add(e.Id);
                all.Worn["bubble"] = "fx.bubble.spiral"; all.Worn["card"] = "fx.card.lace"; all.Worn["behind"] = "fx.behind.whisper"; all.Worn["word"] = "fx.word.marquee";
                all.Worn["mix"] = SuperCore.FogLooks.Twotone; all.Worn["fog2"] = SuperCore.FogLooks.Pink;   // too close to hot pink: the note shows

                foreach (var width in new[] { 460, 600 })
                foreach (var (tag, store) in new (string, ILookStore)[] { ("none", none), ("some", some), ("all", all) })
                    With(store, () =>
                    {
                        var panel = Sheet(width, out var host);
                        foreach (var slot in new[] { "bubble", "card", "behind", "word" }) panel.Children.Add(new LookRow { Slot = slot });
                        var fog = new FogTwoToneBlock();
                        panel.Children.Add(fog);
                        Layout(host);
                        foreach (var r in panel.Children.OfType<LookRow>()) r.Rebuild();
                        fog.Rebuild();
                        Layout(host);
                        if (dump is { Length: > 0 }) Save(host, Path.Combine(dump, $"look-rows-{tag}-{width}.png"));
                        var chips = Descendants(panel).OfType<Button>().ToList();
                        Assert.All(chips, c => Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(c))));
                        Assert.All(chips, c => Assert.True(c.Focusable));
                        // two lines of chips at most in the options column
                        foreach (var r in panel.Children.OfType<LookRow>())
                        {
                            var wrap = (TwoLinePanel)r.Children[1];
                            Assert.True(wrap.ActualHeight <= 2 * 36 + 1, $"{tag} {r.Slot}: {wrap.ActualHeight:0} px of chips");
                        }
                        if (tag == "none")
                        {
                            Assert.Contains(chips, c => (string?)c.ToolTip != null && ((string)c.ToolTip!).Contains("360"));
                            var locked = chips.First(c => c.Tag is LookChips.State.Locked);
                            locked.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            Assert.NotEmpty(opened);
                        }
                        if (tag == "all") Assert.Single(chips, c => c.Tag is true);   // one ringed swatch
                    });
            });
        }
        finally { LookRow.OpenShopOn = was; }
    }

    /// <summary>The options column as the Studio draws it: about 460 px of dark glass.</summary>
    private static StackPanel Sheet(int width, out Border host)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        host = new Border { Width = width, Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x18, 0x2C)), Child = panel };
        return panel;
    }

    private static void Layout(FrameworkElement host)
    {
        host.Measure(new Size(host.Width, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static void Save(FrameworkElement el, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * 2), (int)Math.Ceiling(el.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}

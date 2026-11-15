using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF FriendsDrawer.Juice.cs and the drawer's photos, menu skin and foot label, reached through the rail chip.</summary>
public sealed class FriendsDrawerJuiceTests
{
    private static T? Find<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);
    private static IEnumerable<FriendsDrawer.LinkBurst> Bursts(Control d) => d.GetLogicalDescendants().OfType<FriendsDrawer.LinkBurst>();
    private static Grid AvatarOf(Control d, string rowTag) =>
        Find<Border>(d, rowTag)!.GetLogicalDescendants().OfType<Grid>().First(g => g.Width == 38);
    private static Color Rgb(IBrush b) => ((ISolidColorBrush)b).Color;

    private static async Task Open(MotionLevel level, Func<FriendsDrawer, FriendsDrawerTests.Wire, FriendsService, SteppedTime, Task> body, string? state = null)
    {
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.GentleVisuals, UiMotionPreference.OsProbe);
        var oldTime = BeatLoop.Time; var time = new SteppedTime(); BeatLoop.Time = time;
        var (svc, wire) = FriendsDrawerTests.Service();
        if (state != null) wire.StateBody = state;
        var chip = new FriendsRailChip(svc);
        var owner = new Window { Width = 500, Height = 700, Content = chip };
        try
        {
            settings.MotionLevel = level; settings.GentleVisuals = false;
            UiMotionPreference.OsProbe = () => true; UiMotionPreference.CacheStamp = long.MinValue;
            await svc.RefreshAsync();
            owner.Show();
            chip.Toggle();
            Assert.True(chip.IsOpen);
            await body(chip.Drawer, wire, svc, time);
        }
        finally
        {
            chip.CloseDrawer(); owner.Close(); svc.Dispose();
            (settings.MotionLevel, settings.GentleVisuals, UiMotionPreference.OsProbe) = old;
            UiMotionPreference.CacheStamp = long.MinValue;
            time.Advance(TimeSpan.FromSeconds(5)); BeatLoop.StepAll(); BeatLoop.Time = oldTime;
        }
    }

    // WPF Juice.cs:34-56: scale 1-0.06k and Y 14k spring back; rows fade in from a 10 px rise.
    [Theory]
    [InlineData(MotionLevel.Full, 1.0)]
    [InlineData(MotionLevel.Reduced, 0.5)]
    public Task OpeningSpringsTheCardAndStaggersTheRows(MotionLevel level, double k) => AvaloniaTestDispatcher.RunAsync(() => Open(level, async (d, _, _, time) =>
    {
        var group = Assert.IsType<TransformGroup>(d.RenderTransform);
        var scale = group.Children.OfType<ScaleTransform>().Single();
        var slide = group.Children.OfType<TranslateTransform>().Single();
        Assert.Equal(1 - 0.06 * k, scale.ScaleX, 5);
        Assert.Equal(14 * k, slide.Y, 5);
        var row = Find<Border>(d, "friends-row:u_off")!;
        Assert.Equal(0, row.Opacity);
        Assert.Equal(10, Assert.IsType<TranslateTransform>(row.RenderTransform).Y, 5);
        time.Advance(TimeSpan.FromSeconds(1)); BeatLoop.StepAll();
        Assert.Equal(1, scale.ScaleX, 5);
        Assert.Equal(0, slide.Y, 5);
        Assert.Equal(1, row.Opacity);
        // A card that opens grows in under its row (WPF Juice.cs:59-63).
        d.Toggle("u_on");
        var opened = Find<Border>(d, "friends-row:u_on")!;
        Assert.Equal(0, opened.Opacity);
        Assert.Equal(10, Assert.IsType<TranslateTransform>(opened.RenderTransform).Y, 5);
        // So does the add box (WPF FriendsDrawer.cs:458).
        d.ToggleAddBox(true);
        var box = Find<Border>(d, "friends-add-box")!;
        Assert.Equal(0, box.Opacity);
        await Task.CompletedTask;
    }));

    [Fact]
    public Task OffMovesNothingAndThrowsNoFx() => AvaloniaTestDispatcher.RunAsync(() => Open(MotionLevel.Off, async (d, wire, svc, _) =>
    {
        Assert.Null(d.RenderTransform);
        Assert.Equal(1, d.Opacity);
        var row = Find<Border>(d, "friends-row:u_off")!;
        Assert.Equal(1, row.Opacity);
        Assert.IsNotType<TranslateTransform>(row.RenderTransform);
        d.Toggle("u_on");
        Assert.Equal(1, Find<Border>(d, "friends-row:u_on")!.Opacity);
        wire.StateBody = FriendsDrawerTests.State.Replace("\"name\":\"Zed\",\"tier\":0,\"online\":false", "\"name\":\"Zed\",\"tier\":0,\"online\":true");
        await svc.RefreshAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(AvatarOf(d, "friends-row:u_off").RenderTransform);
        Find<Button>(d, "friends-copy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await FriendsDrawerTests.Until(() => Find<TextBlock>(d, "friends-copy-label")?.Text == Loc.Get("friends_copied"));
        Assert.Equal(Loc.Get("friends_copied"), Find<TextBlock>(d, "friends-copy-label")!.Text);
        Assert.Empty(Bursts(d));
    }));

    // WPF Juice.cs:136-151 via FriendsDrawer.cs:251-257: Core CameOnline picks the friend; their avatar hops 10 px with 8 mint sparks.
    [Fact]
    public Task AFriendComingOnlineHopsWithMintSparks() => AvaloniaTestDispatcher.RunAsync(() => Open(MotionLevel.Full, async (d, wire, svc, time) =>
    {
        wire.StateBody = FriendsDrawerTests.State.Replace("\"name\":\"Zed\",\"tier\":0,\"online\":false", "\"name\":\"Zed\",\"tier\":0,\"online\":true");
        await svc.RefreshAsync();
        await FriendsDrawerTests.Until(() => Bursts(d).Any());
        var sparks = Assert.Single(Bursts(d));
        Assert.Equal("friends-hop-sparks", sparks.Tag);
        Assert.Equal(8, sparks.Sparks);
        Assert.Equal(Rgb(FriendsDrawer.Mint), Rgb(sparks.Color));
        var hop = Assert.IsType<TranslateTransform>(AvatarOf(d, "friends-row:u_off").RenderTransform);
        time.Advance(TimeSpan.FromMilliseconds(170)); BeatLoop.StepAll();
        Assert.Equal(-10, hop.Y, 5);
        Assert.Null(AvatarOf(d, "friends-row:u_on").RenderTransform);
    }));

    // WPF FriendsDrawer.cs:995 (lilac Pop on Copy), :1122 (mint Shockwave on add) and Honesty.cs:69 (on Accept).
    [Fact]
    public Task CopyPopsLilacAndAddAndAcceptSendAMintWave() => AvaloniaTestDispatcher.RunAsync(() => Open(MotionLevel.Full, async (d, wire, _, _) =>
    {
        Find<Button>(d, "friends-copy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await FriendsDrawerTests.Until(() => Bursts(d).Any());
        var pop = Assert.Single(Bursts(d));
        Assert.Equal("friends-link-pop", pop.Tag);
        Assert.Equal(Rgb(FriendsDrawer.Lilac), Rgb(pop.Color));

        wire.RequestBody = """{"ok":true,"status":"sent"}""";
        Assert.Equal(AddResult.Sent, await d.AddByCodeAsync("ABCDF"));
        var wave = Assert.Single(Bursts(d), b => b.Wave);
        Assert.Equal(Rgb(FriendsDrawer.Mint), Rgb(wave.Color));

        Find<Button>(d, "friends-accept")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await FriendsDrawerTests.Until(() => Bursts(d).Count(b => b.Wave) == 2);
        Assert.Equal(2, Bursts(d).Count(b => b.Wave));
    }));

    // WPF FriendsLook.cs:226-243 and Juice.cs:63-82: a tagged, glowing dot of at least 10 px that breathes only at Full.
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public Task OnlineDotsGlowAndBreatheOnlyAtFull(MotionLevel level) => AvaloniaTestDispatcher.RunAsync(() => Open(level, async (d, _, _, time) =>
    {
        var dot = AvatarOf(d, "friends-row:u_on").Children.OfType<Ellipse>().Single();
        Assert.Equal("friends-dot-on", dot.Tag);
        Assert.True(dot.Width >= 10);
        Assert.IsType<DropShadowEffect>(dot.Effect);
        Assert.Equal("friends-dot-off", AvatarOf(d, "friends-row:u_off").Children.OfType<Ellipse>().Single().Tag);
        time.Advance(TimeSpan.FromSeconds(0.9)); BeatLoop.StepAll();
        if (level == MotionLevel.Full && AmbientFxCanvas.Env.AllowAmbientLoops) Assert.Equal(0.725, dot.Opacity, 3);
        else Assert.Equal(1, dot.Opacity);
        Assert.Equal(level == MotionLevel.Full, AmbientFxCanvas.Env.AllowAmbientLoops);
        await Task.CompletedTask;
    }));

    // WPF FriendsLook.cs:184-283: the picture beats the preset; a failed fetch keeps the initials.
    [Fact]
    public Task RemotePhotosReplaceInitialsAndPresetThroughTheKnockCardSeam() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var id = Guid.NewGuid().ToString("N");
        byte[] png;
        using (var rt = new RenderTargetBitmap(new PixelSize(4, 4)))
        using (var ms = new MemoryStream()) { rt.Save(ms); png = ms.ToArray(); }
        var urls = new List<string>();
        var oldFetch = KnockCard.FetchPicture;
        KnockCard.FetchPicture = url => { urls.Add(url); return url.Contains("broken") ? Task.FromException<byte[]>(new IOException("gone")) : Task.FromResult(png); };
        var state = FriendsDrawerTests.State
            .Replace("\"name\":\"Mia\",", $"\"name\":\"Mia\",\"avatar\":\"/friends/avatar/{id}.png\",\"cosmetics\":{{\"avatar_id\":\"avatar_bambi_1\"}},")
            .Replace("\"name\":\"Zed\",", $"\"name\":\"Zed\",\"avatar\":\"/friends/avatar/broken-{id}.png\",\"cosmetics\":{{\"avatar_id\":\"avatar_bambi_1\"}},")
            .Replace("\"name\":\"Ann\",", $"\"name\":\"Ann\",\"avatar\":\"https://cdn.example/{id}.png\",");
        try
        {
            await Open(MotionLevel.Off, async (d, _, _, _) =>
            {
                var mia = (Border)AvatarOf(d, "friends-row:u_on").Children[0];
                var brush = Assert.IsType<ImageBrush>(mia.Background);
                Assert.NotSame(ModArt.AvatarPreset("avatar_bambi_1"), brush.Source);
                Assert.Null(mia.Child);
                var ann = (Border)AvatarOf(d, "friends-request-in:u_req").Children[0];
                Assert.IsType<ImageBrush>(ann.Background);
                var zed = (Border)AvatarOf(d, "friends-row:u_off").Children[0];
                Assert.IsNotType<ImageBrush>(zed.Background);
                Assert.Equal("Z", Assert.IsType<TextBlock>(zed.Child).Text);
                Assert.Contains("https://codebambi-proxy.vercel.app/friends/avatar/" + id + ".png", urls);
                Assert.Contains("https://cdn.example/" + id + ".png", urls);
                await Task.CompletedTask;
            }, state);
        }
        finally { KnockCard.FetchPicture = oldFetch; }
    });

    // WPF FriendsDrawer.Pickers.cs:325-370 and Honesty.cs:80-90.
    [Fact]
    public Task FriendAndRequestMenusCarryTheSkin() => AvaloniaTestDispatcher.RunAsync(() => Open(MotionLevel.Off, async (d, _, _, _) =>
    {
        foreach (var menu in new[] { Find<Border>(d, "friends-row:u_on")!.ContextMenu!, Find<Border>(d, "friends-request-in:u_req")!.ContextMenu! })
        {
            Assert.Equal(Color.FromRgb(0x22, 0x16, 0x41), Rgb(menu.Background!));
            Assert.Equal(Rgb(FriendsDrawer.Line2), Rgb(menu.BorderBrush!));
            Assert.Equal(Rgb(FriendsDrawer.Text), Rgb(menu.Foreground!));
            Assert.Equal(FriendsDrawer.Display, menu.FontFamily);
            Assert.Equal(13.5, menu.FontSize);
        }
        await Task.CompletedTask;
    }));

    // WPF FriendsDrawer.cs:909-922: in a long language Add friend is the glyph alone, its words the tooltip and name.
    [Theory]
    [InlineData("en", false)]
    [InlineData("de", true)]
    public Task AddFriendCollapsesToItsGlyphInLongLanguages(string language, bool collapsed) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var previous = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage(language);
            await Open(MotionLevel.Off, async (d, _, _, _) =>
            {
                var add = Find<Button>(d, "friends-add-open")!;
                var words = ((StackPanel)add.Content!).Children[1];
                Assert.Equal(!collapsed, words.IsVisible);
                Assert.Equal(collapsed ? Loc.Get("friends_add_title") : null, ToolTip.GetTip(add));
                Assert.Equal(collapsed ? Loc.Get("friends_add_title") : null, AutomationProperties.GetName(add));
                await Task.CompletedTask;
            });
        }
        finally { LocalizationManager.Instance.SetLanguage(previous); }
    });
}

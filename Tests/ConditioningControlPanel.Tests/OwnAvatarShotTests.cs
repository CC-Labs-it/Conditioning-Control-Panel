using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The four own avatar surfaces wear every shop frame (owner 2026-11-17: a bought frame was nowhere): the profile
/// hero, the friends drawer's own row, the friends chip bottom left and the account chip top right. Each draws the
/// frame unclipped at the one frame geometry, and the presence dot stays over it. With <c>CCP_OWN_AVATAR_DUMP=&lt;dir&gt;</c>
/// set, one strip per frame is saved there to look at.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class OwnAvatarShotTests
{
    private static readonly Color Ground = Color.FromRgb(0x1C, 0x12, 0x33);

    [Fact]
    public void Every_own_surface_wears_every_frame_unclipped()
    {
        Assert.True(PackUriBootstrap.Failure == null, PackUriBootstrap.Failure);
        var frames = ShopCosmetics.Frames.Select(f => f.Id).ToList();
        Assert.Equal(5, frames.Count);
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var id in frames.Prepend(null))
            {
                var look = new PlayerLook(null, id);
                var (hero, heroAvatar) = Hero(id);
                var row = FriendsLook.Avatar("Cleo", null, 40, true, look);
                var chip = Chip(look);
                var (bubble, bubbleFrame, bubbleRing) = Bubble(id);

                var strip = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Ground) };
                foreach (var part in new FrameworkElement[] { hero, Pad(row, 72), Pad(chip, 240), Pad(bubble, 72) })
                {
                    part.Margin = new Thickness(18);
                    part.VerticalAlignment = VerticalAlignment.Center;
                    strip.Children.Add(part);
                }
                strip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                strip.Arrange(new Rect(strip.DesiredSize));
                strip.UpdateLayout();

                if (id == null)
                {
                    Assert.DoesNotContain(Images(row), i => (string?)i.Tag == "friends-frame");
                    Assert.True(bubbleFrame == null || bubbleFrame.Visibility != Visibility.Visible);
                    Assert.Equal(Visibility.Visible, bubbleRing.Visibility);
                }
                else
                {
                    // hero: the frame layer is on and as wide as the frame geometry says
                    var layer = (Image)heroAvatar.FindName("FrameLayer")!;
                    Assert.Equal(Visibility.Visible, layer.Visibility);
                    Assert.Equal(104 / ShopCosmetics.FrameWindowRatio, layer.RenderSize.Width, 1);
                    // the drawer's own row: framed, unclipped, the presence dot over the frame
                    var f = Images(row).Single(i => (string?)i.Tag == "friends-frame");
                    Assert.Null(LayoutInformation.GetLayoutClip(f));
                    Assert.Equal(40 / ShopCosmetics.FrameWindowRatio, f.RenderSize.Width, 1);
                    var kids = row.Children.Cast<UIElement>().ToList();
                    Assert.True(kids.FindIndex(k => k is Ellipse { Tag: "friends-dot-on" }) > kids.IndexOf(f), "the dot is drawn over the frame");
                    // the friends chip
                    var cf = Images(chip).Single(i => (string?)i.Tag == "friends-frame");
                    Assert.Null(LayoutInformation.GetLayoutClip(cf));
                    // the account chip: the frame replaces the pink ring and is not clipped by the 34 box
                    Assert.NotNull(bubbleFrame);
                    Assert.Equal(Visibility.Visible, bubbleFrame!.Visibility);
                    Assert.Equal(Visibility.Collapsed, bubbleRing.Visibility);
                    Assert.Null(LayoutInformation.GetLayoutClip(bubbleFrame));
                    Assert.Equal(28 / ShopCosmetics.FrameWindowRatio, bubbleFrame.RenderSize.Width, 1);
                }

                if (Environment.GetEnvironmentVariable("CCP_OWN_AVATAR_DUMP") is { Length: > 0 } dir)
                    Save(strip, System.IO.Path.Combine(dir, $"own-avatars-{id ?? "none"}.png"), 2);
            }
        });
    }

    /// <summary>The profile hero as DiscordTabView draws it: a 104 AdornedAvatar with the pink ring.</summary>
    private static (FrameworkElement, AdornedAvatar) Hero(string? frameId)
    {
        var a = new AdornedAvatar { AvatarSize = 104, FrameId = frameId };
        a.AvatarBrush.ImageSource = null;
        ((Border)a.FindName("AvatarRing")!).Background = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x9E));
        return (Pad(a, 150), a);
    }

    /// <summary>The real friends chip with its own look (the drawer's MeLook), painted as on the rail.</summary>
    private static FrameworkElement Chip(PlayerLook look)
    {
        var chip = new FriendsRailChip(null);
        chip.Drawer.MeName = () => "Cleo";
        chip.Drawer.MeLook = () => look;
        typeof(FriendsRailChip).GetMethod("RefreshFace", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chip, null);
        chip.Width = 220;
        return chip;
    }

    /// <summary>The account chip's face as MainWindow.xaml builds it (34 box, fill margin 3, ring margin 1), then the
    /// same ProfileBubbleFrame call the window makes.</summary>
    private static (FrameworkElement, Image?, Ellipse) Bubble(string? frameId)
    {
        var g = new Grid { Width = 34, Height = 34 };
        var fill = new Ellipse { Margin = new Thickness(3), Fill = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x9E)) };
        var initials = new TextBlock { Text = "C", FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var ring = new Ellipse { Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)), StrokeThickness = 2, Margin = new Thickness(1) };
        var glow = new Ellipse { Stroke = Brushes.Gold, StrokeThickness = 2, Margin = new Thickness(1), Opacity = 0 };
        g.Children.Add(fill); g.Children.Add(initials); g.Children.Add(ring); g.Children.Add(glow);
        var frame = ProfileBubbleFrame.Apply(g, fill, ring, null, frameId);
        return (g, frame, ring);
    }

    private static FrameworkElement Pad(FrameworkElement el, double size)
    {
        var host = new Grid { Width = size, Height = size };
        el.HorizontalAlignment = HorizontalAlignment.Center;
        el.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(el);
        return host;
    }

    private static System.Collections.Generic.IEnumerable<Image> Images(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is Image img) yield return img;
            foreach (var d in Images(c)) yield return d;
        }
        if (root is Panel p && VisualTreeHelper.GetChildrenCount(root) == 0)
            foreach (var c in p.Children.OfType<Image>()) yield return c;
    }

    private static void Save(FrameworkElement el, string path, double scale)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var w = (int)Math.Ceiling(el.ActualWidth * scale);
        var h = (int)Math.Ceiling(el.ActualHeight * scale);
        var bmp = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}

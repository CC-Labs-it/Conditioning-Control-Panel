using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls;
using Xunit;
using SuperCore = ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Plain card frame chip on the Studio look rows reads as "no frame" (owner 2026-11-17: it read as a pink card):
/// a picture card with a thin neutral edge and a ring-and-slash mark, never the pink ground. Set
/// <c>CCP_LOOK_DUMP</c> to a folder to also write a PNG of the four card chips side by side, four times up.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class LookThumbsPlainTests
{
    [Fact]
    public void Plain_is_a_bare_picture_card_with_a_none_mark()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var plain = Assert.IsType<Grid>(LookThumbs.PlainCard(16, 22));
            Assert.Equal(2, plain.Children.Count);
            var card = Assert.IsType<Border>(plain.Children[0]);
            Assert.IsNotType<SolidColorBrush>(card.Background);   // a picture, not the old pink ground
            Assert.IsType<System.Windows.Shapes.Path>(plain.Children[1]);

            if (Environment.GetEnvironmentVariable("CCP_LOOK_DUMP") is { Length: > 0 } dir)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x17, 0x40)) };
                row.Children.Add(new Border { Padding = new Thickness(6), Child = LookThumbs.PlainCard(16, 22) });
                foreach (var back in new[] { SuperCore.DeckBack.Ink, SuperCore.DeckBack.Check, SuperCore.DeckBack.Lace })
                    row.Children.Add(new Border
                    {
                        Margin = new Thickness(6), Width = 16, Height = 22, CornerRadius = new CornerRadius(3),
                        Background = LookThumbs.CardGround(back), BorderBrush = LookThumbs.CardMark(back), BorderThickness = new Thickness(3),
                    });
                row.LayoutTransform = new ScaleTransform(4, 4);
                row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                row.Arrange(new Rect(row.DesiredSize));
                row.UpdateLayout();
                var bmp = new RenderTargetBitmap((int)Math.Ceiling(row.DesiredSize.Width), (int)Math.Ceiling(row.DesiredSize.Height), 96, 96, PixelFormats.Pbgra32);
                bmp.Render(row);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                Directory.CreateDirectory(dir);
                using var fs = File.Create(System.IO.Path.Combine(dir, "studio-card-chips.png"));
                enc.Save(fs);
            }
        });
    }
}

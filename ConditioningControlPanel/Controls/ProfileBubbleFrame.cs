using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// The worn profile frame round the header account chip (MainWindow.ProfileBubble): the small still (ProfileFrameArt
/// draws a simpler one at this size) at the one frame geometry every surface uses, over the pink ring (which it
/// replaces), under the glow ring and the tier badge. No frame: the pink ring as before. Code-made, so the chip's
/// XAML stays as it was.
/// </summary>
internal static class ProfileBubbleFrame
{
    public const string Tag = "profile-bubble-frame";

    /// <summary>Puts <paramref name="frameId"/> on the chip (null takes it off). <paramref name="frame"/> is the image a
    /// previous call made, or null; the answer is the one to keep for the next call.</summary>
    public static Image? Apply(Grid visual, Shape fill, Shape ring, Image? frame, string? frameId)
    {
        var box = double.IsNaN(visual.Width) ? visual.ActualWidth : visual.Width;
        var face = box - fill.Margin.Left - fill.Margin.Right;   // the picture's circle (28 of 34)
        var size = face / ShopCosmetics.FrameWindowRatio;
        var art = face > 0 ? CosmeticsCatalog.GetFrameImage(frameId, size) : null;
        if (art == null)
        {
            if (frame != null) frame.Visibility = Visibility.Collapsed;
            ring.Visibility = Visibility.Visible;
            return frame;
        }
        if (frame == null)
        {
            frame = new Image
            {
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = Tag,
            };
            visual.Children.Insert(visual.Children.IndexOf(ring) + 1, frame);
        }
        frame.Width = size;
        frame.Height = size;
        // Larger than the box without a layout clip, as AdornedAvatar and the friends rows do it.
        frame.Margin = new Thickness(-(size - box) / 2d);
        frame.Source = art;
        frame.Visibility = Visibility.Visible;
        ring.Visibility = Visibility.Collapsed;
        return frame;
    }
}

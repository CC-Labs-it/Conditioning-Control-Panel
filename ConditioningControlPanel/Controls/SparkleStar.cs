using System;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// The one Sparkles star (owner, 2026-11-17: Sparkles are one star picture everywhere). Set its
/// Height; the width follows the square picture. Decorative: it has no text and no automation peer of
/// its own, so the amount beside it stays the accessible name. The bitmap is decoded once, frozen and
/// shared by every star on screen.
/// </summary>
public sealed class SparkleStar : Image
{
    public const string PackUri = "pack://application:,,,/Resources/features/sparkle_star.png";

    private static readonly Lazy<ImageSource?> Picture = new(Load);

    public SparkleStar()
    {
        Source = Picture.Value;
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        Focusable = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    private static ImageSource? Load()
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(PackUri, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            // A missing picture must never take a wallet down with it: the amount still reads.
            return null;
        }
    }
}

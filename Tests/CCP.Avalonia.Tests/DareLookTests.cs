using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using Xunit;

namespace CCP.Avalonia.Tests;

public class DareLookTests
{
    [Theory]
    [InlineData(DareTone.Cant, 0xC22B2B)]
    [InlineData(DareTone.Duel, 0x2C8A57)]
    [InlineData(DareTone.Pics, 0xE0663A)]
    [InlineData(DareTone.Minutes, 0x1F8A8A)]
    [InlineData(DareTone.Video, 0xCC3D7A)]
    [InlineData(DareTone.Session, 0x8A3F8F)]
    [InlineData(DareTone.Bubbles, 0x2F86C9)]
    [InlineData(DareTone.Dare, 0xC22B2B)]
    [InlineData(DareTone.Sp, 0xB07A00)]
    [InlineData(DareTone.Lock, 0x33618F)]
    [InlineData(DareTone.CameraOn, 0x2C8A57)]
    [InlineData(DareTone.CameraOff, 0x7A6A8F)]
    [InlineData(DareTone.Lockdown, 0x3A1D78)]
    [InlineData(DareTone.When, 0x3A1D78)]
    [InlineData(DareTone.Who, 0x3A1D78)]
    public void RedesignedTonePaletteFollowsWpf(DareTone tone, int rgb)
    {
        Assert.Equal(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), DareLook.ToneColor(tone));
    }

    [Theory]
    [InlineData(DareTone.Pics, "picture")]
    [InlineData(DareTone.Minutes, "clock")]
    [InlineData(DareTone.When, "clock")]
    [InlineData(DareTone.Video, "video")]
    [InlineData(DareTone.Session, "play")]
    [InlineData(DareTone.Bubbles, "bubble")]
    [InlineData(DareTone.Sp, "star")]
    [InlineData(DareTone.Lock, "lock")]
    [InlineData(DareTone.Lockdown, "ldn")]
    [InlineData(DareTone.CameraOn, "eye")]
    [InlineData(DareTone.CameraOff, "eyeoff")]
    [InlineData(DareTone.Cant, "cross")]
    [InlineData(DareTone.Duel, "versus")]
    [InlineData(DareTone.Dare, "envelope")]
    [InlineData(DareTone.Who, "envelope")]
    public void RedesignedToneIconsFollowWpf(DareTone tone, string icon)
    {
        Assert.Equal(icon, DareLook.ToneIcon(tone));
    }

    [Theory]
    [InlineData(DareTone.Pics, "\uE8B9")]
    [InlineData(DareTone.Lockdown, "\uEA18")]
    [InlineData(DareTone.CameraOff, "\uED1A")]
    [InlineData(DareTone.Who, "\uE77B")]
    [InlineData(DareTone.When, "\uE787")]
    public void OriginalToneGlyphMappingsRemainAvailable(DareTone tone, string glyph)
    {
        Assert.Equal(glyph, DareLook.ToneGlyph(tone));
    }

    [Fact]
    public void ChoiceIsSquareCorneredAndInvokesAction()
    {
        var clicks = 0;
        var choice = DareLook.Choice("10", Colors.Red, "tip", () => clicks++, "choice");
        Assert.Equal(new CornerRadius(7), choice.CornerRadius);
        Assert.Equal(new Thickness(1, 1, 1, 4), choice.BorderThickness);
        Assert.Equal("tip", ToolTip.GetTip(choice));
        Assert.Equal("choice", choice.Tag);
        choice.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, clicks);
    }
}

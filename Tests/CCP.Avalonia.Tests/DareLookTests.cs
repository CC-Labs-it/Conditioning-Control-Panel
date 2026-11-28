using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Input;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

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
        Assert.Equal(icon, DareLook.ToneIconName(tone));
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

    [Theory]
    [InlineData(DareTone.Pics, IconKind.Image)]
    [InlineData(DareTone.Lockdown, IconKind.Shield)]
    [InlineData(DareTone.CameraOff, IconKind.EyeOff)]
    [InlineData(DareTone.Who, IconKind.Person)]
    [InlineData(DareTone.When, IconKind.Calendar)]
    public void OriginalToneIconsFollowWpf(DareTone tone, IconKind icon)
    {
        Assert.Equal(icon, DareLook.ToneIcon(tone));
    }

    [Fact]
    public void ChoiceIsSquareCorneredAndInvokesAction()
    {
        var clicks = 0;
        var choice = DareLook.Choice("10", Colors.Red, "tip", () => clicks++, "choice");
        var grid = Assert.IsType<Grid>(choice.Content);
        var shadow = Assert.IsType<Border>(grid.Children[0]);
        var face = Assert.IsType<Border>(grid.Children[1]);
        var text = Assert.IsType<TextBlock>(face.Child);
        Assert.Equal(new CornerRadius(7), face.CornerRadius);
        Assert.Equal(new Thickness(1, 1, 1, 4), face.BorderThickness);
        Assert.Equal(new Thickness(14, 6, 14, 5), face.Padding);
        Assert.Equal(Color.Parse("#F7EEF8"), Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
        Assert.Equal(Color.Parse("#DCC6DD"), Assert.IsAssignableFrom<ISolidColorBrush>(face.BorderBrush).Color);
        Assert.Equal(19, text.FontSize);
        Assert.Equal(FontWeight.Bold, text.FontWeight);
        Assert.Equal(Color.Parse("#2E183A"), Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
        Assert.Equal(new Thickness(8, 6, 8, 1), shadow.Margin);
        Assert.False(shadow.IsHitTestVisible);
        Assert.Equal(BoxShadows.Parse("0 3 7 0 #66000000"), shadow.BoxShadow);
        void Hover(bool on) => choice.RaiseEvent(new PointerEventArgs(
            on ? InputElement.PointerEnteredEvent : InputElement.PointerExitedEvent, choice,
            new Pointer(1, PointerType.Mouse, true), choice, default, 0, new PointerPointProperties(), KeyModifiers.None));
        Hover(true);
        Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
        Hover(false);
        Assert.Equal(Color.Parse("#F7EEF8"), Assert.IsAssignableFrom<ISolidColorBrush>(face.Background).Color);
        Assert.Equal("tip", ToolTip.GetTip(choice));
        Assert.Equal("choice", choice.Tag);
        choice.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, clicks);
    }
}

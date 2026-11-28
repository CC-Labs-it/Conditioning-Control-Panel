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
    [InlineData(DareTone.Pics, 0xFF9A5C, IconKind.Image)]
    [InlineData(DareTone.Lockdown, 0x9C7BFF, IconKind.ShieldLock)]
    [InlineData(DareTone.CameraOff, 0x6F629A, IconKind.EyeOff)]
    [InlineData(DareTone.Who, 0xB99CFF, IconKind.Mail)]
    [InlineData(DareTone.When, 0xA395C4, IconKind.Clock)]
    public void TonePaletteAndIconFollowWpf(DareTone tone, int rgb, IconKind icon)
    {
        Assert.Equal(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), DareLook.ToneColor(tone));
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

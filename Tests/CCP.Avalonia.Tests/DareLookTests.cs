using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
        Assert.Equal(new CornerRadius(7), choice.CornerRadius);
        Assert.Equal(new Thickness(1, 1, 1, 4), choice.BorderThickness);
        Assert.Equal("tip", ToolTip.GetTip(choice));
        Assert.Equal("choice", choice.Tag);
        choice.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, clicks);
    }
}

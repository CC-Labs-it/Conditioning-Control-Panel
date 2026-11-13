using System;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class CompanionPortraitFadeTests
{
    [Fact]
    public void PortraitFadeHonoursMotionAndMissingImage() => AvaloniaTestDispatcher.Run(() =>
    {
        var s = CoreSettings.Current;
        var old = s.MotionLevel;
        try
        {
            var card = new CompanionPickerCard();
            var image = card.FindControl<Image>("ImgPreview")!;
            s.MotionLevel = MotionLevel.Full;
            card.ShowPortrait(new DrawingImage());
            var transition = Assert.IsType<DoubleTransition>(Assert.Single(image.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(160), transition.Duration);
            s.MotionLevel = MotionLevel.Off;
            card.ShowPortrait(new DrawingImage());
            Assert.Null(image.Transitions);
            Assert.Equal(1, image.Opacity);
            s.MotionLevel = MotionLevel.Full;
            card.ShowPortrait(null);
            Assert.Null(image.Transitions);
            Assert.Equal(1, image.Opacity);
        }
        finally { s.MotionLevel = old; }
    });
}

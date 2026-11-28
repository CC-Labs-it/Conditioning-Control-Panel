using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AvatarPoseFadeTests
{
    [Fact]
    public void PoseFadesBeforeSwapping() => AvaloniaTestDispatcher.Run(() =>
    {
        var tube = new AvatarTubeWindow();
        try
        {
            var image = tube.FindControl<Image>("ImgAvatar")!;
            var clock = new TestAnimationClock();
            clock.Attach(image);
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var first = image.Source;
            Assert.NotNull(first);
            tube.AdvancePose();
            clock.Step(0);
            clock.Step(75);
            Assert.InRange(image.Opacity, 0.64, 0.66);
            Assert.Same(first, image.Source);
            clock.Step(149);
            Assert.InRange(image.Opacity, 0.30, 0.32);
            clock.Step(151);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, image.Opacity, 3);
            Assert.NotSame(first, image.Source);
        }
        finally { tube.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}

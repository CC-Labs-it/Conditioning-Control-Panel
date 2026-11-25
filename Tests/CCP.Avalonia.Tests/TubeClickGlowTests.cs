using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeClickGlowTests
{
    [Fact]
    public Task ClickPulsesAndPanicRestoresTheAvatarShadow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settings = CoreSettings.Current;
        var enabled = settings.AvatarEnabled;
        settings.AvatarEnabled = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tube = Assert.IsType<ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow>(shell.Tube);
            var image = tube.FindControl<Image>("ImgAvatar")!;
            // Use one known static layer regardless of the saved avatar or Rain settings.
            foreach (var name in new[] { "ImgAvatarB", "ImgAvatarAnimated", "ImgAvatarAnimatedB" })
                tube.FindControl<Image>(name)!.IsVisible = false;
            image.IsVisible = true;
            image.Opacity = 1;
            using var bitmap = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96));
            image.Source = bitmap;
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            var original = Assert.IsType<DropShadowEffect>(image.Effect);
            tube.HandleAvatarClick();
            var glow = Assert.IsType<DropShadowEffect>(image.Effect);
            Assert.NotSame(original, glow);
            tube.StepAvatarGlow(200);
            Assert.Equal(60, glow.BlurRadius);
            Assert.Equal(1, glow.Opacity);
            tube.StepAvatarGlow(300);
            Assert.Equal(40, glow.BlurRadius);
            Assert.Equal(0.8, glow.Opacity, 2);
            tube.StepAvatarGlow(400);
            Assert.Same(original, image.Effect);

            tube.HandleAvatarClick();
            tube.StepAvatarGlow(200);
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(shell);
            Assert.Same(original, image.Effect);
            tube.StepAvatarGlow(200);
            Assert.Same(original, image.Effect);

            // A new click is user initiated, so panic's trigger quiet must not suppress it.
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Assert.True(ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced());
            tube.HandleAvatarClick();
            var afterPanic = Assert.IsType<DropShadowEffect>(image.Effect);
            Assert.NotSame(original, afterPanic);
            tube.StepAvatarGlow(200);
            Assert.Equal(60, afterPanic.BlurRadius);
            Assert.Equal(1, afterPanic.Opacity);
            tube.StepAvatarGlow(400);
            Assert.Same(original, image.Effect);

            tube.HandleAvatarClick();
            tube.StepAvatarGlow(200);
            PanicSurfaces.All.Single(x => x.Id == "tube-speech").Stop(shell);
            Assert.Same(original, image.Effect);
        }
        finally
        {
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            shell.MarkExitRequestedForTest();
            shell.Close();
            settings.AvatarEnabled = enabled;
        }
        return Task.CompletedTask;
    });
}

using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class TubeFloatingTests
{
    // WPF Windowing.cs:746-750: attached and detached legacy sprites use the same bob.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SpriteBobsWithoutMovingTheTubeAndStopsOnClose(bool detached) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.AvatarTubeDetached = detached;
        var shell = new Window { Width = 1000, Height = 700, Position = new PixelPoint(2000, 200) };
        var tube = new AvatarTubeWindow(shell);
        try
        {
            shell.Show();
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var sprite = tube.FindControl<Image>("ImgAvatar")!;
            var bob = Assert.IsType<TranslateTransform>(sprite.RenderTransform);
            tube.StartFloatingAnimation(); // reset the phase after layout jobs, as WPF ShowTube does
            var position = tube.Position;
            // Starting/restarting the reply bounce must not replace the sprite's transform.
            tube.PlayDoubleBounce();
            // Sample a full wave, including both peaks and the return through neutral.
            var positive = false;
            var negative = false;
            for (var i = 1; i <= 126; i++)
            {
                tube.TickFloatingAnimation();
                Assert.Equal(Math.Sin(i * 0.05) * 4, bob.Y, 8);
                positive |= bob.Y > 3.99;
                negative |= bob.Y < -3.99;
            }
            Assert.True(positive && negative);
            Assert.Same(bob, sprite.RenderTransform);
            Assert.Equal(position, tube.Position);
            tube.Close();
            Assert.Equal(0, bob.Y);
            tube.TickFloatingAnimation(); // a late tick cannot revive a closed sprite
            Assert.Equal(0, bob.Y);
        }
        finally { tube.Close(); shell.Close(); }
        return Task.CompletedTask;
    });
}

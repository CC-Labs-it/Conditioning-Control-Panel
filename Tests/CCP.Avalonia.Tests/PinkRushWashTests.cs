using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PinkRushWashTests
{
    [Fact]
    public async Task WashUsesNativeAlphaKeepsOnePaintAndClosesOnTimeAndPanic()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var saved = (PinkRushWash.Clock, PinkRushWash.After, PinkRushWash.SetAlpha);
            long clock = 1000;
            var pending = new Queue<Action>();
            var alphas = new List<double>();
            PinkRushWash.Clock = () => clock;
            PinkRushWash.After = (delay, next) =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(33), delay);
                pending.Enqueue(next);
            };
            PinkRushWash.SetAlpha = (_, alpha) => { alphas.Add(alpha); return true; };
            try
            {
                var window = new TintOverlayWindow();
                PinkRushWash.Start(window);
                Assert.True(window.IsVisible);
                Assert.Equal(60 / 255.0, Assert.Single(alphas));
                Assert.Equal(1, window.Opacity);
                Assert.True(window.Transitions is null || window.Transitions.Count == 0);
                clock += 250;
                pending.Dequeue()();
                Assert.Equal(30 / 255.0, alphas[1]);
                Assert.Equal(1, window.Opacity);
                clock += 250;
                pending.Dequeue()();
                Assert.False(window.IsVisible);
                Assert.Empty(pending);

                var second = new TintOverlayWindow();
                PinkRushWash.Start(second);
                Assert.True(second.IsVisible);
                PinkRushHost.Stop(); // existing pink-rush PanicSurfaces entry owns the wash too
                Assert.False(second.IsVisible);
                var count = alphas.Count;
                pending.Dequeue()();
                Assert.Equal(count, alphas.Count); // no native request against a closed surface

                PinkRushWash.SetAlpha = (_, _) => false;
                var refused = new TintOverlayWindow();
                PinkRushWash.Start(refused);
                Assert.False(refused.IsVisible);
                Assert.Empty(pending);

                var accepts = true;
                PinkRushWash.SetAlpha = (_, _) => accepts;
                var lost = new TintOverlayWindow();
                PinkRushWash.Start(lost);
                accepts = false;
                pending.Dequeue()();
                Assert.False(lost.IsVisible);
                Assert.Empty(pending);
            }
            finally
            {
                PinkRushWash.CloseAll();
                (PinkRushWash.Clock, PinkRushWash.After, PinkRushWash.SetAlpha) = saved;
            }
            return Task.CompletedTask;
        });
    }
}

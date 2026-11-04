using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ScreenShakeTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void VoiceGrammarOffersShake() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        try { Assert.Contains("shake the screen", shell.VoiceCmds.Grammar()); }
        finally { shell.Close(); }
    });

    [Fact]
    public void RepeatedShakeClampsAndRestoresTransforms() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var prior = new ScaleTransform(0.8, 0.8);
        var root = new Border { RenderTransform = prior };
        var window = new Window { Content = root };
        var service = new ScreenShakeService();
        window.Show();
        try
        {
            service.Start(new[] { window }, 60, 1200);
            var first = Assert.IsType<TranslateTransform>(root.RenderTransform);
            for (var i = 0; i < 100; i++)
            {
                service.Tick();
                Assert.InRange(first.X, -28, 28);
                Assert.InRange(first.Y, -28, 28);
            }
            service.Start(new[] { window }, 60, 1200);
            Assert.NotSame(first, root.RenderTransform);
            service.Reset();
            Assert.Same(prior, root.RenderTransform);
        }
        finally { service.Reset(); window.Close(); }
    });
}

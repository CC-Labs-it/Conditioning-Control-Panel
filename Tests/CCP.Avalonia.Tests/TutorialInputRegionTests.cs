using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class TutorialInputRegionTests
{
    [Fact]
    public Task SpotlightLetsTargetThroughBlocksDimAndManualStepRestoresAllInput() => Run(overlay =>
    {
        Draw(overlay, "DrawSpotlightOverlay", new Rect(100, 100, 200, 50), true);
        Assert.NotNull(overlay.InputRectangles);
        Assert.False(Hits(overlay, 150, 125)); // the WPF target rect, not the padded visual glow
        Assert.True(Hits(overlay, 99, 125));
        Assert.True(Hits(overlay, 300, 125)); // half-open right edge
        Draw(overlay, "DrawSpotlightOverlay", new Rect(100, 100, 200, 50), false);
        Assert.Null(overlay.InputRectangles);
        Draw(overlay, "DrawFullOverlay", true);
        Assert.Null(overlay.InputRectangles);
    });

    [Fact]
    public Task TransparentWaitLetsBackgroundThroughAndKeepsSkipButtonsClickable() => Run(overlay =>
    {
        Draw(overlay, "DrawFullOverlay", false);
        overlay.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var card = overlay.FindControl<Border>("TextPanel")!;
        var p = card.TranslatePoint(default, overlay)!.Value;
        Assert.NotNull(overlay.InputRectangles);
        Assert.Single(overlay.InputRectangles!);
        Assert.True(Hits(overlay, (int)p.X + 10, (int)p.Y + 10));
        Assert.False(Hits(overlay, 0, 0));
        Draw(overlay, "DrawFullOverlay", true);
        Assert.Null(overlay.InputRectangles);
    });

    private static bool Hits(TutorialOverlay overlay, int x, int y)
        => Win32Overlay.Hits(overlay.InputRectangles!, overlay.InputRectangles!.Length, x, y);

    private static void Draw(TutorialOverlay overlay, string method, params object[] args)
        => typeof(TutorialOverlay).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, args);

    private static Task Run(Action<TutorialOverlay> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var overlay = new TutorialOverlay { Width = 1000, Height = 700 };
        try
        {
            overlay.Show();
            overlay.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            body(overlay);
        }
        finally { overlay.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });
}

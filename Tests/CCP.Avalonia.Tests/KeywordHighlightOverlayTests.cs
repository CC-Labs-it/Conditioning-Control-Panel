using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class KeywordHighlightOverlayTests
{
    private sealed class Lease(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    [Fact]
    public Task CaptureVisibilityChangesExistingWindowsImmediately() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var screens = KeywordHighlightOverlay.Screens;
        var configure = KeywordHighlightOverlay.Configure;
        var exclude = KeywordHighlightOverlay.ExcludeFromCapture;
        var settings = CoreSettings.Current;
        var enabled = settings.KeywordHighlightEnabled;
        var visible = settings.OcrHighlightVisibleInCapture;
        var acquired = 0; var released = 0;
        try
        {
            KeywordHighlightOverlay.Screens = () => [new(new PixelRect(0, 0, 500, 500), 1), new(new PixelRect(500, 0, 500, 500), 1)];
            KeywordHighlightOverlay.Configure = (_, _) => true;
            KeywordHighlightOverlay.ExcludeFromCapture = _ => { acquired++; return new Lease(() => released++); };
            settings.KeywordHighlightEnabled = true; settings.OcrHighlightVisibleInCapture = true;
            KeywordHighlightOverlay.Show([new("a", new(10, 10, 10, 10)), new("b", new(510, 10, 10, 10))]);
            Assert.Equal(2, KeywordHighlightOverlay.WindowCount);
            Assert.Equal(0, acquired);
            settings.OcrHighlightVisibleInCapture = false;
            Assert.Equal(2, acquired); Assert.Equal(0, released);
            settings.OcrHighlightVisibleInCapture = true;
            Assert.Equal(2, released);
            settings.OcrHighlightVisibleInCapture = false;
            Assert.Equal(4, acquired);
            Assert.Equal(2, KeywordHighlightOverlay.WindowCount);
            KeywordHighlightOverlay.CloseAll();
            Assert.Equal(4, released);
            settings.OcrHighlightVisibleInCapture = true;
            Assert.Equal(4, released);
        }
        finally
        {
            KeywordHighlightOverlay.CloseAll(); KeywordHighlightOverlay.Screens = screens;
            KeywordHighlightOverlay.Configure = configure; KeywordHighlightOverlay.ExcludeFromCapture = exclude;
            settings.KeywordHighlightEnabled = enabled; settings.OcrHighlightVisibleInCapture = visible;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BoxesUseEachScreenScaleAndPanicClosesThem() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var screens = KeywordHighlightOverlay.Screens;
        var configure = KeywordHighlightOverlay.Configure;
        var enabled = CoreSettings.Current.KeywordHighlightEnabled;
        try
        {
            KeywordHighlightOverlay.Screens = () => [new(new PixelRect(0, 0, 1000, 800), 1), new(new PixelRect(1000, 0, 1500, 1200), 1.5)];
            KeywordHighlightOverlay.Configure = (_, _) => true;
            CoreSettings.Current.KeywordHighlightEnabled = true;
            var words = new[] { new ScreenOcrService.Word("a", new(100, 60, 30, 15)), new ScreenOcrService.Word("b", new(1150, 90, 45, 30)) };
            KeywordHighlightOverlay.Show(words);
            Assert.Equal(2, KeywordHighlightOverlay.WindowCount);
            var boxes = KeywordHighlightOverlay.Canvases.Select(c => Assert.Single(c.Children)).ToArray();
            Assert.Equal(90, Canvas.GetLeft(boxes[0])); Assert.Equal(50, Canvas.GetTop(boxes[0]));
            Assert.Equal(90, Canvas.GetLeft(boxes[1])); Assert.Equal(50, Canvas.GetTop(boxes[1]));
            Assert.Equal(50, boxes[0].Width); Assert.Equal(50, boxes[1].Width);
            PanicSurfaces.All.Single(x => x.Id == "keyword-highlights").Stop(null);
            Assert.Equal(0, KeywordHighlightOverlay.WindowCount);
            CoreSettings.Current.KeywordHighlightEnabled = false;
            KeywordHighlightOverlay.Show(words);
            Assert.Equal(0, KeywordHighlightOverlay.WindowCount);
            KeywordHighlightOverlay.Show(null);
        }
        finally
        {
            KeywordHighlightOverlay.CloseAll(); KeywordHighlightOverlay.Screens = screens;
            KeywordHighlightOverlay.Configure = configure; CoreSettings.Current.KeywordHighlightEnabled = enabled;
        }
        return Task.CompletedTask;
    });
}

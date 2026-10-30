using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class QuestClipboardArtTests
{
    [Fact]
    public Task ClipboardPaintsTheLoopAndReleasesItsClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = MotionLevel.Full;
        settings.Current.PerformanceMode = false;
        var art = new QuestsArtView(new Dictionary<string, int> { ["done"] = 5, ["total"] = 7 });
        Assert.Equal(5, art.Shown);
        Assert.Equal(3, art.DoneShown);
        var window = new Window { Width = 600, Height = 340, Content = art };
        try
        {
            window.Show();
            art.Run();
            Assert.True(art.Running);
            art.Hold();
            Assert.False(art.Running);
            foreach (var size in new[] { new Size(600, 340), new Size(1000, 630) })
            {
                art.Measure(size);
                art.Arrange(new Rect(size));
                byte[]? first = null;
                bool changed = false;
                for (double t = 0; t < 13; t += 0.2)
                {
                    art.Frame(t);
                    using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(size, 1));
                    bitmap.Render(art);
                    using var png = new MemoryStream();
                    bitmap.Save(png);
                    var frame = png.ToArray();
                    first ??= frame;
                    changed |= !first.AsSpan().SequenceEqual(frame);
                }
                Assert.True(changed, "The clipboard story must change across the loop.");
            }
            settings.Current.MotionLevel = MotionLevel.Off;
            art.Run();
            Assert.False(art.Running);
            settings.Current.MotionLevel = MotionLevel.Full;
            art.Release();
            art.Run();
            Assert.False(art.Running);
        }
        finally { art.Release(); window.Close(); }
        return Task.CompletedTask;
    });
}

using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FirstShowArtTests
{
    [Fact]
    public Task PictureHasSoftPinkHaloInsteadOfAStroke() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        var settings = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => settings;
        var path = Path.Combine(Path.GetTempPath(), $"first-show-{Guid.NewGuid()}.png");
        using (var bitmap = new SKBitmap(100, 100))
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(20, 80, 220));
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);
        }
        var fx = new FirstShowEffects(_ => { }) { Width = 1000, Height = 700, Particles = () => false };
        var host = new Window { Width = fx.Width, Height = fx.Height, Content = fx };
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            await fx.LoadAsync(new[] { path });
            fx.Tick(2, 0);
            fx.Tick(2.31, .1);
            host.Show();
            Dispatcher.UIThread.RunJobs();
            using var target = new RenderTargetBitmap(new PixelSize(1000, 700));
            target.Render(fx);
            using var stream = new MemoryStream();
            target.Save(stream);
            if (Environment.GetEnvironmentVariable("CCP_FIRST_SHOW_ART_PNG") is { Length: > 0 } output) File.WriteAllBytes(output, stream.ToArray());
            using var shot = SKBitmap.Decode(stream.ToArray());
            int blueLeft = shot.Width, blueRight = 0, blueTop = shot.Height, blueBottom = 0;
            for (int y = 0; y < shot.Height; y++)
                for (int x = 0; x < shot.Width; x++)
                {
                    var p = shot.GetPixel(x, y);
                    if (p.Blue > 190 && p.Red < 40 && p.Alpha > 240)
                    { blueLeft = Math.Min(blueLeft, x); blueRight = Math.Max(blueRight, x); blueTop = Math.Min(blueTop, y); blueBottom = Math.Max(blueBottom, y); }
                }
            Assert.True(blueRight > blueLeft + 100 && blueBottom > blueTop + 100, "The picture must be visible.");
            // At the middle of the top edge, the image starts 4 px inside the actor box.
            // The halo extends beyond that box, while a 4 px solid border does not.
            int mid = (blueLeft + blueRight) / 2;
            var outer = shot.GetPixel(mid, blueTop - 6);
            Assert.True(outer.Alpha > 3 && outer.Red > outer.Green && outer.Blue > outer.Green, $"Missing pink halo: {outer}");
            var edge = shot.GetPixel(mid, blueTop - 2);
            Assert.InRange((int)edge.Alpha, 5, 170);
        }
        finally
        {
            host.Content = null; host.Close(); fx.Dispose();
            settings.SaveImmediate(); CoreSettings.ServiceProvider = old;
            File.Delete(path);
        }
    });
}

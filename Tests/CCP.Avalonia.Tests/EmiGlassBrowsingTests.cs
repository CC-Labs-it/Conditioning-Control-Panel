using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassBrowsingTests
{
    [Theory]
    [InlineData(60)]
    [InlineData(175)]
    public Task PagesRenderAndTicksAdvanceTheirBeats(int width) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        int height = (int)(width * .91);
        var layer = new Canvas { Width = width, Height = height, ClipToBounds = true };
        var window = new Window { Width = width, Height = height, Content = layer };
        var painter = new BrowsingPainter(width, height);
        try
        {
            painter.Attach(layer);
            window.Show();
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int slot = 0; slot < 6; slot++)
            {
                double t = slot * 2500;
                painter.Tick(t);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var page = Assert.Single(layer.Children.OfType<Canvas>().Where(c => c.IsVisible));
                var title = Assert.IsType<TextBlock>(page.Children[1]);
                Assert.False(string.IsNullOrEmpty(title.Text));
                seen.Add(title.Text!);
                Assert.IsType<Rectangle>(page.Children[0]);
                using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
                bitmap.Render(layer);
                var pixels = new byte[width * height * 4];
                var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { bitmap.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), pixels.Length, width * 4); }
                finally { pin.Free(); }
                Assert.True(pixels.Where((_, i) => i % 4 == 3).Count(a => a > 0) > width * height / 2);
                int ink = 0;
                for (int y = 0; y < Math.Min(height, (int)Math.Ceiling(title.Bounds.Bottom)); y++)
                    for (int x = 0; x < Math.Min(width, (int)Math.Ceiling(title.Bounds.Right)); x++)
                    {
                        int i = (y * width + x) * 4;
                        if (pixels[i] < 80 && pixels[i + 1] < 80 && pixels[i + 2] < 80) ink++;
                    }
                Assert.True(ink > 0, $"{title.Text} title ink");
                if (title.Text == "BOARD")
                {
                    var row = Assert.IsType<Rectangle>(page.Children[8]);
                    var before = row.Fill;
                    painter.Tick(t + 500);
                    Assert.NotSame(before, row.Fill);
                }
                if (title.Text == "DEEP")
                {
                    var trace = page.Children.OfType<Polyline>().Single();
                    var before = trace.Points!.ToArray();
                    painter.Tick(t + 1000);
                    Assert.False(before.SequenceEqual(trace.Points!));
                }
                if (title.Text == "BANK")
                {
                    var balance = page.Children.OfType<TextBlock>().Single(x => x.Text == "BALANCE");
                    var number = Assert.IsType<TextBlock>(page.Children[3]);
                    string? before = number.Text;
                    painter.Tick(t + 1500);
                    Assert.NotEqual(before, number.Text);
                    Assert.True(page.Children.OfType<TextBlock>().Single(x => x.Text == "-14").IsVisible);
                    Assert.True(balance.IsVisible);
                }
            }
            Assert.Equal(new[] { "BANK", "BOARD", "CARDS", "DEEP", "MAIL", "REC" }, seen.OrderBy(x => x));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public Task BrowsingPagesAdvanceAtEveryMotionLevel(MotionLevel motion) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var bedtime = EmiState.Current.BedtimeUntil;
        var desk = new EmiDeskWindow { GlassBrowsingOverride = true };
        try
        {
            CoreSettings.Current.EmiDeskGlass = true;
            CoreSettings.Current.MotionLevel = motion;
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip);
            desk.AdvanceGlass(flip.AddMilliseconds(220));
            var layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            var page = Assert.Single(layer.Children.OfType<Canvas>().Where(c => c.IsVisible));
            desk.AdvanceGlass(flip.AddMilliseconds(5220));
            Assert.NotSame(page, Assert.Single(layer.Children.OfType<Canvas>().Where(c => c.IsVisible)));
        }
        finally
        {
            desk.ShutDown(); EmiState.Current.BedtimeUntil = bedtime; CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}

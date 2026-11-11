using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Avalonia.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiGlassShopTests
{
    [Fact]
    public Task IdleGlassCanLandOnShopAndActivityClosesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var choice = EmiDeskWindow.GlassChooseShop;
        var bedtime = EmiState.Current.BedtimeUntil;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        EmiDeskWindow.GlassChooseShop = () => true;
        var desk = new EmiDeskWindow();
        try
        {
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            CoreSettings.Current.EmiDeskGlass = true;
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var flip = DateTime.UtcNow.AddSeconds(61);
            desk.PollGlass(flip); desk.AdvanceGlass(flip.AddMilliseconds(220));
            var layer = Assert.IsType<Canvas>(Assert.Single(desk.GlassHost.Children));
            Assert.Contains(layer.Children.OfType<TextBlock>(), t => t.Text == "SHOP AT HOME");
            desk.NoteGlassActivity();
            Assert.False(desk.GlassIsLive);
            Assert.Empty(desk.GlassHost.Children);
        }
        finally
        {
            desk.ShutDown(); EmiState.Current.BedtimeUntil = bedtime;
            EmiDeskWindow.GlassChooseShop = choice; CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ShopStepsAndFitsSmallGlass() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var canvas = new Canvas { Width = 60, Height = 60 * 137.0 / 152 };
        var shop = new ShopPainter(canvas, 0);
        var text = canvas.Children.OfType<TextBlock>().ToArray();
        var body = canvas.Children.OfType<Rectangle>().ElementAt(3);
        var foot = canvas.Children.OfType<Rectangle>().Last();
        double full = body.Width;
        shop.Tick(219); Assert.Equal(full, body.Width);
        shop.Tick(220); Assert.True(body.Width < full);
        shop.Tick(440); Assert.True(body.Width < full * .6);
        shop.Tick(0);
        int previous = 480;
        for (int i = 1; i <= 100; i++)
        {
            shop.Tick(i * 90 - 1);
            Assert.Equal(previous, int.Parse(text[2].Text!.Split(' ')[0]));
            shop.Tick(i * 90);
            int next = int.Parse(text[2].Text!.Split(' ')[0]);
            Assert.True(next <= previous);
            if (previous > 9) Assert.True(next < previous);
            previous = next;
        }
        shop.Tick(0); var lit = foot.Fill!.ToString();
        shop.Tick(499); Assert.Equal(lit, foot.Fill!.ToString());
        shop.Tick(500); Assert.NotEqual(lit, foot.Fill!.ToString());
        canvas.Measure(new Size(60, canvas.Height)); canvas.Arrange(new Rect(0, 0, 60, canvas.Height));
        for (int i = 0; i < text.Length - 1; i++)
            Assert.True(Canvas.GetTop(text[i]) + text[i].Bounds.Height <= Canvas.GetTop(text[i + 1]) + .01);
        using var bitmap = new RenderTargetBitmap(new PixelSize(60, 55));
        bitmap.Render(canvas);
        return Task.CompletedTask;
    });
}

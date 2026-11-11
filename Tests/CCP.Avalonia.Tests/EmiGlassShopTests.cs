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
using ConditioningControlPanel.Localization;
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
        var foot = canvas.Children.OfType<Rectangle>().ElementAt(8);
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
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task ProductCutoutsFollowTheTurnAndScanlinesCoverTheGlass(int item) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var canvas = new Canvas { Width = 152, Height = 137 };
        var shop = new ShopPainter(canvas, item);
        var boxes = canvas.Children.OfType<Rectangle>().ToArray();
        var body = boxes[3];
        int count = item == 0 ? 4 : 1;
        var cuts = boxes.Skip(4).Take(count).ToArray();
        Assert.All(cuts, cut => Assert.True(cut.IsVisible));
        double left = Canvas.GetLeft(body);
        if (item == 0) Assert.Equal(left + 4, Canvas.GetLeft(cuts[0]), 5);
        else if (item == 1) Assert.Equal(body.Width - 8, cuts[0].Width, 5);
        else Assert.Equal(body.Width / 2, cuts[0].Width, 5);
        shop.Tick(220);
        Assert.Equal(item == 0 ? 2 : 1, cuts.Count(c => c.IsVisible));
        if (item != 0) Assert.True(cuts[0].Width < (item == 1 ? 22 : 18));
        shop.Tick(440);
        Assert.All(cuts, cut => Assert.False(cut.IsVisible));
        shop.Tick(660);
        Assert.Equal(item == 0 ? 2 : 1, cuts.Count(c => c.IsVisible));

        // Lines are above all content, non-interactive, and cover the full height.
        var lines = boxes.Skip(5 + count).ToArray();
        Assert.Equal(69, lines.Length);
        Assert.Same(lines[0], canvas.Children[canvas.Children.Count - lines.Length]);
        for (int i = 0; i < lines.Length; i++)
        {
            Assert.Equal(i * 2, Canvas.GetTop(lines[i]));
            Assert.Equal(152, lines[i].Width);
            Assert.Equal(.18, lines[i].Opacity);
            Assert.False(lines[i].IsHitTestVisible);
        }
        foreach (double width in new[] { 60.0, 175.0 })
        {
            double height = width * 137 / 152;
            double pitch = Math.Max(2, 2 * width / 152);
            var scaled = new Canvas { Width = width, Height = height };
            _ = new ShopPainter(scaled, item);
            var scaledLines = scaled.Children.OfType<Rectangle>().Skip(5 + count).ToArray();
            Assert.Equal((int)Math.Ceiling(height / pitch), scaledLines.Length);
            Assert.Equal(pitch, Canvas.GetTop(scaledLines[1]), 5);
            Assert.Equal(pitch / 2, scaledLines[0].Height, 5);
            Assert.Equal(width, scaledLines[0].Width);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ShopLabelsUseAllTenLanguagesAndFitSmallGlass() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            var expected = new (string Lang, string Title, string Call, string Card, string Watch, string Disc)[]
            {
                ("en", "SHOP AT HOME", "CALL NOW", "CARD", "WATCH", "DISC"),
                ("de", "ZU HAUSE SHOPPEN", "JETZT ANRUFEN", "KARTE", "UHR", "SCHEIBE"),
                ("es", "COMPRA EN CASA", "LLAMA YA", "TARJETA", "RELOJ", "DISCO"),
                ("fr", "ACHETEZ CHEZ VOUS", "APPELEZ", "CARTE", "MONTRE", "DISQUE"),
                ("it", "ACQUISTA DA CASA", "CHIAMA ORA", "CARTA", "OROLOGIO", "DISCO"),
                ("ja", "おうちで買い物", "今すぐ電話", "カード", "腕時計", "ディスク"),
                ("ko", "집에서 쇼핑", "지금 전화", "카드", "시계", "디스크"),
                ("pt-BR", "COMPRE EM CASA", "LIGUE AGORA", "CARTÃO", "RELÓGIO", "DISCO"),
                ("ru", "МАГАЗИН НА ДОМУ", "ЗВОНИТЕ", "КАРТА", "ЧАСЫ", "ДИСК"),
                ("zh-CN", "居家购物", "立即致电", "卡片", "手表", "光盘")
            };
            foreach (var e in expected)
            {
                LocalizationManager.Instance.SetLanguage(e.Lang);
                string[] names = { e.Card, e.Watch, e.Disc };
                for (int item = 0; item < 3; item++)
                {
                    var canvas = new Canvas { Width = 60, Height = 60 * 137.0 / 152 };
                    _ = new ShopPainter(canvas, item);
                    var text = canvas.Children.OfType<TextBlock>().ToArray();
                    Assert.Equal(e.Title, text[0].Text);
                    Assert.Equal(names[item], text[1].Text);
                    Assert.Equal(e.Call, text[3].Text);
                    foreach (var line in text)
                    {
                        var probe = new TextBlock { Text = line.Text, FontFamily = line.FontFamily, FontSize = line.FontSize };
                        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Assert.True(probe.DesiredSize.Width <= 60, $"{e.Lang}: {line.Text}");
                    }
                    canvas.Measure(new Size(60, canvas.Height)); canvas.Arrange(new Rect(0, 0, 60, canvas.Height));
                    for (int i = 0; i < text.Length - 1; i++)
                        Assert.True(Canvas.GetTop(text[i]) + text[i].Bounds.Height <= Canvas.GetTop(text[i + 1]) + .01, e.Lang);
                }
            }
        }
        finally { LocalizationManager.Instance.SetLanguage(old); }
        return Task.CompletedTask;
    });

}

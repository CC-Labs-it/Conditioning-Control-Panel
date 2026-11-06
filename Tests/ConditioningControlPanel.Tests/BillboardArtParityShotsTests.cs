using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Controls.Billboard;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The WPF half of the billboard art parity shots (Linux beta fix2): with CCP_BILLBOARD_PARITY_SHOTS
/// set to a folder, every drawn art is written there as wpf-{name}-{t}.png at 1000 x 420 on the
/// card well colour, at the same moments CCP.Avalonia.Tests BillboardArtParityTests writes port-*.png.
/// A look check for the port, not a judge. Does nothing otherwise.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class BillboardArtParityShotsTests
{
    internal static readonly double[] Times = { 0.6, 1.6, 3.1, 5.3 };

    private static readonly (string Name, Func<BillboardVectorArt> Make, string Hue)[] Scenes =
    {
        ("discord", () => new DiscordHouseArt(), "#7b86ff"),
        ("webapp", () => new WebAppHouseArt(), "#5fe3ff"),
        ("remix", () => new RemixHouseArt(), "#ff4fa8"),
        ("loom", () => new LoomHouseArt(), "#9b7bff"),
        ("support", () => new SupportHouseArt(), "#ff4fa8"),
        ("wheel", () => new WheelArtView(), "#ffc94a"),
        ("tip-quests", () => new QuestsTipArt(), "#3ce67d"),
        ("tip-awareness", () => new AwarenessTipArt(), "#5fe3ff"),
        ("tip-programs", () => new ProgramsTipArt(), "#ffc94a"),
        ("tip-deeper", () => new DeeperTipArt(), "#9b7bff"),
        ("tip-blink", () => new BlinkTipArt(), "#ff4fa8"),
        ("tip-haptics", () => new HapticsTipArt(), "#ff4fa8"),
        ("tip-folders", () => new FoldersTipArt(), "#ffc94a"),
        ("tip-lockdown", () => new LockdownTipArt(), "#ff4fa8"),
        ("tip-remote", () => new RemoteTipArt(), "#5fe3ff"),
        ("tip", () => new TipArtView(), "#9b7bff"),
        ("invite", () => new InviteArtView(), "#ffc94a"),
        ("spiral", () => new SpiralArtView(null), "#ff4fa8"),
        ("calendar", () => new CalendarArtView(BuiltInArtTestData.Calendar()), "#ff4fa8"),
        ("tables", () => new TablesArtView(new[] { "Velvet", "mort5366", "Pika" }, 3), "#5fe3ff"),
    };

    [Fact]
    public void Shots_when_asked() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CCP_BILLBOARD_PARITY_SHOTS");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        var only = Environment.GetEnvironmentVariable("CCP_BILLBOARD_PARITY_ONLY");
        foreach (var s in Scenes)
        {
            if (!string.IsNullOrEmpty(only) && Array.IndexOf(only.Split(','), s.Name) < 0) continue;
            var view = s.Make();
            view.Accent = BillboardVectorArt.ParseHue(s.Hue, Colors.HotPink);
            var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), Width = 1000, Height = 420 };
            host.Children.Add(view);
            host.Measure(new Size(1000, 420));
            host.Arrange(new Rect(0, 0, 1000, 420));
            host.UpdateLayout();
            foreach (var t in Times)
            {
                view.SecondsForTests = t;
                view.Redraw();
                host.UpdateLayout();
                var bmp = new RenderTargetBitmap(1000, 420, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(host);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var f = File.Create(Path.Combine(dir, $"wpf-{s.Name}-{t.ToString("0.0", CultureInfo.InvariantCulture)}.png"));
                enc.Save(f);
            }
            view.Release();
        }
    });
}

internal static class BuiltInArtTestData
{
    internal static CalendarInfo Calendar() => BuiltInArt.CalendarData(
        new Dictionary<string, int> { ["counted"] = 6, ["need"] = 25, ["days"] = 31, ["today"] = 7 }, new DateTime(2026, 10, 7));
}

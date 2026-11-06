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

        // The living poster (PosterArtView): still, landing, rest and hover after fixed steps, the
        // same steps CCP.Avalonia.Tests BillboardLivingPosterTests takes.
        if (!string.IsNullOrEmpty(only) && Array.IndexOf(only.Split(','), "poster") < 0) return;
        var poster = new PosterArtView("features/deeper.png") { Accent = Color.FromRgb(0x9b, 0x7b, 0xff) };
        var frame = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), Width = 1000, Height = 420 };
        frame.Children.Add(poster);
        frame.Measure(new Size(1000, 420));
        frame.Arrange(new Rect(0, 0, 1000, 420));
        frame.UpdateLayout();
        Save(frame, Path.Combine(dir, "wpf-poster-still.png"));
        poster.Play();
        poster.Step(0.05, false, default);
        Save(frame, Path.Combine(dir, "wpf-poster-land.png"));
        for (int i = 0; i < 20; i++) poster.Step(0.033, false, default);
        Save(frame, Path.Combine(dir, "wpf-poster-rest.png"));
        for (int i = 0; i < 30; i++) poster.Step(0.033, true, new Point(0.85, 0.25));
        Save(frame, Path.Combine(dir, "wpf-poster-hover.png"));
        poster.Release();

        // The board tiles (BoardTileView): never played, and played then held (every tile landed).
        var board = new BoardTileView(BoardPicture());
        var boardFrame = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x12, 0x10, 0x1F)), Width = 1000, Height = 420 };
        boardFrame.Children.Add(board);
        boardFrame.Measure(new Size(1000, 420));
        boardFrame.Arrange(new Rect(0, 0, 1000, 420));
        boardFrame.UpdateLayout();
        Save(boardFrame, Path.Combine(dir, "wpf-board-still.png"));
        board.Play();
        board.Pause();
        Save(boardFrame, Path.Combine(dir, "wpf-board-landed.png"));
        board.Release();
    });

    /// <summary>The same 64 x 36 picture as CCP.Avalonia.Tests BillboardBoardTileTests.Picture.</summary>
    private static ConditioningControlPanel.Services.Billboard.Board.BoardPicture BoardPicture()
    {
        const int w = 64, h = 36;
        var px = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool frame = x >= 12 && x < 52 && y >= 8 && y < 28;
                bool inner = x >= 14 && x < 50 && y >= 10 && y < 26;
                px[y * w + x] = unchecked(inner ? (int)0xFFFF4FA8 : frame ? (int)0xFF5FE3FF : (int)0xFF120A20);
            }
        return ConditioningControlPanel.Services.Billboard.Board.BoardPicture.FromPixels(px, w, h,
            new ConditioningControlPanel.Services.Billboard.BoardPost(4, Array.Empty<string>(), null, "lobby",
                ConditioningControlPanel.Services.Billboard.BoardAudience.Everyone, 1, 6, w, h));
    }

    private static void Save(FrameworkElement e, string path)
    {
        e.UpdateLayout();
        var bmp = new RenderTargetBitmap(1000, 420, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}

internal static class BuiltInArtTestData
{
    internal static CalendarInfo Calendar() => BuiltInArt.CalendarData(
        new Dictionary<string, int> { ["counted"] = 6, ["need"] = 25, ["days"] = 31, ["today"] = 7 }, new DateTime(2026, 10, 7));
}

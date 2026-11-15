using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ModCreatorArtFramingEditorTests
{
    private const string Slot = "features/fyp.png";
    private const string Rail = ModArtFramingRegistry.SurfaceRailChip;
    private const string Play = ModArtFramingRegistry.SurfacePlayCard;

    [Fact]
    public void WorkingCopiesPanZoomAndCommitStayIsolated()
    {
        var stored = new Dictionary<string, ModArtFraming> { [Rail] = new() { Zoom = 2 } };
        var working = ModCreatorWindow.BeginFramingEdit(Slot, stored);
        Assert.Equal(2, working.Count);
        Assert.Contains(Play, working.Keys);
        ModCreatorWindow.ZoomFraming(working[Rail], 1, 2, true);
        ModCreatorWindow.PanFraming(working[Rail], 1, 2, .5, .5, 100, 100);
        Assert.Equal(2, stored[Rail].Zoom);
        Assert.Equal(.5, stored[Rail].CenterX);
        Assert.True(working[Play].IsDefault);
        Assert.Empty(ModCreatorWindow.BeginFramingEdit("features/goon_game.png", null));

        var f = new ModArtFraming { Zoom = 2 };
        ModCreatorWindow.PanFraming(f, 1, 2, .5, .5, 100, -100);
        var box = ModArtFramingRegistry.ToViewbox(f, 1, 2);
        Assert.Equal(box.Width / 2, f.CenterX, 8);
        Assert.Equal(1 - box.Height / 2, f.CenterY, 8);
        for (int i = 0; i < 100; i++) ModCreatorWindow.ZoomFraming(f, 1, 2, true);
        Assert.Equal(ModArtFramingRegistry.MaxZoom, f.Zoom);
        for (int i = 0; i < 100; i++) ModCreatorWindow.ZoomFraming(f, 1, 2, false);
        Assert.Equal(1, f.Zoom);
        Assert.Equal(.5, f.CenterX, 8);

        var destination = new Dictionary<string, Dictionary<string, ModArtFraming>>();
        working = ModCreatorWindow.BeginFramingEdit(Slot, null);
        working[Rail].Zoom = 2;
        ModCreatorWindow.CommitFramingEdit(destination, Slot, working);
        Assert.Single(destination[Slot]);
        working[Rail].Zoom = 3;
        Assert.Equal(2, destination[Slot][Rail].Zoom);
        working[Rail].Zoom = 1;
        ModCreatorWindow.CommitFramingEdit(destination, Slot, working);
        Assert.Empty(destination);
    }
    [Fact]
    public Task FrameButtonOpensIndependentPreviewsAndCancelOrOkControlsExport() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var w = new ModCreatorWindow();
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "framing-adam7.png");
        object? Call(string method, params object?[] args) => typeof(ModCreatorWindow)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);
        void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        try
        {
            w.Show();
            var buttons = (Dictionary<string, Button>)typeof(ModCreatorWindow).GetField("_frameButtons", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            Assert.True(buttons.ContainsKey(Slot));
            Assert.False(buttons.ContainsKey("features/goon_game.png"));
            Assert.False(buttons[Slot].IsVisible);
            await (Task)Call("SetImageSlot", Slot, path, false)!;
            Assert.True(buttons[Slot].IsVisible);
            Window Open() { Click(buttons[Slot]); var opened = Assert.Single(w.OwnedWindows); Dispatcher.UIThread.RunJobs(); return opened; }
            var dialog = Open();
            var cards = dialog.GetLogicalDescendants().OfType<StackPanel>().Where(c => c.Tag is string id && (id == Rail || id == Play)).ToList();
            Assert.Equal(2, cards.Count);
            var holder = cards.Single(c => Equals(c.Tag, Rail)).Children.OfType<Grid>().Single();
            var at = holder.TranslatePoint(new Point(holder.Width / 2, holder.Height / 2), dialog)!.Value;
            dialog.MouseWheel(at, new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();
            var railCard = cards.Single(c => Equals(c.Tag, Rail));
            var readout = railCard.Children.OfType<TextBlock>().Last();
            Assert.Contains("1.12x", readout.Text);
            Click(railCard.Children.OfType<Button>().Single());
            Assert.Contains("1x", readout.Text);
            dialog.MouseWheel(at, new Vector(0, 1));
            Click(dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Reset all")));
            Assert.Contains("1x", readout.Text);
            dialog.MouseWheel(at, new Vector(0, 1));
            Click(dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(w.BuildManifestFromForm().ArtFraming);
            dialog = Open();
            holder = dialog.GetLogicalDescendants().OfType<StackPanel>().Single(c => Equals(c.Tag, Rail)).Children.OfType<Grid>().Single();
            at = holder.TranslatePoint(new Point(holder.Width / 2, holder.Height / 2), dialog)!.Value;
            dialog.MouseWheel(at, new Vector(0, 1));
            dialog.MouseDown(at, MouseButton.Left);
            dialog.MouseMove(new Point(at.X + 20, at.Y));
            dialog.MouseUp(new Point(at.X + 20, at.Y), MouseButton.Left);
            Click(dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "OK")));
            Dispatcher.UIThread.RunJobs();
            var framing = w.BuildManifestFromForm().ArtFraming![Slot];
            Assert.Single(framing);
            Assert.True(framing[Rail].Zoom > 1);
            Assert.True(framing[Rail].CenterX < .5);
            Assert.Equal("Framed", buttons[Slot].Content);
            Call("ClearImageSlot", Slot);
            Assert.False(buttons[Slot].IsVisible);
            Assert.Null(w.BuildManifestFromForm().ArtFraming);
        }
        finally { w.Close(); }
    });
    [Theory]
    [InlineData(1800, 1200, 900, 600, false)]
    [InlineData(1800, 1200, 900, 600, true)]
    [InlineData(1200, 1800, 600, 900, false)]
    [InlineData(120, 80, 120, 80, false)]
    public void PreviewDecodeCapsTheLongerEdge(int width, int height, int expectedWidth, int expectedHeight, bool large) => AvaloniaTestDispatcher.Run(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            using (var bitmap = new SkiaSharp.SKBitmap(width, height))
            {
                bitmap.Erase(new SkiaSharp.SKColor(230, 60, 15, 255));
                using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(path, data.ToArray());
            }
            if (large)
            {
                var png = File.ReadAllBytes(path);
                using var file = File.Create(path);
                file.Write(png, 0, png.Length - 12);
                file.Write(new byte[] { 2, 16, 0, 0, 110, 112, 65, 68 }); // 33 MiB ancillary chunk
                var block = new byte[1024 * 1024]; for (int i = 0; i < 33; i++) file.Write(block);
                file.Write(new byte[] { 0x4c, 0xc7, 0x29, 0x5a }); file.Write(png, png.Length - 12, 12);
            }
            using var preview = ModCreatorWindow.DecodeFramingPreview(path);
            Assert.Equal(new PixelSize(expectedWidth, expectedHeight), preview.PixelSize);
            Assert.Equal(new byte[] { 15, 60, 230, 255 }, Pixel(preview, expectedWidth / 2, expectedHeight / 2));
        }
        finally { File.Delete(path); }
    });

    [Fact]
    public void InterlacedPngSamplesAdam7Passes() => AvaloniaTestDispatcher.Run(() =>
    {
        using var preview = ModCreatorWindow.DecodeFramingPreview(Path.Combine(AppContext.BaseDirectory, "Assets", "framing-adam7.png"));
        Assert.Equal(new PixelSize(900, 600), preview.PixelSize);
        Assert.True(Pixel(preview, 400, 0)[2] > 240);
        Assert.True(Pixel(preview, 401, 599)[0] > 240);
        var middle = Pixel(preview, 400, 300);
        Assert.InRange(middle[2], 110, 145);
        Assert.InRange(middle[0], 110, 145);
    });

    private static byte[] Pixel(Bitmap image, int x, int y)
    {
        var pixel = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try { image.CopyPixels(new PixelRect(x, y, 1, 1), pixel, 4, 4); return Enumerable.Range(0, 4).Select(i => System.Runtime.InteropServices.Marshal.ReadByte(pixel, i)).ToArray(); }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(pixel); }
    }

    [Fact]
    public void SurfaceTitlesAndScrimsMatchTheirRegistry() => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        var w = new ModCreatorWindow();
        try
        {
            loc.SetLanguage("fr");
            using var bitmap = new WriteableBitmap(new PixelSize(20, 20), new Vector(96, 96));
            var working = ModArtFramingRegistry.Surfaces.ToDictionary(s => s.Id, _ => new ModArtFraming());
            var dialog = w.CreateFramingDialog(Slot, bitmap, working);
            var cards = dialog.GetLogicalDescendants().OfType<StackPanel>().Where(c => c.Tag is string id && working.ContainsKey(id)).ToArray();
            Assert.Equal(working.Count, cards.Length);
            foreach (var card in cards)
                Assert.Equal(Loc.Get("mod_art_frame_surface_" + card.Tag), card.Children.OfType<TextBlock>().First().Text);
            Assert.Equal("Vignette latérale", cards.Single(c => Equals(c.Tag, Rail)).Children.OfType<TextBlock>().First().Text);
            Assert.Equal("En-tête de carte", cards.Single(c => Equals(c.Tag, Play)).Children.OfType<TextBlock>().First().Text);
            Grid Preview(string id) => dialog.GetLogicalDescendants().OfType<StackPanel>().Single(c => Equals(c.Tag, id)).Children.OfType<Grid>().Single();
            void Check(string id, double[] offsets, byte[] alphas)
            {
                var holder = Preview(id);
                var scrim = holder.Children.OfType<Border>().Skip(1).First();
                Assert.True(double.IsNaN(scrim.Height));
                var gradient = Assert.IsType<LinearGradientBrush>(scrim.Background);
                Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), gradient.StartPoint);
                Assert.Equal(new RelativePoint(0, 1, RelativeUnit.Relative), gradient.EndPoint);
                Assert.Equal(offsets, gradient.GradientStops.Select(s => s.Offset));
                Assert.Equal(alphas, gradient.GradientStops.Select(s => s.Color.A));
            }
            Check(Rail, new[] { .28, .62, 1d }, new byte[] { 0, 0x73, 0xDE });
            Check(ModArtFramingRegistry.SurfaceRailCard, new[] { 0d, .42, 1d }, new byte[] { 0x59, 0xB8, 0xE6 });
            Assert.Equal(2, Preview(ModArtFramingRegistry.SurfaceLoomStrip).Children.OfType<Border>().Count());
        }
        finally { loc.SetLanguage(previous); w.Close(); }
    });
}

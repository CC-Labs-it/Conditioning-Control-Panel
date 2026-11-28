using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SkiaSharp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class ModCreatorWindow
    {
        private readonly Dictionary<string, Button> _frameButtons = new(StringComparer.OrdinalIgnoreCase);

        private Button CreateFrameButton(string slot)
        {
            var button = new Button
            {
                Content = Loc.Get("mod_art_frame"), FontSize = 9, Padding = new Thickness(4, 1),
                Background = Brush.Parse("#BE1E1E32"), Foreground = Brush.Parse("#B0B0C8"),
                BorderThickness = new Thickness(0), HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom, Margin = new Thickness(2), IsVisible = false,
                Tag = slot,
            };
            button.Click += async (_, _) => await OpenFramingEditor(slot);
            _frameButtons[slot] = button;
            return button;
        }

        private void UpdateFrameButton(string slot)
        {
            if (!_frameButtons.TryGetValue(slot, out var button)) return;
            button.IsVisible = _imageSlots.TryGetValue(slot, out var path) && !string.IsNullOrEmpty(path);
            var framed = _artFraming.TryGetValue(slot, out var values) && values.Count > 0;
            button.Content = Loc.Get(framed ? "mod_art_framed" : "mod_art_frame");
            button.Foreground = Brush.Parse(framed ? "#FF69B4" : "#B0B0C8");
        }

        private async System.Threading.Tasks.Task OpenFramingEditor(string slot)
        {
            if (!_imageSlots.TryGetValue(slot, out var path) || string.IsNullOrEmpty(path)) return;
            Bitmap bitmap;
            try { bitmap = DecodeFramingPreview(path); }
            catch
            {
                await MessageDialog.ShowAsync(this, Loc.Get("mod_art_frame_title"), Loc.Get("mod_art_frame_decode"));
                return;
            }
            using (bitmap)
            {
                _artFraming.TryGetValue(slot, out var stored);
                var working = BeginFramingEdit(slot, stored);
                if (working.Count == 0) return;
                var dialog = CreateFramingDialog(slot, bitmap, working);
                if (await dialog.ShowDialogSafe<bool>(this))
                {
                    CommitFramingEdit(_artFraming, slot, working);
                    UpdateFrameButton(slot);
                    UpdateStatusBar();
                }
            }
        }

        // Decode directly at a supported size, or sample scanlines when the codec cannot scale.
        // Avalonia's DecodeToWidth can otherwise decode a full PNG before resizing it.
        internal static Bitmap DecodeFramingPreview(string path)
        {
            using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Invalid image");
            int width = codec.Info.Width, height = codec.Info.Height;
            if (width <= 0 || height <= 0 || width > 65536 || height > 65536)
                throw new InvalidDataException("Invalid image size");
            double scale = Math.Min(1, 900d / Math.Max(width, height));
            int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(height * scale));
            var nearest = codec.GetScaledDimensions((float)scale);
            var targetInfo = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var target = new SKBitmap(targetInfo);
            if (nearest.Width <= 900 && nearest.Height <= 900)
            {
                using var decoded = new SKBitmap(new SKImageInfo(nearest.Width, nearest.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
                    throw new InvalidDataException("Cannot decode image");
                if (!decoded.ScalePixels(target, new SKSamplingOptions(SKFilterMode.Linear)))
                    throw new InvalidDataException("Cannot scale image");
            }
            else
            {
                var sourceInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                if (codec.StartScanlineDecode(sourceInfo) != SKCodecResult.Success || (codec.ScanlineOrder != SKCodecScanlineOrder.TopDown && codec.ScanlineOrder != SKCodecScanlineOrder.BottomUp))
                    return DecodeFramingPng(path, width, height, targetWidth, targetHeight);
                using var row = new SKBitmap(width, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
                var line = new byte[width * 4];
                var pixels = new byte[targetWidth * targetHeight * 4];
                var rows = new Dictionary<int, int>();
                for (int y = 0; y < targetHeight; y++) rows[(int)((long)y * height / targetHeight)] = y;
                for (int i = 0; i < height; i++)
                {
                    int sourceY = codec.NextScanline;
                    if (!rows.TryGetValue(sourceY, out var destY))
                    {
                        if (!codec.SkipScanlines(1)) throw new InvalidDataException("Incomplete image");
                        continue;
                    }
                    if (codec.GetScanlines(row.GetPixels(), 1, row.RowBytes) != 1) throw new InvalidDataException("Incomplete image");
                    Marshal.Copy(row.GetPixels(), line, 0, line.Length);
                    for (int x = 0; x < targetWidth; x++)
                        Buffer.BlockCopy(line, (int)((long)x * width / targetWidth) * 4, pixels, (destY * targetWidth + x) * 4, 4);
                }
                Marshal.Copy(pixels, 0, target.GetPixels(), pixels.Length);
            }
            return new Bitmap(global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul,
                target.GetPixels(), new PixelSize(targetWidth, targetHeight), new Vector(96, 96), target.RowBytes);
        }

        // Skia does not offer scanline decoding for PNG. Inflate one row at a time so a
        // compressed poster never allocates its full pixel buffer, including for Adam7 passes.
        private static Bitmap DecodeFramingPng(string path, int width, int height, int tw, int th)
        {
            using var input = File.OpenRead(path);
            var data = new byte[33]; input.ReadExactly(data);
            if (!data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("Unsupported image");
            int depth = data[24], color = data[25], channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0 || data[28] > 1 || !(depth == 8 || depth == 16 || ((color == 0 || color == 3) && (depth == 1 || depth == 2 || depth == 4))) || (color == 3 && depth == 16))
                throw new InvalidDataException("Unsupported PNG");
            byte[]? palette = null, transparency = null;
            using var compressed = new FileStream(System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName()), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose);
            var header = new byte[8]; var buffer = new byte[65536];
            while (input.Position < input.Length)
            {
                input.ReadExactly(header); uint size = BinaryPrimitives.ReadUInt32BigEndian(header);
                if (size > input.Length - input.Position - 4) throw new InvalidDataException("Invalid PNG chunk");
                var type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
                if (type == "PLTE" || type == "tRNS")
                {
                    if (size > 768) throw new InvalidDataException("Invalid PNG palette");
                    var chunk = new byte[size]; input.ReadExactly(chunk);
                    if (type == "PLTE") palette = chunk; else transparency = chunk;
                }
                else if (type == "IDAT")
                {
                    for (uint left = size; left > 0;) { int count = (int)Math.Min(left, (uint)buffer.Length); input.ReadExactly(buffer.AsSpan(0, count)); compressed.Write(buffer, 0, count); left -= (uint)count; }
                }
                else input.Seek(size, SeekOrigin.Current);
                input.Seek(4, SeekOrigin.Current);
                if (type == "IEND") break;
            }
            if (color == 3 && (palette == null || palette.Length % 3 != 0)) throw new InvalidDataException("Invalid PNG palette");
            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            int bpp = Math.Max(1, (channels * depth + 7) / 8);
            var pixels = new byte[tw * th * 4];
            int Sample(byte[] row, int index) => depth == 16 ? BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(index * 2, 2)) : depth == 8 ? row[index] : (row[index * depth / 8] >> (8 - depth - index * depth % 8)) & ((1 << depth) - 1);
            int ToByte(int value) => depth == 16 ? value >> 8 : depth == 8 ? value : value * 255 / ((1 << depth) - 1);
            var passes = data[28] == 0 ? new[] { (0, 0, 1, 1) } : new[]
            {
                (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
                (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)
            };
            foreach (var (x0, y0, dx, dy) in passes)
            {
                int pw = Math.Max(0, (width - x0 + dx - 1) / dx), ph = Math.Max(0, (height - y0 + dy - 1) / dy);
                if (pw == 0 || ph == 0) continue;
                int rowBytes = checked((pw * channels * depth + 7) / 8);
                var current = new byte[rowBytes]; var previous = new byte[rowBytes];
                for (int py = 0; py < ph; py++)
                {
                int y = y0 + py * dy;
                int ty = (int)(((long)y * th + height - 1) / height);
                int filter = zlib.ReadByte(); if (filter < 0 || filter > 4) throw new InvalidDataException("Invalid PNG filter");
                zlib.ReadExactly(current);
                for (int i = 0; i < rowBytes; i++)
                {
                    int a = i >= bpp ? current[i - bpp] : 0, b = previous[i], c = i >= bpp ? previous[i - bpp] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    int predictor = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, _ => pa <= pb && pa <= pc ? a : pb <= pc ? b : c };
                    current[i] = unchecked((byte)(current[i] + predictor));
                }
                if (ty < th && y == (int)((long)ty * height / th))
                {
                    for (int x = 0; x < tw; x++)
                    {
                        int sourceX = (int)((long)x * width / tw);
                        if (sourceX < x0 || (sourceX - x0) % dx != 0) continue;
                        int src = (sourceX - x0) / dx * channels, r, g, b, alpha = 255, first = Sample(current, src);
                        if (color == 3)
                        {
                            if (first * 3 + 2 >= palette!.Length) throw new InvalidDataException("Invalid PNG palette index");
                            r = palette[first * 3]; g = palette[first * 3 + 1]; b = palette[first * 3 + 2];
                            if (transparency != null && first < transparency.Length) alpha = transparency[first];
                        }
                        else if (color == 0 || color == 4) { r = g = b = ToByte(first); if (color == 4) alpha = ToByte(Sample(current, src + 1)); else if (transparency?.Length >= 2 && first == BinaryPrimitives.ReadUInt16BigEndian(transparency)) alpha = 0; }
                        else { int green = Sample(current, src + 1), blue = Sample(current, src + 2); r = ToByte(first); g = ToByte(green); b = ToByte(blue); if (color == 6) alpha = ToByte(Sample(current, src + 3)); else if (transparency?.Length >= 6 && first == BinaryPrimitives.ReadUInt16BigEndian(transparency) && green == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) && blue == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))) alpha = 0; }
                        int dst = (ty * tw + x) * 4; pixels[dst] = (byte)((b * alpha + 127) / 255); pixels[dst + 1] = (byte)((g * alpha + 127) / 255); pixels[dst + 2] = (byte)((r * alpha + 127) / 255); pixels[dst + 3] = (byte)alpha;
                    }
                }
                (current, previous) = (previous, current);
                }
            }
            using var bitmap = new SKBitmap(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul);
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            return new Bitmap(global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul, bitmap.GetPixels(), new PixelSize(tw, th), new Vector(96, 96), bitmap.RowBytes);
        }

        private static LinearGradientBrush FramingScrim(ArtScrim scrim) => new()
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = scrim == ArtScrim.FullFace
                ? new GradientStops { new(Color.FromArgb(0x59, 0, 0, 0), 0), new(Color.FromArgb(0xB8, 0, 0, 0), .42), new(Color.FromArgb(0xE6, 0, 0, 0), 1) }
                : new GradientStops { new(Color.FromArgb(0x00, 0, 0, 0), .28), new(Color.FromArgb(0x73, 0, 0, 0), .62), new(Color.FromArgb(0xDE, 0, 0, 0), 1) },
        };

        internal Window CreateFramingDialog(string slot, Bitmap bitmap, Dictionary<string, ModArtFraming> working)
        {
            var aspect = (double)bitmap.PixelSize.Width / bitmap.PixelSize.Height;
            var body = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
            body.Children.Add(new TextBlock { Text = Loc.Get("mod_art_frame_help"), TextWrapping = TextWrapping.Wrap, MaxWidth = 780 });
            var cards = new WrapPanel();
            var resets = new List<Action>();
            var caption = _imageNames.TryGetValue(slot, out var name) ? name : System.IO.Path.GetFileNameWithoutExtension(slot);
            var paren = caption.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) caption = caption[..paren];
            foreach (var (id, framing) in working)
            {
                var surface = ModArtFramingRegistry.FindSurface(id);
                if (surface == null) continue;
                var width = Math.Min(300, 160 * surface.AspectRatio);
                var height = width / surface.AspectRatio;
                var card = new StackPanel { Margin = new Thickness(0, 0, 14, 14), Width = width, Spacing = 4, Tag = id };
                card.Children.Add(new TextBlock { Text = Loc.Get("mod_art_frame_surface_" + surface.Id) });
                var brush = new ImageBrush(bitmap) { Stretch = Stretch.Fill };
                var holder = new Grid { Width = width, Height = height, Background = Brushes.Transparent, ClipToBounds = true };
                holder.Children.Add(new Border { Background = brush, CornerRadius = new CornerRadius(surface.CornerRadius), IsHitTestVisible = false });
                if (surface.Scrim != ArtScrim.None)
                {
                    holder.Children.Add(new Border { Background = FramingScrim(surface.Scrim),
                        CornerRadius = new CornerRadius(surface.CornerRadius), IsHitTestVisible = false });
                    holder.Children.Add(new TextBlock { Text = surface.Scrim == ArtScrim.FullFace ? caption + "\n−   15m   +" : caption,
                        Foreground = Brushes.White, TextAlignment = TextAlignment.Center, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = surface.Scrim == ArtScrim.FullFace ? global::Avalonia.Layout.VerticalAlignment.Top : global::Avalonia.Layout.VerticalAlignment.Bottom, IsHitTestVisible = false });
                }
                if (surface.FluidWidth) holder.Children.Add(new Rectangle { Margin = new Thickness(0, height * .15), Stroke = Brush.Parse("#E8D8B0"), StrokeThickness = 1, StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { 4, 3 }, IsHitTestVisible = false });
                holder.Children.Add(new Border { CornerRadius = new CornerRadius(surface.CornerRadius), BorderBrush = Brush.Parse("#505070"), BorderThickness = new Thickness(1), IsHitTestVisible = false });
                card.Children.Add(holder);
                if (surface.FluidWidth) card.Children.Add(new TextBlock { Text = Loc.Get("mod_art_frame_fluid"), TextWrapping = TextWrapping.Wrap, FontSize = 10 });
                var readout = new TextBlock { FontSize = 10 };
                card.Children.Add(readout);
                void Refresh()
                {
                    var box = ModArtFramingRegistry.ToViewbox(framing, aspect, surface.AspectRatio);
                    brush.SourceRect = new RelativeRect(box.X, box.Y, box.Width, box.Height, RelativeUnit.Relative);
                    readout.Text = string.Format(CultureInfo.InvariantCulture, "{0:0.###}, {1:0.###}  /  {2:0.##}x", framing.CenterX, framing.CenterY, framing.Zoom);
                }
                void Reset() { framing.CenterX = .5; framing.CenterY = .5; framing.Zoom = 1; Refresh(); }
                resets.Add(Reset);
                var reset = new Button { Content = Loc.Get("mod_art_frame_reset") };
                reset.Click += (_, _) => Reset();
                card.Children.Add(reset);
                Point? start = null; double x = .5, y = .5;
                holder.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(holder).Properties.IsLeftButtonPressed) return;
                    if (e.ClickCount >= 2) { Reset(); start = null; e.Pointer.Capture(null); }
                    else { start = e.GetPosition(holder); x = framing.CenterX; y = framing.CenterY; e.Pointer.Capture(holder); }
                    e.Handled = true;
                };
                holder.PointerMoved += (_, e) =>
                {
                    if (start == null) return;
                    var p = e.GetPosition(holder);
                    PanFraming(framing, aspect, surface.AspectRatio, x, y, (p.X - start.Value.X) / width, (p.Y - start.Value.Y) / height);
                    Refresh();
                };
                holder.PointerReleased += (_, e) => { start = null; e.Pointer.Capture(null); };
                holder.PointerCaptureLost += (_, _) => start = null;
                holder.PointerWheelChanged += (_, e) => { if (e.Delta.Y == 0) return; ZoomFraming(framing, aspect, surface.AspectRatio, e.Delta.Y > 0); Refresh(); e.Handled = true; };
                Refresh();
                cards.Children.Add(card);
            }
            body.Children.Add(cards);
            var row = new StackPanel { Orientation = global::Avalonia.Layout.Orientation.Horizontal, Spacing = 8, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
            var resetAll = new Button { Content = Loc.Get("mod_art_frame_reset_all") };
            var cancel = new Button { Content = Loc.Get("btn_cancel") };
            var ok = new Button { Content = Loc.Get("btn_ok") };
            row.Children.Add(resetAll); row.Children.Add(cancel); row.Children.Add(ok); body.Children.Add(row);
            var dialog = new Window { Title = Loc.Get("mod_art_frame_title"), Width = 850, Height = 700, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer { Content = body } };
            resetAll.Click += (_, _) => { foreach (var reset in resets) reset(); };
            cancel.Click += (_, _) => dialog.Close(false);
            ok.Click += (_, _) => dialog.Close(true);
            return dialog;
        }

        // Working copies keep pointer edits isolated until the dialog is accepted.
        internal static Dictionary<string, ModArtFraming> BeginFramingEdit(
            string slot, IReadOnlyDictionary<string, ModArtFraming>? stored)
            => ModArtFramingRegistry.FramableBindingsFor(slot).Select(b => b.SurfaceId).Distinct(StringComparer.Ordinal)
                .ToDictionary(id => id, id => stored != null && stored.TryGetValue(id, out var value)
                    ? ModArtFramingRules.Sanitize(value) : new ModArtFraming(), StringComparer.Ordinal);

        internal static void CommitFramingEdit(
            Dictionary<string, Dictionary<string, ModArtFraming>> destination, string slot,
            IReadOnlyDictionary<string, ModArtFraming> working)
        {
            var entries = new Dictionary<string, Dictionary<string, ModArtFraming>> { [slot] = new(working) };
            var kept = ModArtFramingRules.ReadManifestSection(entries);
            if (kept.TryGetValue(slot, out var surfaces)) destination[slot] = surfaces;
            else destination.Remove(slot);
        }

        internal static double ClampFramingCenter(double value, double size)
        {
            if (double.IsNaN(value) || double.IsNaN(size) || size <= 0 || size >= 1) return 0.5;
            return Math.Clamp(value, size / 2, 1 - size / 2);
        }

        internal static void PanFraming(ModArtFraming framing, double sourceAspect, double surfaceAspect,
            double startX, double startY, double deltaX, double deltaY)
        {
            var window = ModArtFramingRegistry.ToViewbox(framing, sourceAspect, surfaceAspect);
            framing.CenterX = ClampFramingCenter(startX - deltaX * window.Width, window.Width);
            framing.CenterY = ClampFramingCenter(startY - deltaY * window.Height, window.Height);
        }

        internal static void ZoomFraming(ModArtFraming framing, double sourceAspect, double surfaceAspect, bool zoomIn)
        {
            framing.Zoom = Math.Clamp(framing.Zoom * (zoomIn ? 1.12 : 1 / 1.12), 1, ModArtFramingRegistry.MaxZoom);
            PanFraming(framing, sourceAspect, surfaceAspect, framing.CenterX, framing.CenterY, 0, 0);
        }
    }
}

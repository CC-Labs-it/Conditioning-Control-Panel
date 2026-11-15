using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

// Windows-only companion to the plain net10.0 Avalonia head. One invocation scans one desktop;
// stdout carries only the recognition result to the parent, never to a log or a temporary file.
internal static class Program
{
    private sealed record Word(string Text, int X, int Y, int Width, int Height, string Screen);
    private static async Task<int> Main()
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return 1;
            var words = new List<Word>();
            foreach (var screen in Screen.AllScreens)
            {
                try
                {
                    var bounds = screen.Bounds;
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                    using (var graphics = Graphics.FromImage(bitmap))
                        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, ImageFormat.Bmp);
                    stream.Position = 0;
                    using var random = stream.AsRandomAccessStream();
                    var decoder = await BitmapDecoder.CreateAsync(random);
                    using var software = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                    var result = await engine.RecognizeAsync(software);
                    foreach (var line in result.Lines)
                    foreach (var word in line.Words)
                    {
                        var r = word.BoundingRect;
                        words.Add(new Word(word.Text, bounds.Left + (int)r.X, bounds.Top + (int)r.Y, (int)r.Width, (int)r.Height, screen.DeviceName));
                    }
                }
                catch { /* WPF ScreenOcrService.cs:203-224: skip an inaccessible screen. */ }
            }
            Console.OutputEncoding = new UTF8Encoding(false);
            await Console.Out.WriteAsync(JsonSerializer.Serialize(words));
            return 0;
        }
        catch { return 1; }
    }
}

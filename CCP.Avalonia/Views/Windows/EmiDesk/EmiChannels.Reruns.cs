using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiChannels.Reruns.cs:43,263,314.
internal sealed class RerunsPainter
{
    internal sealed record Tape(string Date, string GameKey, string Grade);
    private static readonly object Gate = new();
    private static long stamp = -1;
    private static Tape? cached;
    private readonly Canvas canvas;
    private readonly TextBlock osd, time;
    private readonly Rectangle band, under;
    private readonly double ky;

    internal static void ResetCacheForTests()
    {
        lock (Gate) { stamp = -1; cached = null; }
    }

    internal static bool HasMaterial() => GetTape() != null;
    internal static Tape? GetTape()
    {
        try
        {
            var path = System.IO.Path.Combine(CorePaths.UserData, "arcademy_meta.json");
            var info = new FileInfo(path);
            long length = info.Exists ? info.Length : 0;
            long next = info.Exists ? info.LastWriteTimeUtc.Ticks ^ length : 0;
            lock (Gate)
            {
                if (stamp != next)
                {
                    stamp = next;
                    cached = next == 0 || length > 2 * 1024 * 1024 ? null : Read(path);
                }
                return cached;
            }
        }
        catch { lock (Gate) { stamp = -1; cached = null; } return null; }
    }

    private static Tape? Read(string path)
    {
        try
        {
            var days = JObject.Parse(File.ReadAllText(path))["days"] as JObject;
            if (days == null) return null;
            foreach (var day in days.Properties().OrderByDescending(p => p.Name, StringComparer.Ordinal))
                if (day.Value is JObject row && row["classes"] is JObject classes)
                    foreach (var cls in classes.Properties())
                        if (cls.Value is JObject entry)
                        {
                            var grade = (entry["grade"]?.ToString() ?? "").ToUpperInvariant();
                            if (grade == "S" || grade == "A") return new(day.Name, cls.Name, grade);
                        }
        }
        catch { }
        return null;
    }

    internal static string FlapName(string? key)
    {
        var result = new StringBuilder(); bool run = false;
        foreach (char c in key ?? "")
        {
            bool sep = c == '_' || c == '-';
            if (!sep) result.Append(c); else if (!run) result.Append(' ');
            run = sep;
        }
        var s = result.ToString().ToUpperInvariant().Trim();
        return s.Length > 14 ? s.Substring(0, 14) : s.Length == 0 ? "CLASS" : s;
    }

    internal RerunsPainter(Canvas canvas, Tape tape)
    {
        this.canvas = canvas;
        double w = canvas.Width, h = canvas.Height, k = Math.Max(.25, w / 152), ky = this.ky = Math.Max(.25, h / 137);
        Box(0, 0, w, h, "#07070F", 1);
        Label(tape.Grade, 40, 36, "#FF69B4", true);
        Label(FlapName(tape.GameKey), 7, 88, "#8D8DB0", true);
        Label(tape.Date, 7, 102, "#4D4D6A", true);
        osd = Label("<< REW", 8, 5, "#FF69B4", false); Canvas.SetLeft(osd, 5 * k);
        time = Label("0:00", 8, Math.Max(0, h / ky - 14), "#FF69B4", false);
        time.Width = Math.Max(1, w - 5 * k); time.TextAlignment = TextAlignment.Right;
        band = Box(0, -9 * ky, w, Math.Max(1, 9 * ky), "#E6E6FF", .16);
        under = Box(0, -3 * ky, w, Math.Max(.5, 3 * ky), "#FF69B4", .10);
        for (double y = 0; y < h; y += Math.Max(2, Math.Round(2 * ky))) Box(0, y, w, 1, "#000000", .2);
        TextBlock Label(string text, double size, double top, string color, bool center)
        {
            var t = new TextBlock { Text = text, FontFamily = new FontFamily("avares://CCP.Avalonia/Resources/emi/fonts#Press Start 2P, Consolas, monospace"), FontSize = Math.Max(4, Math.Round(size * k)), Foreground = Brush.Parse(color), Width = center ? w : double.NaN, TextAlignment = center ? TextAlignment.Center : TextAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis };
            Canvas.SetTop(t, top * ky); canvas.Children.Add(t); return t;
        }
    }
    private Rectangle Box(double x, double y, double w, double h, string color, double opacity)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = Brush.Parse(color), Opacity = opacity };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y); canvas.Children.Add(r); return r;
    }
    internal void Tick(double ms)
    {
        int state = ms >= 1000 ? 2 : (int)(ms / 160) % 2;
        osd.Text = state == 2 ? "> PLAY" : "<< REW";
        osd.Foreground = Brush.Parse(state == 0 ? "#5A5A7A" : "#FF69B4");
        int seconds = (int)Math.Max(0, ms / 1000);
        time.Text = $"{seconds / 60}:{seconds % 60:00}";
        double y = canvas.Height - ms % 2000 / 2000 * (canvas.Height + 18 * ky);
        Canvas.SetTop(band, y); Canvas.SetTop(under, y + band.Height);
    }
}

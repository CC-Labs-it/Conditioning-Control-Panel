using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Platform;

// The helper is published beside the Windows app: WinRT OCR cannot be referenced by its plain net10.0 TFM.
internal static class WindowsOcrSource
{
    private sealed record Hit(string Text, int X, int Y, int Width, int Height, int Screen = 0);
    internal static Func<CancellationToken, Task<string>>? ExecuteForTests;
    internal static bool Available => ExecuteForTests != null || (OperatingSystem.IsWindows() && File.Exists(HelperPath));
    private static string HelperPath => Path.Combine(AppContext.BaseDirectory, "CCP.Ocr.Windows.exe");

    internal static async Task<IReadOnlyList<ScreenOcrService.Word>> Scan(CancellationToken token)
    {
        var json = await (ExecuteForTests ?? Execute)(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        var hits = JsonSerializer.Deserialize<Hit[]>(json) ?? throw new InvalidDataException("Invalid OCR response");
        return hits.Where(h => !string.IsNullOrEmpty(h.Text) && h.Width > 0 && h.Height > 0)
            .Select(h => new ScreenOcrService.Word(h.Text, new System.Drawing.Rectangle(h.X, h.Y, h.Width, h.Height), h.Screen)).ToArray();
    }
    private static async Task<string> Execute(CancellationToken token)
    {
        using var process = Process.Start(new ProcessStartInfo(HelperPath) { UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 })
            ?? throw new InvalidOperationException("Could not start OCR helper");
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new InvalidOperationException("OCR helper failed");
            return output;
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}

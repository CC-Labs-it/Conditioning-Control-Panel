using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>A bounded, persistent audio trace, written away from playback callbacks.</summary>
    public sealed class AudioDiagnosticTrace
    {
        private const long MaxFileBytes = 1_000_000;
        private readonly string _path;
        private readonly object _fileGate = new();
        private readonly Channel<(string? Line, TaskCompletionSource<bool>? Barrier)> _queue =
            Channel.CreateBounded<(string?, TaskCompletionSource<bool>?)>(4000);

        public AudioDiagnosticTrace(string path)
        {
            _path = path;
            _ = Task.Run(WriteLoop);
        }

        public void Note(string line) => _queue.Writer.TryWrite(($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} AUDIO: {line}", null));

        private async Task WriteLoop()
        {
            await foreach (var item in _queue.Reader.ReadAllAsync())
            {
                if (item.Barrier != null) { item.Barrier.TrySetResult(true); continue; }
                try
                {
                    lock (_fileGate)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                        if (File.Exists(_path) && new FileInfo(_path).Length >= MaxFileBytes)
                            File.Move(_path, _path + ".1", true);
                        using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.WriteThrough);
                        using var writer = new StreamWriter(stream);
                        writer.WriteLine(item.Line);
                        writer.Flush();
                        stream.Flush(true);
                    }
                }
                catch { } // Diagnostics must never interrupt playback.
            }
        }

        /// <summary>Drain queued lines, then read the newest lines from both retained files.</summary>
        public string Tail(int maxLines)
        {
            if (maxLines <= 0) return string.Empty;
            try
            {
                var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (_queue.Writer.TryWrite((null, barrier))) barrier.Task.Wait(TimeSpan.FromSeconds(2));
                lock (_fileGate)
                {
                    var lines = new Queue<string>();
                    foreach (var file in new[] { _path + ".1", _path })
                    {
                        if (!File.Exists(file)) continue;
                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(stream);
                        while (reader.ReadLine() is { } line)
                        {
                            lines.Enqueue(line);
                            if (lines.Count > maxLines) lines.Dequeue();
                        }
                    }
                    return string.Join(Environment.NewLine, lines);
                }
            }
            catch { return string.Empty; }
        }
    }
}

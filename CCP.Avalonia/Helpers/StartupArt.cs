using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// Startup art prewarm. Building the shell decoded ~4 s of art on the one UI thread under CPU
    /// rendering (fix2/startup trace, WSLg: ModArt.Decode inside the shell constructor, most of it
    /// Skia's high-quality downscale), which froze the splash and made launch lag. Each launch records
    /// which <see cref="ModArt"/> loads its first seconds asked for; the next launch decodes that list
    /// on worker threads, in that order, as soon as the active mod is known, and the UI thread takes
    /// the finished bitmaps (or waits for one in flight, or decodes a not-yet-started one itself).
    ///
    /// <para>Same pixels as before: an entry is keyed by kind, name, width AND the mod override path
    /// resolved at prewarm time, so a mod switch or a moved override misses and decodes as usual.
    /// Each entry is handed out once (ModArt still keeps no general cache); whatever startup did not
    /// take is dropped at <see cref="Finish"/>. Real launches only (App.SplashOnStartup): tests and
    /// renders never start it, so <see cref="Take"/> answers "no entry" and ModArt is unchanged.</para>
    /// </summary>
    internal static class StartupArt
    {
        internal const string ManifestName = "startup-art.json";
        internal const int MaxEntries = 400;

        /// <summary>"o": <see cref="ModArt.TryLoad"/> (override first); "b": <see cref="ModArt.TryLoadBuiltIn"/>.</summary>
        internal readonly record struct Request(string Kind, string Name, int? Width);

        private static ConcurrentDictionary<(Request, string?), Lazy<Bitmap?>>? _warm;
        private static ConcurrentDictionary<Request, int>? _recorded;
        private static int _order;

        internal static bool Recording => Volatile.Read(ref _recorded) is not null;

        /// <summary>Reads last launch's list and starts decoding it; records this launch's loads.
        /// Returns the decode task (tests), or null when there was nothing to prewarm.</summary>
        internal static Task? Start(string manifestPath, Func<Request, string?> overrideOf, Func<Request, string?, Bitmap?> load)
        {
            Volatile.Write(ref _recorded, new ConcurrentDictionary<Request, int>());
            var list = Read(manifestPath);
            if (list.Count == 0) return null;
            var warm = new ConcurrentDictionary<(Request, string?), Lazy<Bitmap?>>();
            var queue = new List<Lazy<Bitmap?>>();
            foreach (var r in list)
            {
                string? ov;
                try { ov = overrideOf(r); } catch { continue; }
                var lazy = new Lazy<Bitmap?>(() => load(r, ov), LazyThreadSafetyMode.ExecutionAndPublication);
                if (warm.TryAdd((r, ov), lazy)) queue.Add(lazy);
            }
            Volatile.Write(ref _warm, warm);
            var workers = Math.Clamp(Environment.ProcessorCount / 2, 1, 8);
            Log.Information("[StartupArt] prewarming {Count} startup images on {Workers} threads", queue.Count, workers);
            return Task.Run(() => Parallel.ForEach(queue, new ParallelOptions { MaxDegreeOfParallelism = workers }, lazy =>
            {
                if (Volatile.Read(ref _warm) is null) return;   // startup is over: nobody will take it
                try { _ = lazy.Value; }
                catch (Exception ex) { Log.Debug("[StartupArt] prewarm decode failed: {E}", ex.Message); }
            }));
        }

        /// <summary>The prewarmed result for this load. False when there is no entry (no prewarm, not listed,
        /// already taken, or the override moved): the caller decodes as before.</summary>
        internal static bool Take(Request r, string? overridePath, out Bitmap? art)
        {
            if (Volatile.Read(ref _recorded) is { } rec) rec.TryAdd(r, Interlocked.Increment(ref _order));
            art = null;
            if (Volatile.Read(ref _warm) is not { } warm || !warm.TryRemove((r, overridePath), out var lazy)) return false;
            try { art = lazy.Value; return true; }
            catch (Exception ex) { Log.Debug("[StartupArt] prewarmed load failed: {E}", ex.Message); return false; }
        }

        /// <summary>Startup is over: write this launch's list for the next one and drop what was not taken.</summary>
        internal static void Finish(string manifestPath)
        {
            var left = Interlocked.Exchange(ref _warm, null);
            var rec = Interlocked.Exchange(ref _recorded, null);
            if (left is { Count: > 0 }) Log.Debug("[StartupArt] {Count} prewarmed images were not asked for", left.Count);
            if (rec is null || rec.IsEmpty) return;
            try
            {
                var list = rec.OrderBy(kv => kv.Value).Take(MaxEntries)
                    .Select(kv => new Entry { K = kv.Key.Kind, N = kv.Key.Name, W = kv.Key.Width }).ToList();
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
                var tmp = manifestPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(list));
                File.Move(tmp, manifestPath, overwrite: true);
            }
            catch (Exception ex) { Log.Debug("[StartupArt] could not save the startup list: {E}", ex.Message); }
        }

        internal static List<Request> Read(string manifestPath)
        {
            try
            {
                if (!File.Exists(manifestPath)) return new();
                var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(manifestPath)) ?? new();
                return entries.Where(e => e.K is "o" or "b" && !string.IsNullOrWhiteSpace(e.N) && e.W is null or (> 0 and <= 8192))
                    .Select(e => new Request(e.K!, e.N!, e.W)).Distinct().Take(MaxEntries).ToList();
            }
            catch (Exception ex) { Log.Debug("[StartupArt] startup list unreadable, ignored: {E}", ex.Message); return new(); }
        }

        private sealed class Entry
        {
            public string? K { get; set; }
            public string? N { get; set; }
            public int? W { get; set; }
        }
    }
}

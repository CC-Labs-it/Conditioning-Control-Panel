using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Newtonsoft.Json.Linq;
using Whisper.net;

namespace ConditioningControlPanel.Services.Glassroad.SongWords;

/// <summary>
/// "Race your own song" for Racing Thoughts 3D, SOLO ONLY (owner 2026-10-31: pick a song, wait at most two or
/// three minutes, race; online rooms keep the signed catalog). The page sends <c>song-pick</c>; the host opens a
/// file dialog, copies the file under <c>&lt;userdata&gt;/songs/&lt;id&gt;/</c>, listens to it with whisper.cpp
/// (Whisper.net, CPU, model fetched on first use) and writes a chart the page plays like any catalog song through
/// the <c>ccp.songs</c> virtual host. Nothing leaves the machine: no upload, no lyrics lookup, no synthetic speech.
/// Progress and results go back as <c>song-words</c> frames (CONTRACT.md section 7). One job at a time; a new
/// pick or the window closing cancels the running one.
/// </summary>
internal static class SongWordsService
{
    public const string HostName = "ccp.songs";
    public const string ChartFile = "chart.json";
    public const int MaxRows = 12;
    public const double MaxSongS = 20 * 60;
    private const int SampleRate = 16000;
    private static readonly string[] Extensions = { ".mp3", ".m4a", ".aac", ".wav", ".flac" };

    private static CancellationTokenSource? _cts;
    private static readonly object Gate = new();

    public static string SongsDir => Path.Combine(App.UserDataPath, "songs");
    public static string ModelsDir => WhisperModelPins.Folder(App.UserDataPath);

    public static string ChartUrl(string id) => $"https://{HostName}/{id}/{ChartFile}";

    /// <summary>The saved songs, newest first, as page rows {id,label,url}.</summary>
    public static JArray Rows()
    {
        var rows = new JArray();
        try
        {
            if (!Directory.Exists(SongsDir)) return rows;
            var charts = Directory.EnumerateDirectories(SongsDir)
                .Select(d => new FileInfo(Path.Combine(d, ChartFile)))
                .Where(f => f.Exists)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(MaxRows);
            foreach (var f in charts)
            {
                var id = Path.GetFileName(f.DirectoryName ?? "");
                if (!IsId(id)) continue;
                string label = id;
                try { label = (string?)JObject.Parse(File.ReadAllText(f.FullName))["title"] ?? id; }
                catch (Exception ex) { Diag.Swallowed(ex); }
                rows.Add(Row(id, label));
            }
        }
        catch (Exception ex) { App.Logger?.Debug("SongWords.Rows: {E}", ex.Message); }
        return rows;
    }

    public static bool IsId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 40 && id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-');

    private static JObject Row(string id, string label) => new() { ["id"] = id, ["label"] = label, ["url"] = ChartUrl(id) };

    /// <summary>The page asked for a song (UI thread): file dialog, then the listening job in the background.</summary>
    public static void Pick(string? trackId, Window? owner, Action<object> post)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Race your own song",
            Filter = "Audio files|" + string.Join(";", Extensions.Select(e => "*" + e)) + "|All files|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        bool ok;
        try { ok = (owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true; }
        catch (Exception ex) { App.Logger?.Debug("SongWords: dialog failed: {E}", ex.Message); return; }
        if (!ok || string.IsNullOrEmpty(dlg.FileName)) return;
        Start(trackId ?? "", dlg.FileName, post);
    }

    /// <summary>Start listening to one file (also the seam for tests and a dropped file).</summary>
    public static void Start(string trackId, string path, Action<object> post)
    {
        CancellationTokenSource cts;
        lock (Gate)
        {
            try { _cts?.Cancel(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _cts = cts = new CancellationTokenSource();
        }
        _ = Task.Run(() => RunAsync(trackId, path, post, cts.Token), CancellationToken.None);
    }

    public static void Cancel()
    {
        lock (Gate) { try { _cts?.Cancel(); } catch (Exception ex) { Diag.Swallowed(ex); } _cts = null; }
    }

    /// <summary>Remove a saved song (its folder under songs/).</summary>
    public static void Forget(string? id)
    {
        if (!IsId(id)) return;
        try { var dir = Path.Combine(SongsDir, id!); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception ex) { App.Logger?.Debug("SongWords.Forget: {E}", ex.Message); }
    }

    private static async Task RunAsync(string trackId, string path, Action<object> post, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        void Progress(string phase, double pct, string note) => post(new JObject
        {
            ["type"] = "song-words", ["trackId"] = trackId, ["status"] = "progress",
            ["phase"] = phase, ["pct"] = Math.Round(Math.Clamp(pct, 0, 1), 3), ["note"] = note,
        });
        void Fail(string note) => post(new JObject { ["type"] = "song-words", ["trackId"] = trackId, ["status"] = "error", ["note"] = note, ["rows"] = Rows() });
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { Fail("That file is gone."); return; }
            var ext = info.Extension.ToLowerInvariant();
            if (!Extensions.Contains(ext)) { Fail("Pick an mp3, m4a, aac, wav or flac file."); return; }
            var id = SongChartBuilder.SongId(info.FullName, info.Length, info.LastWriteTimeUtc);
            var label = SongChartBuilder.Label(info.Name);
            var dir = Path.Combine(SongsDir, id);
            var chartPath = Path.Combine(dir, ChartFile);
            if (File.Exists(chartPath))
            {
                File.SetLastWriteTimeUtc(chartPath, DateTime.UtcNow);   // newest first in the rows
                Ready(trackId, post, id, label);
                return;
            }

            // 1. the model (first time only: a one-off download, size and sha256 pinned)
            var pin = WhisperModelPins.Choose(Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
            if (!WhisperModelPins.IsInstalled(ModelsDir, pin))
            {
                Progress("model", 0, $"Getting the listening model ({pin.SizeLabel}, once)");
                var last = 0.0;
                var prog = new Progress<double>(p => { if (p - last >= 0.01 || p >= 1) { last = p; Progress("model", p, $"Getting the listening model ({pin.SizeLabel}, once)"); } });
                var r = await new WhisperModelInstaller(ModelsDir, pin).InstallAsync(prog, ct).ConfigureAwait(false);
                if (r != WhisperInstallResult.Installed)
                {
                    if (r == WhisperInstallResult.Cancelled) return;
                    App.Logger?.Warning("SongWords: model install {R}", r);
                    Fail(r == WhisperInstallResult.Disk ? "Not enough room for the listening model." : "Could not get the listening model. Check the connection and try again.");
                    return;
                }
            }
            ct.ThrowIfCancellationRequested();

            // 2. the audio: a copy for the page, 16 kHz mono floats for the model
            Progress("read", 0, "Reading the song");
            Directory.CreateDirectory(dir);
            var audioName = "audio" + ext;
            var audioCopy = Path.Combine(dir, audioName);
            File.Copy(info.FullName, audioCopy, true);
            float[] samples;
            double durationS;
            try { (samples, durationS) = Decode(audioCopy, ct); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Logger?.Warning("SongWords: decode failed: {E}", ex.Message);
                TryDeleteDir(dir);
                Fail("Could not read this file.");
                return;
            }
            if (durationS > MaxSongS) { TryDeleteDir(dir); Fail("Songs up to 20 minutes, please."); return; }
            if (durationS < 5) { TryDeleteDir(dir); Fail("That file is too short to race."); return; }
            ct.ThrowIfCancellationRequested();

            // 3. listen
            Progress("listen", 0, "Listening for the words");
            var threads = WhisperModelPins.Threads(Environment.ProcessorCount);
            List<SongWord> words;
            try { words = await Transcribe(WhisperModelPins.PathFor(ModelsDir, pin), samples, threads, p => Progress("listen", p, "Listening for the words"), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                App.Logger?.Warning("SongWords: transcribe failed: {E}", ex.Message);
                TryDeleteDir(dir);
                Fail("Listening failed. Try another file.");
                return;
            }
            ct.ThrowIfCancellationRequested();
            if (words.Count(w => w.P >= SongChartBuilder.DropBelowP && !SongChartBuilder.IsNoise(w.Text)) < 8)
            {
                TryDeleteDir(dir);
                Fail("No words heard in this one. Try a song with clearer vocals.");
                return;
            }

            // 4. the chart, written whole then renamed in
            var chart = SongChartBuilder.Build(id, label, durationS, audioName, words, pin.Id);
            var tmp = chartPath + ".tmp";
            await File.WriteAllTextAsync(tmp, chart.ToString(Newtonsoft.Json.Formatting.None), new UTF8Encoding(false), ct).ConfigureAwait(false);
            File.Move(tmp, chartPath, true);
            App.Logger?.Information("SongWords: {Id} {Model} {Words} words in {S:F0}s ({Dur:F0}s of audio, {Thr} threads)",
                id, pin.Id, ((JArray)chart["words"]!).Count, sw.Elapsed.TotalSeconds, durationS, threads);
            Ready(trackId, post, id, label);
        }
        catch (OperationCanceledException) { /* a new pick or the window closing */ }
        catch (Exception ex)
        {
            App.Logger?.Warning("SongWords: job failed: {E}", ex.Message);
            Fail("Something went wrong. Try again.");
        }
    }

    private static void Ready(string trackId, Action<object> post, string id, string label) => post(new JObject
    {
        ["type"] = "song-words", ["trackId"] = trackId, ["status"] = "ready",
        ["song"] = Row(id, label), ["rows"] = Rows(),
    });

    /// <summary>Decode any Media Foundation format to 16 kHz mono floats (whisper's input), capped at MaxSongS.</summary>
    private static (float[] samples, double durationS) Decode(string path, CancellationToken ct)
    {
        using var reader = new MediaFoundationReader(path);
        var durationS = reader.TotalTime.TotalSeconds;
        if (durationS > MaxSongS) return (Array.Empty<float>(), durationS);
        ISampleProvider sp = reader.ToSampleProvider();
        if (sp.WaveFormat.Channels > 1) sp = sp.ToMono();
        if (sp.WaveFormat.SampleRate != SampleRate) sp = new WdlResamplingSampleProvider(sp, SampleRate);
        var cap = (int)(MaxSongS * SampleRate) + SampleRate;
        var buf = new float[SampleRate];
        var all = new List<float>((int)Math.Min(cap, Math.Max(SampleRate, durationS * SampleRate) + SampleRate));
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (all.Count + n > cap) break;
            all.AddRange(n == buf.Length ? buf : buf.Take(n));
        }
        return (all.ToArray(), all.Count / (double)SampleRate);
    }

    /// <summary>whisper.cpp with token timestamps; a word starts at the first token with a leading space.</summary>
    private static async Task<List<SongWord>> Transcribe(string modelPath, float[] samples, int threads, Action<double> progress, CancellationToken ct)
    {
        using var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = false });
        await using var processor = factory.CreateBuilder()
            .WithLanguageDetection()
            .WithThreads(threads)
            .WithTokenTimestamps()
            .WithProgressHandler(p => progress(p / 100.0))
            .Build();
        var words = new List<SongWord>();
        int line = 0;
        await foreach (var seg in processor.ProcessAsync(samples, ct).ConfigureAwait(false))
        {
            if (seg.Tokens == null) { line++; continue; }
            var cur = new StringBuilder(); double s0 = 0, e0 = 0; var ps = new List<float>();
            void Flush()
            {
                var w = cur.ToString().Trim();
                if (w.Length > 0) words.Add(new SongWord(w, s0, e0, ps.Count > 0 ? ps.Average() : 0, line));
                cur.Clear(); ps.Clear();
            }
            foreach (var t in seg.Tokens)
            {
                var txt = t.Text ?? "";
                if (txt.StartsWith("[_", StringComparison.Ordinal)) continue;   // [_BEG_], [_TT_n] control tokens
                var tStart = t.Start / 100.0;                                   // whisper.cpp times: 10 ms units
                if (txt.StartsWith(' ') || cur.Length == 0) { Flush(); s0 = tStart; }
                cur.Append(txt); e0 = Math.Max(tStart, t.End / 100.0); ps.Add(t.Probability);
            }
            Flush();
            line++;
        }
        return words;
    }

    private static void TryDeleteDir(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception ex) { Diag.Swallowed(ex); }
    }
}

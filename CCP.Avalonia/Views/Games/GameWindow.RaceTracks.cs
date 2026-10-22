// Racing Thoughts own tracks + charting on the shared GameWindow: WPF Services/Chaos/CaucusHostService.cs
// (7.1.5) "track charts" (:353-368 the frames, :736-1035 the host half). The page asks for a file, the host
// charts it on a worker with Core Services/Race (the same code WPF runs) and answers with progress, one or
// two charts, a 250 ms clock and an ended note. Nothing about the audio leaves the machine.
//   page -> host: track-pick, track-play, track-pause {on}, track-stop, track-cancel
//   host -> page: track-progress {stage, pct, name}, track-chart {chart, partial, authored},
//                 track-clock {t, playing, durationSec}, track-ended, track-error {message}
// The player and the decoder seed are RaceTrackPlayer (LibVLC); without libvlc a pick answers
// track-error RaceNoAudioMessage.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Models.Race;
using ConditioningControlPanel.Services.Race;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>The face the race host needs from a track player (WPF TrackPlayer's members).</summary>
    internal interface IRaceTrackPlayer : ITrackTransport, IDisposable
    {
        /// <summary>Raised once when the file plays out to its end (any thread).</summary>
        event Action? Ended;

        /// <summary>Open a file, dropping whatever was loaded. Throws on a file that cannot play.</summary>
        void Load(string path);
    }

    internal sealed partial class GameWindow
    {
        /// <summary>The message a pick gets on a build with no player: plain, and it is the truth.</summary>
        internal const string RaceNoAudioMessage = "audio playback is not available on this machine";

        /// <summary>Seams: the player factory (null = this build has none) and the file dialog
        /// (null result = the player cancelled).</summary>
        internal Func<IRaceTrackPlayer>? RaceNewPlayer = () => new RaceTrackPlayer();
        internal Func<Task<string?>>? RacePickFile;

        /// <summary>The analysis in flight, if any; a test's way to wait for the worker.</summary>
        internal Task RaceAnalysis { get; private set; } = Task.CompletedTask;
        internal bool RaceTrackClockRunning => _raceTrackClock?.IsEnabled == true;

        private IRaceTrackPlayer? _racePlayer;
        private ITrackClock? _raceClock;
        private DispatcherTimer? _raceTrackClock;
        private CancellationTokenSource? _raceAnalysisCts;
        private string _raceTrackName = "";
        private DateTime _raceLastProgressUtc = DateTime.MinValue;
        /// <summary>Bumped per pick (and on teardown) so a superseded worker keeps quiet (WPF _analysisGen).</summary>
        private int _raceAnalysisGen;
        private static readonly AsyncLocal<int?> RaceAnalysisMessageGeneration = new();

        /// <summary>WPF OnPageMessage :353-368. True = a track frame.</summary>
        private bool HandleRaceTrack(string? type, Newtonsoft.Json.Linq.JObject o)
        {
            switch (type)
            {
                case "track-pick": RacePickTrack(); return true;
                case "track-play": RaceTrackPlay(); return true;
                case "track-pause": RaceTrackPause((bool?)o["on"] ?? false); return true;
                case "track-stop": RaceStopTrack(); return true;
                case "track-cancel": RaceCancelAnalysis(postCancelled: true); return true;
                default: return false;
            }
        }

        /// <summary>WPF PostTrack :736: a post from a closed window or a superseded pick is dropped at
        /// both ends of the hop to the UI thread.</summary>
        private void RacePostTrack(object msg)
        {
            int? analysis = RaceAnalysisMessageGeneration.Value;
            bool Current() => !_raceDisposed && !_closing && (analysis == null || analysis == _raceAnalysisGen);
            if (!Current()) return;
            if (Dispatcher.UIThread.CheckAccess()) { try { Post(msg); } catch { } }
            else Dispatcher.UIThread.Post(() => { try { if (Current()) Post(msg); } catch { } });
        }

        /// <summary>WPF PostProgress :759: at most five a second; a forced post always goes.</summary>
        private void RacePostProgress(string stage, double pct, string name, bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _raceLastProgressUtc).TotalMilliseconds < 200) return;
            _raceLastProgressUtc = now;
            RacePostTrack(new { type = "track-progress", stage, pct = Math.Clamp(pct, 0, 1), name });
        }

        private void RacePostChart(TrackChart chart, bool partial, bool authored = false)
            => RacePostTrack(new { type = "track-chart", chart, partial, authored });   // WPF PostChart :771

        /// <summary>WPF PickTrack :781: a cancelled dialog is a cancelled progress post, not an error.</summary>
        private void RacePickTrack()
        {
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    string? path = RacePickFile != null ? await RacePickFile() : await RaceShowFileDialog();
                    if (_raceDisposed) return;
                    if (string.IsNullOrEmpty(path)) { RacePostProgress("cancelled", 0, "", force: true); return; }
                    RaceBeginTrack(path);
                }
                catch (Exception ex)
                {
                    Log.Warning("RaceHost.track-pick: {E}", ex.Message);
                    RacePostTrack(new { type = "track-error", message = ex.Message });
                }
            });
        }

        private async Task<string?> RaceShowFileDialog()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load a track",   // WPF :790 (not localized there either)
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Audio") { Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.wma", "*.flac", "*.ogg" } },
                    FilePickerFileTypes.All,
                },
            });
            return files.FirstOrDefault()?.TryGetLocalPath();
        }

        /// <summary>WPF BeginTrack :810: load into the player so track-play can start it, chart on a worker.</summary>
        private void RaceBeginTrack(string path)
        {
            RaceCancelAnalysis(postCancelled: false);
            _raceTrackName = Path.GetFileName(path);
            _raceLastProgressUtc = DateTime.MinValue;
            RaceLoadTrackFile(path);

            var cts = new CancellationTokenSource();
            _raceAnalysisCts = cts;
            int gen = ++_raceAnalysisGen;
            string name = _raceTrackName;
            var ct = cts.Token;
            RaceAnalysis = Task.Run(() => RaceAnalyzeTrack(path, name, gen, ct, cts));
        }

        /// <summary>WPF LoadTrackFile :825.</summary>
        private void RaceLoadTrackFile(string path)
        {
            try
            {
                if (_racePlayer == null)
                {
                    _racePlayer = RaceNewPlayer?.Invoke() ?? throw new InvalidOperationException(RaceNoAudioMessage);
                    _racePlayer.Ended += OnRaceTrackEnded;
                }
                _racePlayer.Stop();
                _racePlayer.Load(path);
                _raceClock = new LocalTrackClock(_racePlayer);
                StartRaceTrackClock();
                Log.Information("RaceHost: track loaded {Name} ({Dur:0.0}s)", _raceTrackName, _racePlayer.DurationSec);
            }
            catch (Exception ex)
            {
                Log.Warning("RaceHost: track load failed: {E}", ex.Message);
                RacePostTrack(new { type = "track-error", message = ex.Message });
            }
        }

        /// <summary>WPF TryChartWithoutAnalysis :857: an authored chart (the player's, then shipped) or a
        /// cache hit posts once and nothing is analysed; a "none" word pass is redone once Vosk exists.</summary>
        private bool RaceTryChartWithoutAnalysis(string hash, string name)
        {
            var authored = AuthoredCharts.Find(hash, null, out string door);
            if (authored != null)
            {
                Log.Information("RaceHost: chart for {Name} via {Door}", name, door);
                RacePostChart(authored, partial: false, authored: true);
                return true;
            }
            var cached = TrackChartCache.TryLoad(hash);
            if (cached == null) return false;
            if (!AuthoredCharts.IsAuthored(cached) && cached.Analysis?.Words != "vosk-v1" && TrackWordSpotter.ModelAvailable)
                return false;
            Log.Information("RaceHost: chart for {Name} via {Door}", name, AuthoredCharts.DoorCache);
            RacePostChart(cached, partial: false, authored: AuthoredCharts.IsAuthored(cached));
            return true;
        }

        /// <summary>WPF AnalyzeTrack :887, off the UI thread: every failure becomes a track-error.</summary>
        private void RaceAnalyzeTrack(string path, string name, int gen, CancellationToken ct, CancellationTokenSource cts)
        {
            var prior = RaceAnalysisMessageGeneration.Value;
            RaceAnalysisMessageGeneration.Value = gen;
            try
            {
                ct.ThrowIfCancellationRequested();
                RacePostProgress("decode", 0, name, force: true);

                string hash = TrackDecoder.HashFile(path);
                if (RaceTryChartWithoutAnalysis(hash, name)) return;
                Log.Information("RaceHost: chart for {Name} via {Door}", name, AuthoredCharts.DoorGenerated);

                var pcm = TrackDecoder.Decode(path, new Progress<double>(v => RacePostProgress("decode", v, name)), ct);
                ct.ThrowIfCancellationRequested();
                var chart = TrackAnalyzer.Energy(pcm, new Progress<double>(v => RacePostProgress("energy", v, name)), ct);
                ct.ThrowIfCancellationRequested();
                chart.Analysis.Partial = true;
                RacePostChart(chart, partial: true);

                // The slow word pass, which is why the page already has a playable chart.
                var lexicon = TrackLexicon.Build();
                var words = TrackWordSpotter.Spot(pcm, lexicon, new Progress<double>(v => RacePostProgress("words", v, name)), ct);
                ct.ThrowIfCancellationRequested();
                TrackChartWords.Apply(chart, words, lexicon);
                chart.Analysis.Partial = false;
                TrackChartCache.Save(chart);
                RacePostChart(chart, partial: false);
                Log.Information("RaceHost: charted {Name}: {Events} events", name, chart.Events?.Count ?? 0);
            }
            catch (OperationCanceledException)
            {
                // A newer pick superseded this one and owns the plate, so say nothing.
                if (_raceAnalysisGen == gen) RacePostProgress("cancelled", 0, "", force: true);
                Log.Information("RaceHost: track analysis cancelled for {Name}", name);
            }
            catch (Exception ex)
            {
                Log.Warning("RaceHost: track analysis failed for {Name}: {E}", name, ex.Message);
                RacePostTrack(new { type = "track-error", message = ex.Message });
            }
            finally
            {
                RaceAnalysisMessageGeneration.Value = prior;
                if (ReferenceEquals(_raceAnalysisCts, cts)) _raceAnalysisCts = null;
                try { cts.Dispose(); } catch { }
            }
        }

        /// <summary>WPF CancelAnalysis :942: with nothing running, an explicit cancel is answered here.</summary>
        private void RaceCancelAnalysis(bool postCancelled)
        {
            var cts = _raceAnalysisCts;
            _raceAnalysisCts = null;
            if (cts == null)
            {
                if (postCancelled) RacePostProgress("cancelled", 0, "", force: true);
                return;
            }
            try { cts.Cancel(); }
            catch (Exception ex) { Log.Debug("RaceHost.CancelAnalysis: {E}", ex.Message); }
        }

        /// <summary>WPF TrackPlay :957: the run started, so the file starts from its own zero.</summary>
        private void RaceTrackPlay()
        {
            var c = _raceClock;
            if (c == null) return;
            c.Start();
            StartRaceTrackClock();
            PostRaceClock();
        }

        /// <summary>WPF TrackPause :970: the Brake, a host pause and a video pop all land here.</summary>
        private void RaceTrackPause(bool on)
        {
            var c = _raceClock;
            if (c == null) return;
            c.SetPaused(on);
            PostRaceClock();
        }

        /// <summary>WPF StopTrack :982: end of run, exit or teardown.</summary>
        private void RaceStopTrack()
        {
            StopRaceTrackClock();
            RaceCancelAnalysis(postCancelled: false);
            try { _raceClock?.Stop(); }
            catch (Exception ex) { Log.Debug("RaceHost.StopTrack: {E}", ex.Message); }
        }

        /// <summary>WPF OnTrackEnded :993 (the player may raise it off the UI thread).</summary>
        private void OnRaceTrackEnded()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnRaceTrackEnded); return; }
            StopRaceTrackClock();
            RacePostTrack(new { type = "track-ended" });
            Log.Information("RaceHost: track ended");
        }

        /// <summary>WPF StartTrackClock :1001: the 250 ms clock the page integrates between.</summary>
        private void StartRaceTrackClock()
        {
            if (_raceDisposed) return;
            if (_raceTrackClock == null)
            {
                _raceTrackClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _raceTrackClock.Tick += (_, _) => PostRaceClock();
            }
            _raceTrackClock.Start();
        }

        private void StopRaceTrackClock()
        {
            try { _raceTrackClock?.Stop(); } catch { }
        }

        /// <summary>WPF PostClock :1021.</summary>
        internal void PostRaceClock()
        {
            var c = _raceClock;
            if (c == null) { StopRaceTrackClock(); return; }
            RacePostTrack(new
            {
                type = "track-clock",
                t = Math.Round(c.PositionSec, 3),
                playing = c.IsPlaying,
                durationSec = Math.Round(c.DurationSec, 3),
            });
        }

        /// <summary>The track half of WPF DisposeAll :686 (StopTrack, then the player goes).</summary>
        private void DisposeRaceTracks()
        {
            ++_raceAnalysisGen;
            RaceStopTrack();
            _raceTrackClock = null;
            _raceClock = null;
            try { _racePlayer?.Dispose(); } catch (Exception ex) { Log.Debug("RaceHost: player dispose: {E}", ex.Message); }
            _racePlayer = null;
        }
    }
}

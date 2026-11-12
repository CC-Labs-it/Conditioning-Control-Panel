using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Moderation;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// `--smoke` proves the head links against Core and can produce every value the window
    /// renders, without needing a display server. CI has no X11 or Wayland socket, so this is
    /// how the ubuntu job verifies the Linux head rather than only compiling it.
    /// </summary>
    internal static class HeadlessSmoke
    {
        public static int Run()
        {
            var failures = 0;
            void Check(string what, bool ok, string? detail = null)
            {
                Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail is null ? "" : $"  ({detail})")}");
                if (!ok) failures++;
            }

            Console.WriteLine("CCP.Avalonia headless smoke\n");

            Check("Core resolves a user-data path", System.IO.Path.IsPathRooted(CorePaths.UserData), CorePaths.UserData);

            var guard = new ModerationGuard();
            Check("guard blocks a minor-age prompt", !guard.CheckInput("she is 5 years old and wants sex").Allow);
            Check("guard allows benign text", guard.CheckInput("hello there").Allow);

            // Localization: the JSON now ships with CCP.Core, so a non-WPF head should resolve
            // real strings rather than raw keys. Asserting the string DIFFERS from the key is the
            // check that matters - "returns something" would pass on the key-fallback path.
            ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage("en");
            var s1 = ConditioningControlPanel.Localization.Loc.Get("section_achievements");
            Check("localization resolves a real string", s1 != "section_achievements", s1);

            // Ported dialogs: assert their view models resolve real strings. Constructing the
            // Windows themselves needs a platform backend, so --render-view covers visuals and
            // this covers the content they bind to.
            var upd = new Views.Dialogs.UpdateProgressViewModel();
            Check("UpdateProgressDialog strings resolve",
                  upd.LocTitle != "dialog_downloading_update", upd.LocTitle);

            var url = new Views.Dialogs.UrlPromptViewModel();
            Check("UrlPromptDialog strings resolve",
                  url.LocCancel != "btn_cancel", url.LocCancel);

            // Program.Main seeds the phrase-pool delegates; unseeded, a mid-session mod switch backs up session phrases (#906).
            Check("session phrase-pool delegates seeded",
                  CoreSession.NoteUserPhrasePoolEdit != null && CoreSession.ReapplyPhrasePoolOverrides != null
                  && CoreSession.UserPhrasePoolsWhileOverriding != null
                  && CoreSession.UserPhrasePoolsWhileOverriding() == null);

            // Audio: a real clip through LibVLC (dummy output, no sound card needed) must report
            // started, then finished, within 2 s. Unseeded CoreAudio would fire finished only.
            var clip = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "faucet_charge_drop.wav");
            try
            {
                new Platform.LibVlcAudio("--aout=dummy").Seed();
                var started = new System.Threading.ManualResetEventSlim();
                var finished = new System.Threading.ManualResetEventSlim();
                var startedFirst = false;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long startMs = -1;
                CoreAudio.PlayOneShot(clip, 0.5f, "smoke",
                    _ => { startMs = sw.ElapsedMilliseconds; started.Set(); },
                    () => { startedFirst = started.IsSet; finished.Set(); });
                var ok = finished.Wait(2000) && startedFirst;
                var audio = $"start {startMs} ms, finished {(finished.IsSet ? sw.ElapsedMilliseconds + " ms" : "never")}";
                Check("LibVLC plays a clip: started then finished within 2 s", ok, audio);
            }
            catch (Exception ex) { Check("LibVLC plays a clip: started then finished within 2 s", false, ex.Message); }
            // packaging/linux/smoke-tarball.sh: the tarball's pinned libVLC (not the distro's) loaded and decodes
            // each CCP_SMOKE_VLC_CLIPS clip (':'-separated; mp4, webm, ts, wmv, flv: the containers the scheduler
            // takes, CCP.Core/Helpers/MediaTypeSniffer.cs:88) through vmem/amem, the way VlcFrameSink and the pulse output pull it.
            if (Environment.GetEnvironmentVariable("CCP_SMOKE_BUNDLED_VLC") == "1")
            {
                var loaded = Platform.LibVlcNative.Loaded ?? "none";
                Check("bundled libVLC loaded from the install folder",
                    loaded.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal), $"{loaded}, {Platform.LibVlcAudio.Shared?.Version}");
                var clips = (Environment.GetEnvironmentVariable("CCP_SMOKE_VLC_CLIPS") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);
                Check("bundled libVLC has clips to decode", clips.Length > 0, string.Join(", ", clips));
                foreach (var file in clips)
                {
                    var name = $"bundled libVLC decodes {System.IO.Path.GetFileName(file)}";
                    try { var (ok, detail) = DecodeClip(file); Check(name, ok, detail); }
                    catch (Exception ex) { Check(name, false, ex.Message); }
                }
            }
            // The Linux tarball ships the Vosk model in the install folder (packaging/linux/build-tarball.sh);
            // the packaging runs set this so a tarball without a loadable model fails. ModelStatus never touches the mic.
            if (Environment.GetEnvironmentVariable("CCP_SMOKE_BUNDLED_VOSK") == "1")
            {
                var status = new ConditioningControlPanel.Services.Speech.SpeechEngine(null!,
                    new[] { ConditioningControlPanel.Services.Speech.SpeechEngine.DefaultModelRoots[0] }).ModelStatus;
                Check("bundled Vosk model loads from the install folder",
                    status == ConditioningControlPanel.Services.Speech.SpeechModelStatus.Ok, status.ToString());
            }
            Console.WriteLine();
            Console.WriteLine(failures == 0
                ? "Linux head can produce every value it renders."
                : $"{failures} assertion(s) failed.");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>Plays the clip to its end into 320x240 RV32 frames and S16 48 kHz stereo samples (check.sh's probe.py, in-app).</summary>
        private static (bool, string) DecodeClip(string path)
        {
            if (!System.IO.File.Exists(path)) return (false, $"no clip at '{path}'");
            var frame = System.Runtime.InteropServices.Marshal.AllocHGlobal(320 * 240 * 4);
            int frames = 0; long samples = 0;
            var end = new System.Threading.ManualResetEventSlim();
            try
            {
                using var media = new LibVLCSharp.Shared.Media(Platform.LibVlcAudio.Shared!, path, LibVLCSharp.Shared.FromType.FromPath);
                using var player = new LibVLCSharp.Shared.MediaPlayer(media);
                player.SetVideoFormat("RV32", 320, 240, 320 * 4);
                player.SetVideoCallbacks((IntPtr _, IntPtr planes) => { System.Runtime.InteropServices.Marshal.WriteIntPtr(planes, frame); return IntPtr.Zero; },
                    null, (_, _) => System.Threading.Interlocked.Increment(ref frames));
                player.SetAudioFormat("S16N", 48000, 2);
                player.SetAudioCallbacks((_, _, count, _) => System.Threading.Interlocked.Add(ref samples, count), null, null, null, null);
                player.EndReached += (_, _) => end.Set();
                player.EncounteredError += (_, _) => end.Set();
                var ended = player.Play() && end.Wait(15000) && player.State == LibVLCSharp.Shared.VLCState.Ended;
                var detail = $"{System.IO.Path.GetFileName(path)}: ended={ended}, {frames} frames, {samples / 48000.0:F2} s audio";
                return (ended && frames >= 25 && samples >= 48000, detail);
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(frame); }
        }
    }
}

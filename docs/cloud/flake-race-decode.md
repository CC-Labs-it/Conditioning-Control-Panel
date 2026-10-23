- Close decode cancellation gaps without changing the stereo/mono conversion, progress ranges, or existing assertions. The native wait now observes the token even when completion wins the first wait; cancellation is checked after native stop, terminal progress, and WAV conversion. Temporary WAV cleanup remains in `finally`.
- Check cancellation in Core before calling the head decoder and before accepting its returned PCM. Keep Core free of head/native dependencies.
- Add deterministic Core regressions for a pre-cancelled request and a head that cancels just before returning. Both fail against the base production code with `Assert.Throws() Failure: No exception was thrown`; both pass after the fix. Add head regressions for a missing source with a pre-cancelled token and cancellation from the synchronous terminal progress report. Keep the existing stereo conversion test unchanged.
- Reference inspected: read-only `parity/trunk`, race import commit `b632b54d758248fec0751af93980e6bf3023993f`. No new trunk code imported; the existing head implementation wins. The required base already includes the earlier entry-guard fix `828ccd4ded80c3b66c85197da23a908241ac4b0e`.

## Verification limits

All commands use .NET SDK 10.0.402 and `DiffEngine_Disabled=true`. Release builds of Core, Avalonia, and every Linux test project in `.github/workflows/build.yml` succeeded with zero errors (existing warnings remain):

| `dotnet build <project> -c Release` | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |

- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.`
- Before the production fix: `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --filter FullyQualifiedName~DecoderRejectsCancellationBeforeAndAfterTheHead`: exit 1, 2 failed / 0 passed, both with no exception thrown.
- After the fix: `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --filter FullyQualifiedName~RaceCoreSeamsTests`: exit 0, 5 passed / 0 failed / 0 skipped.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: first run exit 1, 3744 passed / 1 failed / 5 skipped. The unchanged `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected Loops but got Clips at line 185. An isolated run with `--filter FullyQualifiedName~Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` passed (exit 0, 1 passed). One full rerun passed (exit 0, 3745 passed / 0 failed / 5 skipped). No test was weakened or quarantined. The five pre-existing Vosk tests skip because the model is unavailable; its download is blocked by this environment.
- Avalonia tests were compiled, **not run**, per the supplied cloud font-creation limitation. In particular, native `RaceTrackPlayerTests` and the 20x proof remain for GitHub/rcheck. Other Avalonia suites were only compiled. No Windows runtime/desktop tests were run.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 552 existing warnings / 0 errors. The WPF reference head and test assembly compile on Linux with these flags; Windows code was not executed.

## Race analysis

`Tests/CCP.Avalonia.Tests/RaceTrackPlayerTests.cs:66` is the reported `Assert.Throws` assertion: it passes an already-cancelled token directly to `RaceTrackPlayer.Decode`. The original loop checked cancellation only inside the body of `while (!done.Wait(100))`. LibVLC can finish a short WAV inside that first wait. `EndReached` signals `done`, the body never executes, and decoding returns normally. It is a completion-versus-cancellation ordering bug, independent of codec speed assumptions.

The required base already checks the token at `CCP.Avalonia/Views/Games/RaceTrackPlayer.cs:232`, so that exact pre-cancelled assertion cannot return normally at this base. This unit preserves that fix and covers the remaining cancellation window: the token can be cancelled after entry, while the final wait succeeds, or by the final `Report(1)` callback. The token-aware wait at line 270 and post-stop check at line 281 close the wait boundary. Checks at lines 238 and 240 close terminal progress and PCM conversion boundaries. Core's checks at `CCP.Core/Services/Race/TrackPcm.cs:40` and line 47 prevent a head from publishing cancelled PCM through the shared seam.

Other candidates: tests disable parallelization assembly-wide; tests save/restore `TrackDecoder.DecodeProvider`; source directories and transcode WAV names are unique; every transcode owns a new Media, MediaPlayer, and completion event, while only LibVLC itself is shared. `Decode` is synchronous: it waits for native completion and calls `Stop` before reading the WAV. No ffmpeg process is involved. Missing LibVLC would fail setup (Linux CI rethrows), not produce the observed missing cancellation exception. There is no evidence that shared decoder state, test file reuse, or library availability caused that assertion.

For the supervisor's native proof on GitHub (LibVLC and fonts installed):

```bash
export DiffEngine_Disabled=true
for attempt in $(seq 1 20); do
  dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~RaceTrackPlayerTests || exit "$?"
done
```

To break the behaviour deliberately: remove Core's entry/post-provider cancellation checks; the two new Core theory cases must fail with no exception thrown. In the head, restore the immediate `return ReadMono16(File.ReadAllBytes(wav));` after `TranscodeToWav`, removing both following cancellation checks. `DecodeRefusesCancellationAtCompletion` must then fail: its callback cancels on the caller thread after the native wait/stop and the old implementation returns PCM. This regression calls the head directly, so Core's guard cannot mask the head bug.

## Assumptions

- The requested base is authoritative. Since it already contains the historical pre-cancel fix, retain it and harden the remaining completion boundary instead of claiming the old assertion reproduces on this base.
- Cooperative cancellation observed before PCM acceptance should throw even when native completion is already signalled. Conversion, volume, playback timing, and WPF reference code stay unchanged.
- No new imports, abstractions, interfaces, configuration, PR, or workflow changes are needed for this unit.

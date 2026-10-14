# deeper-core-e: Deeper engine, dispatch seams and speak rules move to CCP.Core

Base: `avalonia-port/quiz-core-a`. Branch: `avalonia-port/cloud-deeper-core-e`.

## Already in CCP.Core before this unit (left alone)
`Models/Deeper/*` and `Services/Deeper/` DeeperConfig, DeeperLocalLibrary, EnhancementAutoTagger,
EnhancementCapabilities, EnhancementFetcher, EnhancementImportRules, EnhancementLibrary(+Entry, Filter),
EnhancementMediaBundler, EnhancementResolver, EnhancementSerializer, EnhancementValidator,
HtMetadataFetcher, MandatoryVideoEnhancementScanner, MediaDurationCache, UrlSafety. None of them
held a copy of the rules moved here, so each rule still has one home.

## What changed
- **`IPlaybackTimeSource` moved** (`git mv`) to `CCP.Core/Services/Deeper`. `GetVideoRect()` now
  returns the Core `PlaybackRect` instead of `System.Windows.Rect`.
- **`EnhancementEngine` moved** (`git mv`, about 27 changed lines) to Core. Its WPF-only parts now go
  through seams:
  - `App.Webcam` became `IEnhancementWebcam`.
  - `Application.Current.Dispatcher` became `CoreDispatch.Post`.
  - `App.Overlay`, `App.Flash` and `App.Subliminal` on Stop became `IEnhancementRunCleanup` on the dispatcher.
  - `App.Haptics` became `CoreHaptics.Service`.
  - `App.Logger` became Serilog `Log`.
  - `System.Windows.Point` became `PlaybackPoint`.
- **New `EnhancementSeams.cs`** (Core) holds `PlaybackRect`, `PlaybackPoint`, `IEnhancementWebcam` and
  `IEnhancementRunCleanup`. Adapted from trunk `0f7262df2`
  ("feat(parity deeper): rule engine fires in the player…"), which also supplied the engine adaptation.
- **`IActionDispatcher.cs` split.** `EnhancementDispatchContext`, `IActionDispatcher`,
  `LoggingActionDispatcher` and `RecordingActionDispatcher` are now in Core (from trunk `0f7262df2`).
  `RealActionDispatcher` stays in WPF: the file is renamed `RealActionDispatcher.cs` and still
  shows as a move. It now implements `IEnhancementRunCleanup`, which calls the same three App
  services the engine used to call.
- **Fix over trunk:** `RecordingActionDispatcher` also implements `IEnhancementRunCleanup` and passes
  each call to its inner dispatcher. The host always gives the engine the recorder, so with trunk's
  version Stop would quietly skip resetting overlay bands and the one-shot flash and subliminal
  cancel. `StopRetiresOverlaysAndOnlyTheOneShotKindsThisRunFired` fails without this.
- **New `SpeakPromptPlan`** (Core) holds the speak effect's rules, taken out of WPF
  `SpeakPromptSession`: cue default, interval clamp, reps clamp, feedback defaults, hard deadline,
  hold decision (`HoldsPlayback`, `PausesAtCue` for #1221, `HoldStepAt` with the 0.30 s lookahead) and
  listen-result classification. The WPF session keeps the consent dialog, cue UI, DispatcherTimers,
  recognizer, Autonomy eviction and giggle, and asks the plan for every decision.
- **New `WebcamEnhancementEvents`** (WPF) adapts `WebcamTrackingService` to `IEnhancementWebcam`. It
  marshals to the UI thread the same way the engine used to (inline on the UI thread, otherwise
  BeginInvoke, dropped after shutdown). WPF installs no `CoreDispatch.PostProvider`, so this keeps
  engine state on the UI thread. `EnhancementHostService` creates one per bind and disposes it on unbind.
- WPF time sources (`VideoServiceTimeSource`, `BrowserVideoTimeSource`,
  `EnhancementAudioPlayerTimeSource`) return `PlaybackRect`. The DIP math is unchanged.
- One diagnostic line changed wording: "but App.Webcam is null" now reads "but no webcam service"
  (core-guards forbids `App.` in Core).
- **Tests (new, Core):**
  - `EnhancementEngineCoreTests` covers four things: a time_reached rule firing, Stop cleanup through
    the recorder, gaze mapped onto `PlaybackRect` (plus the Empty skip and the unsubscribe), and the
    logging dispatcher's dry-run line.
  - `SpeakPromptPlanTests` covers defaults and clamps, deadlines, hold steps, the #1221 pause at cue
    time, and classification.
  - No sleeps or delays.
  - There were no existing WPF tests for these files to move.

## Next units
- `EnhancementHostService` to Core: library/sidecar resolution and the bind/unbind lifecycle, with
  App.Webcam, App.Video, App.Browser and App.Achievements behind seams.
- An Avalonia `IActionDispatcher` and `IEnhancementRunCleanup` (overlay, flash, subliminal, haptics,
  bubbles), plus Avalonia time sources (LibVLC video, audio player). Together these run the engine in
  the Avalonia player.
- An Avalonia `SpeakPromptSession` built on `SpeakPromptPlan` (cue overlay, timers, mic consent).
- The `RealActionDispatcher` pure helpers (band haptic restart math, overlay band-kind bookkeeping)
  to Core, once a second head needs them.

## Verification limits
- `dotnet build`, all succeeded:
  - `CCP.Core`
  - `CCP.Avalonia`
  - `Tests/CCP.Avalonia.Tests`, `.Consent`, `.Language`, `.CatalogueStartup`, `.Scheduler`, `.Report`,
    `.GeneralSettings`, `.Snapshot`
  - `Tests/CCP.Core.Settings.Tests`, `Tests/CCP.Core.Content.Tests`
  - WPF `ConditioningControlPanel.csproj` and `Tests/ConditioningControlPanel.Tests`, built on Linux
    with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 2786 passed, 2 failed, 6 skipped. The 2 failures are in code
  this unit does not touch, and both come from the container:
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted…`: tests run as root, so the
    permission setup doesn't hold.
  - `LoopbackOAuthTests…("[::1]")`: the container has no IPv6 loopback.
- The new tests: 9/9 pass. With the recorder forward removed, the Stop test fails ("Collections differ").
- `dotnet test Tests/CCP.Avalonia.Tests --filter Deeper|Enhancement`: 13/13 passed.
- The WPF code compiles, but none of it was run: the playback time sources, webcam adapter,
  RealActionDispatcher cleanup and SpeakPromptSession need Windows. The real-webcam threading path is
  untested here.

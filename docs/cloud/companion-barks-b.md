# companion-barks-b

Base: `avalonia-port/lock-title`. Claimed and completed only on
`avalonia-port/cloud-companion-barks-b`; no PR or merge.

- Extended the existing Core `BarkEngine`; no second bark engine. Idle dispatch
  uses eligible-rule rotation persisted across launches, no immediate repeat on
  recycle, the WPF 0.35 conditional-rule bias, and the existing timing/safety gates.
- Added dry-run evaluation (`DryRun`, `CCP_BARK_DRYRUN=1`) without rendered speech,
  brain echo, or persisted latch/rotation changes; in-memory gates still advance.
- Added live-tier persisted keys and the whisper/narrator gate rules. Subliminal
  playback reports its actual started duration to the existing engine, including
  the WPF 250 ms tail and extend-only semantics. Text-only possession and
  emergency-exit attributions retain their whisper exemption.
- The open tube now owns the companion slider's idle cadence, restarts on speech
  and slider changes, checks speech readiness, and stops its timer on close.
  Webcam-conditional idle rules and the brain's voiced/held echo use live state.
- Ordinary voiced barks enter `Giggle`; priority barks retain `GigglePriority`.
  Delayed ordinary speech stays text-only, as in WPF. Idle sound respects engine,
  unpaused session, opt-in, and the five-minute monotonic panic quiet. Local panic
  rungs (except settings-palette dismissal), stop-all and remote panic arm quiet;
  explicit user Start/Resume clears it. Panic cuts pending queued audio, and the
  remote head callback also silences an idle clip that is already playing.
  The Core helper is named `CompanionIdleVoicePolicy` to avoid shadowing WPF's
  frozen public `CompanionIdleVoice` type.
- Updated only ledger row `feat-companion-barks`, retaining `stub` and naming
  **companion-barks-c** for the remaining work below.
- Read-only import source `parity/trunk` fetched at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported from that branch.
  Behavior was adapted directly from this base's frozen WPF sources.

Changed production files: `CCP.Core/Services/Bark/BarkEngine.cs`,
`CCP.Core/Services/Companion/CompanionIdleVoicePolicy.cs`,
`CCP.Core/Services/RemoteControl/RemoteCommands.cs`,
`CCP.Avalonia/Platform/BarkHead.cs`, the tube's main/Idle/SpeechQueue partials,
`WorkshopBehaviorCell.axaml.cs`, `SubliminalWhisperShow.cs`, and the shell's
PanicKey/StartStop/SessionRun/RemoteControl/PanicSurfaces files. Tests cover the engine, remote
panic, head routing/idle lifecycle and live whisper callback.

## Verification limits

Environment: Linux, .NET SDK 10.0.402 (`PATH=/workspace/dotnet:$PATH`),
`DiffEngine_Disabled=true`. Commands below ran from the repository root.

- `dotnet build <project> -c Release -m:1 --nologo
  -p:CreateHardLinksForCopyLocalIfPossible=true
  -p:CreateHardLinksForAdditionalFilesIfPossible=true`: **0 errors** for Core,
  Avalonia, and all 11 Linux test projects listed in `build.yml`: Core,
  Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup,
  Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language,
  Avalonia.Report, Avalonia.Scheduler, and Avalonia.Snapshot.
  Initial parallel copies exhausted workspace disk and caused build/compiler
  failures. The results above are from a clean rebuild after removing generated
  outputs and enabling hard-link copies, then rebuilt after the final helper
  rename and remote panic fix; no source or assertions were removed.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed**;
  429 XAML type references checked against 1700 app / 1952 Core types.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release
  --no-build --no-restore`: final **Failed: 0, Passed: 2949, Skipped: 5,
  Total: 2954** (37 s). An earlier run failed the unchanged
  `BoardTilesTests.A_frame_fits_the_budget` (30.15 ms under concurrent build
  load); the final full-suite rerun passed it. The five existing Vosk skips
  are described below.
- The same Core test command with
  `--filter 'FullyQualifiedName~BarkEngineTests|FullyQualifiedName~RemoteRelayTests'`:
  **Passed: 49, Failed: 0, Skipped: 0**, including the remote panic quiet guard.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release
  --no-build --no-restore --filter FullyQualifiedName~<class>` in separate hosts:
  **CompanionBarkHeadTests 8, SubliminalWhisperShowTests 6,
  TubeSpeechQueueTests 4, TubePanicTests 1 passed**, all zero failures/skips.
  Font registration in the two existing tube classes now matches production;
  their assertions are unchanged.
- Fails-before proof: temporarily restoring the original ordinary voiced route
  made `OrdinaryVoicedBarkQueuesWithoutPreemptingAndPanicCancelsIt` fail with expected
  `first`, actual `queued voice`; the restored implementation passes.
- Additional fails-before proof: with the original remote callback,
  `RemotePanicCutsTheCurrentIdleClipAndBubble` failed with expected clip stop
  count **1**, actual **0**. The final head suite passes this regression.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -m:1 -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false` with the same hard-link
  flags: **564 warnings, 0 errors** (2m 28s). Both WPF app and tests compiled;
  Windows tests were not run. The earlier attempt was stopped after identifying
  the shared type-name conflict; the final policy rename removes it.
- `git diff --check`: passed. Added lines vs base: **783**.

Raw command output is retained locally in `/workspace/ccp-barks-evidence/`
(`*.build-final.log`, `core-policy-final.log`, `core-focused-final.log`,
`remote-red.test.log`, `core-guards-final.log`), `/workspace/bark-final-<ClassName>.log`, and
`/workspace/bark-head-red-final.log`.

All test profiles are the repository's existing `CCP_USERDATA_DIR` sandboxes;
headless tests use the production font registrations. No real user profile,
desktop, camera, audio device, or secrets were accessed. No render was required
for this nonvisual brief. Live Windows/audio playback and the real desktop idle
cadence were not run. Existing optional Vosk tests require a model not installed
in this environment; no test was deleted, skipped by this change, or weakened.

## Follow-up: companion-barks-c

- Port the head's Chaos narrator producer and seed `IsChaosNarratorPlaying`.
  Core's floor and safety/guaranteed exceptions are tested, but the current
  Avalonia `ChaosNarrator` is still a no-op nested in `ChaosOverlayWindow`.
- Adapt WPF's newer `ClearStaleSpeechLatch` watchdog to this tube's pending speech
  state before closing the parity row. The idle tick currently honors existing
  `IsSpeechReady` and queue lifecycle; it does not implement that repair system.
- The other approximately 30 WPF `WireSubscriptions` sources are outside this
  brief, as directed. No new trigger names or parallel engine were introduced.

The remaining narrator playback/ducking/lifecycle and watchdog work needs its
own coherent tested slice; the row is not promoted based on unseeded delegates.

# fin4-overlay-speak-cue: dependency analysis

- No implementation or ledger change. `overlay-speak-cue` remains `stub`: the persistent cue has no production ShowCue/HideCue caller in this head.
- Base inspected: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. The checkout initially differed; fetched the required base and checked out that exact commit before analysis.
- Read-only trunk inspected: `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported. Its `CCP.Avalonia/Views/Deeper/RealActionDispatcher.cs:334` explicitly handles `EffectTypes.Speak` with `LogNoTwin("speak")`; importing this dispatcher cannot complete the row.

## WPF behaviour and remaining gaps

At the pinned base, `ConditioningControlPanel/Overlays/SpeakCueOverlay.cs` defines:

1. Ignore null/blank cues; marshal show/hide to the UI thread; create on first use and update text in place.
2. A transparent, topmost, nonactivating, click-through window spanning the virtual screen; centre the cue over the primary work area, 120 DIP from its top.
3. Outlined pink `SAY IT` (22 DIP, opacity 0.8, stroke 2) above white cue text (60 DIP, stroke 3), with a 2 DIP gap and near-black outline.
4. Linear 180 ms fade to 0.92, with no automatic dismissal; hide fades over 180 ms; teardown closes immediately.
5. Retain the hidden WPF window between prompts. Avalonia deliberately closes after fade-out to avoid retaining an application window under OnLastWindowClose; this existing documented head-lifetime adaptation is not a new change.

These presentation behaviours already exist in `CCP.Avalonia/Views/Overlays/SpeakCueOverlay.cs` and `CCP.Core/Services/Deeper/SpeakCueState.cs`, with existing focused tests, a render-all constructor, panic surface `speak-cue`, and shell teardown. The platform shim also routes HWND handles to Win32; its X11 name does not mean Windows is excluded.

The missing user-visible behaviours are all controlled by the unported **Deeper playback runtime and SpeakPromptSession**, rather than additional overlay drawing:

- A live persistent-mode speak action must show the authored/default cue after validating target, speech availability and microphone consent. Intermittent mode must use subliminal flashes instead.
- Start replaces the session for an EffectId; Stop removes it; Restart/Update leave the running session alone. Rule-fired one-shots need engine-token cancellation.
- Completion, region/band exit, deadline, recognition failure and engine stop must hide the cue and stop recognition/timers. Required repetitions, feedback and silent retries determine when completion occurs.
- UntilSatisfied prompts must loop or pause playback as authored, release a held pause, and borrow/restore the wake/PTT microphone owner. WPF has a 120 ms hold watch, 10 s listens, bounded availability retries, 600/700 ms feedback delays and a 30 s one-shot deadline; drawing a cue from the editor alone would bypass these rules.

## Dependency proof and line budget

Reproducible searches at the pinned base:

```sh
rg -n 'SpeakCueOverlay\.(ShowCue|HideCue|CloseActive)' ConditioningControlPanel CCP.Avalonia
rg -n 'EnhancementHost|DispatcherFactory|IPlaybackTimeSource' CCP.Avalonia
rg --files | rg 'RealActionDispatcher|SpeakPromptSession|EnhancementAudioPlayer|PlaybackTimeSource'
```

The first search finds WPF's live ShowCue/HideCue calls in `Services/Deeper/SpeakPromptSession.cs:127,151`, but only panic/shell CloseActive calls in Avalonia. Thus P122 (dead WPF caller) does **not** apply.

The second search finds explanatory comments in Avalonia's `EnhancementPlayerWindow.axaml.cs`, not a production engine binding. Its play handler drives a browser script directly; its audio branch explicitly refuses playback. `LoadFromFile`/`LoadFromMemory` validate data without constructing a host/engine. Core already contains `EnhancementHostService`, `EnhancementEngine`, `SpeakPromptPlan`, and the playback contract; these should be reused by the runtime lane.

Porting only SpeakPromptSession would still leave no caller. Porting only a dispatcher would still leave no engine binding. A complete path also needs the player's media time-source adapters, transport/seek events, host load/bind/unbind/close ownership, and recognition/consent/microphone teardown tests. The WPF session (352 lines), dispatcher (502), audio player (237), and audio time-source adapter (54) alone total 1,145 reference lines, before the browser adapter, player integration and focused behavioural tests. Trunk's adapted dispatcher alone is 598 lines and still omits speak. This is a runtime feature port, not an overlay-sized host shim; adapting and testing all those paths does not fit this unit's 1,000-added-line limit. No partial editor/test-only cue button is added and the row is not promoted.

## Assumptions

- Follow the task's pure-dependency rule: preserve the existing overlay and honest `stub` row until the Deeper playback runtime plus speak session can reach it through real media playback.
- Keep the base's documented close-after-fade lifetime adaptation; do not introduce a hidden application window solely to match WPF's render-thread workaround.

## Verification limits

Verification results are recorded below. No Windows desktop, microphone, recognition model, or real display is used. No new runtime surface or behaviour is introduced, so no new panic registration or behavioural tests are required. The test-plan coverage script is absent on this base.

- Installed .NET SDK 10.0.402 using the explicit version after channel discovery returned HTTP 403.
- `dotnet build -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia, and all Linux-compatible test projects named by `.github/workflows/build.yml`: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests. Existing warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML type references checked). Initial restricted execution could not create temporary files; reran with the required temporary-file access.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (file absent, exit 127).
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3,952 passed, 5 skipped (Vosk model cases), 1 failed. The failure was the existing `BoardTilesTests.A_frame_fits_the_budget` performance threshold (13.99 ms) during concurrent compilation. Retried that test with `--no-build --filter FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget`: exit 0, 1 passed. The aggregate suite is not claimed fully green.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~SpeakCueOverlayTests`: exit 1; 2 passed, 2 failed. ShowCue expected eyebrow/cue text but saw no children; show-during-fade-out expected 0.92 alpha but saw 0. This is the unchanged base implementation. The result is consistent with the task's reported font-creation limitation (OutlinedTextBlock defaults to Segoe UI), but the caught presentation exception is not exposed by the assertions, so the precise cause is unconfirmed. These tests were attempted, not skipped or weakened.
- No real desktop, Windows runtime, microphone, Vosk model download, or render-all run was used. No claim of live parity or verified rendering is made.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 existing warnings, zero errors. WPF and its tests compiled on Linux; Windows tests and native Windows overlay behaviour were not run.
- `git diff --cached --check`: exit 0. Documentation-only result remains below the 1,000-added-line cap.

NOOP: Deeper playback runtime (media time-source/host binding and SpeakPromptSession/action dispatcher).

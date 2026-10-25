# fin5-overlay-speak-cue

- Dependency-only analysis; no production or test code changed. `overlay-speak-cue` remains `stub`; it must not be promoted on render-only evidence.
- Verified clean checkout at required base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a` after fetching the base and resetting the cloud `work` branch. Work branch: `avalonia-port/cloud-fin5-overlay-speak-cue`.
- Read-only trunk inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (`git fetch origin parity/trunk`). No imports: its Avalonia `Views/Deeper/RealActionDispatcher.cs` explicitly logs `speak` as having no twin. Importing it alone does not close this row.

## WPF comparison and missing behavior

Reference paths below are pinned to the required base SHA above.

The WPF overlay in `ConditioningControlPanel/Overlays/SpeakCueOverlay.cs` has:

1. A virtual-screen, transparent, topmost, nonactivating, click-through tool window.
2. Blank cue rejection; thread-safe show/update; replacement of the label in the active window.
3. Primary-work-area centering with clamped relative left/top offsets plus 120 DIP vertically.
4. An outlined pink `SAY IT` eyebrow (22, opacity 0.8, stroke 2) and white cue (60, stroke 3, 2 DIP top margin), near-black outline.
5. Linear 180 ms fades to 0.92/from current opacity, indefinite display, explicit hide, and immediate teardown.
6. One retained window, hidden and cleared after fade-out, reused on the next show.

Items 1–5 already have counterparts in the Avalonia overlay/Core state, with existing focused tests. Item 6 is an explicit pre-existing head adaptation: Avalonia closes after fade-out instead of retaining a hidden window, avoiding an `OnLastWindowClose` lifetime leak. This is documented in the overlay and asserted in its tests; this unit does not reverse that lifetime decision. Showing during fade-out already supersedes the pending close. Unsupported overlay platforms refuse the cue with a once-per-process warning.

The remaining user-visible gap is that **none of the persistent cue's show/update/hide behavior is reachable from authored Deeper playback**. Specifically, the head lacks the WPF session lifecycle that:

1. Validates target/speech availability, asks for mic consent at most once per run, and skips declined/unavailable prompts.
2. Releases the wake/PTT microphone owner, waits up to 24 × 25 ms for release, and restores voice modes on completion or stop.
3. Shows a persistent cue at prompt start; otherwise flashes an intermittent cue immediately and on its configured timer (minimum 80 ms).
4. Recognizes the target in 10-second attempts, applies repetitions and optional deadline, retries temporary unavailability (200 ms), and handles unavailable speech (400 ms, bounded attempts).
5. Flashes correct/incorrect feedback, sends successful feedback to the avatar without sound, and observes 600/700 ms retry delays.
6. Holds UntilSatisfied playback by loop/seek or pause at the region boundary on a 120 ms timer; pauses at the cue for a regionless pause-mode prompt; resumes only a pause owned by this session.
7. Fades the persistent cue out on completion, deadline, band exit, or cancellation; stops timers/listening and restores the mic owner idempotently.
8. Tracks sessions by EffectId, replaces a previous Start, ignores Restart/Update, handles Stop, and binds one-shot session cancellation to engine lifetime.

These are live WPF behaviors, not a P122 dead-caller exception. Pinned grep proof:

```sh
git grep -n 'SpeakCueOverlay\.' 73b1ccf0d793bb3fde6091c18a9fa6b5c692964a -- ConditioningControlPanel
```

The external callers are `Services/Deeper/SpeakPromptSession.cs:127` (ShowCue) and `:151` (HideCue). `RealActionDispatcher.cs:277–338` dispatches the session from the live Speak effect case, including band and one-shot lifetimes.

## Dependency and scope decision

The blocking feature is the **Deeper playback runtime host and spoken-prompt dispatcher/session** (the ledger's historical deeper-core-c/d dependency; current player row `views-deeper-enhancement-player`). Core already supplies `EnhancementHostService`, `EnhancementEngine`, `SpeakPromptPlan`, and `IPlaybackTimeSource`; these must be reused, not duplicated.

At this base, Avalonia has no production dispatcher or SpeakPromptSession, no assignment to `EnhancementHostService.DispatcherFactory`, and no player call to `EnhancementHostService.Bind`. `EnhancementPlayerWindow.axaml.cs:777–793` explicitly documents the absent time source/dispatcher and reports audio playback unavailable. Loading an enhancement currently validates and populates view state without binding an engine. Existing `DeeperLocalAudio` is the editor's transport, not a bound enhancement player. Speech and consent facilities elsewhere in the head are present; speech itself is not claimed to be unported.

A complete fix therefore needs playback-source/host binding (including seek, pause, end, unload and shutdown), a real spoken-session dispatcher, the session/mic/feedback lifecycle above, and focused tests of each new behavior plus cancellation/panic. Porting just ShowCue from a play button would show a fake prompt without the WPF completion or release behavior. The WPF session, dispatcher and audio transport are respectively 352, 502 and 237 lines before adaptation or tests (1,091 total); the head's player and lifecycle adapters also require integration. These are scope indicators, not a claim that every WPF line must be imported. Even adapting only Speak, a faithful runtime plus meaningful per-behavior and integration tests is judged beyond this unit's 1,000-added-line budget. Per the task's pure-dependency rule, this branch contains only this analysis. No new surface needs panic registration; existing `speak-cue` panic and shell-close registrations remain.

## Verification limits

Validation results are recorded below. No code behavior changed, so no new fail-before/fix test applies. Existing coverage is `SpeakCueStateTests`, `SpeakPromptPlanTests`, and `SpeakCueOverlayTests`; numbered manual steps already exist in `docs/avalonia-test-plan.md:466–467`. The coverage script named in the task is absent at this base. Windows desktop behavior, microphone recognition, and live enhancement playback cannot be verified by this documentation-only unit. Render-all discovery already includes the overlay via its parameterless render constructor; no new render proof or P122 promotion is claimed.

### Commands and results

- SDK channel installation encountered a denied `ci.dot.net` lookup; pinned SDK `10.0.100` installed successfully from the supported Microsoft builds host.
- `dotnet build <project> -c Release --nologo`: exit 0, zero errors for all 14 Linux projects below (existing warnings remain).
  - `CCP.Core/CCP.Core.csproj`
  - `CCP.Avalonia/CCP.Avalonia.csproj`
  - `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
  - `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
  - `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
  - `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
  - `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`
  - `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
  - `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
  - `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
  - `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`
  - `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
  - `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
  - `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
- `bash .github/scripts/core-guards.sh`: exit 0; “All Core guards passed.” An initial sandboxed attempt could not create temporary files; rerun with workspace execution permissions succeeded.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable at this base (exit 127); no ledger row added or promoted.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --nologo`: exit 1; 3,952 passed, one failed, five skipped, total 3,958. The failure is unchanged `BoardTilesTests.A_frame_fits_the_budget`: 16.45 ms versus a 12 ms limit while builds were running. The Vosk tests self-skipped for the unavailable model; no tests were edited or disabled.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~SpeakCueStateTests|FullyQualifiedName~SpeakPromptPlanTests' --nologo`: exit 0; 12 passed, zero failed/skipped.
- Focused board retry after Linux builds completed: `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget' --nologo`: exit 1, one failed (40.83 ms). This unchanged performance guard is not green on this worker.
- WPF cross-build: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` stopped after no progress. Retried with `timeout 180s`, `--no-restore -p:UseSharedCompilation=false -m:1`: exit 124 (timeout), no completed build result. WPF compilation is therefore unverified; Windows tests were not run.
- `SpeakCueOverlayTests` compiled with zero errors but were not executed, following the supplied cloud font-creation limitation. No desktop, notification, or real-profile testing was attempted. No Vosk model download was attempted.
- No render-all run: no code changed and no P122 claim is made. Windows desktop code cannot be executed in this Linux environment.

## Assumptions

- The existing close-after-fade lifetime adaptation is intentional and should remain until the runtime is ported; hidden-window reuse is not independently introduced here.
- A faithful end-to-end runtime and its behavioral tests exceed the cap; defer it as a named dependency rather than wire an incomplete cue-only path.

NOOP: Deeper playback runtime host and spoken-prompt dispatcher/session

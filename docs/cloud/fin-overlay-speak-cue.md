# fin-overlay-speak-cue

- Audited `overlay-speak-cue` at base `888cdbffe4d2ecf3967cee4a4334557ee533e54d` against the frozen WPF overlay, its `SpeakPromptSession`, and `RealActionDispatcher`.
- Kept the ledger row `stub` with an explicit Missing list and this evidence link. No application or test code changes: the unit's pure-dependency exception applies.
- Inspected read-only `parity/trunk` commit `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported: its Avalonia `Views/Deeper/RealActionDispatcher.cs:334-336` still handles `EffectTypes.Speak` with `LogNoTwin("speak")`.

## WPF behavior audit

The existing head and Core state already implement the overlay's presentation:

- Ignore blank text; marshal show/hide to the UI thread; create on first show and replace a visible cue in place.
- Borderless, transparent, topmost, nonactivating, taskbar-hidden, nonfocusable, click-through virtual-screen window. The existing platform seam routes X11 and Windows separately.
- Primary work-area centering, nonnegative virtual-screen-relative offsets, 120 DIP top offset.
- Outlined `SAY IT` eyebrow: 22 DIP, pink `#FF8AD8`, opacity 0.8, 2 DIP dark outline; white 60 DIP cue, 3 DIP `#12061A` outline and 2 DIP gap.
- Linear 180 ms fade to 0.92, indefinite hold, replacement during fade-out, 180 ms fade-out and content cleanup, immediate teardown. Panic registration (`speak-cue`) and shell-close cleanup already exist.
- Existing Core tests cover blank text, replacement/hide state, fade timing and placement. Existing headless tests cover rendering/show/update/hold/hide, show during fade-out, platform refusal, and panic.

Remaining user-visible gaps, all owned by the unported Deeper runtime (`SpeakPromptSession` / action dispatcher; the ledger's deeper-core-c/d work):

1. Starting a live persistent speak effect must show its trimmed custom cue, or `Say {target}` by default. Empty targets, unavailable speech and declined microphone consent must skip the prompt before showing it. Another prompt can replace the current label.
2. The cue must remain throughout recognition, repetitions and any UntilSatisfied playback hold; successful completion, the duration deadline and recognition termination must hide it through `MarshalComplete`.
3. Band replacement/exit, engine cancellation and one-shot cancellation must stop the owning session and hide its persistent cue; restart/update edges must preserve session ownership. WPF handles these in `RealActionDispatcher.DispatchSpeakEffect` and `SpeakPromptSession.Stop`.
4. Persistent mode must select this overlay while intermittent mode and correct/incorrect feedback use subliminal flashes. The missing session also owns microphone handover/restoration, repetition count and playback hold/release; calling the overlay directly from the editor would bypass that lifecycle.

There is no independent missing overlay implementation identified. The WPF window is kept alive and hidden after fade-out; Avalonia deliberately closes it to respect OnLastWindowClose. That existing documented lifetime adaptation is preserved. The WPF Loaded/pending-text mechanism is represented by constructing the Avalonia content after Show. Neither difference justifies inventing a runtime caller in this unit.

## Pinned caller evidence

At base `888cdbffe4d2ecf3967cee4a4334557ee533e54d`:

```sh
git grep -n -E 'SpeakCueOverlay\.(ShowCue|HideCue)' 888cdbffe4d2ecf3967cee4a4334557ee533e54d -- ConditioningControlPanel
```

Returns only the two live calls in `Services/Deeper/SpeakPromptSession.cs`: line 172 shows the persistent cue; line 196 hides it. WPF `RealActionDispatcher.cs:211-212` dispatches `EffectTypes.Speak`; lines 281-337 manage the session's start/stop and cancellation. `SpeakPromptSession.cs:390-416` hides on completion and stop. This is live WPF behavior, so P122 (dead WPF caller exception) is inapplicable.

```sh
git grep -n -E 'SpeakCueOverlay\.(ShowCue|HideCue)|DispatcherFactory[[:space:]]*=' 888cdbffe4d2ecf3967cee4a4334557ee533e54d -- CCP.Avalonia
```

Returns no matches (exit 1). The only external head references to the overlay are immediate-close cleanup in `App.axaml.cs:912` and `Views/Windows/PanicSurfaces.cs:72`. Core `EnhancementHostService.cs:188` falls back to `LoggingActionDispatcher` without a head factory; it cannot call a head window. `Views/Deeper/EnhancementPlayerWindow.axaml.cs:103-114` documents the missing playback-source/factory binding. Neither Core nor Avalonia contains `SpeakPromptSession`.

`RenderProof.cs:247-261` discovers concrete view controls and invokes even internal parameterless constructors; `SpeakCueOverlay.cs:98-104` supplies the existing `Say YES` render sample. Render discovery demonstrates presentation only, not a user path, so it cannot upgrade this row to wired.

## Verification limits

.NET SDK: `10.0.402`. All builds below used `dotnet build <project> -c Release --nologo`; each exited 0 with result tail `0 Error(s)` (existing warnings remain).

| Project | Exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | `0 Error(s)` |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | `0 Error(s)` |

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false --nologo`: exit 0; `554 Warning(s), 0 Error(s)`. Compiled Windows/WPF code only; not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0; `All Core guards passed.`
- `export DiffEngine_Disabled=true`; Core tests used a throwaway `CCP_USERDATA_DIR` sandbox. Initial `dotnet test Tests/CCP.Core.Tests -c Release --nologo`: exit 1; 3,544 passed, 1 failed, 5 skipped, 3,550 total. The unchanged `BoardTilesTests.A_frame_fits_the_budget` measured 13.28 ms against its 12 ms ceiling while builds were concurrent.
- Sequential `dotnet test Tests/CCP.Core.Tests -c Release --no-build --nologo` after builds finished: exit 0; `Failed: 0, Passed: 3545, Skipped: 5, Total: 3550`, duration 40 s. This includes `SpeakCueStateTests` and the previously failing board performance test.
- Five existing Vosk tests self-skip without the unavailable model; the task records the model download as blocked (403). No test was changed, weakened, or newly skipped.
- `git diff --check` and ledger uniqueness/status/changed-content privacy checks: exit 0.

No new behavior was introduced, so no new tests or fail-before mutation were added. Avalonia headless tests are compile-only in this environment under the task's measured font-creation limitation; `SpeakCueOverlayTests` must run in the supervisor's GitHub rcheck environment. Windows native code cannot run on Linux. No live microphone, multi-monitor, click-through or compositor exercise was performed. Full render-all was not run for this dependency-only documentation change; the existing render registration was inspected.

## Assumptions

- Follow the explicit pure-dependency NOOP rule rather than pulling an unrelated Deeper runtime into the overlay unit. Trunk's dispatcher does not supply the missing speak session either.
- Preserve the base's documented fade-out/close lifetime adaptation and platform support boundaries.
- Keep `stub` until a real user path reaches the session lifecycle; do not use the dead-WPF exception for these live WPF callers.

NOOP: unported Deeper SpeakPromptSession and live action-dispatch/playback binding (deeper-core-c/d).

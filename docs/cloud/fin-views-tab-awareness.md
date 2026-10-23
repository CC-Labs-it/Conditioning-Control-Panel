- Audited `views-tab-awareness` against WPF at base `c540339182e8b91cf4d0cad68135b4fb2696cd14`; retained `stub` with an explicit missing-behavior list.
- Dependency-only NOOP: no application code, tests, surfaces, or panic registrations changed. The missing user paths require the unported keyword-trigger runtime, desktop input/foreground adapter, OCR capture, and keyword-highlight overlay feature (`shell-keyword-triggers`).
- Replaced the ledger row's machine-local evidence references with repository-relative analysis and existing test class names. There is still exactly one row for this id.
- Read-only import review: fetched `parity/trunk`, pinned at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported. Relevant source credit: `7fdca1be6a1aec9af640cf8cf81c9b40a99bbe8c` (keyword runtime), `fa7fcf4425b292b5b9d5b8aff461e56283cf4074` (OCR/highlights/X11 keys), and `119ec005fcfa4c05a3ced4bd390c84ecf2497a03` (live feed/recent apps).

## WPF audit and dependency decision

Read the three reference files listed in the ledger, then followed their live forwarding handlers into `ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs`, `MainWindow.KeywordTriggers.cs`, `MainWindow.Settings.cs`, `Services/KeywordTriggerService.cs`, `Services/ScreenOcrService.cs`, `Services/KeywordHighlightService.cs`, and `App.xaml.cs`. References below are pinned to the base above.

Already reachable on this head: settings seeding on attach/show; master/keyboard and OCR entitlement refusal with WPF messages; saved source/safety/highlight flags; cooldown ladder; app-scope selection and Enter/blur canonicalization; swatches/hex validation; custom-trigger/OCR detail editors; preset cards, activation/deletion/new/detail; installed-preset-first advanced link with drawer fallback; tutorial opener and completed-tour preset-editor hook. The title observer (`WindowAwarenessService`) is a separate feature and does not supply keyword fires, process-name history, or OCR hits.

Every remaining behavior of this row depends on the runtime:

| Missing behavior | WPF behavior and evidence | Required dependency |
| --- | --- | --- |
| Master/source lifecycle and real keyword actions | `MainWindow.Awareness.cs:393-508`: master ON starts keyword engine, keyboard hook, then OCR if enabled; OFF stops engine/OCR and releases the hook unless panic/leash needs it. Keyboard ON routes through master; OFF preserves master and the hook when panic/OCR still needs it. Keyword service clears its rolling buffer on start/stop, rechecks entitlement on input, handles timeout/backspace/delimiters, and dispatches configured actions with cooldowns/echo protection. | Real keyword engine and platform input adapter; the existing `Platform/KeywordTriggerHead.cs` contains only a no-op tutorial `FireDemo` stand-in. |
| OCR source, confirmation and self-exclusion | OCR ON starts only when master is enabled; OFF stops immediately. Persisted scan interval/confirmation/highlight mode must drive capture and matching. `ScreenOcrService.cs:143-199` drops own-window intersections before dispatch; `App.xaml.cs:1581,1588` adds active bouncing-text and subliminal text rectangles, including overlays excluded by the full-screen-window filter. | OCR capture/recognition and own-window/text-rectangle provider; keyword matcher consuming recognized hits. |
| Foreground filtering and safety | `KeywordTriggerService.cs:261-332,741-773`: keyboard app gate runs before buffering and clears blocked text, preventing a later focus change from firing it. OCR/clipboard use the same scope; own-focus suppression precedes list membership. Unknown process fails closed only in OnlyListed mode. Loop-protection settings must affect actual detection and dispatch. Core `AwarenessAppScopeRules` already contains parsing/membership/suppression rules, but the head has no live keyword caller/resolver. | Keyword engine, foreground-process resolver, input/OCR/clipboard sources. |
| Recently focused app chips | `KeywordTriggerService.cs:580-714` maintains an in-memory eight-app ring, newest first, case-insensitive, excluding this process, sampled during engine input even in Everywhere mode. `MainWindow.Awareness.cs:592-669` filters listed apps, hides an empty label, offers `+ app` buttons, saves additions, and refreshes canonical text/chips on tab show or edits. | Running foreground sampler/ring. Adding an empty chip renderer would not create a user path to this behavior. |
| Recent-fire feed | `MainWindow.Awareness.cs:90-138,140-370` subscribes once and refreshes visible tabs on TriggerFired, also refreshes on open; reads newest records, displays up to five, clears empty-state count, pluralizes count, shows quoted keyword/source/action tooltips/time. Time buckets: under 10 seconds "just now", under 60 seconds seconds, under 60 minutes minutes, under 24 hours hours, otherwise HH:mm. AddXp and unknown action keys are hidden; tooltip delay is 200 ms. | Real TriggerFired/GetRecentFires producer. Importing the renderer alone would leave it unreachable from real detection. |
| Keyword highlights and capture visibility | `MainWindow.Awareness.cs:750-762` refreshes capture affinity immediately on existing overlays; `KeywordHighlightService.cs` creates click-through per-screen overlays and applies saved color/mode/duration to actual OCR matches. | Highlight overlays and OCR matches, with platform capture-visibility seam and panic cleanup when implemented. |
| Tutorial keyword demo | `Tours/TutorialService.cs:78` calls `KeywordTriggerHead.Engine.FireDemo`; the base implementation is empty. WPF `KeywordTriggerService.FireDemoTrigger` dispatches the demo and records a fire. Tour progression/spotlight limitations belong to `win-tutorial-overlay`. | Same missing keyword engine/action dispatch and feed producer. |
| Status-dot live breathing | WPF `UpdateAwarenessStatusIndicator` calls SetAwarenessStatusPulse. The head already has a shared `MainShellWindow.TabFxTakeoverLabStatus.cs:290` implementation; this tab only sets Live/Off text and fill. | Live engine lifecycle caller; shared motion belongs to `shell-awareness`, so no second animation/timer should be introduced. |

A structural comparison of both XAML files found only three WPF-only named controls: `CompanionPrivacyHost`, `CompanionTuningFold`, and `CompanionTuningHost`; all other named tab controls have head counterparts. The WPF view calls `HostCompanionPrivacy` on load/visibility. Its implementation is in `AwarenessTabView.Companion.cs`, independently tracked as `views-awareness-companion`: it moves the v2 privacy surface and hosts the companion tuning shelf. That requires the unported CompanionExperience v2 host/shelf, rather than a replacement surface in this unit.

This is not a P122 dead-code case. Reproducible live-caller checks:

```sh
git grep -n 'SyncAwarenessTabUI' c540339182e8b91cf4d0cad68135b4fb2696cd14 -- ConditioningControlPanel/MainWindow/MainWindow.TabNavigation.cs
git grep -n 'KeyPressedWithVkCode.*KeywordTriggers' c540339182e8b91cf4d0cad68135b4fb2696cd14 -- ConditioningControlPanel/MainWindow/MainWindow.xaml.cs
git grep -n -E 'KeywordTriggers = new|ScreenOcr = new|KeywordHighlight = new|GetActiveTextScreenRects' c540339182e8b91cf4d0cad68135b4fb2696cd14 -- ConditioningControlPanel/App.xaml.cs
```

Results: tab navigation calls SyncAwarenessTabUI at line 599; the global hook forwards keys at line 339; App constructs the three services at lines 3071, 3100, 3101 and includes bouncing/subliminal rectangles at lines 1581, 1588. Thus the missing paths are live in WPF, and neither a `wired` claim nor P122 is justified.

At the fetched trunk tip the engine is 452 lines, its OCR partial 204, the head adapter 457, and screen capture 196: 1,309 lines before the X11 input/foreground helpers, highlight overlay, startup/lapse/panic wiring, live view, adaptation, and behavioral tests. The live-view import references that absent engine and head. Importing a subset would not reach every WPF behavior and would strand imported code. The task explicitly requires no code change for a pure dependency, so the engine/capture feature must land before this row can finish.

## Verification limits

All commands used SDK 10.0.402 and Release configuration. `DiffEngine_Disabled=true` was exported for the Core test run.

| Command | Exit/result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors (48 existing warnings). |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors (172 existing warnings); platform-specific code compiled, not executed. |
| `dotnet build Tests/<project>/<project>.csproj -c Release` for every Linux test project in `build.yml` | All 11 exit 0, 0 errors; warnings remain. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests. |
| `bash .github/scripts/core-guards.sh` | 0; "All Core guards passed." Three pre-existing forbidden-API baseline entries; 424 XAML references checked. |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0; 2,832 passed, 0 failed, 5 skipped, 2,837 total, 48 seconds. Vosk's existing tests skip when the unavailable model is absent; no tests changed or disabled. |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 512 existing warnings, 0 errors, 2m51s. Compiled WPF app/tests; Windows runtime not run. |
| `git diff --check` | 0. |

The initial checkout fetch failed because the supplied snapshot was read-only; used the task-authorized fallback clone and verified its clean HEAD equals the required base. An initial Core-guards attempt lacked writable temporary storage; rerunning with writable storage passed. The first all-project build batch exhausted disk space while copying app assets into test outputs (later processes exited 134). Removed only generated test `bin` outputs, then reran all 11 Linux test builds sequentially with cleanup between projects; every retry passed.

Avalonia test execution and `--render-all` were not run, following the task's measured font-creation limitation; only compilation is established here. Relevant existing classes for the supervisor's headless rcheck: AwarenessAppListEditorTests, AwarenessCooldownLadderTests, KeywordTriggersPanelTests, ProgramsRoadmapAwarenessOpenersTests. Core AwarenessAppScopeRulesTests ran as part of the full Core suite. No new behavioral tests or fail-before/fix proof apply because this unit changes documentation only. No new surface requires panic registration. Windows desktop/input/OCR/capture-affinity behavior was not run. Vosk model download was not retried because the task reports it blocked (403).

## Assumptions

- Applied the task's dependency-only NOOP exception instead of porting a separate runtime feature or claiming `wired` for persisted settings.
- The existing head wins over trunk files. All trunk code was reviewed read-only; source hashes are credits for the analysis, not claims of imports.
- Shared status motion, CompanionExperience v2 hosting, and tutorial spotlight/progression retain their existing ledger ownership.

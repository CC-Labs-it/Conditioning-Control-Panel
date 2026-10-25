# fin5-views-companion-keyword-triggers

- Dependency-only analysis; no application or test code imported or changed.
- Verified clean snapshot, then corrected HEAD from `f81ea01fb079b8b0ab750757ca23309cb78fdf30`
  to the required base `2d07fdf141a8933d604eb4ec4e16468f9f7f8e13` before review.
- Keep `views-companion-keyword-triggers` at `stub`. No ledger promotion, new
  surface, panic registration, or test-plan coverage entry is warranted.
- Read-only source considered: `parity/trunk` fetched at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported.

## WPF comparison and remaining behavior

Reference paths and line numbers below are pinned to the required base commit.
Read both `ConditioningControlPanel/Views/Controls/Companion/KeywordTriggersPanel.xaml`
and `.xaml.cs`, their forwarded handlers in `MainWindow/MainWindow.KeywordTriggers.cs`,
and the live Awareness/lifecycle callers, rather than treating dead legacy
Exclusives controls as requirements.

Already present: add/import with completion messages, delete, enable, keyword
commit and linked-audio lookup, Browse, visual-effect/cooldown/volume/haptic/duck
editors, buffer/session/highlight/scan-interval persistence, confirmation and
highlight-mode selection/seed guards, gated detail rows, preset-clone filtering,
outer wheel routing, and reveal scroll/pulse. Existing `KeywordTriggersPanelTests`
cover these editor paths. Fluent icons are the established intentional divergence.

The remaining gaps all consume the absent shell runtime:

1. `MainWindow.KeywordTriggers.cs:109-119`: changing scan interval must also call
   the running scanner's `UpdateInterval(value * 1000)`, after updating the label
   and saving. Avalonia's `KeywordTriggersPanel.axaml.cs:186-194` only persists it.
2. `MainWindow.Awareness.cs:393-438,441-475`: master and OCR-source toggles must
   start/stop matching, keyboard input and OCR subject to access and master state.
   Avalonia's Awareness tab saves flags and resyncs the panel but cannot arm sources.
3. The saved buffer timeout, per-trigger/global cooldowns, session multiplier,
   enabled state and actions must affect actual matches: keyboard buffering,
   matching, access/app-scope gates, audio (volume and ducking), visual effects,
   haptics and XP dispatch belong to WPF `Services/KeywordTriggerService.cs`.
   Here the only `Platform/KeywordTriggerHead.cs` engine is a no-op demo stub.
4. Confirmation count and all/random highlight mode must affect real OCR results,
   including consecutive scans and positional repeat suppression; OCR timing and
   in-memory screen reading are in WPF `Services/ScreenOcrService.cs` and
   `KeywordTriggerService.cs`. No corresponding Core engine/scanner exists here.
5. Highlight duration/color/enabled settings need real matched-word overlays;
   capture visibility must update existing overlays immediately
   (`MainWindow.Awareness.cs:733-743`, `Services/KeywordHighlightService.cs`).
   Avalonia persists these settings but has no keyword highlight overlays.
6. The same sources need panic stop/resume (`MainWindow.xaml.cs:1114,1160-1162`),
   entitlement stop/restart (`MainWindow.Patreon.cs:186,433-434`) and disposal
   (`MainWindow.WindowChrome.cs:317-319`). A scanner without these hooks would
   not be a complete or safe user path.

These are live WPF callers: P122's dead-code exception does not apply.
`rg -n 'ScreenOcr|KeywordHighlight|KeywordTriggerEngine|UpdateInterval' CCP.Core CCP.Avalonia`
on the base finds settings, help, explanatory comments and panel persistence,
not runtime implementations. The `shell-keyword-triggers` ledger row explicitly
owns this missing host (`wire-shell-keyword-triggers-b`).

## Cap and dependency decision

The fetched trunk has these runtime files (`git show <sha>:<path> | wc -l`):

| File | Lines |
| --- | ---: |
| `CCP.Core/Services/KeywordTriggers/KeywordTriggerEngine.cs` | 452 |
| `CCP.Core/Services/KeywordTriggers/KeywordTriggerEngine.Ocr.cs` | 204 |
| `CCP.Avalonia/Platform/KeywordTriggerHead.cs` | 457 |
| `CCP.Avalonia/Platform/ScreenOcrService.cs` | 196 |
| `CCP.Avalonia/Views/Overlays/KeywordHighlightOverlay.cs` | 164 |
| Total | 1473 |

This is source size, not an asserted minimal implementation or a proposed diff.
Even this five-file host excludes the Windows reader (183 lines), X11 listener
(173 lines), startup/access/panic/capture hooks and focused tests. Existing files
must retain this base's behavior; trunk's overlay also delays capture-affinity
changes until the next fire, whereas WPF refreshes immediately. A wholesale copy
would therefore exceed the 1000-added-line budget and still require adaptation.
Splitting off only an inert interval callback would leave the same runtime gap.
Use the task's explicit pure-dependency path: no code changes in this panel unit;
finish the shell runtime first, then connect the panel and test real lifecycle
and interval changes with fake input/readers. Linux OCR remains a platform-seam
question (trunk provides no Linux reader), not grounds to claim WPF parity.

## Verification limits

Commands used .NET SDK 10.0.402, Release configuration and
`DiffEngine_Disabled=true` for test execution.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <project> -c Release` for every Linux-compatible test project
  named in `.github/workflows/build.yml`: all 11 exit 0, 0 errors. Projects:
  `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`,
  `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`,
  `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`,
  `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`,
  `CCP.Avalonia.Snapshot.Tests` (each under `Tests/<name>/<name>.csproj`).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed
  (424 XAML references checked). Initial sandbox attempt could not create temp
  files; the subsequent run with write access passed.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 1;
  2810 passed, 1 failed, 5 skipped, 2816 total. Failure:
  `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`,
  `Assert.NotNull` at line 80. An isolated `--no-build --filter
  FullyQualifiedName~GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`
  rerun exited 0 (1 passed). Full-suite timing/isolation remains unresolved;
  no application or test source changed. The five Vosk tests skipped themselves
  because the model is unavailable; tests were not weakened or edited.
- `KeywordTriggersPanelTests` compiled, not executed: the supplied environment
  limit identifies font creation failures in this headless suite; its test app
  still uses direct `UseSkia().UseHeadless(...)` setup without the cloud font
  fix. GitHub must run the panel filter. No desktop/screens/notifications were
  exercised by this documentation change.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base
  (exit 127). No ledger row is added or promoted.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors
  (512 warnings). WPF application and tests cross-compiled, not executed.
- Windows OCR/capture/highlight/input behavior was not run on Linux.
- `git diff --check`: exit 0. No render-all run (no surface change or P122 claim).

## Assumptions

- The task's dependency-only exception applies when the missing shell runtime,
  its platform seams and behavioral tests cannot fit this panel unit's cap.
- Keep the existing honest stub row unchanged; do not duplicate or promote it.
- No render-all is needed for a documentation-only dependency finding; P122 is
  not being used. No new behavior requires new tests or panic registration.

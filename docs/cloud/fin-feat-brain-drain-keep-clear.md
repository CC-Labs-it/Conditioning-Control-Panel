- Audited `feat-brain-drain-keep-clear` at base `db079bf91474c77a5ea67ec306f6337a7de44b83` against its live WPF reference. This is the task's dependency-only NOOP: the remaining effect requires the unported Brain Drain desktop-capture/blur overlay, tracked by `views-studio-brain-drain-feature`.
- Kept application code, tests and the single ledger row unchanged. Its `stub` status and Missing list remain accurate: the Studio preference works, but cannot keep pictures above a blur surface that this head does not implement.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code was imported. That tree lists Brain Drain overlay/capture files; landing that dependency is separate work rather than adding an unused ordering shim here.

## WPF comparison at the pinned base

Already present in Avalonia/Core:

- Studio > Brain Drain contains `ChkKeepClear`, using the same localized label and hint, after Melt and before High refresh. It remains editable mid-session, as in WPF.
- The toggle reads `BrainDrainKeepPicturesClear`, saves edits through `CoreSettings`, reloads on visual attachment and repaints on `CurrentReplaced`.
- The preference defaults off and round-trips through settings JSON. Core's `BrainDrainKeepClear.AnchorIfNeeded` selects the lowest owned non-drain window only when a drain is above it; foreign windows and drains cannot become anchors, and an already-correct band needs no move.
- Existing focused tests cover the ordering rule, multiple drains, absent drains/owned windows, default/JSON round-trip, UI edits and replacement-settings repaint.

Every remaining WPF behavior for this row depends on the missing blur overlay:

1. Keep CCP flashes, videos, lock cards, subliminals and bubbles sharp while the captured desktop and other applications remain blurred. WPF collects legacy blur HWNDs and visible capture-excluded compositor hosts; Avalonia has neither Brain Drain surface.
2. Reconcile the visible topmost band, capped at 1,024 steps and ending at the first non-topmost window. Identify our process, ignore invisible entries, call the shared rule, then insert all drain windows below the lowest owned window while preserving their relative order and without moving, resizing or activating them.
3. Run that sink last, after every other overlay/attention-layer pin, on the 500 ms reconciler. Do nothing when disabled, not showing, without valid drain handles, without an owned anchor, or already ordered correctly.
4. Apply preference changes immediately through a forced z-order pass: on sinks the blur; off allows the normal topmost pass to raise it again. The current head only persists the preference because there is no live blur to reorder.

The WPF path is live, so P122's dead-caller exception does not apply. There is no new surface requiring panic registration and no new behavior requiring a new test or fail-before proof.

## Reproducible source evidence

All base paths below are pinned to `db079bf91474c77a5ea67ec306f6337a7de44b83`:

- `ConditioningControlPanel/Views/Controls/Studio/BrainDrainFeatureControl.xaml:217-230`: row order, copy and no session-lock ownership marker. Its code-behind loads the setting at line 105 and writes/saves it at lines 326-335.
- `ConditioningControlPanel/Services/Notifications/OverlayService.cs:514-515,876`: the 500 ms timer reaches `ReassertZOrder`.
- The same file, lines 3250-3300: `SinkBrainDrainUnderOwnWindows` is the final ordering step, with both render paths, band scan and stable drain ordering. Lines 3811-3815 force the pass when the setting changes.
- `CCP.Core/Services/Notifications/BrainDrainKeepClear.cs`: shared pure ordering rule.
- `CCP.Avalonia/Views/Tabs/StudioTabView.axaml:241`: reachable Brain Drain card. Its code-behind at `Views/Controls/Studio/BrainDrainFeatureControl.axaml.cs:47-80` explicitly documents the missing overlay and implements the preference.

Useful checks from the repository root:

```sh
git rev-parse HEAD
rg '^\| feat-brain-drain-keep-clear \|' docs/avalonia-parity.md
rg -n 'SinkBrainDrainUnderOwnWindows|BrainDrainKeepPicturesClear' ConditioningControlPanel/Services/Notifications/OverlayService.cs
rg -n 'BrainDrainKeepClear|BrainDrainKeepPicturesClear|StartBrainDrainBlur|class BrainDrain(Layer|CapturePump|Service)' CCP.Avalonia CCP.Core
```

The final scan finds the preference in the head and the pure rule in Core, but no head call to the rule and no Brain Drain blur/capture implementation. Comments and the help-loop illustration are not live desktop blur surfaces.

## Verification limits

- SDK: .NET `10.0.402` (installed with the task's installer).
- `dotnet build <project> -c Release --nologo`: exit 0 and `0 Error(s)` for every Linux project required by `.github/workflows/build.yml`:

  | Project | Result |
  | --- | --- |
  | `CCP.Core/CCP.Core.csproj` | PASS |
  | `CCP.Avalonia/CCP.Avalonia.csproj` | PASS |
  | `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | PASS |
  | `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | PASS |
  | `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | PASS |
  | `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | PASS |

- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` The initial read-only invocation could not create temporary files; the writable retry passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore`: exit 1; 3,551 passed, 2 failed, 5 skipped, 3,558 total. Failures in unchanged code: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, got Clips) and `BoardTilesTests.A_frame_fits_the_budget` (12.68 ms against a 12 ms limit). The five existing speech tests skipped because the Vosk model is absent; its download is blocked by the documented environment limit. No tests were changed, disabled or filtered out of this full run.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~BrainDrainKeepClearTests`: exit 0; 7 passed, 0 failed, 0 skipped.
- `BrainDrainKeepClearRowTests` was compiled but not executed: this base lacks the `cloud-test-fonts` fix and the task identifies font creation as an environment failure. The supervisor's GitHub runner must execute these two UI tests. Both existing test assemblies initialize isolated user-data sandboxes; no live desktop/profile tests were invoked.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false --nologo`: exit 0, 554 warnings, `0 Error(s)`. The WPF head and its test project compiled; Windows code was not executed on this Linux environment.
- Full render-all, interactive Windows z-order/capture behavior and WPF tests were not run. This is a dependency analysis, not P122 render evidence or a parity verification claim.
- Test output copying temporarily exhausted the fallback filesystem. Removed only generated build outputs, rebuilt the affected project successfully and completed subsequent builds one at a time.

## Assumptions

- The explicit dependency-only exception applies to the absent Brain Drain desktop-capture/blur feature. Importing that larger feature would expand this one-row unit rather than complete an independent keep-clear gap.
- Retain the existing honest `stub` row unchanged; do not promote it to `wired` while the user path cannot produce the effect.

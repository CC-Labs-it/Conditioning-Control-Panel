# fin-views-companion-keyword-triggers

- Audited expected base `005af90e0fa8be7f39be2c56bd13b19e9eccccff` against the WPF reference and live callers. The supplied snapshot had a different HEAD and read-only Git metadata; used the authorized fallback clone and verified the expected commit before creating the unit branch.
- Restored the live WPF trigger-list wheel path: tunneling input moves the outer Awareness viewport by 120 pixels per notch and leaves the inner list in place, even before its edge. Without an ancestor ScrollViewer the normal inner handler remains available. Removed the inaccurate markup comment that edge chaining was equivalent.
- Added `KeywordTriggersPanelTests.WheelOverScrollableTriggerListMovesOuterViewport` with downward and upward cases, including a scrolled inner list. Kept the ledger **stub** with an explicit remaining dependency list for the unported **shell-keyword-triggers** runtime.
- Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; no imported commits to credit.

## Existing editor behaviour

Read `ConditioningControlPanel/Views/Controls/Companion/KeywordTriggersPanel.xaml` and `.xaml.cs`, plus `ConditioningControlPanel/MainWindow/MainWindow.KeywordTriggers.cs`. The real Avalonia Awareness tab already provides:

- Add defaults and action rebuilding; import skips blank/existing phrases, finds linked audio and displays WPF success/no-new-triggers messages; preset clones stay outside the custom list.
- Delete, enable, keyword commit on lost focus with audio auto-match only when no clip is set, Browse, eight visual effects in WPF order, cooldown, volume, haptic and audio ducking.
- Buffer timeout, session XP multiplier, scan interval and highlight duration; confirmation and highlight mode persist and seed without writing back. OCR detail requires source-on and access; highlight detail follows its master.
- Advanced-editor reveal expands and synchronously lays out, aligns the header 24px below the outer viewport (clamped, with fallback), then pulses twice: 220ms per leg, quadratic ease, auto-reverse. Re-click restarts, detach clears, Motion Off suppresses the pulse. Avalonia uses its existing finite shared beat and sibling BoxShadow.
- Existing `KeywordTriggersPanelTests` cover editor/reveal paths; the new wheel test covers the additional routed user path. No new surface, clock or runtime was introduced, so no new panic registration is needed.

## Additional gap completed

WPF markup wires `PreviewMouseWheel` on the inner list at `KeywordTriggersPanel.xaml:192-193`. Its live handler, `MainWindow/MainWindow.Assets.cs:444-456`, always moves the nearest ancestor ScrollViewer by minus the raw wheel delta and handles the event. Avalonia previously relied on edge chaining, so the inner list consumed ordinary scrolling. The new tunneling handler preserves WPF's routing and converts Avalonia's notch delta to WPF's 120-pixel step, with viewport clamping. The test injects real headless wheel input over the visible trigger list and checks outer movement and unchanged inner offset.

## Every remaining WPF behaviour

References are pinned to base `005af90e0fa8be7f39be2c56bd13b19e9eccccff`. Paths in this table are relative to `ConditioningControlPanel/`.

| Missing behaviour | Live WPF reference | Dependency |
| --- | --- | --- |
| Re-time the running scanner after saving an OCR interval change. | `MainWindow/MainWindow.KeywordTriggers.cs:119`, `App.ScreenOcr?.UpdateInterval(value * 1000)` | `ScreenOcrService` |
| Master/source switches arm and stop detection; OCR respects master/access state; keyboard hook lifetime respects panic/OCR needs. | `MainWindow/MainWindow.Awareness.cs:395-503`, OCR calls at 420, 426, 466, 468 | Keyword engine, keyboard source, OCR service |
| Access loss stops OCR; restored access starts it when enabled. | `MainWindow/MainWindow.Patreon.cs:186,418-434` | Runtime entitlement lifecycle |
| Panic stops OCR; resume requires enabled OCR, master-on and access. | `MainWindow/MainWindow.xaml.cs:1114,1160-1162` | Runtime panic lifecycle |
| Capture-visibility changes update existing highlight windows. | `MainWindow/MainWindow.Awareness.cs:743`, `RefreshCaptureVisibility()` | `KeywordHighlightService` |
| Closing disposes OCR and highlight resources. | `MainWindow/MainWindow.WindowChrome.cs:317-319` | Both services |
| Saved buffer/cooldown/XP/confirmation/highlight settings affect detection; custom triggers dispatch configured audio, volume, ducking, visual and haptic actions. | `Services/KeywordTriggerService.cs`, `ScreenOcrService.cs`, `KeywordHighlightService.cs` | Complete `shell-keyword-triggers` runtime |

## Dependency proof and decision

At the pinned base this definition search has no matches (exit 1):

```sh
git grep -n -E 'class (ScreenOcrService|KeywordHighlightService|KeywordTriggerService|KeywordTriggerEngine)' \
  005af90e0fa8be7f39be2c56bd13b19e9eccccff -- CCP.Core CCP.Avalonia
```

`CCP.Avalonia/Views/Windows/MainShellWindow.KeywordTriggers.cs:11-15` explicitly records missing matching/dispatch, OCR and highlight services. `CCP.Avalonia/Platform/KeywordTriggerHead.cs` is only a no-op demo stub. The panel interval handler (`KeywordTriggersPanel.axaml.cs:170-178`) saves its value and identifies the missing scanner; `AwarenessTabView.axaml.cs:209,254,282` identifies missing lifecycle/capture calls. The `shell-keyword-triggers` ledger row remains stub and owns these services.

These are live WPF callers: P122 (dead WPF path) does not apply, and this row cannot be wired. After fixing the independent wheel gap, all remaining gaps require that runtime. Revisit these hooks after `shell-keyword-triggers` lands; do not duplicate runtime ownership in an editor unit. This is not a pure dependency-only NOOP because the live wheel path required a code fix. Its lane must resolve platform limitations; this audit does not presume they remove live WPF behaviour.

## Verification limits

SDK: .NET 10.0.402. `DiffEngine_Disabled=true` was exported for test work.

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors |
| `dotnet build <test-project> -c Release` for every Linux-compatible test project in `.github/workflows/build.yml` | 0 for all 11 projects; 0 errors (list below) |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 512 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:1 -nr:false` (final fix + tests) | 0; 222 warnings, 0 errors |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0; Failed: 0, Passed: 2811, Skipped: 5, Total: 2816 |

The 11 Linux test projects are `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`, `CCP.Avalonia.Tests`, `CCP.Core.Content.Tests`, `CCP.Core.Settings.Tests`, and `CCP.Core.Tests`, each under `Tests/` with its matching csproj name.

Initial multi-project builds exhausted disk while copying native runtime assets; removed only generated test outputs and rebuilt one project at a time, all successfully. The initial guard attempt could not create temporary files under the read-only sandbox; reran with write access and it passed. The final Avalonia compilation encountered a transient MSBuild worker failure and was retried with `-m:1 -nr:false`; the retry succeeded with 0 errors.

Avalonia tests (including the relevant `--filter FullyQualifiedName~KeywordTriggersPanelTests`), live rendering and render-all were **not run**, following the task's measured cloud font limitation. Consequently the new regression test is compiled but has no red/green execution proof here; GitHub rcheck must execute it. The pre-fix code lacked any list wheel handler and depended on inner scrolling/edge chaining. No font workaround or test weakening was added. The five existing Vosk tests self-skip without the unavailable model; no Vosk download was attempted because the task records its 403. WPF/Windows code was cross-compiled only, not run. No real desktop capture, notifications or user profile were used.

## Assumptions

- Preserve WPF wheel routing and its 120-pixel notch step rather than treating Avalonia edge chaining as equivalent. The runtime belongs to the shell unit; retain stub until it exists.
- The supplied cloud font limitation applies. This base's keyword tests create Skia headless apps without the later cloud-font setup. Compile Avalonia tests and leave execution to the supervisor's GitHub rcheck, as instructed. No test source is weakened or skipped.

# fin5: Brain Drain keep pictures clear dependency analysis

- Outcome: dependency-only NOOP. `feat-brain-drain-keep-clear` remains `stub`; no application code or ledger status changed.
- Verified base: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. The initial clean snapshot differed; fetched the requested base and checked out that exact commit before creating this branch.
- Read-only import source inspected: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code was imported. Existing Core rules and the head's preference control take precedence.

## WPF behavior and remaining gaps

Reference paths and line numbers below are pinned to the base commit above. The ledger's old OverlayService line numbers have drifted; its live implementation is `ConditioningControlPanel/Services/Notifications/OverlayService.cs`.

1. Studio's localized Keep pictures clear option loads the preference, writes and saves changes (`Views/Controls/Studio/BrainDrainFeatureControl.xaml:220-229`, `.xaml.cs:105,326-335`). Already implemented in Avalonia, including attach and settings-replacement repaint; existing `BrainDrainKeepClearRowTests` cover it.
2. With the preference on and blur visible, collect all legacy blur window handles and visible capture-excluded compositor hosts. Walk only the visible topmost band, capped at 1024 windows; identify this process's non-drain windows. Put every drain directly below the lowest of those windows, preserving drain order (`OverlayService.cs:3264-3301`). Missing: the native host caller and application of the existing Core rule. Core tests already cover the anchor/no-op cases, including multiple drains and other processes.
3. Heal ordering after the other overlay pins on every reconciler pass, at 2 Hz (`OverlayService.cs:3250-3262`). Missing: periodic ordering for live blur hosts.
4. Apply toggles immediately: enabled sinks the blur; disabled forces the normal raise back above pictures (`OverlayService.cs:3811-3815`). Missing: both live native transitions. No blur, no own pictures, or already-correct ordering must do nothing.
5. Keep the desktop blurred while pictures remain sharp. This requires a captured desktop that excludes layered CCP windows, not a tint or blurring the Avalonia control tree. Missing: the Brain Drain desktop capture/blur/melt feature and its real engine path (`views-studio-brain-drain-feature`).

These are live WPF callers, so P122 does not apply. `SinkBrainDrainUnderOwnWindows` is called at the end of `ReassertZOrder`; the preference change calls a forced pass. Grep at the pinned base confirms both. The absence of an Avalonia caller is a dependency, not dead WPF behavior.

## Dependency and line budget

The separate unported feature is **Brain Drain desktop visual host** (`views-studio-brain-drain-feature`): capture, blur/melt rendering, monitor targeting, capture exclusion, and engine/pause/stop/panic lifecycle. The base has generic click-through overlay seams, but no desktop capture pump or Brain Drain overlay. `ChaosFlashOverlay` is a flash-pool wash; `WelcomeShow/FirstShowEffects` explicitly uses a wash because capture is absent. Neither supplies a blurred desktop or drain handles to sort.

The read-only trunk implementation provides a concrete size estimate:

- `CCP.Avalonia/Views/Overlays/BrainDrainOverlay.cs`: 458 lines.
- `CCP.Avalonia/Views/Overlays/BrainDrainCapturePump.cs`: 459 lines.
- `CCP.Core/Services/BrainDrainVisual.cs`: 100 lines, including duplicates of our existing policy and ordering rule; only its layer rules would be adapted.
- `Tests/CCP.Avalonia.Tests/BrainDrainOverlayTests.cs`: 372 lines, including frame ownership/alpha, blur/melt, live settings, capture affinity, and rendering checks.
- `Tests/CCP.Core.Tests/BrainDrainVisualTests.cs`: 89 lines, partly overlapping our existing tests.

The host and pump alone consume 917 lines before adapted layer rules, required focused behavior tests, engine/card integration, panic registration, and this analysis. The complete tested dependency exceeds the 1000-added-line unit budget. The WPF pump and layer themselves total 1205 lines. A partial host would leave the row stub and add a new surface without proving its complete lifecycle. Per the dependency-only instruction, no partial code is introduced. A follow-up host unit must land first; keep-clear can then reuse the existing Core rule and control.

## Verification limits

Commands used .NET SDK 10.0.402, Release configuration, and `DiffEngine_Disabled=true` for tests.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <project> -c Release` for every Linux test project listed in `.github/workflows/build.yml`: exit 0, 0 errors each. Projects: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests` (all under `Tests/`).
- `bash .github/scripts/core-guards.sh`: exit 0; all guards passed (3 existing baselined APIs; 429 XAML references checked). The first attempt could not create temporary files in the read-only sandbox; reran with permitted write access successfully.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3953 passed, 5 skipped, 0 failed. Skips are the existing Vosk tests; the model is unavailable here. No tests were changed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~BrainDrainKeepClearTests`: exit 0; 7 passed, 0 failed.
- `BrainDrainKeepClearRowTests` was compiled, not executed, following the supplied environment instruction about font-creation failures; the test bootstrap still lacks the font setup in this base. Headless execution remains for the supervisor's GitHub check. No real desktop or user profile was opened.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; 554 warnings, 0 errors. Compiled but not run.
- Windows code cannot run on this Linux host. Native z-order, capture affinity, and real desktop rendering were not exercised.
- `git diff --check`: exit 0. No new surface was introduced, so no new panic registration or tester step is needed. The base does not contain `.github/scripts/test-plan-coverage.sh`.

## Assumptions

- The desktop visual host is the separate missing Brain Drain feature dependency, rather than treating a colored wash as equivalent behavior.
- Preserve the ledger's honest `stub` status and existing tests while that dependency is absent.

- Add six headless `ThemeEvidenceTests` through actual awareness-picker rows, Companion room/Workshop controls and season recap window. Tests use the assembly's isolated profile and the shipped font setup.
- Fix the WPF parity gap exposed by `OpenPickerBrushFollowsRuntimeColorChange`: app brush color references now use `DynamicResource`, so an already open view follows runtime color-token changes. Preserve all keys and values.
- Consolidate the six target ledger IDs to one row each, keep `wired`, link named tests, and add numbered tester steps with checked WPF source line references.
- No trunk code imported; no trunk commit credits apply.

## Verification limits

- SDKs installed: .NET 10.0.100 and 10.0.402; final `dotnet --version`: 10.0.402. The channel installer recovered from an initial HTTP 403; fixed-version installation also succeeded.
- Initial focused run: exit 1, 5 passed / 1 failed. Runtime DarkerBg replacement expected `#203040` but retained `#121220`, demonstrating the production gap before the fix.
- After the fix: exit 0, all 6 passed, no skips.
- Negative run: exit 1, all 6 failed at the intended assertions. Temporary mutations, one per test, were applied together: SurfaceBg changed by one channel value; DarkerBg brush reverted to StaticResource; HasDetail binding replaced with false; Workshop disabled opacity changed to 0.44; room shell clipping disabled; recap rank foreground fixed to white. Every temporary mutation was restored before final builds/tests.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The initial read-only sandbox invocation could not create temporary files; the normal writable execution passed.
- `test-plan-coverage.sh` is absent on the required base; numbered steps were added directly.
- Final `dotnet test Tests/CCP.Core.Tests -c Release --no-build` with `DiffEngine_Disabled=true`: exit 0; 3,953 passed, 5 Vosk tests skipped (model unavailable), 0 failed.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~ThemeEvidenceTests` with `DiffEngine_Disabled=true`: exit 0; 6 passed, 0 skipped, 0 failed, after all temporary mutations were restored.
- Additional regression command with filter `FullyQualifiedName~ThemeEvidenceTests|FullyQualifiedName~SharedDeeperThemeTests|FullyQualifiedName~SeasonRecapPortTests`: exit 1; 5 passed / 9 failed. All nine failures were `Could not create glyphTypeface` for Inter: older test setup can initialize the shared App without the shipped font configuration. The standalone required class uses `Program.BuildAvaloniaApp` and passes. This cloud font limitation remains; no tests were weakened or skipped.
- Every project below passed `dotnet build <project> -c Release` with exit 0 and 0 errors. The WPF test project additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; it compiled but was not run.

| Project | Build exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 |
- Windows UI behavior cannot run on this Linux worker. No WPF tests, native desktop interactions, full Avalonia suite, Vosk-model download, or visual screenshot comparison were run. The focused tests assert visible control properties and input behavior, not pixel snapshots.
- Converter evidence covers boolean collapse/reveal in real row templates. Existing inverse-binding and Fluent icon decisions are retained; this unit does not claim a new emoji-image converter or full converter call-site coverage.

## Assumptions

- Last ledger row per ID is authoritative; duplicate historical entries for these six IDs are removed, with current decisions retained.
- Use the actual ported controls as headless entry points; no new navigation or testing-only production seam is needed.
- Apply the demonstrated dynamic-color fix consistently across the app brush dictionary, matching the WPF dictionary's resource semantics.

- Remove the duplicate `FlashOverlay.CloseAll` from gaze panic cleanup. The engine panic surface already reaches `App.StopDesktopOverlays`, including when the engine is idle, so one panic invalidates the shared ambient/reward spawn generation exactly once. This follows the WPF gaze close handler's division of responsibility with `FlashService`.
- Restore `ShellTrayTests` to `Assert.Equal(generation + 1, FlashOverlay.Generation)` and strengthen the gaze reward assertion to the same exact value. No assertions were weakened.
- Exercise coordinated panic in gaze reward tests with the desktop cleanup hook wired. Add a no-window stopped-engine regression: before the fix it failed with expected generation 2 / actual 3; after the fix it passes.
- No code imported from `parity/trunk`; it was fetched read-only. There are no imported trunk commits to credit.

## Verification limits

Used .NET SDK 10.0.402 and `DiffEngine_Disabled=true` for tests. The original snapshot had the wrong commit and a read-only Git directory; the authorized fallback checkout was verified clean at base `8b85316da45bfcce85c6e557633319dda4171fe1` before branching.

All required builds exited 0 with 0 errors. Commands used `dotnet build <project> -c Release --nologo`, plus `-p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true` to avoid duplicating large native assets. An initial matrix attempt exhausted disk space; deleting an interrupted clone's temporary pack and completed generated test outputs allowed every build to succeed on retry.

| Project | Result |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | Exit 0; 0 errors |
| `CCP.Avalonia/CCP.Avalonia.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | Exit 0; 0 errors |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | Exit 0; 0 errors |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | Exit 0; 0 errors |

The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. WPF and Windows-specific Avalonia code compiled, but Windows runtime behavior was not run on Linux. Existing compiler/analyzer warnings remain.

- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.`
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore`: exit 0; 3339 passed, 5 existing Vosk-model skips, 0 failed. The model is unavailable in this environment; the model-required speech gate was not run.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~PanicInvalidatesFlashesExactlyOnceWithStoppedEngine`: exit 1 before the production fix (exact-value assertion), exit 0 after it (1 passed, 0 skipped). This test creates no windows and runs despite the font limitation.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ShellTrayTests|FullyQualifiedName~GazeMinigameTests|FullyQualifiedName~PanicSurfacesTests'`: exit 1; 9 passed, 11 font-creation failures, 3 existing LibVLC skips. Every failure was `Could not create glyphTypeface` for Segoe UI Black or Inter. The affected UI assertions did not execute successfully; their runtime verification remains for GitHub rcheck. No font workaround or new skip was added.
- Other Linux test projects were compiled only; the full Avalonia suite was not run. Tests use their existing temporary `CCP_USERDATA_DIR` profiles and sandbox notification/secret-store behavior.
- `git diff --check`: exit 0.

## Assumptions

- Choose option (a): the two calls invalidate the same shared flash generation, so the second increment is redundant. Gaze cleanup remains responsible for its window, suspended schedule, bubbles, mind wipe and pulse; the coordinated engine stop owns flash-window closure and queued-spawn invalidation.

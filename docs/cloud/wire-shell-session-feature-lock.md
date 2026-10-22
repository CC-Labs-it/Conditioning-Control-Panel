# wire-shell-session-feature-lock

- Port WPF `MainWindow.SessionFeatureLock.cs:95-137` program reason copy to the existing Avalonia lock painter. It uses the existing Core `ProgramService.IsProgramSession` discriminator and the actual running `App.Sessions` session; enrollment alone does not label an unrelated preset as a program session.
- Keep WPF's title/day, title-only, unnamed-program, and ordinary-session fallbacks. Both the ribbon and marked dose controls use the same reason; stop restores original tooltips.
- Audit stale ledger notes: `PulseSessionLockRibbon` was already implemented on the base, as were all five LockCard repeat/target controls and their `SessionLock.Owned` markers. Cover those five controls with a real running-session headless test.
- Keep `shell-session-feature-lock` at `stub`: BubblePop has no `CmbMotion` control to mark. Follow-up `wire-shell-session-feature-lock-b` owns the marker when the BubblePop motion control is ported. No new control is invented in this lane.
- No trunk code imported. Read-only `parity/trunk` inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; adaptation follows the frozen WPF reference already on this base.

## Verification limits

All commands used .NET 10 and Release configuration. Tests used `DiffEngine_Disabled=true`. The successful combined Avalonia run used a task-local `FONTCONFIG_FILE` that excludes this executor's unsupported OpenAI Sans WOFF2 default; it does not change the app or repository. Initial attempts hit shared-disk exhaustion, so final builds used MSBuild hardlinks (`CreateHardLinksForCopyLocalIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, and `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, all `true`). Ancillary test builds used `BuildProjectReferences=false` against the already-built Core/Avalonia assemblies. Disposable ancillary bins were removed after each build.

- `dotnet build CCP.Core -c Release`: succeeded, `0 Warning(s)`, `0 Error(s)`.
- `bash .github/scripts/core-guards.sh`: passed; 424 XAML references checked against 1591 app / 1881 Core types.
- Before the production fix, `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~SessionFeatureLockProgramTests` failed at the intended copy assertion: expected `First Week is running this - Day 3...`, actual `You're in a session!...`.
- After the fix, the same new class passed: 1 passed, 0 failed/skipped.
- Final combined command: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --no-restore --filter "FullyQualifiedName~SessionFeatureLockProgramTests|FullyQualifiedName~SessionIoTests|FullyQualifiedName~LockCardRepeatShapePanelTests|FullyQualifiedName~ProgramsRunLifecycleTests|FullyQualifiedName~IconGuardTests"`: **15 passed, 0 failed/skipped**, 6 m 14 s. This includes real program start/panic/withdraw, generic preset copy, all five existing LockCard markers, tooltip restoration and the existing ribbon-pulse frames.
- `dotnet test Tests/CCP.Core.Tests -c Release`: **2830 passed, 1 failed, 5 skipped, 2836 total**, 1 m 57 s. The unchanged `BoardTilesTests.A_frame_fits_the_budget` measured 23.01 ms against its 12 ms assertion under concurrent compiler load; two isolated retries also failed (29.35 ms, 48.43 ms). No threshold, test or implementation was changed. The five existing Vosk tests skipped because their external model is absent. The full Core suite is therefore not green in this session.
- `CCP_USERDATA_DIR=/tmp/ccp-worker4-render-profile-final dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/ccp-worker4-render-final`: exit 0, **225/225 views rendered**. `MainShellWindow.png` was visually inspected; `SettingsTabView.png` and `LockCardFeatureControl.png` also exist. Render-all seeds no running program, so these PNGs show the ordinary layout; the new active-program reason is asserted in the headless test, not shown in an active-program screenshot.
- WPF/native Windows execution was not run on this Linux worker. The pulse test checks animation/keyframe configuration rather than rendered temporal frames. Other Linux test projects below were compiled, not executed.

Build commands below were `dotnet build <project> -c Release` plus the hardlink flags above. Test projects other than WPF reused built references. The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.

| Project | Result tail |
| --- | --- |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | `0 Error(s)`; exit 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | `0 Error(s)`; exit 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | exit timeout (300 s); see limit below |

The conditional WPF build restored successfully but did not finish its temporary Windows-head compilation within 300 seconds under sustained shared 16 GiB / 4 CPU pressure. The client was stopped; successful WPF compilation and execution are not claimed. Exact command:

`dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`

Last output:

```text
  Determining projects to restore...
/workspace/ccp-worker-4/ConditioningControlPanel/ConditioningControlPanel.csproj : warning NU1510: PackageReference System.Security.Cryptography.ProtectedData will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item. [/workspace/ccp-worker-4/Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj]
  Restored /workspace/ccp-worker-4/CCP.Core/CCP.Core.csproj (in 1.39 sec).
  Restored /workspace/ccp-worker-4/Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj (in 5.37 sec).
  Restored /workspace/ccp-worker-4/ConditioningControlPanel/ConditioningControlPanel.csproj (in 5.39 sec).
  CCP.Core -> /workspace/ccp-worker-4/CCP.Core/bin/Release/net10.0/CCP.Core.dll
/workspace/ccp-worker-4/ConditioningControlPanel/ConditioningControlPanel.csproj : warning NU1510: PackageReference System.Security.Cryptography.ProtectedData will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.
/workspace/ccp-worker-4/ConditioningControlPanel/ConditioningControlPanel_mzpdszx1_wpftmp.csproj : warning NU1510: PackageReference System.Security.Cryptography.ProtectedData will not be pruned. This package is automatically available and does not need to be referenced explicitly. Remove the PackageReference item.
```

Logs are retained locally as `/tmp/worker4-*.log`; build commands and result tails are in `/tmp/worker4-build-results.json`. No PR was opened.

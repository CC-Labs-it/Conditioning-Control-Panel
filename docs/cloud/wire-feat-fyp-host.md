- Added the FYP `file-menu` bridge path: an existing library clip or GIF opens localized **Open file** and **Show in folder** actions at the pointer. The feed's existing games panic surface closes the menu with its owner.
- Moved WPF's library-id policy to `CCP.Core/Services/Fyp/FypFileRules.cs`; the WPF menu delegates to it, and Avalonia uses the same policy. Normalized Windows separators on either OS. Remote IDs, traversal, hidden folders, executables and non-feed media are refused. File existence is checked again when an action is clicked.
- Reused `ExternalOpener` for OS integration. `Show in folder` opens the containing directory; selecting the file remains a platform limitation, documented with `ponytail:`.
- Added Core policy tests and a headless bridge test for action labels/targets, unsafe/missing/deleted files and panic cleanup. No shell process is launched by the test.
- No files imported from `parity/trunk`; fetched read-only tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Behaviour comes from WPF `FypFileMenu.cs` on BASE `0e1aa0473e7a5b6120544923e94bb04cc62a7e50`.

## Remaining lane: wire-feat-fyp-host-b

The row stays **stub**. The full remaining host involves the native ghost surface and camera/service integration, exceeding the 1000-line unit cap. This branch provides the independent file-menu slice.

- Camera: current consent prompt, async startup, restore the prior camera lifecycle on close/panic, blink and 2-second held-eye gestures, calibrated gaze coordinates and 100 ms coalescing, status frames, and native calibration dialog.
- Ghost mode: native live mirror, monitor parking/restoration, opacity, gear/mute controls, minimize/visibility healing, composition diagnostics and fallback; current head retains its explicit unsupported response.
- Feed exclusivity: stand down mandatory videos, bubble-count videos, autonomy web video and possession while FYP is open, matching the reference service checks.
- EMI: `fypOpened`/`fypClosed` moments with clamped whole-minute session duration; the existing usage score remains in place.

## Verification limits

SDK: .NET 10.0.402. Test commands used `DiffEngine_Disabled=true`; builds used `-m:1 -p:BuildInParallel=false` and `CreateHardLinksForCopyLocalIfPossible`, `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, and `CreateHardLinksForCopyAdditionalFilesIfPossible` to fit shared memory/disk.

- `dotnet build CCP.Core/CCP.Core.csproj --no-restore` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj --no-restore`: final builds succeeded, 0 errors.
- Built every Linux test csproj named by `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, and the dedicated Avalonia `CatalogueStartup`, `Consent`, `GeneralSettings`, `Language`, `Report`, `Scheduler`, and `Snapshot` suites. All completed builds passed. Dedicated auxiliary suites were compiled, not executed.
- `dotnet test Tests/CCP.Core.Tests --filter FullyQualifiedName~FypFileRulesTests`: 19 passed, 0 failed/skipped.
- `dotnet test Tests/CCP.Core.Tests --no-build`: 2829 passed, 0 failed, 6 existing skips (5 Vosk tests without the external model and the BoardTiles frame-budget test).
- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~GameWindowFypTests -p:BuildProjectReferences=false`: 4 passed, 0 failed/skipped, against the freshly compiled head. The initial bare test builder failed 4/4 on missing Segoe UI Black glyphs; the fixture now uses the shipped `Program.BuildAvaloniaApp` and Inter font, with all assertions retained. Early build attempts also encountered stale reference metadata and shared-disk ENOSPC/SIGBUS; the final head and test assembly were rebuilt successfully.
- `bash .github/scripts/core-guards.sh`: all guards passed (424 XAML references checked).
- `CCP_USERDATA_DIR=/tmp/worker2-fyp-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/worker2-fyp-render`: exit 0, 224/224 views rendered. `GameWindow.png` shows the default host frame. The focused FYP test additionally exported the real menu to `/tmp/worker2-fyp-render/FypFileMenu.png` using `CCP_FYP_MENU_RENDER`; inspected PNG shows **Open file** then **Show in folder**. That rerun also passed 4/4. Live WebKitGTK/browser gestures and native shell launch were not exercised; test shell launches are intercepted.
- Attempted `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. The Windows temporary-project compiler emitted warnings, but the build was interrupted when shared disk fell to 170 MB. No completed WPF test assembly was emitted; no Windows code or WPF tests were run. Generated WPF outputs were removed to recover space; source and tests were preserved.

No row is marked verified. The missing camera, ghost, exclusivity and EMI behaviour remains named above and in the `stub` ledger row.

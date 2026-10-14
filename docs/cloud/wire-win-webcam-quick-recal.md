# wire-win-webcam-quick-recal

- Base: `avalonia-port/goongame-core-c` at `0e1aa0473`; claimed branch `avalonia-port/cloud-wire-win-webcam-quick-recal`.
- Added the real Windows `RegisterHotKey` slot `0xB1B3`, Ctrl+Alt+G with no-repeat, tied to the shell HWND and released on shutdown. Native registration stays behind one platform seam.
- Ported WPF shortcut safety/lifecycle: settings disable, shared Core `PanicPolicy` clash checks for modifier-blind panic/pause bindings before registration and at invocation; silent consent/calibration refusals; one dialog in flight; tracker start only if needed and stop only if this shortcut started it. A hidden/minimized shell does not own the dialog. Settings/preset/remote binding changes are observed on a slow timer, and invocation checks immediately.
- Added quick recal to the shared panic registry and its safety-critical test list. Panic closes it even after tracking stopped; any close cancels pending sampling and restores a parked offset, including null, unless successful.
- Added focused headless tests using sandbox profiles and fake frame sources. Updated the touched test harnesses to load shipped fonts through `Program.BuildAvaloniaApp()`.
- Only `win-webcam-quick-recal` changed in the ledger; it remains `stub`. Existing sampling, sounds and calibrated-monitor placement are preserved.
- WPF specification read: `Windows/WebcamQuickRecalWindow.xaml` / `.xaml.cs`, `MainWindow/MainWindow.xaml.cs` quick-recal shortcut flow, and `Services/Input/GlobalHotkeyService.cs`. No code imported from parity/trunk; fetched read-only trunk tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.

## Verification limits

Commands use .NET 10.0.402, `PATH=/tmp/ccp-dotnet:$PATH`, `DOTNET_ROOT=/tmp/ccp-dotnet`, and `DiffEngine_Disabled=true`. Builds use `-c Release -m:1` plus `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true`, `CreateHardLinksForAdditionalFilesIfPossible=true`, and `CreateHardLinksForCopyLocalIfPossible=true` to fit the worker workspace. Auxiliary Linux test builds also use `BuildProjectReferences=false` after the head/Core build.

- `dotnet build CCP.Core` and `dotnet build CCP.Avalonia`: passed, 0 errors.
- Explicit builds of every Linux test project listed in `.github/workflows/build.yml`: passed, 0 errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests; CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests.
- `dotnet test Tests/CCP.Core.Tests -c Release`: `Passed! - Failed: 0, Passed: 2811, Skipped: 5, Total: 2816`. The five skips are existing optional tests; no test was removed or weakened.
- Fail-before proof: `QuickRecalHotkeyTests.Panic_ClosesQuickRecalEvenWhenTrackingWasAlreadyStopped` failed before adding the registry entry; filtered run had 3 passed, 1 failed. Repeated with the runtime path configured and observed the same failure.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~QuickRecalHotkeyTests|FullyQualifiedName~WebcamGazeTests'`: 8 passed, 0 failed, 0 skipped. Fake camera sources only.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~QuickRecalHotkeyTests|FullyQualifiedName~WebcamGazeTests|FullyQualifiedName~PanicSurfacesTests'`: `Passed! - Failed: 0, Passed: 13, Skipped: 0, Total: 13` (2 m 18 s).
- Added an owned-camera failure regression: the shortcut starts a fake tracker, a synthetic dialog failure occurs, and the tracker is stopped in `finally`. Final rebuilt safety filter (QuickRecalHotkeyTests, WebcamGazeTests, PanicSurfacesTests): `Passed! - Failed: 0, Passed: 14, Skipped: 0, Total: 14` (21 s).
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: first attempt timed out at 180 seconds. Final attempt with `UseSharedCompilation=false` passed: `560 Warning(s), 0 Error(s)`, elapsed `00:05:04.96`. WPF app and tests compiled; WPF tests were not run on Linux.
- `bash .github/scripts/core-guards.sh`: `All Core guards passed.`
- Initial SDK/path, font-harness and disk-copy failures were corrected before successful runs. No Windows runtime, real camera, real desktop, or real user profile was exercised.
- No visual layout changed in this shortcut/lifecycle slice, so render-all was not rerun; the existing quick-recal window layout and hidden hint are preserved.

## Follow-up: wire-win-webcam-quick-recal-b

- Implement independent Linux/X11 and native Wayland global quick-recal backends, including registration refusal and teardown tests.
- Port the camera start/stop shortcut before teaching the full localized two-chord hint. Show a hint only for shortcuts actually available on the running platform; it remains hidden in this slice.
- The WPF monitor picker remains unported; existing placement matches the saved calibration origin.
- Run native Windows shortcut collision, repeat suppression, tray/minimized ownership, and teardown checks on Windows. Exercise physical gaze sampling and offset persistence with consented hardware.

## Publication blocked

Implementation commit: `ac1f23cee` on the claimed branch. The fast-forward push failed:

```
remote: Permission to CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git denied to ClearlyNotMich.
fatal: unable to access 'https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git/': The requested URL returned error: 403
```

The coordinating worker also attempted GitHub connector publication and received HTTP 403, `Resource not accessible by integration`. No remote ref was changed by that attempt. Origin still has the empty claim; this implementation is preserved locally and as `/workspace/scratch/wire-win-webcam-quick-recal.patch`. No new claim was taken while writes were denied. Publishing this existing branch is required before its claim is finished.

## Account correction for publication retry

The publication-block notes above describe the earlier `ClearlyNotMich` attempt. The user selected `ObviouslyNotMich` for this retry; that connection reports repository push permission and successfully uploaded a Git blob. The current origin branch head is the publication source of truth.

# wire-shell-multi-monitor

- Open the Bubble Count result typist before enumerating monitors, so backends that report topology only after mapping a TopLevel can produce all mirrors.
- Select and position the same reported primary/fallback as WPF, even with DualMonitor disabled. Move in physical pixels while temporarily normal so maximized window managers can accept the monitor change. Activate the editable primary last; mirrors remain read-only and echo its input.
- Add an internal OS-enumeration seam and five headless behavioral cases with synthetic monitors (negative desktop coordinates and 1.5/2.0 scale), including no marked primary, unavailable topology, mirror input and force-close without game completion.
- Keep only ledger row `shell-multi-monitor` updated and **stub**. The live mandatory-video path already shares a decoder through VlcFrameSink; the WPF standalone DualMonitorVideoService has no shipped Play/PlayFile caller. Browser/Deeper clone-display fullscreen integration remains a separate coherent lane rather than a new trigger.
- No source imported from read-only `parity/trunk` (`a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` inspected); existing head files/helpers reused.

## Verification limits

- Initial `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~MultiMonitorResultTests`: **4 failed / 1 passed**, showing missing mirrors and wrong 100,100 primary placement.
- After fix, the same command: **5 passed / 0 failed / 0 skipped**; tail `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.
- Tests use the existing assembly profile sandbox and shipped font setup. Synthetic platform screens never reach a native backend; real mixed-DPI window-manager behavior and Windows display-topology changes were not exercised.
- Full `dotnet test Tests/CCP.Core.Tests --no-build` with `CCP_REQUIRE_VOSK=1` and the pinned model: **2815 passed / 0 failed / 1 existing Debug performance skip**, total 2816. Real speech-model tests ran.
- Related headless `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~MultiMonitorResultTests|FullyQualifiedName~BubbleCountGameTests|FullyQualifiedName~VideoTargetMonitorTests|FullyQualifiedName~GlobalTargetMonitorResolveTests'`: **23 passed / 0 failed / 1 existing missing-libvlc skip**, total 24. The Bubble Count clip playback test could not run without the native library; placement/mirror cases and existing monitor resolution cases ran. This combined run used the native DejaVu Sans fontconfig fallback from the shared test environment.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed**, 424 XAML type references checked.
- `dotnet run --project CCP.Avalonia --no-build -- --render-all /workspace/shared/root-multimonitor-renders`: **224/224 views rendered**, exit 0. Inspected `BubbleCountResultWindow.png`; static rendering does not prove native multi-monitor behavior.
- Build/test environment: SDK 10.0.402, `DiffEngine_Disabled=true`, `CCP_VOSK_MODEL=/workspace/shared/vosk/vosk-model-small-en-us-0.15`. Build options: `-m:1 -p:UseSharedCompilation=false -p:CreateHardLinksForAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true`, to avoid duplicated media filling the workspace. WPF cross-builds are serialized by the shared resource lock.
- `dotnet build` succeeded for **all 13 Linux targets** (CCP.Core, CCP.Avalonia and all 11 Linux workflow test projects); every build returned exit 0. Optional `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` also passed, tail **509 warnings / 0 errors**, elapsed 3m15s. WPF compiled only.
- WPF and Windows native display APIs were not executed; this worker has no Windows desktop or physical monitor topology.

## Follow-up: wire-shell-multi-monitor-b

Port the ScreenMirror clone/extend lifecycle behind a small Windows seam, with state/rules shared through Core and called by WPF, then integrate the actual browser and Deeper HTML5 fullscreen enter/exit/teardown paths. Their fullscreen signal/reparent plumbing is still absent in Avalonia. Do not invent a UI door for the dormant standalone WPF DualMonitorVideoService; port it only with an actual WPF caller. Real Windows topology restoration and mixed-DPI monitors require platform validation.

# fin4-shell-blink-trainer — blocked runtime report

- Required base verified: `ca15351f65930e2e37dcb2c10e04268feab2aee6`. The initial clean snapshot was at `f81ea01fb079b8b0ab750757ca23309cb78fdf30`; fetched the requested base, reset local work, and verified the exact required commit.
- Remote branch contains only this report. No implementation or ledger promotion is published. The existing row remains stub.
- The cloud executor disconnected during verification. Runtime status reported offline; waiting for it returned “neither ready nor starting.” Uncommitted implementation files and logs became inaccessible.
- This report is preserved through the connected GitHub API on the authorized branch only. No PR was opened. This is an infrastructure block, not a pure-feature-dependency NOOP.
- Read-only `parity/trunk` fetch succeeded. No trunk code was imported; no imported commit attribution applies.

## Remaining WPF behaviors identified

1. Camera shortcut: `ConditioningControlPanel/MainWindow/MainWindow.SessionIO.cs:1274-1431` defines the focused-window binding, global registration, global toggle, capture/cancel/reset and shared labels. Defaults are Ctrl+Alt+K; OS refusal retains the focused-window fallback.
2. Camera toggle: `ConditioningControlPanel/MainWindow/MainWindow.BlinkTrainer.cs:297-374` stops an active tracker, otherwise requests current consent before starting. The hotkey restores and activates a hidden/minimized owner before consent. Successful start/stop announce localized toasts for two seconds; a failed start must not announce success.
3. Monitor editor: `ConditioningControlPanel/MainWindow/MainWindow.LabTab.cs:1441-1515` lists Primary followed by numbered device names and dimensions. Population must not save over an absent monitor; explicit selection persists and logs the change.
4. Monitor placement: `ConditioningControlPanel/App.xaml.cs:1408-1455` selects the named tracking monitor, falling back to primary when unavailable, for calibration/Quick Recal/tracker test.
5. Session overlay precedence: `ConditioningControlPanel/Services/Tracking/GazeContentScreenPolicy.cs:39` gives an explicit monitor pick priority over DualMonitorEnabled. An unplugged explicit pick falls back to primary even in dual mode. Primary follows the global multi-monitor policy for overlays, while calibration still uses primary.

The ledger's existing demo/live stage, premium gate, session media/tiling/timing, consent, calibration, Deeper setup twins, loading splash and shared chip paths were already on the base. The two outstanding groups named by the row were camera shortcuts and explicit monitor selection.

## Unpublished work attempted

- Adapted the existing X11 summon listener to allow independent camera and EMI Desk registrations; added a small Win32 RegisterHotKey seam and focused-shell fallback.
- Routed Devices and Workshop capture actions through the existing shortcut dialog, tracker, consent dialog and notifications.
- Added a camera-shortcut panic entry to close owned modals and invalidate pending continuations.
- Enabled the Devices monitor editor and added a pure Core screen-selection policy consumed by session overlays and calibration-related dialogs.
- Added focused Core and headless tests, tester steps and a draft ledger promotion locally. These edits are uncommitted and their persistence cannot be confirmed after executor loss; do not consider them delivered.
- Final headless-native-registration guard was attempted as the executor disconnected; its application cannot be confirmed.

## Verification limits

- Base fetch, exact checkout verification, unit-branch creation and read-only trunk fetch: exit 0.
- Installed .NET SDK 10.0.402. Initial channel discovery encountered HTTP 403; the GA SDK was available.
- Observed successful Release builds, exit 0: CCP.Core, CCP.Avalonia, CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Language.Tests. These were intermediate builds, not validation of the final edits.
- Other intermediate builds failed, exit 1: a missing namespace, then a missing virtual-key mapping import; both received source corrections. Concurrent builds also collided on the Avalonia PDB and one MSBuild worker exited. A sequential clean final verification was still required.
- Intermediate focused headless test invocation, exit 1: the test assembly had not been produced successfully, so no behavior tests executed. The known font issue was not reached or established in this run.
- WPF cross-build was attempted with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. The WPF application DLL was produced; the test-project completion and exit code were not observed before disconnection.
- Core guards and full Core tests were queued after the WPF build; no completion was observed. No pass is claimed.
- The final added-line count of the inaccessible local implementation could not be measured. The remote change consists only of this report.
- Windows native behavior, physical cameras, global OS grabs, monitor changes and final headless behavior tests were not run. No test was weakened, skipped or deleted to obtain a passing result.

## Assumptions and recovery

- Use Avalonia display names as the platform equivalent of WPF device names.
- Reuse the existing X11/XWayland infrastructure on Linux; native Wayland retains a focused-window fallback unless a separate global-shortcut host is added.
- Treat unrelated tracker-state distinctions and the debug cursor as separate webcam-service scope.
- Recover the original workspace if available. Otherwise restart from the pinned base, implement the behaviors above, retain stub until complete, and run all required builds/tests/guards before publishing code.

BLOCKED: Cloud executor went offline before final verification, local commit and push; implementation remains undelivered.

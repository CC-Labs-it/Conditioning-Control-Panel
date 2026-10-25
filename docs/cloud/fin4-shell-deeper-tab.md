# fin4: Deeper shell paths

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; work is isolated on `avalonia-port/cloud-fin4-shell-deeper-tab`.
- Restore welcome/header Deeper tours through the existing tutorial service and its panic registration.
- Restore the first-visit Play-door announcement: 1200 ms delay, four finite opacity dips, quiet/motion gates, cancellation on visit, close and panic. Navigation through the live Play card now records the visit too.
- Instantiate the existing Core enhancement library, seed its bundled demos when available, refresh the visible hub after debounced library changes and dispose its watcher on shell close.
- Share WPF import orchestration in Core; wire the multi-file picker and global drop path. Preserve schema sniffing, duplicate detection, validation, source-directory persistence, recents and batch toasts. Imported/duplicate rows scroll into view and flash for 1800 ms across rescans.
- Restore local-media player/editor entry points, first-launch argument dispatch and second-instance handoff. Preserve WPF extension/path validation; extend the pipe's length framing for long media paths while retaining short launcher messages.
- Route the player's existing editor jump through the shell so editor deduplication and refresh-on-close stay shared.
- Replace the repeated historical ledger prose with current evidence; retain `stub` and add numbered tester steps.
- Read-only import source fetched: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; implementations adapt the pinned WPF reference and reuse this base's Core/head code.

## WPF gap inventory and remaining dependencies

The original row named tutorial, rail pulse, browser binding/webcam, import, file associations and the mandatory-video nudge. Source inspection also found the visible-library change callback and import row reveal missing. The tutorial, pulse, library lifecycle, import and file entry paths are addressed here.

The browser manual webcam button already implements consent, pre-calibration confirmation, background start and stop-on-cancel in `SettingsTabView.BrowserCard.cs`; its state caption and BambiCloud visibility/selection override are also already wired. The player's header/event-log editor jump already existed; its shell relay is now shared.

Remaining WPF behavior:

- Browser enhancement discovery/binding/unbinding, badge text/tag, per-page webcam-needing prompt, enhancement-match status and the enabled enhancement toggle/refresh. The head has no `BrowserAutoDiscovery` or `BrowserEnhancementBridge` service. The WPF hosts alone are 234 and 191 lines, before browser integration, UI, lifecycle and focused tests. Adding them to this unit exceeds the 1000-added-line cap. No simulated tracking/binding state is introduced.
- Mandatory-video engine-start scan and combined enhancement/webcam prompt, once per launch, remote/lockdown suppression, post-scan engine checks, queued-open recheck and the persistent “Don't ask again” choice on either button. Core already has `MandatoryVideoEnhancementScanner`; the missing host is the startup **modal** ladder. `Platform/StartupLadder.cs` explicitly implements passive routing only, with no modal pump. The WPF `StartupPresenter.cs` reference is 646 lines before this nudge and its tests; a faithful pump plus browser/nudge work cannot fit this unit.

These are live WPF behaviors, so P122 does not apply and the row is not promoted to wired. This is a partial implementation, not a dependency-only NOOP.

## Assumptions

- Reuse the existing Core library instead of adding a parallel import service/duplicate algorithm.
- Retire the announcement on every successful Deeper navigation because the live Play card reaches `ShowTab` directly; retaining state only in the unused legacy rail handler would keep announcing an already visited tab.
- Preserve existing short single-instance messages; use bounded extended framing for media paths rather than truncate them.

## Verification limits

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- Every Linux test csproj named in `.github/workflows/build.yml` (12 projects): build exit 0, 0 errors. Some existing warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors, 251 existing warnings. Compiled only; Windows execution is unavailable here.
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 0; 3955 passed, 5 existing skips, 0 failed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~DeeperShellEntryTests|FullyQualifiedName~SingleInstanceTests|FullyQualifiedName~DeeperHubTests|FullyQualifiedName~PanicSurfaces'` with `DiffEngine_Disabled=true`: exit 0; 16 passed, 0 skipped/failed. Includes real sandbox file-watcher refresh and retained import highlight.
- Regression proof: restoring the original welcome-tour no-op made the new test fail at its active-tour assertion (exit 1); restoring the fix passes.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `git diff --check`: exit 0.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this pinned base (exit 127); manually checked one ledger row and four numbered WPF/Rows tester steps.
- `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --nav-check` in an isolated profile: exit 1. Failures: “OpenDeviceSettings reveals Devices (saw data)” and “an unknown section still opens the door and changes nothing.” Devices routing was not changed in this unit; these failures remain unresolved.

Initial attempts hit a read-only checkout, omitted sparse assets, concurrent-build file locking, disk pressure, and a frame-budget timing failure during compilation. The final required builds/tests above ran successfully after restoring tracked test inputs and running builds sequentially. No physical camera, real desktop notification, live browser enhancement, Windows execution or remote service is exercised. All headless tests use sandbox profiles. The row remains stub for the dependencies above.

## Changed files

- `CCP.Avalonia/App.axaml.cs`
- `CCP.Avalonia/Platform/SingleInstance.cs`
- `CCP.Avalonia/Program.cs`
- `CCP.Avalonia/Views/Deeper/EnhancementPlayerWindow.axaml.cs`
- `CCP.Avalonia/Views/Tabs/DeeperTabView.axaml`
- `CCP.Avalonia/Views/Tabs/DeeperTabView.axaml.cs`
- `CCP.Avalonia/Views/Windows/LauncherWindow.axaml.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.DeeperHub.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.DeeperImport.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.DeeperPulse.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.DeeperTab.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.SessionIO.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.axaml.cs`
- `CCP.Avalonia/Views/Windows/PanicSurfaces.cs`
- `CCP.Core/Services/Deeper/DeeperFileOpen.cs`
- `CCP.Core/Services/Deeper/EnhancementImportBatch.cs`
- `Tests/CCP.Avalonia.Tests/DeeperShellEntryTests.cs`
- `Tests/CCP.Avalonia.Tests/SingleInstanceTests.cs`
- `Tests/CCP.Core.Tests/DeeperShellEntryTests.cs`
- `docs/avalonia-parity.md`
- `docs/avalonia-test-plan.md`
- `docs/cloud/fin4-shell-deeper-tab.md`

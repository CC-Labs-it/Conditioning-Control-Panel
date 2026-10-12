# refactor(core): Brain Drain notification rules move to CCP.Core (notifications-core-a)

Unit A of the Core move for `ConditioningControlPanel/Services/Notifications`.

## Inventory of `ConditioningControlPanel/Services/Notifications`

| File | Lines | State before this unit | This unit |
|------|------:|------------------------|-----------|
| `BrainDrainKeepClear.cs` | 42 | WPF only, pure (no WPF/App.* use) | **moved** to `CCP.Core/Services/Notifications/` |
| `BrainDrainVisualPolicy.cs` | 40 | WPF only, pure | **moved** to `CCP.Core/Services/Notifications/` |
| `NotificationService.cs` | 288 | WPF toast UI; Avalonia has its own port in `CCP.Avalonia/Helpers/NotificationService.cs` (sticky toasts not ported) | stays (next units) |
| `OverlayService.cs` | 3862 | WPF + Win32 overlay host; holds a few pure static rules | stays (next units) |
| `TrayIconService.cs` | 280 | WinForms tray; Avalonia has `MainShellWindow.Tray.cs` | stays (next units) |

No Core copy of either moved rule existed, and `parity/trunk` has no Core version of anything in
this folder (only `TrayIconService.cs`, still in WPF), so nothing is imported from trunk and there
are no trunk commits to credit for this unit.

## What changed

- `git mv` `BrainDrainKeepClear.cs` and `BrainDrainVisualPolicy.cs` into
  `CCP.Core/Services/Notifications/`. Namespace (`ConditioningControlPanel.Services`) and
  `internal` visibility are unchanged; Core already grants `InternalsVisibleTo` to the WPF app and
  its tests, so `OverlayService`, `AutonomyService` and `Chaos/EffectPayload` keep compiling with
  no edits and no seams (neither rule touched `App.*`, settings, paths or logging).
- `git mv` `Tests/ConditioningControlPanel.Tests/BrainDrainKeepClearTests.cs` to
  `Tests/CCP.Core.Tests/` (namespace `CCP.Core.Tests`).
- Split `BrainDrainBlurZeroTests`: the settings-clamp and policy value tests move to
  `Tests/CCP.Core.Tests/BrainDrainVisualPolicyTests.cs`; the melt-bubble test and the WPF
  call-site source scans (OverlayService, AutonomyService, EffectPayload, the XAML slider,
  BrainDrainService) stay in the WPF test project because they read WPF code. The scanned files
  did not move, so no `SourceRoots` change is needed.
- No embedded resources moved, so no `core-guards` baseline entry.
- No Avalonia UI in this unit.

## Next units

- **notifications-core-b**: OverlayService's pure statics -> Core:
  `ShouldStopHeldOverlay`, `ShouldForceZOrderTick`, `ResolveAdHocOverlayOpacity`,
  `ResolveZOrderAction` / `ResolveVideoAnchor` / `ResolveAttentionLayerAction` (+ `ZOrderAction`),
  `TryParseHexColor`, with their WPF tests.
- **notifications-core-c**: NotificationService's sticky-toast rule (`ShowSticky` skip when the key
  is in `DismissedNotificationKeys`, dismiss persists the key, `Dismiss(key)` drops pending) as a
  small Core class; then port `ShowSticky`/`Dismiss` to the Avalonia `NotificationService`.
- **notifications-core-d**: TrayIconService menu/visibility rules vs `MainShellWindow.Tray.cs`.
- Avalonia Brain Drain overlay wiring to `BrainDrainVisualPolicy` / `BrainDrainKeepClear` (the
  Avalonia head has no blur-strength reader yet).

## Verification limits

- `dotnet test Tests/CCP.Core.Tests -c Release` (with `DiffEngine_Disabled=true`):
  1160 passed, 5 skipped (Vosk model absent), 2 failed, both sandbox-only and outside this diff:
  `LoopbackOAuthTests ... (host: "[::1]")` (no IPv6 loopback in the container) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` (the container runs as root,
  so the read-only folder can still be deleted). Filter `BrainDrain`: 23/23 passed.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet build -c Release` succeeded for CCP.Avalonia, CCP.Core.Settings.Tests,
  CCP.Core.Content.Tests, CCP.Avalonia.Tests, and the Avalonia Consent, Language,
  CatalogueStartup, Scheduler, Report, GeneralSettings and Snapshot test projects.
- `Tests/ConditioningControlPanel.Tests` (which compiles the WPF app) **built** with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, but
  its tests could not **run** here (no Microsoft.WindowsDesktop.App runtime on Linux), so the
  trimmed `BrainDrainBlurZeroTests` and the WPF call sites of the moved rules were compiled, not
  executed. CI's Windows job runs them.

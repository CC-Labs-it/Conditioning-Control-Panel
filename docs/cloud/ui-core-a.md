# ui-core-a: Core move, unit A (ConditioningControlPanel/Services/UI)

Base: `avalonia-port/lang-italian`. Branch: `avalonia-port/cloud-ui-core-a`.

## Inventory of ConditioningControlPanel/Services/UI

| File | State before this unit | This unit |
|---|---|---|
| `LayoutDrift.cs` | pure, WPF-only copy | **moved** to `CCP.Core/Services/UI/` |
| `LuckyToastRule.cs` | pure, WPF-only copy | **moved** to `CCP.Core/Services/UI/` |
| `DashboardCardDepth.cs` | WPF view helper; its travel rule already routes to Core `DepthLaw` via `DepthRules` | stays (WPF visuals) |
| `DoNotDisturbGuard.cs` | already a thin user32 shim over Core `DndGuard` / `DndProcessList` | stays (Win32 seam) |
| `HelpTooltipBuilder.cs` | WPF ToolTip builder; markup rule already in Core `HelpMarkup` | stays (WPF visuals) |
| `HangContext.cs` | WPF-free except its `App.*` / `UiOpTracker` / `VideoDiag` / `SessionEngine` probes | next unit |
| `UiOpTracker.cs`, `UiHangWatchdog.cs`, `UiThreadStacks.cs` | bound to WPF `Dispatcher` (+ ClrMD, kernel32) | next unit |
| `FontGuard.cs` | pure `Sanitize` / `FilterChain` mixed with WPF `FontFamily` resources | next unit |
| `InteractionQueueService.cs` | queue rules + WPF dispatcher trigger and `App.Logger` | next unit |
| `ScreenShakeService.cs`, `WindowChromeHelper.cs` | WPF / DWM / WebView2 only | stay in WPF |

## What changed

- `git mv` `ConditioningControlPanel/Services/UI/LayoutDrift.cs` -> `CCP.Core/Services/UI/LayoutDrift.cs`
  (namespace unchanged, so `MainWindow.WorkAreaFit.cs` compiles as is; doc comment now says where
  the caller lives and that Avalonia has no drift path).
- `git mv` `ConditioningControlPanel/Services/UI/LuckyToastRule.cs` -> `CCP.Core/Services/UI/LuckyToastRule.cs`
  (unchanged; stays `internal`, Core already has `InternalsVisibleTo("ConditioningControlPanel")`).
- Neither file needed a seam: no `App.*`, no logger, no resources. No core-guards baseline entry.
- Tests: `git mv` `Tests/ConditioningControlPanel.Tests/LayoutDriftTests.cs` -> `Tests/CCP.Core.Tests/`
  (namespace only). The three pure `LuckyToastRule.Decide` facts moved out of the WPF
  `RenderStallGuardTests` into `Tests/CCP.Core.Tests/LuckyToastRuleTests.cs`; the source scan
  `LuckyToast_HandlerNeverBuildsAWindowPerProc` stays in the WPF suite (it reads WPF
  `MainWindow.Enhancements.cs`, which did not move).
- No trunk import: `parity/trunk` has neither file in Core (it still carries both under
  `ConditioningControlPanel/Services/UI/`), so there are no trunk commits to credit.
- No Avalonia UI in this unit.

## Next units

- **ui-core-b:** `HangContext` to Core behind one state-probe seam set at WPF `App` startup
  (the `App.*`, `SessionEngine`, `UiOpTracker`, `VideoDiag` lines), with its pure tests from
  `UiHangWatchdogTests`.
- **ui-core-c:** the pure halves of `UiHangWatchdog` (`ThresholdFor`, `IsHung`, resource line
  formatting) and `UiOpTracker.ShortName`, split from their WPF `Dispatcher` hooks.
- **ui-core-d:** `FontGuard.Sanitize` / `FilterChain` (chain rules) to Core; WPF keeps `FontFamily`
  resolution and resource publishing.
- **ui-core-e:** `InteractionQueueService` queue rules to Core with the dispatcher trigger and logger
  as seams (Avalonia then shares the queue).
- Stay in WPF: `ScreenShakeService`, `WindowChromeHelper`, `DashboardCardDepth`, `HelpTooltipBuilder`,
  `DoNotDisturbGuard` (shim), `UiThreadStacks`.

## Verification limits

- .NET SDK 10.0.112 (Ubuntu archive package; `builds.dotnet.microsoft.com` is blocked by this
  session's egress policy).
- `dotnet build` Release, 0 errors: CCP.Core, CCP.Avalonia, CCP.Core.Settings.Tests,
  CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.{Consent,Language,CatalogueStartup,
  Scheduler,Report,GeneralSettings,Snapshot}.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: succeeded (so the WPF app compiles
  with the moved files), but the WPF suite **cannot run on Linux** (Microsoft.WindowsDesktop.App
  missing); `RenderStallGuardTests` was compiled, not run.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests` (DiffEngine_Disabled=true): 1171 passed, 5 skipped, 2 failed.
  Moved tests (`LayoutDriftTests`, `LuckyToastRuleTests`): 13/13 passed. The 2 failures are
  environmental and touch nothing in this diff:
  `LoopbackOAuthTests...(host: "[::1]")` (no IPv6 loopback in the container) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (tests run as root, so the
  folder can always be deleted).
- No Avalonia headless tests run: this unit changes no Avalonia code.

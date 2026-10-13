# refactor(chaster): move the booked-pop rules into CCP.Core (chaster-core-a)

Core move, unit A of `ConditioningControlPanel/Services/Chaster`.

## What is in Services/Chaster on the base

Most of Chaster already lives in `CCP.Core/Services/Chaster` (ChasterService and its partials,
ChasterClient, the ladder/raffle, the tab rules, BookedFlashPlan, CapNoticeRule, ...). Only five
files were left in the WPF head:

| File | Status after this unit |
|---|---|
| `BookedPopLayout.cs` | **moved** to `CCP.Core/Services/Chaster/` (pure layout/motion maths) |
| `CapNotice.cs` | **moved** to `CCP.Core/Services/Chaster/` (pure; already delegated to Core `CapNoticeRule`) |
| `ChasterHooks.cs` | stays (unit B): App.* event wiring, with three pure rules inside |
| `ChasterServiceApp.cs` | stays: the head's construction of ChasterService + DEBUG demo (head-only by design) |
| `DpapiChasterTokenStore.cs` | stays: Windows DPAPI token store (Avalonia has its own `SecretChasterTokenStore`) |

## Changes

- `git mv` `BookedPopLayout.cs` and `CapNotice.cs` into `CCP.Core/Services/Chaster/` (same
  namespace `ConditioningControlPanel.Services.Chaster`).
- `BookedPopLayout` no longer uses `System.Windows.Point` / `Rect`: it takes the Core
  `ScreenPoint` (already the type `ChasterService.BookedAt` / `CapRefused` raise) and the monitor
  as the existing Core `AvatarTubeLayout.Box`. No new types. Only difference: a zero-size monitor
  rect now counts as "unknown" (`Box.IsEmpty`) where WPF's `Rect.IsEmpty` only matched
  `Rect.Empty`; the head always passes a real screen's bounds, so behaviour is unchanged.
- WPF routed to the Core copy: `MainWindow.Chaster.cs` keeps the service's `ScreenPoint` end to
  end (the `new Point(o.X, o.Y)` conversions are gone, cursor read returns `ScreenPoint`);
  `ChasterBookedPop.cs` stores a `ScreenPoint` and passes the screen bounds as a `Box`.
- Tests moved: `BookedPopLayoutTests` and `CapNoticeTests` from `Tests/ConditioningControlPanel.Tests`
  to `Tests/CCP.Core.Tests` (Point/Rect → ScreenPoint/Box, assertions unchanged).
- No embedded resources, no new seams, no core-guards baseline change needed.
- No imports from `parity/trunk` in this unit (nothing to credit).

## Next units

- **chaster-core-b**: split `ChasterHooks` — move its pure rules (`EscapeCosts`, `QuestRow`,
  `SlotLineRow`, `OpenDailies`) into Core, route WPF `ChasterHooks` and Avalonia
  `Platform/ChasterHead.Attach` (which re-spells the quest/weekly ternary and the open-dailies
  count) to them; move the pure parts of `ChasterHooksTests` (the source-scan guard must keep
  reading the hooks file through `SourceRoots`).
- **chaster-core-c** (optional): pull the DEBUG demo fakes (`DemoRaffle`, `DemoChaster`,
  `DemoTokens`) out of `ChasterServiceApp` into Core so Avalonia can offer `CCP_CHASTER_DEMO`;
  `ChasterLadderScrapRenderTests` uses `DemoRaffle.Board`.
- **Avalonia UI** (not Core): the booked "+3:00" pop window / "day is full" tag on the Avalonia
  head (`ctrl-chaster-booked-pop`), now able to use the Core `BookedPopLayout` / `CapNotice`.
- `ChasterServiceApp` (head construction) and `DpapiChasterTokenStore` (Windows DPAPI) stay in
  the WPF head by design.

## Verification limits

- .NET SDK 10.0.112 (Ubuntu package; the dotnet-install host is blocked by egress policy here).
- `dotnet build` succeeded: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, Tests/CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests,
  .Language.Tests, .CatalogueStartup.Tests, .Scheduler.Tests, .Report.Tests,
  .GeneralSettings.Tests, .Snapshot.Tests, and Tests/ConditioningControlPanel.Tests with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  (so the edited WPF files compile).
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1253 passed, 5 skipped, 2 failed — both unrelated to this
  diff and environmental: `LoopbackOAuthTests ... host: "[::1]"` (no IPv6 loopback in this
  container) and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root,
  so the read-only folder trick does not block deletion). The moved tests: 31/31 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~Chaster`: 33/33 passed.
- Not run: the WPF app and Tests/ConditioningControlPanel.Tests (Windows only); the WPF
  `ChasterBookedPop` placement path (Win32 `SetWindowPos`, `Screen.FromPoint`) is compiled, not run.

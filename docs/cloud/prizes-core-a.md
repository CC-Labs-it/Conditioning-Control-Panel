# refactor(core): move Services/Prizes rules into CCP.Core (prizes-core-a)

Core move, unit A of the Back Room prizes port. Both heads now share one copy of the prize rules.

- `git mv` ConditioningControlPanel/Services/Prizes/{OwnershipService,PrizeGrants,V2Badges,V2PurchaseRule,V2PurchaseService,V2WalletAdoption}.cs -> CCP.Core/Services/Prizes/ (same namespace). `V2WalletAdoption` matches trunk 2d3523b2b, which made the same move; the rest are new here (trunk keeps head-local copies under CCP.Avalonia/Platform instead).
- `OwnershipService`: `App.UnifiedUserId` -> `CoreAccount.UnifiedUserId` (WPF's property already reads that field); `App.Logger` -> Serilog `Log`. The WPF dispatcher marshal moved out: the public ctor is now `OwnershipService(Action<Action> raise)`, and WPF `App` passes the same marshal it had (inline on the UI thread, BeginInvoke otherwise, dropped during shutdown, synchronous with no Application). `ReadDebugOverride` is internal so the Avalonia head reads `CCP_PRIZE_GRANTS` through it.
- `V2PurchaseService`: `App.Logger` -> Serilog `Log`. Its relay contract (`IBackRoomRelay`, `BackRoomStationResult`) moved out of WPF BackRoomApi.cs into CCP.Core/Services/BackRoom/BackRoomRelay.cs, same namespace; `BackRoomApi` still implements it.
- Duplicate rules removed:
  - Core `RacingAccess` had its own `RacingTrack(n)`/`RacingTrackMax`; it now uses `PrizeGrants`. WPF `RacingTrackIdParityTests` only pinned the two copies together, so it is deleted. Core `RaceCoreSeamsTests` now checks that the door asks for exactly `PrizeGrants.RacingTrack(0..10)`.
  - Avalonia `Platform/PrizeOwnership` had its own override parser/matcher, env read and `FlashDriftBounce` spelling. It now calls Core `OwnershipService.ParseOverride/MatchesOverride/ReadDebugOverride` and `PrizeGrants.FlashDriftBounce`. Its existing `FlashDriftTests` still pass.
- Tests moved (renames) to Tests/CCP.Core.Tests: PrizeOwnershipServiceTests, V2BadgesTests, V2PurchaseRuleTests, V2PurchaseServiceTests, V2WalletAdoptionTests (namespace `CCP.Core.Tests`). One assertion depends on WPF `AmbientBubbleMotion.AnyV2Owned`, so it moved to WPF `BubbleMotionV2Tests`.
- New test `OwnershipServiceHeadSeamTests`: the public ctor reads `CoreAccount.UnifiedUserId`, raises only through the head's marshal, and stops counting grants after an account switch.
- No Avalonia UI in this unit. Nothing in the Avalonia head constructs an `OwnershipService` yet.

## Next units
- prizes-avalonia-b: Avalonia attaches an `OwnershipService` (marshal = `Dispatcher.UIThread.Post`) to `PrizeGrants`, feeds it from the server snapshot (port of `ProfileSyncService.ApplyValidatePrizes`), and replaces `Platform/PrizeOwnership` with `PrizeGrants.IsGranted` (`RacingAccess.IsGrantedProvider` too).
- prizes-avalonia-c: an Avalonia `BackRoomApi` relay, then the "Get it" row (WPF `Controls/V2GetItRow`) on the Flashes and Bubble Pop panels, driven by Core `V2PurchaseService`/`V2PurchaseRule`, with wallet adoption through `V2WalletAdoption`.
- prizes-avalonia-d: the champagne v2 pill on the dashboard tiles and side rail from `V2Badges` (MainShellWindow.FavoritesRail ponytail note), plus redraws on `PrizeGrants.GrantsChanged` (LauncherWindow, FlashFeatureControl).

## Verification limits
- `dotnet test Tests/CCP.Core.Tests` (Linux, .NET 10.0.112): 1555 passed, 5 skipped, 2 failed (1562 total). The 2 failures are `LoopbackOAuthTests.GoodState_OnEitherLoopback...(host: "[::1]")` (no IPv6 loopback in the container) and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so the folder can be deleted). Both fail the same way with this change stashed, and neither touches prizes.
- Prize/race filter (`OwnershipServiceHeadSeam|PrizeGrants|PrizeOwnership|V2|RaceCoreSeams`): 145/145 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter FlashDriftTests|GameWindowTests`: 15/15 passed.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- Builds succeeded: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR, and every Linux test csproj in build.yml (Core, Core.Settings, Core.Content, Avalonia, Avalonia.Consent/Language/CatalogueStartup/Scheduler/Report/GeneralSettings/Snapshot).
- Compiled but NOT run: the WPF app and Tests/ConditioningControlPanel.Tests (built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; the Windows Desktop runtime is not available on Linux). This covers the WPF `App` ownership marshal and the moved `BubbleMotionV2Tests` assertion. Windows CI has to run them.

# refactor(backroom): move the Back Room rules into CCP.Core (backroom-core-a)

Unit A of the trunk 'BackRoom' import. The WPF-independent Back Room rules now live once in
`CCP.Core/Services/BackRoom/`, so the Avalonia head can host the room on the same code as WPF.
No Avalonia UI in this unit. WPF keeps the same behaviour.

- `git mv` from `ConditioningControlPanel/Services/BackRoom/` to `CCP.Core/Services/BackRoom/`, unchanged
  (100% renames): `BackRoomBridge.cs` (the page protocol), `BackRoomContracts.cs`, `BackRoomStubs.cs`,
  `BackRoomFxArgs.cs`, `BackRoomFxPlan.cs`, `BackRoomFxQueue.cs` (hero gate, strobe pacer),
  `BackRoomTabLedger.cs`, `Overlays/BackRoomOverlayMath.cs`. Same namespaces, so no consumer changed.
- `BackRoomFx.cs` (the fx dispatcher) moved; `App.Logger` became Serilog's static `Log` (WPF sets
  `Log.Logger = App.Logger`, so the same sink).
- `BackRoomApi.cs` (station relay, the Ops whitelist, refusal mapping) moved with two small seams in
  place of `App.*`: `BackRoomApi.MergedReply` (contract D; WPF wires `MergedAccountRecovery.TryHandle`) and
  `BackRoomApi.DefaultApplyPrizes` (WPF wires `App.Ownership.ApplySnapshot`). `AppIdentity` reads
  `CoreSettings.Current`; `App.Logger` became `Log`. Both seams are set next to the other `BackRoomApi`
  defaults in `App.xaml.cs`.
- Tests: `BackRoomApi`, `BackRoomBridge`, `BackRoomBridgeLifecycle`, `BackRoomFx`, `BackRoomFxPlan`,
  `BackRoomFxXp`, `BackRoomHypnoFx`, `BackRoomHypnoPlan`, `BackRoomOverlayMath` and `BackRoomTabLedger`
  test classes moved from `Tests/ConditioningControlPanel.Tests` to `Tests/CCP.Core.Tests`, so they now run on Linux CI.
  The four head-bound checks inside them moved to WPF tests instead of being dropped:
  the OwnershipService replay case (`BackRoomApiOwnershipTests`), `ChasterHooks.SlotLineRow`
  (`ChasterHooksTests`), `BackRoomLoomSpiralOverlay.FitLongSide` (`BackRoomSpiralCellTests`) and
  `e_backroom_slot` in `FeatureDayEntry.EventKeys` (`FeatureDayLogTests`).
  The bridge rig (`Req`, `Clock`, `Relay`, `CountingFx`, `Rig`) moved out of `BackRoomBridgeTests.cs` into
  `Tests/Shared/BackRoomBridgeRig.cs` (a `partial` of the same class), linked into both test projects,
  because the WPF haptic, voice, media, room-option and race-handoff suites still drive the bridge with it.
  New: `Counter_Buy_WithNoApplier_GoesToTheHeadsDefault`, `EveryReply_IsHandedToTheMergedAccountHook`
  for the two seams. The test sink's spiral record splits on both separators, because the fixtures
  are `C:\woven\...` paths and `Path.GetFileName` keeps them whole on Linux.
- `docs/avalonia-parity.md` svc-prize-ownership row points at the new `BackRoomApi.cs` path.
- Trunk credit (`parity/trunk`, read only, used as a guide; trunk copies these files into
  `CCP.Avalonia/Views/Games/BackRoom/`, while this unit moves them into Core): `b9f96824e` (Back Room
  station relay + bank), `a71790395` (fx plan, dispatcher and XP suites run against the ported
  dispatcher), `8459d203f` (`BackRoomFxIntensity` in Core, already on the base).

Stays in WPF (head-bound): `BackRoomHostService` (WebView2 host), `BackRoomFxServices` (the WPF fx sink,
Dispatcher), `BackRoomMedia` + `BackRoomRemotePool` (App.Flash, `RemoteMediaCache`), `BackRoomVoice`
(NAudio), `Overlays/*` windows, `BreakoutHostService`, `BreakoutAccess`.

Next units:
- **backroom-core-b (deal)**: `BackRoomMedia` + `BackRoomRemotePool` into Core behind provider Funcs
  (image list, assets root, materialize/release), with their tests (`BackRoomMediaTests`,
  `BackRoomMediaSourceTests`, `BackRoomClipDealTests`, `BackRoomActiveWordsTests`).
- **backroom-host (host window)**: the Avalonia WebView host + an `IBackRoomFxSink` for Avalonia overlays
  (trunk `CCP.Avalonia/Views/Games/GameWindow.BackRoom.cs`, `BackRoomFxHead.cs`, `BackRoomOverlays.cs`,
  `BackRoomWire.cs`), voice and haptics; gates/room-option rules out of `BackRoomHostService`.
- **backroom-races**: the race return (trunk `GameWindow.BackRoom.RaceReturn.cs`, `BackRoomRaceReturnTests`).
- Follow-up: `CCP.Avalonia/Platform/FriendsHead.cs` keeps its own copy of `AppIdentity` and can now call
  `BackRoomApi.AppIdentity`.

## Verification limits

- `dotnet build` Release, all 0 errors: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR, every Linux test
  csproj in `.github/workflows/build.yml` plus Tests/CCP.Avalonia.Tests, and
  `Tests/ConditioningControlPanel.Tests` (pulls in the WPF app) with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests` (DiffEngine_Disabled=true): 1687 passed, 5 skipped, 2 failed. The 2
  failures fail the same way on the base `avalonia-port/lag-fixes-a` in this container and are
  environmental: `LoopbackOAuthTests ... (host: "[::1]")` (no IPv6 loopback) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so the read-only
  folder can still be deleted). All moved Back Room tests pass.
- Not run: the WPF test suite (`win-x64` testhost, Windows only), so the relocated WPF test cases and
  `BackRoomApiOwnershipTests` are compile-verified only. The WPF app is compiled, not run: the
  `App.xaml.cs` seam wiring and the Back Room room itself were not exercised on Windows.
- No Avalonia UI changed, so no headless Avalonia test classes are relevant to this unit.

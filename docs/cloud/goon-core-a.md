# goon-core-a: Lobby open-table rules into CCP.Core

Unit A of the trunk import "Goon / Lobby / Stakes": the first coherent group of WPF-independent
rules, the open tables the Lobby merges, now has one copy in CCP.Core that both heads share. No
Avalonia UI in this unit.

## What changed

- `git mv` (renames, `git diff -M` shows 0/0 or a small edit):
  - `ConditioningControlPanel/Services/Lobby/LobbyModel.cs` -> `CCP.Core/Services/Lobby/LobbyModel.cs`
    (LobbyRow/LobbySnapshot/LobbyMerge/LobbySummary/LobbyGates, unchanged).
  - `ConditioningControlPanel/Services/Lobby/PbpLobbyApi.cs` -> `CCP.Core/Services/Lobby/PbpLobbyApi.cs`.
  - `ConditioningControlPanel/Services/GoonGame/GoonOpenTablesApi.cs` -> `CCP.Core/Services/GoonGame/GoonOpenTablesApi.cs`
    (OpenTable, GoonJoinCode, the `/v2/goon/open` wire, the shared GoonOpenTables cache).
  - Tests: `LobbyWireTests`, `LobbyMergeTests`, `GoonOpenTablesTests` -> `Tests/CCP.Core.Tests/`.
- Core seam `LobbyWire` (in `PbpLobbyApi.cs`): `DefaultIdentity` / `DefaultBaseUrl`, the same shape
  as `InviteApi`'s. It replaces the head-only `BackRoomApi.AppIdentity` / `BaseUrl` defaults in the two
  wires. Unseeded = signed out and no url: nothing is sent and the wires read as empty.
  WPF seeds it in the `static App()` ctor next to the InviteApi seeds, so WPF behaves the same.
- `App.Logger?.Debug` -> Serilog `Log.Debug` in the moved files (same messages).
- `LobbyPollPlan` (pure poll backoff) moved from WPF `LobbyService.cs` into Core `LobbyModel.cs`;
  `LobbyService` itself stays in WPF (DispatcherTimer, App.Friends, App.AvailableSubjects).
- New test `LobbyWireSeamTests.Unseeded_wire_sends_nothing_and_reads_empty`: with no proxy seeded neither
  wire sends a request; once seeded the chess wire calls `{base}/v2/pbp/lobby?unified_id=`.
- Trunk credit (shape hint only, adapted to our tree): `9b666936e` (one copy of Lobby, stakes, ...
  in Core; the `LobbyWire` seam), `fc6ca6fd4` (source tripwires follow the rules to Core). None of the
  moved WPF files existed on our base under CCP.Core, so nothing was overwritten.

## Next units

- **goon-core-b (Stakes)**: `StakeApi`, `StakeSettlement`, `StakeBridge` + `StakeText` into
  `CCP.Core/Services/Stakes/` next to `StakeRules`. Needs seams for `StakeSettlement.Shared`
  (App.Chaster `NoteSeconds`, App.Settings SP adoption on the UI thread, `V2WalletAdoption`) and
  `StakeBridge.ForApp` / `AppTimeOk` / `AppEnableLossRow`; move `StakeTests` (source scan in
  `GoonStakeWireTests` keeps reading WPF hosts).
- **goon-core-c (Lobby service)**: `LobbyService` with a UI-timer seam, plus a Core
  `RemoteDirectoryApi` for the Remote seats (trunk `LobbyWire.cs`).
- **goon-core-d (Goon match rules)**: MatchClock, GoonRoundModel + Rounds, GoonSuddenDeath,
  GoonShareCard, TransferInboxStore, GoonCacheBridge, GoonOnlineMedia, transports and the windowless
  half of GoonHostService.
- **Avalonia Goon host window**, then **Avalonia Lobby page / launcher chip and Goon game UI**, seeding
  `LobbyWire` from `Platform/FriendsHead.cs`.

## Verification limits

- `dotnet build` (Debug, Linux, .NET 10.0.112): CCP.Avalonia, Tests/CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests and the six other
  CCP.Avalonia.*.Tests projects: all `Build succeeded`.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: `Build succeeded` (WPF app compiles with
  the moves and the seed). WPF tests and the WPF app were compiled but NOT run (Windows only).
- `bash .github/scripts/core-guards.sh`: `All Core guards passed.`
- `dotnet test Tests/CCP.Core.Tests` (DiffEngine_Disabled=true): 1168 passed, 5 skipped, 2 failed.
  The 2 failures (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` and
  `LoopbackOAuthTests.GoodState_OnEitherLoopback_...(host: "[::1]")`) fail identically on the base
  `origin/avalonia-port/lag-fixes-a` in this container (runs as root, no IPv6 loopback); unrelated.
  Moved + new Lobby/Goon open-table tests: 35/35 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter Invite|Friends`: 33/33 passed (no Avalonia code changed).

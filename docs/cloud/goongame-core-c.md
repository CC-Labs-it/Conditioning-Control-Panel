# goongame-core-c: move the Goon Game clock and sudden-death rounds into CCP.Core

Base: `avalonia-port/game-pbp`. Third Core move for `ConditioningControlPanel/Services/GoonGame`.

Already in `CCP.Core/Services/GoonGame` before this unit: `GoonContracts`, `GoonDraft`,
`GoonMatchTypes`, `GoonRng` (87b13e299), `GoonWire` (f21718006), `GoonScoring` (511338042).

## What changed

- **Moved (pure `git mv`, namespace unchanged)** — the shared match clock and the sudden-death
  round group, which only depend on each other, the Core contracts and two UI hops:
  - `MatchClock.cs`
  - `GoonSuddenDeath.cs` (`GoonSuddenDeathRunner`, `GoonRoundJudge`)
  - `Rounds/GoonRoundModel.cs`, `Rounds/QuickDrawRound.cs`, `Rounds/StaringContestRound.cs`,
    `Rounds/ReactionDuelRound.cs`, `Rounds/BubbleRaceRound.cs`
- **No new seam** — both UI hops reuse the existing `CoreDispatch` seam that WPF `App` already
  seeds at startup:
  - `GoonDispatch` (async post, was WPF-only in `GoonTransportBase.cs`) is now
    `CCP.Core/Services/GoonGame/GoonDispatch.cs` and routes through `CoreDispatch.Post`, keeping
    its "callback exceptions are logged, never reach the dispatcher" wrapper. The WPF copy is
    deleted; `GoonTransportBase` resolves the Core one (InternalsVisibleTo), so there is one copy.
    Same adaptation as parity/trunk 08e9b76a9 (which ported `MatchClock` with `CoreDispatch.Post`
    as a copy); here it is a move, and the exception wrapper is kept.
  - `GoonUi.Invoke` (sync presenter call) now uses `CoreDispatch.Invoke` with the same 5 s bound
    `DispatcherHelper.RunOnUISync` had; presenter faults are still logged and swallowed, and a
    timed-out hop is now logged too. `App.Logger` became Serilog `Log` (same logger).
- **Tests** — new `Tests/CCP.Core.Tests/GoonClockAndRoundsTests.cs` (12 cases; nothing tested
  this code before): a wired host/guest pair converges on a 5 s skew with the host pinned at 0;
  `ScheduleAt` refuses an unsynced clock; `Synced` and a scheduled fire both hop through
  `CoreDispatch.PostProvider`; a throwing dispatched callback is contained; presenter calls run
  inline headless, swallow faults, use the 5 s bounded hop, and give up on a wedged UI thread;
  `GoonRoundJudge` decides each round kind and agrees with itself with the arguments swapped.

### WPF behaviour delta (deliberate, small)

While the WPF dispatcher is shutting down, the WPF `CoreDispatch` providers run the action in
place, where the old Goon-only dispatchers dropped it. During shutdown a Goon transport event or
presenter call can therefore run on the caller's thread; any exception it raises is caught and
logged by the wrappers above. Everything else (inline with no app, inline on the UI thread,
`BeginInvoke` at Normal priority otherwise, 5 s bounded sync hop) is unchanged.

## Next units (still in `ConditioningControlPanel/Services/GoonGame`)

- **goongame-core-d**: transports — `GoonTransportBase`, `GoonLoopbackTransport`,
  `GoonRelayTransport` (now only `App.Logger` away from Core), then `GoonSignalingClient`,
  `GoonWebRtcTransport` (SIPSorcery package reference has to move with it).
- **goongame-core-e**: `GoonMatchService`, `GoonGameService`, `GoonHostService`.
- **goongame-core-f**: media/cache — `GoonAvatarCache`, `GoonCacheBridge`, `GoonOnlineMedia`,
  `GoonOpenTablesApi`, `GoonShareCard`, `TransferInboxStore`, `GoonVectorDumper`
  (parity/trunk 08e9b76a9 has Core ports of several of these to merge from).
- Then the Avalonia Goon Game UI on top (`GoonTestWindow` already exists in the head).

## Verification limits

Run on Linux (Ubuntu 24.04, .NET SDK 10.0.112 from the Ubuntu archive; the dotnet-install host
is blocked by this environment's egress policy):

- `dotnet build` succeeded: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.Avalonia.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests and the Avalonia Consent / Language /
  CatalogueStartup / Scheduler / Report / GeneralSettings / Snapshot test projects.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false` succeeded (compiles the WPF head too).
- `bash .github/scripts/core-guards.sh`: all Core guards passed (no baseline entry needed; no
  embedded resources moved).
- `dotnet test Tests/CCP.Core.Tests`: 1383 passed, 5 skipped, 2 failed. The new
  `GoonClockAndRoundsTests` pass 12/12. The 2 failures are pre-existing and environmental; both
  fail identically on the untouched base `avalonia-port/game-pbp`:
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` (the container runs as root,
  so the "undeletable" folder is deletable) and `LoopbackOAuthTests.GoodState_OnEitherLoopback_...
  (host: "[::1]")` (no IPv6 loopback in the container).
- `dotnet test Tests/CCP.Avalonia.Tests --filter PlayWallTests|FriendsDrawerTests` (the classes
  touching the Goon Game): 19/19 passed.
- **Not run:** the WPF head and `Tests/ConditioningControlPanel.Tests` (WPF does not run on Linux;
  compiled only). The WPF edit is the deletion of its `GoonDispatch` copy plus two `using` lines;
  `GoonTransportBase`, `GoonTestPanel` and `GoonTestSimRoundDriver` now resolve the moved types
  from CCP.Core. No WPF source-scan test referenced the moved paths, so no SourceRoots change.
- No Avalonia UI changed in this unit.

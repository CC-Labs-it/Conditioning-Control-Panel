# goongame-core-e: Goon match engine and host rules move to CCP.Core

Base: `avalonia-port/companion-core-b`. Branch: `avalonia-port/cloud-goongame-core-e`.

## Already in Core before this unit
GoonContracts, GoonDraft, GoonMatchTypes, GoonRng, GoonScoring (incl. GoonPayloadRateLimiter),
GoonWire, GoonOpenTablesApi. None of them had a second copy in WPF, and none of the files below
had one in Core.

## What changed
- **Match engine moved (git mv, shows as renames)** from `ConditioningControlPanel/Services/GoonGame`
  to `CCP.Core/Services/GoonGame`: `GoonMatchService` (match state machine: lobby, consent, draft,
  live scoring, mercy, sudden death, result handshake), `MatchClock`, `GoonSuddenDeath`,
  `GoonTransportBase` (+ `GoonDispatch`), `GoonLoopbackTransport`, and `Rounds/*` (round model,
  quick draw, staring contest, bubble race, reaction duel).
- **Seams instead of App.\*/WPF:**
  - `GoonDispatch.Post` now hops through `CoreDispatch.Post`; the "dispatcher is shutting down,
    drop the callback" rule is a new `GoonDispatch.ShutdownStarted` `Func<bool>`, seeded in WPF's
    static `App()` next to the CoreDispatch providers. Unseeded (tests, no head) runs in place, as
    the WPF code did with no `Application`.
  - `GoonUi.Invoke` (round presenters) uses `CoreDispatch.Invoke` with the same 5 s bound as
    `DispatcherHelper.RunOnUISync`, still logging and swallowing presenter faults.
  - `GoonMatchService`'s two `DispatcherTimer`s (1 s live, 200 ms phase) became `TimeProvider`
    timers whose ticks post through `GoonDispatch` (the `BubbleCountScheduler` idiom); a tick queued
    before a stop recognises itself as stale. `_dispatcher.HasShutdownStarted` -> `GoonDispatch.ShuttingDown`.
  - `App.Settings.Current.UserDisplayName` -> `CoreSettings.Service?.Current`, `UpdateService.AppVersion`
    -> `CoreReleaseContent.AppVersion` (WPF already seeds it from UpdateService), `App.Logger` -> Serilog.
- **Host rules extracted** to `CCP.Core/Services/GoonGame/GoonHostRules.cs` (one copy): paint-stall
  verdict and its thresholds, peer-pool online switch, host / send / join gates (via `CoreAccount`,
  which WPF seeds from `App.Patreon` / `App.UnifiedUserId`), snowflake check, page-string cap.
  WPF `GoonHostService` (the WebView2 host) and its two outside callers (`FriendsDrawer.Tables`,
  `MainWindow.Lobby`) now call `GoonHostRules`. The gate shape (CoreAccount instead of App.Patreon)
  is adapted from parity/trunk `08e9b76a9`; trunk's Core host facade itself is not imported.
- **GoonGameService stays in WPF**: it is the transport-owning facade (WebRTC/SIPSorcery, relay
  fallback, `App.Friends` presence) and has no pure rule of its own left to move.
- **Tests:** `GoonPaintStallRuleTests` moved to `Tests/CCP.Core.Tests` (git mv). The peer-pool
  theory moved from WPF `GoonHostRulesTests` into new `GoonHostGateTests` (plus gate, snowflake and
  cap cases); the WPF file keeps the `GoonOnlineMediaRules.CleanSubs` case, whose rule is still
  WPF-side. New `GoonMatchEngineTests`: two engines over the loopback pair walk lobby -> consent ->
  draft with no head attached, and a head reporting shutdown gets no Goon callbacks.

Added lines vs base: 445 (cap 1000).

## Next units
- goongame-core-f: `GoonOnlineMedia` + `GoonOnlineMediaRules`, `GoonShareCard`, `GoonAvatarCache`,
  `TransferInboxStore`, `GoonCacheBridge` to Core (trunk `5628b4ecc`, `bc101bb84` hold adapted copies),
  moving the CleanSubs / OnlineMedia / ShareCard tests with them.
- goongame-core-g: `GoonSignalingClient` + `GoonRelayTransport` (App.Settings / App.UnifiedUserId ->
  CoreSettings / CoreAccount), leaving only WebRTC + the facade in WPF.
- goongame-core-h: the page-protocol half of `GoonHostService` (init block, net-post allowlist,
  discord prefs, room code) as a Core facade, per trunk `08e9b76a9` / `a7e958635` / `24a6e0a00`.
- Avalonia (head unit): `GoonTestWindow` can now wire `GoonLoopbackTransport.CreatePair` +
  `GoonMatchService` from Core; its stub comments saying they are WPF-only are stale.

## Verification limits
- `dotnet build` OK: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, Tests/CCP.Avalonia.Tests,
  CCP.Core.Settings/Content.Tests, CCP.Avalonia.{Consent,Language,CatalogueStartup,Scheduler,Report,
  GeneralSettings,Snapshot}.Tests.
- WPF compiled, not run: `Tests/ConditioningControlPanel.Tests` (and with it the WPF app) builds on
  Linux with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`;
  the WPF tests and the real dispatcher / WebView2 paths cannot run here.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 2198 passed, 6 skipped, 2 failed. The two failures
  (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` - root can delete the folder;
  `LoopbackOAuthTests...(host: "[::1]")` - no IPv6 loopback in the sandbox) fail identically on the
  base branch. Goon filter: 39/39 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter Goon|Lobby|Friends`: 24/24 passed.
- Not exercised: a real two-machine match (WebRTC/relay) and the WPF timers under a live dispatcher.

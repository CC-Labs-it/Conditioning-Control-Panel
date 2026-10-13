# goongame-core-d: Goon Game transports move to CCP.Core

Before this unit CCP.Core held only GoonContracts, GoonDraft, GoonMatchTypes, GoonRng, GoonScoring and GoonWire. No transport rule had a Core copy, so nothing is duplicated.

- `git mv` from `ConditioningControlPanel/Services/GoonGame/` to `CCP.Core/Services/GoonGame/`:
  `MatchClock.cs` (the transport base needs it; it was already pure), `GoonTransportBase.cs` (with `GoonDispatch`), `GoonLoopbackTransport.cs`, `GoonSignalingClient.cs` (the relay needs it), `GoonRelayTransport.cs`, `GoonWebRtcTransport.cs`.
- `App.*` swapped for Core seams that already existed. Nothing new was added except the Contract D hook below:
  - `App.Logger` becomes Serilog's static `Log`. WPF sets `Log.Logger = App.Logger`, so the sink is the same.
  - `GoonDispatch` (WPF `Application.Current.Dispatcher`) now calls `CoreDispatch.Post`. It still wraps the callback in try/catch and logs. ponytail: the WPF original dropped callbacks once the dispatcher began shutting down. The WPF `CoreDispatch` seed runs them inline instead, which does no harm on an exiting app.
  - `App.Settings.Current.UserDisplayName` / `.AuthToken` become `CoreSettings.Current.*`. WPF seeds `CoreSettings.ServiceProvider`.
  - `App.UnifiedUserId` becomes `CoreAccount.UnifiedUserId`. The WPF property already proxies to it.
  - `UpdateService.AppVersion` becomes `CoreReleaseContent.AppVersion`. WPF seeds it from `UpdateService.AppVersion`.
  - `MergedAccountRecovery.TryHandle` (Contract D, App-bound) becomes a new `GoonSignalingClient.MergedAccountHook`. WPF `App` sets it to `MergedAccountRecovery.TryHandle`, next to the existing `V2AuthService.MergedRecovery` seed. While it is unseeded, no merged-account recovery runs.
- The `SIPSorcery` 10.0.14 PackageReference (comment included) moves from the WPF csproj to `CCP.Core.csproj`, so both heads now get it transitively.
- `CCP.Avalonia/Views/Windows/GoonTestWindow.axaml.cs`: the header comment was stale and still said the loopback transport lives in WPF; it is corrected. This is a comment-only change with no UI change.
- New `Tests/CCP.Core.Tests/GoonTransportCoreTests.cs` (4 tests):
  - A loopback pair syncs its clocks, and `MessageReceived` arrives through `CoreDispatch`.
  - A `GoonDispatch` callback that throws is contained, and later callbacks still run.
  - The signaling `/v2/goon/invite` body carries `CoreAccount.UnifiedUserId` and `CoreReleaseContent.AppVersion`.
  - The relay invite sends `CoreSettings.Current.UserDisplayName`. On a 403 `no_host_access` it reports the error and ends in `Disconnected`.
  - No pure tests for these files existed anywhere, so none were moved.
- Trunk credit: nothing was imported verbatim, because `parity/trunk` never moved the transports. Its `MatchClock` port (08e9b76a9) also uses `CoreDispatch.Post`, and this unit follows the same seam. Here `MatchClock` is a pure rename that keeps `GoonDispatch`, which now sits on `CoreDispatch`.

## Next units
- goongame-core-e: `GoonSuddenDeath`, `Rounds/*` (GoonRoundModel, BubbleRace, QuickDraw, ReactionDuel, StaringContest) and `GoonMatchService`, if each is WPF-free.
- goongame-core-f: `GoonAvatarCache`, `GoonCacheBridge`, `GoonOnlineMedia`, `GoonOpenTablesApi`, `GoonShareCard`, `TransferInboxStore` and `GoonHostService`. Trunk already has Core versions of these, so merge them into ours. Also `GoonVectorDumper`.
- `GoonGameService` (it touches WPF the most). After that, the Avalonia `GoonTestWindow` loopback wiring (the stubbed `CreatePair` / `SimulateOutage` ponytails).

## Verification limits
- Built on Linux (.NET SDK 10.0.112) with 0 errors:
  - CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR
  - Tests/CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests and every Tests/CCP.Avalonia.*.Tests project
  - Tests/ConditioningControlPanel.Tests, which also compiles the WPF head, with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests`: 1430 passed, 5 skipped, 2 failed. Both failures fail identically on the untouched base `origin/avalonia-port/emi-line-engine` in this container, so they come from the environment:
  - `LoopbackOAuthTests.GoodState_OnEitherLoopback(host: "[::1]")`: there is no IPv6 loopback here.
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: the tests run as root, which ignores the read-only mode.
- `GoonTransportCoreTests`: 4/4 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter Goon`: 1/1 passed.
- Compiled but not run:
  - The WPF head and its tests (Windows only), including the new `MergedAccountHook` seed in `App.xaml.cs`.
  - `GoonWebRtcTransport` against a real peer, and real `/v2/goon/*` HTTP. Tests use the `GoonHttpPost` override, and the sandbox blocks the network.

# Piece by Piece media and friends

- Base: `73eb085671ce6576c4b3d6b3f6197bb720acb55f`; the supplied snapshot differed, so the authorized independent-clone fallback was used. No local requester changes were available.
- Adapted the online game pool and head media bridge from parity/trunk commit `2d3523b2b0078ce8ebb3ccca88dbb2cef7228724`. Existing head files were merged, never replaced. Unused peer/Sort pools and a second downloader were omitted; the existing feed coordinator and guarded, bounded assets copier are reused. Core media namespaces avoid colliding with the frozen WPF types.
- Added the saved one-time picture choice, including own-only, cleaned flavours/niches/custom data, game-specific opt-in without app-wide consent, app-wide versus picked shares, still/clip waves, refills, bounded ownership and cancellation/release on close or panic.
- Added the off-thread active-library probe and mediaAvailable refresh after an empty library or dry online feed. Boot frames retain WPF order: settings, identity, media state, online start, then the queued friend intent. Whisper clips remain the base host implementation.
- Connected the existing chess drawer tile to challenge creation and existing invite sending, and valid chess knocks to challenge acceptance. Same-friend task reuse, replacement, timeout, malformed replies and close completion follow WPF. Page watch toggles are whitelisted, saved and sent at the next boot. No new window type: the existing games panic registration owns cleanup.

## WPF audit and remaining dependency

At the pinned base the row was `missing` (the task described `stub`). The head lacked every Media.cs path: saved-choice adoption/state, flavour persistence, online start/off/refill/snapshots/disposal, and availability probing/refresh. It also lacked Friends.cs challenge/reply/accept, saved page watch settings, and Lobby host/join/watch plus fresh-board spectate initialization. Whisper selection/fallback was already present in the host and was retained.

The first two groups now have user paths and focused Core/headless tests. The row remains **stub** because Lobby host/join/watch and the fresh settings frame's spectateMatchId still depend on the unported **views-tab-available-subjects** Lobby surface. Unused host entry points were not added. This is not a pure dependency-only NOOP: media and friend paths were absent on the base and are implemented here.

Pinned live WPF callers (paths beneath `ConditioningControlPanel/` at `73eb085671ce6576c4b3d6b3f6197bb720acb55f`):

- `Services/PieceByPiece/PieceByPieceHostService.cs`: Launch calls StartLocalMediaProbe; ready adopts the saved choice and posts media state/starts the pool; message dispatch calls flavour, refill, friend reply and page-setting handlers; close disposes media and drops friend state.
- `Controls/Friends/FriendsDrawer.Pickers.cs`: InviteToChessAsync calls ChallengeFriendAsync with 45 seconds, then sends the returned challenge as an invite.
- `Services/Friends/FriendsLanding.cs`: the Chess destination validates the challenge id and calls JoinFriendChallenge.
- `MainWindow/MainWindow.Lobby.cs`: live Lobby calls to the chess host/join/watch methods (see the grep command below). These are not dead WPF code; P122 does not apply.

Reproduce the dependency audit: `git grep -n -E 'HostOpenTable|JoinOpenTable|HostSpectate|ChallengeFriendAsync|JoinFriendChallenge' 73eb085671ce6576c4b3d6b3f6197bb720acb55f -- ConditioningControlPanel`.

## Verification limits

All final commands used .NET SDK 10.0.402, Release configuration, and `DiffEngine_Disabled=true` for tests.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build Tests/<project>/<project>.csproj -c Release`: exit 0, 0 errors for every Linux workflow test project: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, and CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors. The earlier duplicate-type attribute error was resolved by isolating Core media namespaces, without editing frozen WPF sources.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed (429 XAML type references checked).
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~PbpMediaTests`: exit 0, 4 passed, 0 failed, 0 skipped.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0, 3,549 passed, 0 failed, 5 existing Vosk tests skipped (model download unavailable).
- `git diff --check`: exit 0. Generated output copies initially filled the cloud disk; affected builds/checks were rerun successfully with generated binaries cleaned between projects.

Avalonia tests were compiled, not executed, under the supplied cloud font limitation. The supervisor should run `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~GameWindowTests.PieceByPiece|FullyQualifiedName~FriendsDrawerTests"`. Render-all, Windows execution, live WebView behaviour and provider downloads were not run here. No tests were weakened, disabled or deleted; the existing exact ready-frame assertion was updated to the new three-frame protocol.

## Assumptions

- Preserve WPF behaviour and wire names; keep Lobby work in its own parity unit and retain an honest stub.
- Reuse the existing guarded 24 MB temporary-media copier rather than introduce a second download implementation.
- Do not alter frozen WPF sources; isolate imported Core names instead.

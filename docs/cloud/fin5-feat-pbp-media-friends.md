# fin5: shared chess media engine; Lobby paths remain stub

- Base verified at `d56130f5de2287b6c83759af1063034f5757cb8f`; independent cloud snapshot, clean before changes.
- Route WPF chess through Core `PbpMediaRules` and `GoonOnlineMedia`, removing the duplicate WPF chess rules. Keep WPF's `RemoteMediaCache.MaterializeAsync(ownerReleases: true)` and release operation through per-pool delegates. Other WPF Goon pools retain their implementation; no unused peer/noise machinery imported into Core.
- Test independent file owners, existing wave/refill/late-download cleanup, and the WPF production factory's shared-engine reachability. Existing games panic surface remains the sole owner; no new window or surface.
- Keep `feat-pbp-media-friends` **stub** and add numbered manual tester coverage.
- Read-only import source fetched: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported from this revision. Existing Core pool credits its original adaptation from `2d3523b2b0078ce8ebb3ccca88dbb2cef7228724`; this unit merges into that implementation.

## WPF gap inventory at the pinned base

Read the final ledger row and WPF `PieceByPieceHostService.Media.cs`, `.Friends.cs`, original `PbpMediaRules.cs`, and the shared Core `PbpWhisperClips.cs` (already moved out of WPF). Consent, saved choices including own-only, cleaned niches/custom JSON, source shares, still/clip waves and refill, bounded file ownership, active-library probe, dry-online availability refresh, whisper selection, settings/identity/media/intent order, drawer challenge reuse/replacement/timeout/close, invite acceptance, and saved watch toggles already have Avalonia implementations. This unit closes the separate P28 chess media sharing gap.

The remaining user-visible gaps are:

1. Lobby Host: WPF `MainWindow.Lobby.cs:277` calls `HostOpenTable`, which sends `pbp:friend` mode `host` after identity, using the page's default clock.
2. Lobby Join: `MainWindow.Lobby.cs:243` gates sign-in/join access, then sends mode `join` with a validated `p_` table id (ASCII letters/digits/underscore/hyphen, length 3..64); invalid ids do nothing. The page handles refusal.
3. Lobby Watch: the same handler gates sign-in/chess access on Playing watchable rows. Existing boards get a `spectate` friend frame; fresh boards consume `spectateMatchId` once in settings, never both. Closing or failed launch clears pending intent.
4. Launcher dropdown Join: `LauncherWindow.Lobby.cs:245` folds the dropdown, checks row/gates, requests sign-in if needed, avoids queuing a join under a leash punishment, then uses the launcher launch/hide/return flow.
5. Live billboard Join: `Providers/LiveProvider.cs:128` parses the target, requests sign-in when needed, then calls chess Join.
6. Featured billboard Watch: `Providers/FeaturedProvider.cs:69` validates the match target and checks the same Lobby chess gates before spectating.

These are live WPF callers at the pinned base, so P122 does not apply. No unreachable Avalonia host methods were added.

## Scope and assumptions

The missing feature is `views-tab-available-subjects` Lobby plus its launcher/Live/Featured integrations. Core already contains `LobbyModel`, `PbpLobbyApi`, and `GoonOpenTablesApi`; those are not the missing host. Avalonia still has preview roster rows and inert actions in `AvailableSubjectsTabView`, no Lobby polling service, no launcher Lobby partial, and no Live/Featured provider wiring. WPF's Lobby watches chess/Goon/Remote together, polls at 10 seconds with failure backoff to 60 seconds, reads friends and gates, and owns watch leases. WPF `LobbyService` + shell Lobby glue + launcher Lobby + Live/Featured providers alone are 1007 lines before the tab/row rendering, Featured feed, Remote directory claim host, port adaptations or focused tests. Porting that complete user path within this unit's 1000-added-line cap would require dropping WPF behavior. Keep those dependencies explicit instead of presenting a chess-only substitute as parity.

Assume the smallest correct independent change is sharing the chess media engine while leaving this row stub. This is a partial implementation, not the pure-dependency NOOP: there was an independent P28 gap to close. Windows execution remains a verification limit, not a reason to keep a duplicate engine. No new icons, surfaces, persistent configuration, or desktop-opening tests were introduced.

## Verification limits

Windows code can only be cross-compiled here; the WPF reachability test must run on Windows. No real desktop or user profile is used by the focused Core/headless tests. No live server or WebView interaction was exercised.

Completed commands (Release configuration; `DiffEngine_Disabled=true` for tests):

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-restore --no-dependencies`: exit 0, 0 errors. The initial simultaneous app/test-project builds hit an Avalonia output-file collision; the standalone rerun passed.
- `dotnet build` for every Linux test project named in `.github/workflows/build.yml`: all exit 0, 0 errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests; CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors; WPF application and tests compiled, not executed on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent on this base. Numbered WPF/Rows tester steps were added manually.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3550 passed, 5 skipped, 0 failed. The existing Vosk tests skip without their model; no tests were weakened, deleted, or newly skipped.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore --filter 'FullyQualifiedName~PbpMediaTests'`: exit 0, 5 passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~GameWindowTests.PieceByPiece'`: exit 0, 9 passed, 0 failed. This runner did support font creation for these tests.
- `git diff --cached --check`: exit 0.

Other compiled test suites were not executed. No Windows runtime, live online feed, visual render-all, or real desktop verification. The existing media-wave tests cover refill, bounded deck size, late downloads on close, dry/error backoff and consent; the new isolation test covers the per-head file-owner seam.

Regression proof: temporarily replacing the per-pool release callback with a no-op made `HeadSpecificFileOwnersStayWithTheirPool` fail (exit 1, `Assert.NotEmpty` at the release assertion). Restoring the production implementation and rerunning `PbpMediaTests` passed all 5 tests (exit 0). The mutation was not committed.

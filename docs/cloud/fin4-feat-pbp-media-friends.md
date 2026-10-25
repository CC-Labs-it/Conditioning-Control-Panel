# fin4-feat-pbp-media-friends

- Dependency-only analysis; no production or test code changed. Keep `feat-pbp-media-friends` at `stub`; no parity promotion or new surface/panic registration.
- Base and WPF evidence pinned at `d3e9301525c780ab64b43c904a7ec6f1e56f2a81`.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing Media partial credits `2d3523b2b0078ce8ebb3ccca88dbb2cef7228724`; this unit does not re-import it.

## Remaining WPF behaviours for this row

1. Lobby **Host a chess table** calls `HostOpenTable`, opens/reuses the board and sends `pbp:friend` mode `host` at the default clock.
2. Lobby **Join** calls `JoinOpenTable`, validates a nonempty `p_` table id of at most 64 ASCII letters/digits/underscore/hyphen, then sends mode `join` with `target`. A fresh board waits for identity; an existing ready board takes the intent immediately. Page/server owns refusals.
3. Lobby **Watch** calls `HostSpectate`, validates with `PbpWatchRules.IsMatchId`, and uses exactly one route: existing board gets mode `spectate` with `matchId`; fresh board gets `spectateMatchId` once on its settings frame, before identity. Failed launch/close clears the target. Avalonia currently always passes null to `SettingsFrame`.
4. These actions need a real Lobby discovery/list surface and its account gates, not public host methods without callers. WPF signs out to the login dialog, offers Watch only on watchable playing chess rows, and lists open/playing/friend rows from the merged service. The launcher and Live billboard also join tables; Featured opens spectating. Their missing Lobby/board integration is not solved by adding unused intents.

Saved own-only/online choices, consent, niche/custom cleaning, shares, still/clip waves and refill/cleanup, active-library availability and dry-online refresh, settings/identity/media/intent order, friend challenge reuse/replacement/timeout/close, invite acceptance and watch-toggle persistence already have user paths and focused tests on this base (`PbpMediaTests`, `GameWindowTests.PieceByPiece_*`). Whisper selection is shared `CCP.Core/Services/PieceByPiece/PbpWhisperClips.cs`, called by both heads: own clips first, permitted sub-audio fallback, then words, bounded and escaped. Avalonia maps those URLs through its asset server. No further whisper implementation is needed for this row.

The ledger also records P28 architectural debt: WPF still uses its own `PbpMediaRules` and `GoonOnlineMedia`; Core copies serve Avalonia. This is not an additional missing user behaviour. Consolidating those copies and proving Windows callers is separate from the Lobby dependency; no architecture-completion claim is made here.

## Why the dependency does not fit this unit

`views-tab-available-subjects` is the unported feature. Core already has `LobbyModel`, `PbpLobbyApi`, `BackRoomApi` and `GoonOpenTablesApi`/`GoonOpenTables`; those are not the missing host. There is no `LobbyService` in Core/Avalonia. Avalonia's `AvailableSubjectsTabView` still loads a sample subject roster and has empty Connect/Become handlers. `MainShellWindow.DashboardBillboard.cs` explicitly leaves Live waiting for that service.

WPF's service polls only with watcher leases, joins in-flight refreshes, gates network work on sign-in, merges chess/Goon/Remote/friends and backs off from 10 to 60 seconds. Its UI owns three columns, counts, empty/error states, account/tier gates, visibility leases, refresh, responsive column/tab switching and row entrance handling. Remote directory ownership remains in WPF `AvailableSubjectsService` (395 lines); a separate Avalonia Remote directory flow is not a port of that shared Lobby producer.

The WPF dependency reference spans 1,058 lines before adapting remaining dependencies, wiring shell/launcher/billboard consumers, or adding focused tests: `MainWindow.Lobby.cs` 303; `LobbyRowView.cs` 125; `AvailableSubjectsTabView.xaml` 344; its code-behind 93; `LobbyService.cs` 193. This is a scope estimate, not a claim that raw WPF code can be imported unchanged. Preserving the full merged Lobby behaviour plus tests under the 1,000-added-line cap is not credible; a chess-only replacement would narrow the reference feature. Follow the unit's explicit pure-dependency NOOP rule instead.

P122 does **not** apply: these are live WPF behaviours. Reproduce at the pinned base:

```sh
git grep -n -E 'HostOpenTable|JoinOpenTable|HostSpectate' d3e9301525c780ab64b43c904a7ec6f1e56f2a81 -- ConditioningControlPanel
```

Callers include `MainWindow/MainWindow.Lobby.cs:250,263,287`, `Windows/Launcher/LauncherWindow.Lobby.cs:260`, `Services/Billboard/Providers/LiveProvider.cs:133`, and `FeaturedProvider.cs:76`. Fresh settings consume `TakeSpectateInit` in `PieceByPieceHostService.cs:371`. No render-only promotion can substitute for these callers.

## Verification limits

Commands used .NET SDK 10.0.402, Release configuration and `DiffEngine_Disabled=true` for tests.

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors (48 existing warnings on first build) |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors (187 existing warnings on first build) |
| `dotnet build Tests/<project>/<project>.csproj -c Release` for all 12 Linux workflow test projects | 0 each; 0 errors. Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Migration.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests (Avalonia prefixes as in the workflow). |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 0 errors, 554 existing warnings; Windows application and tests compiled but were not run |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed; 429 XAML references checked |
| `dotnet test Tests/CCP.Core.Tests -c Release` | 0; 3,549 passed, 5 skipped, 0 failed; 39 seconds |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~GameWindowTests.PieceByPiece` | 0; 9 passed, 0 skipped, 0 failed; 13 seconds |

Five existing Vosk integration tests self-skipped because the model is unavailable; no test was changed or weakened. Headless Piece by Piece tests use the suite's sandbox profile and bundled font registration and ran successfully. Initial batch builds exhausted disk copying native dependencies; removed generated test outputs and reran sequentially, with all Linux builds passing. An initial non-escalated Core guard run could not create temporary files in the read-only sandbox; the writable rerun passed. No Windows runtime, native media playback or real desktop was exercised. No new behaviour was introduced, so no new tests or tester steps were added. The ledger is unchanged. The test-plan coverage script is absent at this pinned base.

## Assumptions

- Preserve the complete three-game WPF Lobby rather than substitute a chess-only list to fit the cap.
- P28 consolidation debt is architectural follow-up, not a new media/friends user-path gap.

NOOP: views-tab-available-subjects Lobby host/service and user paths exceed this unit's line cap.

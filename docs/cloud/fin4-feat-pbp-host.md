# fin4 — Piece by Piece host

- Add the board to the existing Core panic pause policy: first Escape is `pbp:escape` with web focus restored; booting pages receive no queued Escape; a second press within two seconds follows the normal panic stop. Existing `games` panic registration covers the board; no new surface.
- Route all four stakes messages through the existing Core `StakeBridge`. Adapt WPF `StakeAppHooks` to the existing Chaster service and wallet: time consent enables the loss row before offering, wallet writes recheck account identity on the UI thread, settlement persists beyond board close, and late frames never target a closed board.
- Resolve the settings motion cap for gentle visuals and Windows animation effects, including WPF's gentle-only intro speed. Query Windows through a small platform seam; the pure cap lives in Core.
- Record fresh board launches in the existing feature day log. WPF's `TrackFeature(pbp)` only records `e_pb`: `SeasonFeatureKeys.Find(pbp)` is null, so no season bucket or recap service needs importing for this hook.
- Replace the duplicated, stale parity row with one honest `stub` row and add numbered WPF tester steps.
- Source: WPF reference at base `d3e9301525c780ab64b43c904a7ec6f1e56f2a81`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files imported. Existing base files win.

## Reference comparison

Read `PieceByPieceHostService.cs`, `.Friends.cs`, `StakeAppHooks.cs`, `MotionFx.cs`, `SeasonRecapService.TrackFeature`, and the main-window Escape routing at the pinned base above. Existing base functionality already covers launcher/leash, ready ordering, identity, net whitelist, own library media, whisper routes, online pictures, friend challenge/accept, settings persistence, watchdog timing, presence, and panic cleanup. The ledger's old whisper/media/friends omissions were stale.

Initial live WPF gaps and disposition:

| Gap | Disposition |
| --- | --- |
| Kept Escape and keyboard focus | Implemented through existing panic policy; regression test fails against the pre-fix host. |
| Reduced motion, OS cap, gentle intro speed | Gentle + Windows cap implemented; non-Windows OS preference remains missing. WPF defaults to animations enabled on failed queries. |
| PvP stake frames and head booking hooks | Implemented using existing Core bridge/settlement and head services. |
| SeasonRecap TrackFeature | Implemented as its exact reachable pbp behavior, the day-log event only. |
| Lobby host/open-table join/spectate launch intents | Missing: `views-tab-available-subjects` is an unported user-facing Lobby. Core lobby wire alone is not a user path. Porting its full browser/list/actions is a separate feature. |
| MergedAccountRecovery Contract D | Missing: head-wide account recovery lifecycle (stop sync, provider reauthentication, quest owner migration, profile reload and finish prompt) is not seeded. FriendsHead documents the same dependency; do not wire a partial destructive account swap. |
| Browser process-failed close | Missing: the current NativeWebView public host seam does not expose the WPF process-failure callback. Existing heartbeat closure remains; it is not immediate process-failure parity. |
| Shared WPF net/media/watchdog delegation | Retained Windows follow-up P28; WPF's inline reference is frozen in this unit. It is not an Avalonia user-path gap. |

No P122 dead-code waiver: remaining WPF paths have live callers. The row remains `stub`, never `verified`.

## Verification limits

All commands used .NET SDK 10.0.100, Release configuration, and `DiffEngine_Disabled=true` for tests.

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors |
| `dotnet build <test project> -c Release` for every Linux test csproj in build.yml | All 0; 0 errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 0 errors; compiled only |
| `dotnet test Tests/CCP.Core.Tests -c Release` | 0; Failed 0, Passed 3553, Skipped 5, Total 3558 |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~GameWindowTests.PieceByPiece\|FullyQualifiedName~GameWindowTests.Dtrh_Escape'` | 0; Failed 0, Passed 15, Skipped 0 |
| Escape regression against original host | 1; expected Assert.True failure on the first boot-time pause claim; proves the test catches the original gap |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed |
| `bash .github/scripts/test-plan-coverage.sh` | 127; script absent from this base; numbered WPF steps added regardless |
| `git diff --check` | 0 |

An initial concurrent build of the head tests hit an Avalonia PDB file lock; the serial final test run rebuilt successfully and passed. The SDK channel installer encountered a 403 at its discovery endpoint; installing pinned SDK 10.0.100 succeeded.

Windows native animation behavior and WPF tests were compiled but not run on Linux. Full Avalonia suite and live embedded-browser gameplay were not run. Five existing Vosk tests self-skip without the model; no tests were weakened or disabled. No real desktop, profile, notification, public game server or Chaster endpoint is used by the focused tests.

## Assumptions

- Use the existing board transport and sandbox-aware proxy selection for stakes instead of introducing a second network configuration.
- Keep one stakes bridge and settlement book for the app session, matching WPF's after-close settlement behavior.
- Non-Windows OS motion preference defaults to enabled until a platform-supported preference seam is available; user Reduced/Off and gentle visuals still apply.
- Leave the row `stub` because the remaining live dependencies are broader than this host unit; do not count host methods with no real Lobby caller as wired.

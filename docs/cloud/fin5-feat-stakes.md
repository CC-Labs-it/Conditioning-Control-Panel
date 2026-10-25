# fin5: Piece by Piece stakes host

- Route Piece by Piece's four stake frames into Core's session bridge and post replies on the UI dispatcher to the current board. Keep settlement watches alive after close/panic, matching WPF's static bridge, with shared match/account deduplication.
- Seed Avalonia's live Chaster eligibility gate, synchronous copy-on-write `pvp_loss` consent, lost-time booking and wallet adoption. Reuse `V2PurchaseHead.AdoptSp` for the receipt/snapshot and account-after-dispatch checks.
- Add focused headless tests and numbered manual tester steps. Keep `feat-stakes` **stub** with one honest remaining dependency instead of promoting it to wired.
- Sources: WPF at pinned base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## WPF gap inventory and disposition

At the pinned base, `rg 'StakeBridge|stake-' CCP.Avalonia` found only a comment naming the missing route. Core already implements table validation, authenticated `/v2/stakes` requests, labels, refusal handling, match validation, account checks, settlement caps/deduplication and the 1/2/3/5/8/13/20/30/40-second watch ladder. This unit reuses that logic unchanged.

1. Missing Piece by Piece frame routing and replies: implemented in `GameWindow.Pbp.cs`, matching WPF `PieceByPieceHostService.cs:396-400,663-670`. The app-session bridge sends replies to the currently open board and keeps watches alive after it closes.
2. Missing head eligibility: implemented from `StakeAppHooks.AppTimeOk` (linked, tab on, not paused, no safety hold). The existing page keeps solo/bot games unstaked.
3. Missing time-pick consent: implemented from `StakeAppHooks.AppEnableLossRow`; enable `pvp_loss` before the offer, copy the list and save, synchronously on the UI thread.
4. Missing settlement effects: implemented from `StakeAppHooks.CreateSettlement`; `ChasterService.NoteSeconds` applies tab switches/limits/safety hold, and wallet writes recheck the account after dispatch. The existing Chaster head still has its documented `RemoteOpen: false` limitation; Remote Control's host/service is unported, so Remote cap integration remains a shared dependency.
5. Missing Goon user path: remains. `GameWindow.Games` has no Goon entry and `PlayTabView.axaml` disables `BtnPlayGoon`. WPF `GoonHostService.cs:723-727` is a live caller, so P122 does not apply. That host is 2,151 lines before its cache, transfer, toy and online-media collaborators. A faithful playable port requires init/identity/network/consent, authenticated `net-post`, room lifecycle/watchdogs, manifests/cache/inbox and match ledger claims, not simply a new game dictionary entry. This does not fit the remaining 1,000-added-line unit budget. In particular Goon's countdown lock, four-second lobby polling and recap settlement/claims remain unreachable until that host is ported.

The existing page scripts stay unchanged: Piece by Piece preserves Off/time/SP order, three-second lobby polling, first-move lock, one-second busy retry, withdrawal, per-match pick spending and result copy; Goon retains its equivalent row, countdown lock and 150-second local give-up. This unit adds no surface: the existing `games` panic registration already closes Piece by Piece.

## Assumptions

- Prefer the existing live Piece by Piece path and keep the row stub while Goon and Remote Control dependencies remain, as permitted by the user-visible-first line-cap rule. This is partial implementation, not a pure dependency NOOP.
- Use the existing shared wallet adoption implementation because its receipt semantics match WPF; do not introduce a second wallet writer.

## Verification limits

- `dotnet build -c Release`: Core and Avalonia, plus every Linux workflow test project (CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia, Core.Content, Core.Settings and Core), all finished with zero errors. GeneralSettings required a successful `-m:1` retry after an MSBuild worker exited. An initial overlapping app build hit a generated-PDB lock; the serialized retry passed.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. WPF and Windows platform code compiled but were not run on Linux.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,953 passed, 5 skipped, total 3,958. Vosk-dependent tests cannot run without the model in this environment; no tests were changed or skipped by this unit.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release -m:1 --filter FullyQualifiedName~StakeHostTests`: exit 0; all 4 passed in a fresh process using the shipped font setup. The fixture uses memory-only secrets and an isolated profile/tab ledger.
- A combined `StakeHostTests|GameWindowTests` run initially failed (16 failed, 2 passed): the older class initializes Avalonia without the shipped font setup, causing the known Segoe UI Black glyphTypeface exception. That attempt also exposed an unseeded fake token store in the new wallet fixture, which was fixed before the isolated 4/4 pass. The existing game-window suite is compiled but cannot be validated with its current font setup here; no tests were weakened.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The optional `.github/scripts/test-plan-coverage.sh` is absent on this base (attempt exited 127); numbered tester steps were added manually.
- `node Assets/web/piecebypiece/smoke/stake-smoke.mjs`: exit 0, 72 passed.
- Real online stakes, live accounts and Windows desktop behavior were not run. No new surface was added; the existing `games` panic surface is reused.

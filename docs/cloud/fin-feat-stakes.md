# fin-feat-stakes: dependency analysis

- Keep `feat-stakes` as `stub` with one row and an explicit Goon Game host dependency. No application code, tests, or new surfaces changed; no new panic registration is needed.
- Correct the stale claim that linked-Chaster coverage is absent: `GameWindowStakeTests.cs` already contains `LinkedChaster_TimeOk_TimeOfferSwitchesTheLossRowOn` on the required base.
- Read-only trunk reference fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Its `GameWindow.Goon.cs` has a full host with stakes, but depends on its own Goon host/media/transfer services and head seams. No trunk code imported; transplanting that separate feature would exceed this unit's dependency-only scope.

## Verification limits

SDK: .NET 10.0.402. All test commands set `DiffEngine_Disabled=true`; test hosts use their existing temporary profile sandboxes.

- `dotnet build <project> -c Release`: exit 0, `0 Error(s)` for `CCP.Core`, `CCP.Avalonia`, and every Linux test project named by `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and `CCP.Avalonia.Snapshot.Tests`. Each project was built through its same-named csproj, in its repository directory. Existing warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, `559 Warning(s)`, `0 Error(s)`. WPF app and tests compiled on Linux; Windows runtime behavior was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` An initial invocation in the read-only sandbox could not create temporary files; the authorized writable invocation passed.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: initial exit 1, 2,870 passed / 1 failed / 5 skipped / 2,876 total. Existing `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` failed at its non-null scheduled-handle assertion. No application or test sources differ from the base. The complete class then passed in isolation (`--filter FullyQualifiedName~GoonClockAndRoundsTests`): exit 0, 12 passed. Full-suite repeat: exit 0, `2,871 passed / 0 failed / 5 skipped / 2,876 total` (37 seconds). Retain the initial failure as an observed timing-sensitive test risk.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~StakeTests`: exit 0, `25 passed`, no failures or skips.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~AppStartupSeedsTheStakeHooks`: exit 0, `1 passed`. This is a source-only hook check, with no window construction.
- `node Assets/web/piecebypiece/smoke/stake-smoke.mjs`: exit 0, `stake-smoke: 72 passed`.
- `node Assets/web/goon/test/selftest-stake.js`: exit 0, `selftest-stake: 83/83 checks passed`.
- `git diff --cached --check`, single-row/status checks and added-content privacy scan: passed. Final added-line count is reported with the pushed commit.

Font-dependent Avalonia runtime tests (`GameWindowTests` stakes routing/linked Chaster and `ChasterBillTests`) were compiled but not run, per the supplied cloud font limitation. The base's shared dispatcher/test setup does not contain the cloud test font fix. They still require the supervisor's GitHub check; the source-only seed check above is not a substitute. No render-all run: P122 is inapplicable and no surfaces changed. No live stakes server calls, real desktop, notifications or profile were used. Five speech tests skip under their existing missing-model behavior; the Vosk model download is unavailable in this environment. No tests were weakened, skipped by new code, deleted or changed.

## Reference and existing behavior

Analysis is pinned to base `f8fe021a37a742fabf0db74e614028f82f661337`.
The original ledger cites WPF `Services/Stakes/**`; its rules, API, bridge and settlement now live in `CCP.Core/Services/Stakes/` and are reused by both heads. WPF retains `StakeAppHooks.cs`.

- `StakeRules`: Off, 900/1800-second time picks, 5/10/25 SP picks; time requires linked, enabled, unpaused Chaster without a safety hold; losses cap at 1800 seconds. The server remains authoritative for caps and settlement.
- `StakeApi`/`StakeBridge`: authenticated limits/offer/state requests, account capture before awaiting, validation/refusals, localized labels, consent before sending time offers, and `lock:true` forwarding. Watch delays are 1/2/3/5/8/13/20/30/40 seconds; busy/too-fast may retry, final refusals stop, exhaustion gives up without booking.
- `StakeSettlement`/head hooks: receipts may lower SP, snapshots only raise it; account is rechecked inside the UI write; only a settled loss books through the player's Chaster limits, once per game/match/account. Closing or panic does not cancel the app-lifetime watch; replies to closed boards are dropped.
- Piece by Piece uses the same `Assets/web/piecebypiece/net/stake.js`: online PvP eligibility, server options and host labels, lobby/found-screen picks, first-move locking, 3-second state polling capped at five minutes, one busy retry after one second, per-match pick consumption, and pending/won/lost/void result copy are already reached through `GameWindow.Pbp.cs`. Solo/hotseat never stakes. Existing Core and head tests cover the bridge, wallet, booking, linked gate, consent and panic lifetime.

## Every remaining WPF behavior without an Avalonia user path

All depend on the unported **Goon Game host**:

1. A hosted, signed-in, non-anonymous real PvP room exposes the lobby row; practice/standalone/guest seats do not. Picker order is Off, allowed time options, then SP; server options win. Opponent chip, pending/locked state, consent note and refusal copy accompany the selection. The lobby polls every four seconds until locked; a busy offer retries once after one second.
2. Both seats lock once at Countdown via `stake-state {lock:true}`; the picker stays locked. Shared Core already forwards this flag, but no Goon page can send it on this head.
3. End-of-match claims go to `/v2/goon/ledger` with matching end reason, winner and scores, excluding the local survival clock. Practice/pre-live/anonymous results do not claim. Mercy, abandon, draw, dispute or missing claims void on the server; the client never invents a loss.
4. A staked finish starts the host settlement watch. Recap shows pending, win, actual booked loss seconds, lost SP or returned stake; local give-up is 150 seconds when no final reply arrives. No stake produces no result line.
5. Goon's four stake verbs reach a single app-lifetime `StakeBridge.ForApp("goon")`, and replies return to the live Goon page on the UI thread. Settlement survives closing that page. Shared settlement and head gates exist, but the Goon carrier and its window lifecycle do not.

## Pinned caller and absence proof

These are source checks against the exact base, not claims based on filenames alone:

```sh
git grep -n -E 'StakeBridge.Handles|StakeBridge.ForApp|private static StakeBridge|private static void PostStake' f8fe021a37a742fabf0db74e614028f82f661337 -- ConditioningControlPanel/Services/GoonGame/GoonHostService.cs
# Live handler at 735/737; app-lifetime bridge at 855; UI post at 858.
git grep -n -E 'stakeEligible|mountStakeRow|submitClaim|stake[.]lock|stake[.]finish' f8fe021a37a742fabf0db74e614028f82f661337 -- Assets/web/goon
# Page clients and live lobby/countdown/result callers exist.
git grep -n 'StakeBridge' f8fe021a37a742fabf0db74e614028f82f661337 -- CCP.Avalonia
# Only StakeHead hooks and GameWindow.Pbp.cs routing; no Goon route.
git show f8fe021a37a742fabf0db74e614028f82f661337:CCP.Avalonia/Views/Games/GameWindow.cs
# Games registers breakoutdemo, fyp, justdrop, piecebypiece and race; no Goon.
git grep -n 'key is not' f8fe021a37a742fabf0db74e614028f82f661337 -- CCP.Avalonia/Views/Tabs/ExclusivesTabView.axaml.cs
# Line 354 explicitly excludes goon from this build.
```

`GoonTestWindow` is a diagnostic placeholder, not a playable host. WPF's caller is live, so the dead-code exception P122 cannot mark the row wired; render-all would not establish the absent user path.

## Assumptions

- Missing live-service validation is a verification limit, not an independent unimplemented behavior. No production calls were made or credentials inspected.
- Existing linked-Chaster coverage resolves the stale ledger note without a new test. Runtime results are reported separately from compilation.
- Follow the explicit pure-dependency NOOP rule: preserve the base implementation and document the missing host rather than porting another feature into this unit.

NOOP: Goon Game host (unported on the required base).

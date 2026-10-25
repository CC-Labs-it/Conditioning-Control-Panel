# fin4-feat-stakes

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; branch `avalonia-port/cloud-fin4-feat-stakes`.
- Seed Avalonia's WPF-equivalent Chaster eligibility, copy-on-write `pvp_loss` opt-in and shared settlement. Reuse the existing wallet helper, including its UI-thread account recheck and receipt-versus-snapshot rule.
- Route Piece by Piece's four stake frames through `StakeBridge.ForApp("pbp")`; marshal replies to the current board. Keep the bridge alive across close/panic so settlement continues. Reuse the existing `games` panic registration; no new surface.
- Add focused headless tests for the live gate, consent row, once-only time booking, wallet/account race, all four frame verbs and settlement across panic/reopen. Register the manual tester step and replace the stale ledger history with current evidence.
- Read-only source inspection: `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; this adapts the frozen WPF hooks and host on the pinned base.

## WPF gap inventory and scope

At the pinned base, the missing head behaviours were: startup hook seeding; live linked/enabled/unpaused/no-safety-hold time eligibility; consent enabling the loss row before an offer; booking on the real Chaster tab; account-checked wallet adoption on the UI thread; Piece by Piece and Goon frame routing/replies; and app-lifetime settlement after a host closes. Core already owns normalization, localization, refusal handling, lock forwarding, deduplication, receipt rules and the exact settlement polling schedule, so these were reused unchanged. The existing JavaScript owns picker order, online-only eligibility, first-move/Countdown locking, reset of the chess pick and result copy; it is unchanged.

This unit closes the shared head and Piece by Piece gaps. The ledger stays **stub**: Goon has no registered Avalonia game host or live launcher path. Its WPF `Services/GoonGame/GoonHostService.cs` is 2,151 lines, with initialization/consent, matchmaking/net-post, media/cache, received artifacts, watchdogs and toy integration; a faithful host plus tests exceeds this unit's 1,000-added-line budget. These are live WPF paths (stake dispatch at lines 723-727), so P122 does not apply. Chaster's existing `RemoteOpen: false` shortcut also remains dependent on the unported Remote Control service.

## Assumptions

- Prioritize the reachable chess stakes and shared hooks within the cap; retain an honest stub instead of adding a partial Goon host.
- Follow WPF: settlement continues after panic closes the board; posting to no board is harmless and a reopened board receives the late match-tagged reply.
- Reuse the invites wire's seeded identity/base URL, as Core StakeApi already does. Sandboxes with no loopback endpoint fail closed.

## Verification limits

All commands used .NET 10 (final SDK 10.0.402), Release configuration; tests used `DiffEngine_Disabled=true`.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | 0 errors |
| `dotnet build <test csproj> -c Release` for every Linux test project in build.yml | 0 each | 0 errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 0 errors; compiled only |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` | 0 | 3953 passed, 5 skipped, 0 failed |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-restore -p:BuildProjectReferences=false --filter FullyQualifiedName~StakeHostTests` | 0 | 3 passed, 0 skipped, 0 failed |
| `node Assets/web/piecebypiece/smoke/stake-smoke.mjs` | 0 | stake-smoke: 72 passed |
| `git diff --cached --check` | 0 | no whitespace errors |

Linux test projects compiled: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests; CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests.

Initial overlapping builds caused output-file contention in Avalonia and CatalogueStartup; both passed when rerun serially. The initial Core run failed only the board frame timing budget under concurrent compilation (3952 passed); the serial rerun above passed it unchanged. Initial stake fixture failures (font registration and credential provider) were corrected using the existing shipped-font setup and isolated memory-only secrets; the final focused run above passes. The initial read-only guard attempt could not create temporary files; the writable rerun passed.

Not run: WPF/Windows runtime tests, a real online two-player match, the full Avalonia suite, and render-all. The five existing Vosk tests auto-skip without the unavailable model; no tests were disabled or weakened. `test-plan-coverage.sh` is absent from this pinned base, so that command is unavailable. No new window, platform-specific code or desktop access was introduced; the game test uses headless windows and the assembly's sandbox profile.

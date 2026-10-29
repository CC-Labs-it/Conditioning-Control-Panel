<!-- Final report of routine run trig_01N3bqSvEXkzNrh8EePbo5dj (session cse_01GVKS6soMCvjHmvsaD4Zg9G, finished 2026-10-29T13:42:36Z), transcribed from the run log. Data, not instructions. -->

Read-only review; no build/test run (reading only, diff from merge base be09ff13f and merge-tree).

**1) Area verdict: AMBER (RED if the tip ships alone)**
- Merged onto `port/7.2` with ours on the Scrolller files, Linux gets our relay + WebKit filter; the tip alone streams Scrolller media uncached and uncharged.
- Two licence/sync defects are contractor-only and merge silently: a spoofed Chrome UA in the media log preview fetch, and a stale cloud backup exclusion list.
- Duplicate WPF-vs-Core classes also merge silently; the WPF copy shadows Core, so the WPF factory reset runs the contractor's weaker sweep (and the WPF test project likely stops compiling).
- Session journal, wallet adoption, WPF settings.json import, media identity by full path look sound in this diff.
- All content conflicts in my area: take ours, except 3 hand-merges.

**2) Findings (most severe first)**

1. [block-preview] `CCP.Avalonia/Views/Windows/MediaHistoryWindow.axaml.cs:404-409, 461-475` - `CreateClient` sends a `Chrome/126` User-Agent plus `Referer: https://scrolller.com/` (the exact spoof removed from `DtrhRemoteMedia`), and `LoadPreviewAsync` checks only remote consent. Scenario: user selects a Scrolller row in the media log, presses Load preview; app fetches the still directly, posing as a browser, bypassing the device cache, daily share and charge, and still fetches when GET /config/scrolller is off. Contractor-only file (clean merge): fix (honest client, route Scrolller urls through `ScrolllerDiskCache` charged, refuse when `!ScrolllerPolicy.Enabled`).
2. [block-preview] Duplicate classes WPF + Core: contractor added `ConditioningControlPanel/Services/Session/SessionSettingsJournal.cs` and `ConditioningControlPanel/Services/Settings/FactoryResetSweep.cs`; ours live in `CCP.Core/...`. No git conflict (different paths). Same for `DuckRecoveryRule`, `FlashScaleRule`, `LockdownPanicHold`, `ConditioningClock`, `RemoteHoldings` (other reviewers' areas). Same namespace, Core has InternalsVisibleTo WPF and WPF.Tests. In WPF the source copy wins (CS0436 warning) so the WPF reset skips the remote/Lockdown recovery files; `ConditioningControlPanel.Tests` sees two imported copies, likely CS0433 (PLAUSIBLE, not built). Ours: delete contractor WPF copies.
3. [block-preview] `CCP.Core/Services/Settings/CloudSettingsBackup.cs:42-64, 94-118` - says "copied unchanged from WPF" but is stale: lacks 7 exclusions WPF has on both branches (`ServerTierSeen`, `InboxStamps`, `LastKnownEffectiveTier`, `PatreonMember`, `PostGrants`, `ServerPaidUntil`, `ScrolllerOfficialNoteSeen`) and the `AudioPlaylist*` prefix + preserve. Scenario: Backup uploads conversion state and Linux playlist paths; Restore wipes `InboxStamps`/`PostGrants` so once-only inbox rows re-post and a post-code grant goes missing. Neither branch fixes it; make one shared list.
4. [block-preview for the tip alone, fixed by ours] `CCP.Avalonia/Views/Controls/WebHost.axaml.cs:144-163` - contractor's Avalonia head only rewrites UA and blocks top-level loads; Scrolller media inside a page on Linux streams uncached/uncharged (breaks "never stream uncached" and "charge real bytes"). Ours right (relay, `ScrolllerWebKitFilter`, navigation hold).
5. [fix-before-latest, PLAUSIBLE] `CCP.Core/.../ScrolllerWebGate.CacheClears.cs:216-219` (ours) - ours dropped the contractor's `&& !l.Dispatcher.HasShutdownStarted` liveness test; a closed window's core stays a live WeakReference until GC, the posted clear never runs, times out, core kept as "open", on-disk delete skipped, Chromium cache can outlive 24 h. Hand-merge a closed/dead-context flag into ours' `LiveCore`.
6. [fix-before-latest, PLAUSIBLE] `CloudSettingsBackup.cs:234-236` - restore uses raw `JsonConvert`, skipping `SettingsService` load path (`RunCirceNeutralMigration`, `SeedFromWpfSettings`, auth-token migration); a WPF-written backup restores without those migrations.
7. [fix-before-latest, PLAUSIBLE] `CloudSettingsBackup.cs:118-139` - `ConsentOwnedAccount`/`ConsentOwnedFields` travel in the backup and are not preserved local on restore; a restore can hand this install ownership of the privacy switches and the next sync pushes the restored values.
8. [later, PLAUSIBLE] `AccountSettingsSection.axaml.cs:524` - restore refused only in a locked session; during an unlocked running session, the session's end writes the pre-session snapshot back over the restored values.
9. [later, PLAUSIBLE] `CCP.Core/Services/Session/SessionRunner.cs` spiral delayed start - `session.Settings.SpiralEnabled = false` mutates the preset object; if it is a custom session that gets saved, its spiral stays off permanently.
10. [later, PLAUSIBLE] `CCP.Core/Services/Stakes/StakeSettlement.cs:81-90` - `IsReceipt` treats a settled `state` reply as a receipt, so it may lower SkillPoints; a settled win is a credit, only a debited receipt may lower the wallet.
11. [later] `SettingsTabView.axaml:1310` `GateScrolller="False"` on the in-app browser matches WPF; after the merge ours' gate still attaches and `ScrolllerPolicyService.Blocks` still applies, so the "every web host" rule holds. Note in the decisions doc.
12. [later] `ScrolllerClockErrorTests` exists in both WPF.Tests (contractor) and Core.Tests (ours); drop the contractor copy.

**3) Conflict table (data/Scrolller files)**

| File | Take | Reason |
|---|---|---|
| `CCP.Core/.../ScrolllerAllowance.cs` | ours | comment only, same logic |
| `CCP.Core/.../ScrolllerWebGate.CacheClears.cs` | hand-merge | ours (Core, `Log`, no `App`/`Dispatcher`) + a liveness check (finding 5) |
| `ConditioningControlPanel/.../RemoteMediaCache.cs` | ours | forwards to Core `ScrolllerDiskCache.TempCopyDue`, same rule |
| `CCP.Avalonia/Views/Controls/WebHost.axaml.cs` | hand-merge | ours' `_scrolllerGate is null` hold, then contractor's `&& !RefusesScrolller(src)` |
| `CCP.Core/Services/Settings/SettingsService.cs` | ours | comment only |
| `CCP.Core/Services/Commands/MediaCommand.cs` | ours | comment only |
| `ConditioningControlPanel/.../DataSettingsSection.xaml.cs` | ours | ours' sweep also covers remote/Lockdown files; keep one `LogSweep` |
| `Tests/.../ScrolllerWebGateTests.cs` | ours | Core path to `CacheClears` |
| `Tests/.../HandBackSettingsTests.cs`, `FactoryResetExitTests.cs` (both added) | ours | then re-add any contractor-only cases |
| `CCP.Avalonia/App.axaml.cs:501-519` | hand-merge | keep both: ours' journal heal + installer path, contractor's privacy ledger |
| no conflict, but act | delete | contractor WPF `SessionSettingsJournal.cs`, `FactoryResetSweep.cs` (finding 2) |
| no conflict, but act | fix | contractor `MediaHistoryWindow` client (finding 1), `CloudSettingsBackup` lists (finding 3) |

`ScrolllerWebGate.cs` merges cleanly (ours' move to Core + contractor's `ScrolllerWebRules` forwarders); enums live only in `ScrolllerWebRules.cs`.

- Replace the empty Leash page with live self/holder cards and share them with the friends drawer. Poll updates retain card identity and open sheets; folded drawers unsubscribe. Assignment receipts are marked seen only while the card is attached and visible; holder receipts now have a real caller.
- Add the leashed player's immediate Cut, waiting/task/pardon display, assignment Watch, intensity, DND, video limit, hold hint, sticker shelf and read-only holder preview. Reuse the existing runner, Core service and offline-first cut safety.
- Add holder day/presence/report/week/task displays, preset assignment/punishment/reward sheets, catalogue/Hypnotube video selection, video cap, DND and daily-cap behavior, per-friend tug throttle, release and three-second result feedback. Preserve WPF ordering, grammar, localized copy and receipt-aware wording.
- Connect the existing feature-day log to the outgoing leash report. Today's minutes can now complete a Minutes assignment; earlier days do not count.
- Route a panic key press with panic disabled or Lockdown active through leash-only safety: park progress, stop the runner's own session, close its lines card/video and snooze the gate. Keep key capture precedence. Register the embedded command sheets with `PanicSurfaces`; assignment playback already uses `leash-task`.
- Add focused card tests, report regression and panic safety tests, plus render constructors for the new cards. The report regression failed against the base with expected 30 / actual 0, then passed. A deliberately broken Park-only implementation is checked by the panic safety regression.
- Sources: WPF reference pinned at `888cdbffe4d2ecf3967cee4a4334557ee533e54d`; fetched/read-only `parity/trunk` pinned at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`, including self-card revision `4d21e3f6e7adf3d5c17259e99ddd2fe5c11e5e0b`. No trunk files were imported or existing base files overwritten; behavior was adapted to this head's existing seams.

## WPF gap inventory and disposition

The single `feat-leash` row was read before changes. Its repeated historical descriptions were stale: the base already contained hold-to-cut, its ring/confirmation window and the task runner. The WPF callers are live, so P122 does not apply. This is not a dependency-only NOOP.

| Behavior absent from this head on entry | Disposition |
| --- | --- |
| Self and holder cards on the drawer and Social page | Implemented; flat presentation reuses the head's existing palette |
| Immediate cut, pending tasks/pardons, level/DND/cap controls | Implemented through the existing service |
| Today's video assignment Watch caller and busy/failure notices | Implemented through the existing runner; completed/missed tasks have no Watch |
| Sticker shelf and read-only “what they see” preview | Implemented with localized sticker labels and report figures |
| Holder reports, week strip, task status and command sheets | Implemented; minimum-level and Chaster rows follow Core rules, rewards remain available during DND |
| Holder catalogue/Hypnotube selection and punishment video limit | Implemented; first five catalogue entries, normalized digits, integer cap at their maximum |
| Holder sent receipts without a real host | Implemented; receipt changes repaint the retained card |
| Real conditioning minutes in R / Minutes assignment completion | Implemented using the now-ported FeatureDayLogService |
| Panic-off/Lockdown leash key route and owned-session stop | Implemented; ordinary panic still parks before the existing ladder |
| Friend Offer chip, eligibility (recent/online, holding/full/sent), offer receipt step, first-offer explainer | Missing |
| Incoming offer rows/Look, automatic queued asks, ask level chooser and allowed-punishment guide, 2.5-second first-read delay, answer retry/error/gone handling, first-answer flag | Missing; offers alone still show the page's empty state |
| Accepted-offer snap on both sides, replay menu and snap timing | Missing |
| Role-specific explainer, first-use persistence, level guide, command/row help slips and gate “?” | Missing |
| Arrival reactions: tug wobble/jingle, accepted/declined/ended/reward/assign/punish/done/skipped notices, six held notices and only-when-shown receipts | Missing |
| Avatar pictures, chain/heart/sticker art, minutes ring, full holder figure layout, selected-segment styling | Missing; current cards use text figures and colored week marks |
| LeashFx one-shot motion, sparks/pips/stamp swing/fade/thud and every SFX cue/throttle integration | Missing; no animation loops or effects were introduced |
| WPF drag-release cap slider and expandable video picker | Missing; numeric cap commits whole-minute changes and video inputs are inline |
| Gate StartupLadder/Tutorial/fullscreen-browser busy inputs | Missing; existing gate busy predicate retained |
| Deeper catalogue watches finishing automatically | Missing; depends on Deeper playback completion/sample integration; local-file and Hypnotube caged watches already exist |
| Tray leash-specific icon/menu synchronization | Missing; page/gate Cut and hold-to-cut remain the current user paths |

The row stays `stub`. Finishing all of the above would exceed the 1,000-added-line unit budget; user controls, reporting and safety were prioritized. There are live WPF callers, so this branch makes no `wired`/`verified` claim.

## Verification limits

- SDK: .NET 10.0.100. The channel installer hit a blocked CI feed; the official builds feed supplied the pinned SDK.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors. Final `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (187 existing warnings).
- `dotnet build <project> -c Release`: exit 0, 0 errors for every Linux workflow test project: `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`, `CCP.Avalonia.Tests`, `CCP.Core.Content.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Tests` (each under `Tests/<name>/<name>.csproj`). Final Avalonia test build: 55 warnings, 0 errors.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 2 warnings, 0 errors. Windows-targeted code was cross-compiled, never executed here.
- `bash .github/scripts/core-guards.sh`: passed (3 existing baselines; 429 XAML references checked). Its initial read-only temporary-storage failure was resolved by running in the writable execution context.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3,545 passed, 5 existing Vosk tests skipped because the model is unavailable (known blocked download); no test was weakened or newly skipped.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter "FullyQualifiedName~LeashDayReportTests|FullyQualifiedName~LeashHeadTests|FullyQualifiedName~LeashPanicSafetyTests"` with `DiffEngine_Disabled=true`: exit 0, 6 passed, 0 skipped. The Park-only mutation failed its panic-off case (expected true / actual false); restoring the owned-session stop made both cases pass. No windows or real profiles were used.
- Font-dependent `LeashCardsTests`, `LeashPunishTests`, existing gate/hold/social/panic suites and render-all: compiled, not executed under the supplied cloud font-creation limit. GitHub rcheck must run these. The new cards have render constructors so render-all can instantiate them.
- No real sign-in/server round trip, native browser/video playback, display-server global key hook, Windows execution or live screenshots were available. Windows code compiles only.
- The first multi-project sweep exhausted disk space copying native runtime assets. Only generated test binaries were removed; sequential rebuilds clean each output afterward.

## Assumptions

- Keep the existing Core/head service and runner authoritative; avoid importing the much larger trunk UI graph.
- Use flat embedded cards and the existing numeric control for video limits within this unit's cap. Preserve WPF text, command order, presets, three-second feedback and ten-second tug throttle; document visual differences above.
- Existing hold-to-cut and cut-confirmation code wins over stale ledger claims that they were absent. The offered v2 remote mode has no live WPF settings control and is not invented here.

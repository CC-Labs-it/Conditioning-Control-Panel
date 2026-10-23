- Port the startup modal ladder over the existing `StartupQueueCore`: priority/FIFO order, case-insensitive deduplication, awaited modal ownership, 750 ms settle, 500 ms polling, and a five-minute wait budget that freezes behind the launcher or a game. Passive arrivals defer behind a busy queue and are re-routed before `Drained`.
- Connect the existing age gate (5), failed-update report (10), first-run wizard (20), welcome show (25), welcome-back sheet (30), and update offer (80). Preserve updater age acceptance and completion semantics; attach the actual shell owner for desktop and headless hosts.
- Supply actual modal, launcher, game, session and tour quiet inputs. Watch quiet transitions at 1 Hz only while needed; unread rows remain user-opened after quiet ends. Exclude the For You feed from game parking, as WPF does.
- Offer the weekly intake nudge at 14 seconds, with its own week dismissal, existing intake-button gates, session/game exclusions, and the server announcement's shared per-launch budget claimed at routing time. Reuse the existing announcement popup and its missing-art layout.
- Render inbox glyphs through the existing Microsoft Fluent icon mapping. Register passive-window panic closure and fold pending passive cards into inbox rows so they cannot pop after panic.
- Add deterministic routing/timing tests and headless surface tests; adapt existing updater and welcome-show tests to the shared ladder and its pacing. Keep `shell-inbox` as `stub` with an explicit residual list.
- Sources: frozen WPF at base `c2505b441176bd3466e1208a7e43be96d71ddfb1`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported or existing head files overwritten. Credit for the adapted behavior belongs to the WPF reference on the base, principally `Services/Startup/StartupPresenter.cs`, `MainWindow/MainWindow.Inbox.cs`, and `MainWindow/MainWindow.Marquee.cs`.

## Initial gap audit and remaining work

The ledger row was unique and `stub` on the pinned base. The cited WPF Inbox file also contains newer conversion posters, so the audit includes those rather than treating the old ledger prose as complete.

| WPF behavior | Base head and this unit |
| --- | --- |
| Badge count, 99+, hidden-at-zero, tooltip, flyout open/dismiss, summary flattening | Already present; retained. |
| ModalUp/LadderBusy/Defer and modal queue | Absent on base; implemented for the live dialogs listed above. |
| Launcher hold, game parking without consuming the timeout | Absent on base; wired to boot, launcher return, tray restoration and existing game/intake/Chaos hosts. |
| Quiet watch and QuietChanged | Absent on base; implemented. The tour provider is already seeded by `Tours/TutorialHead.cs`; the old row's unseeded-tour claim was stale. |
| Weekly intake nudge, news stand-down and independent week dismissal | Absent on base; implemented. Missing: `IntakeNiche.PassCardImage` art resolver, so the WPF null-art fallback is used. |
| Feature intro cards, invite-ending, supporter celebration, programs intro, wardrobe unlocks, invite news | Existing routes on base, retained. Missing: recurring `tier-on` notice after a later tier rise; broader heartbeat conversion integration remains incomplete. |
| Empty-assets remote-media offer | Missing: `App.OfferRemoteMediaSource` trigger/claim and `intro:remote-media` row. Requires the empty-assets offer path; the feature card itself exists. |
| Discord sharing prompt after link | Missing: `OfferAchievementSharingAfterDiscordLink` and the `discord-share-achievements` row. WPF `MainWindow.Patreon.cs` uses `AchievementSharePromptRule`. The older ledger wording also called these Patreon share rows; the Patreon celebration rows are already present. |
| Lab possession rules | Missing: `ShowPossessionRulesIfFirstTime`, open-time `LockdownPossessionIntroSeen` stamp, and rules bark. WPF `MainWindow.Lab.cs`. |
| Merged-account sign-in offer | Missing: seeded `MergedAccountRecoveryHead` and `merged-account-signin:*` rows; depends on canonical-account swap/re-auth integration. |
| Descent migration | Missing: migration ceremony/director and its startup hand-off; existing profile receipt/fuse fields are not that user path. |
| Welcome-back restore-failed retry | Missing: `welcome-back:restore-failed`, retry and cloud-settings restore; depends on the cloud settings backup/restore feature. The backup-less sheet is now on the ladder. |
| Friends inbox | Poke/invite/watch rows already exist. Missing: individual request rows, request-gone removal and the aggregate waiting-request row (`FriendsLanding.Sink` request methods are stubs). Corner notices/cues remain the Friends landing feature's work. |
| Conversion inbox posters in WPF Inbox file | Lapse/reconnect/free-member rows already exist. Missing: daily-gift use/relock rows, live post-code polling/redeem action, companion-cap row/watch, patron-gallery row/caller, plus heartbeat/tier-rise input and associated conversion hooks. |
| Other startup modal triggers | Missing: What's New, season recap/OG welcome, display-name/online-licence and deeper-enhance nudge triggers on the shared ladder. First-launch EMI director hold/release and knock hand-off also remain unported. Their absence does not justify declaring all of the inbox wired. |

This is a partial implementation within the unit's line cap. The remaining work includes real missing callers, not only dependencies or dead WPF code; neither the dependency-only NOOP rule nor P122 applies. No `wired`/`verified` claim is made.

## Verification limits

- SDK: .NET 10.0.402. `DiffEngine_Disabled=true` for test runs.
- `dotnet build -c Release`: `CCP.Core`, `CCP.Avalonia`, and every test project named in `.github/workflows/build.yml` completed with exit 0 and zero errors: Core, Core.Settings, Core.Content; Avalonia CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot and the main Avalonia suite; and `ConditioningControlPanel.Tests` using `EnableWindowsTargeting=true` plus `ValidateExecutableReferencesMatchSelfContained=false`. Existing warnings remain. External disposable build outputs avoided exhausting temporary storage.
- `bash .github/scripts/core-guards.sh`: exit 0, both guards passed (3 baselined Core occurrences; 429 XAML references checked).
- Regression proof: `StartupLadderTests.ActualStartupModalFilesPassiveCard` failed on the base routing with `Assert.False`, actual true. It passed after the fix. `DrainRearmsPassiveWatchWhenAnEarlierCardIsStillSettling` also failed before its fix (actual false), proving drain re-routing must re-arm passive pacing.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~StartupLadderTests`: exit 0, 10 passed, 0 failed, 0 skipped. These tests construct no windows and need no fonts.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 2,884 passed, 0 failed, 5 model-dependent Vosk tests skipped (2,889 total). An initial run from external build output failed 59 source-inspection tests because that location had no repository ancestor; the standard-output rerun corrects the test location without changing tests.
- Compiled, not run here: `InboxStartupSurfaceTests`, `InboxFlyoutTests`, `ServerAnnouncementInboxTests`, `AppUpdaterTests`, `FirstShowStageTests`, `ShellCopyPortTests` and the window-based panic suite. The supplied cloud environment limit reports font-creation failures in control-rendering tests; the font-free router suite above was runnable. The supervisor's font-equipped headless gate must run these classes.
- Windows code and WPF tests were compiled, not executed. No real desktop, notifications, manual launch or render-all proof was exercised. Vosk model download is reported blocked (403) by the supplied environment limits; model-dependent test skips are not speech verification.

## Assumptions

- Use an actually visible Avalonia owner before `ShowDialog`, since a merely loaded hidden owner cannot own the dialog safely. Preserve WPF's timing/order with an awaited head pump rather than WPF's nested modal message loop.
- Treat the existing manual updater busy flag as modal quiet input as well, so a passive card cannot cover that dialog.
- Use WPF's text-only fallback for the weekly card until the existing intake art resolver is ported. Do not invent missing game hosts or migration/restore workflows.

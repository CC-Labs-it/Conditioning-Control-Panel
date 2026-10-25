# fin4-shell-inbox

- Port WPF startup arbitration using existing Core StartupQueueCore/StartupInbox: async modal queue, priority/FIFO, dedupe, 750 ms settle, 500 ms poll, five-minute unparked timeout, exact-key abandonment, exception recovery, drain-time passive re-routing and one-second quiet watch.
- Wire wizard, returning age verification, update offer, launcher boot/back and panel/tray return. Reuse panic registry game-screen probes. No new window or notification surface; existing panic registrations apply.
- Add focused headless tests and numbered tester steps; retain **stub** with an honest remaining-gap list.
- Read-only parity/trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no implementation imported. WPF adapted at required base `e9533d4ec2a904673eb1d718d994976555551b46`.

## WPF gap inventory

Read `MainWindow/MainWindow.Inbox.cs`, `Services/Startup/StartupPresenter.cs`, `Services/Startup/InboxItem.cs`, `Controls/InboxFlyout.xaml.cs` and their live callers at the pinned base above. The ledger repeated stale absence claims: the head already seeds TutorialHead and routes FeatureIntroPopup, What moved, programs, wardrobe, invite-ending, Patreon celebration/Discord and friends rows. Badge/flyout and seven-second announcement/local-server dismissal paths remain intact.

Implemented: modal queue, LadderBusy/Defer, launcher hold, game-host park, quiet watch/QuietChanged, drain/re-ask, timeout/abandonment. Remaining live behavior (P122 does not apply):

- Startup modal callers: failed-update report and first-show (25), What's New (30), season recap, online licence (40), deeper enhance nudge (70). Their predicate/claim/abandonment integration remains; failed-update report still opens directly.
- `MainWindow.Marquee.cs`: weekly intake pass open/dismiss/week stamping and `_serverAnnouncementShownThisLaunch` stand-down.
- `App.xaml.cs`: empty-assets remote-media offer, modal-time pending flush and once-per-launch claim spent at Open.
- `MainWindow.Lab.cs`: possession intro with flag spent at Open.
- Account/startup callbacks: merged-account, welcome-back restore-failed and descent migration offer with original acceptance/receipt flow.
- `MainWindow.Inbox.cs`: server-confirmed lapse/reconnect 7/3/1-day and closed copy, live entitlement suppression, Daily Gift usage/relock, free-member hello, post-code banner request, companion cap after leaving visible companion tab (10-second watch), patron gallery once daily; persisted InboxStamps, InboxNoticeCard and plan/reconnect actions.
- `MainWindow.Patreon.cs`: TierOnNoticePass and pending tier-rise behavior beyond the existing once-ever celebration.
- Live notification/timer/link-click verification; existing announcement timer/link tests remain incomplete.

Finishing all these independently owned service flows with focused tests exceeds 1000 added lines; this unit ports the shared host and leaves the row stub as authorized.

## Assumptions

- Use existing PanicSurfaces.AnyOwnsTheScreen (also used by leash/panic) for parking; includes registered lab games as well as web games.
- Awaited Avalonia Tasks replace blocking WPF ShowDialog; queue state stays on UI dispatcher.
- Use the authorized fallback clone because the supplied snapshot was read-only and at a different commit. No local changes imported.

## Verification limits

Results appended after verification. No live desktop or real profile used. Windows-native behavior cannot run on Linux. No PR opened.

- SDK: .NET 10.0.402. All Linux workflow test projects compiled individually with `dotnet build <project> -c Release -m:1 -p:UseSharedCompilation=false`, zero errors: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, Avalonia.Consent.Tests, Avalonia.Language.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Migration.Tests, Avalonia.Scheduler.Tests, Avalonia.Report.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Snapshot.Tests. Core and shipped Avalonia head also built; clean head rebuild: zero errors (existing warnings).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 4117 passed, 5 existing Vosk-model-dependent skips, 4122 total. No test changed to skip; speech model unavailable in this environment.
- Focused `StartupLadderTests`: exit 0, six passed, zero skipped; bundled font setup, sandbox profile, stepped clock. Covers ordering/dedupe/drain, awaited modal and error recovery, launcher/game park, exact-key timeout, quiet expiry and retained rows.
- Related filter (InboxFlyoutTests, ServerAnnouncementInboxTests, ShellCopyPortTests, first-run welcome integration and StartupLadderTests): exit 1; 11 passed, 11 failed, all failures at missing Segoe UI Black/Inter glyph creation before the relevant UI assertions. Known environment font limit; no test assertions weakened or removed. Full headless suite and live desktop not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. `test-plan-coverage.sh`: unavailable on this base (exit 127); numbered coverage added manually.
- Initial validation attempts exhausted the workspace while duplicating native runtimes and left a zero-byte cached assembly; discarded generated outputs, rebuilt from source, then built test projects one at a time with output cleanup. Successful results above supersede these infrastructure failures.
- Windows-targeted test project cross-build: `dotnet build Tests/ConditioningControlPanel.Tests -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors, two existing warnings after output-copy disk recovery. Compiled only; WPF and Windows-native code not run on Linux.

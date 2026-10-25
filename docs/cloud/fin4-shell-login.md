# fin4-shell-login

- Base verified at `5409d25573d187d89e5d24378ae8d1014491bc81`; the initial clean snapshot was a different commit and was reset before task edits.
- Added all six WPF sync consent fields, preserving independent opt-ins and explicit false revocation. These are locally authoritative in WPF; no cloud echo enables consent.
- Added WPF V2 companion/time merges: higher level, then lifetime XP selects the entire companion record; time takes higher. Time adoption rebaselines the existing feature day log. Each field is sent only after it was read in a sync response this session, independent of skill adoption; logout disarms both.
- Added the first web-XP claim's once-per-install success toast, exact WPF copy and nine-second duration, using SeenFeatureIntros and the existing UI dispatch/notification seams. No new window or panic surface.
- Added the provider-link sharing caller after link confirmation, reusing the already registered question/Inbox surfaces. Refactored the actual post-link continuation for headless tests; OAuth is unchanged.
- Added focused Core and headless wire/UI regressions and numbered manual steps. The consent and companion/time tests failed on the base (2 failed, 0 passed) before the implementation and passed afterward.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; imported no trunk code. WPF files on the pinned base supplied the behavior.

## WPF audit and remaining gaps

The authoritative row is the single `shell-login` row in docs/avalonia-parity.md. References inspected at the pinned base: MainWindow/MainWindow.Login.cs; MainWindow/MainWindow.Patreon.cs:780-882; Services/Settings/ProfileSyncService.cs (V2 sync body, merge and quest helpers); Services/Progression/ProgressionService.cs:AddClaimedXP and AnnounceFirstWebXpClaim. Compared with AccountSeed, LoginDialog, MainShellWindow.Login, AccountSettingsSection, SyncPush and ProfileAdopt.

Before this unit the named gaps were stats, consents, companion/time progress, quest logs, the first-claim toast and the provider-link sharing caller. This unit handles the latter four groups. The row remains **stub**, never wired or verified, because the following live WPF behavior remains:

- Lifetime achievement stats and login-streak date-pair adoption, deferred streak-break settlement, restore of achievements returned by sync, highest-level lifetime adoption, and full XP-stat accounting for web claims.
- Quest aggregate/date/log read-before-write reconciliation, strict invariant day keys/ISO fallback and future-date limits, 90-day union retention, per-day completion evidence, mobile totals kept separate, quest reset/force-streak corrections and acknowledgements, outgoing capped quest/feature day logs.
- Quest ownership stamping on logout and reconciliation before profile load, generation after a different-account cloud load, and UI refresh of the restored quest/skill tree.
- Full account-cache cleanup (profile viewer, Descent and purchase caches) on logout/account switch, WPF's 500 ms token-settle delay, and suppression of achievement popups during the load.
- Broader ProfileSync limitations already declared by Core: restore-from-backup reconciliation, unauthorized-session recovery, heartbeat adoption, anti-cheat/season-reset presentation and migration offer handling. These are not promoted by this unit.

The remaining stats/quest bundle includes ProfileSyncService's merge, strict-date, log, force-streak and payload builders (roughly 600 reference lines before service wiring), achievement restore/streak/day-log handling and tests for each rule. Porting it intact with the required focused cases alongside this unit would exceed the 1,000-added-line budget. The user-visible consent/companion/notification/link paths were prioritized. No claim of a pure dependency NOOP or dead WPF caller is made.

## Assumptions

- Keep the base's conservative read-before-write policy: absent companion/time fields do not authorize pushing defaults. WPF sends these after loading; this head additionally requires each sync field to have been read.
- Consent flags remain device-local authoritative settings, matching WPF's outbound-only handling.
- Use the existing passive notification presenter and MessageDialog/Inbox panic registrations; no new surface needs registration.

## Verification limits

- SDK: .NET 10 (final `dotnet --version`: `10.0.402`). The channel installer initially encountered a blocked latest-version endpoint; a pinned 10.0 SDK installation succeeded, and the channel installation also completed.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all eleven Linux test projects named in build.yml passed, each with zero errors. Existing warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings, zero errors. WPF app and tests cross-compiled, not run.
- `bash .github/scripts/core-guards.sh`: passed (the initial sandbox invocation could not create temporary files; rerun with write access passed). `git diff --check`: passed. test-plan-coverage.sh is absent on this base; the new numbered manual steps use the required WPF/Rows format.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~AccountSeedTests|FullyQualifiedName~LoginAchievementOfferTests'`: exit 0, 56 passed, no failures or skips. Headless fonts worked. Separate LoginAchievementOfferTests run: exit 0, 10 passed.
- Focused ShellLoginSyncTests: exit 0, 3 passed. Before implementation: exit 1, 2 failed, proving the consent and companion/time omissions.
- Full Core suite, first two runs during builds: exit 1, 2,820 passed, one failed, five skipped. Both failed `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` at the null scheduling handle assertion. Its clock computes exactly the minimum lead and refuses scheduling if time advances before ScheduleAt; those source/test files are unchanged from the base. Isolated class recheck: exit 0, 12 passed. Vosk's five existing tests self-skip without the unavailable model; no tests were modified to skip or weaken a gate.
- Final full Core run after compilation: exit 0, **2,821 passed, zero failed, five existing Vosk skips**, duration 53 seconds. This includes all three new Core cases and the previously failing timer test. No test changes were made between the failed and passing full runs. Windows projects may be cross-compiled but cannot be executed on Linux. Real provider OAuth and production server consent/storage semantics are not exercised; all automated network coverage uses fake handlers and isolated settings.

# fin4-shell-launcher

- Conceal the entire panel before its boot Show, keep it unactivated/off-taskbar while Opened work runs, then restore opacity and window flags after surface routing. Reuse the existing companion boot flag and launcher route.
- Connect the existing passive startup router to the launcher's boot hold. Notices offered while held become Inbox rows using Core StartupQueueCore; opening the panel through the launcher releases the hold. No duplicate startup policy in the head.
- Recheck Lockdown during boot routing, after the panel's Opened work: keep the panel visible and release startup instead of tucking it away.
- Add four focused LauncherWindowTests and numbered manual tester steps. No new window/surface was added, so no new panic registration is needed; existing shell/launcher panic paths remain in use.
- Keep shell-launcher **stub**, with an explicit remaining-host list. No speculative modal host or false wired claim.
- Imported trunk commits: none. Fetched parity/trunk at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038 read-only; adapted the WPF reference already on the pinned base instead.

## WPF comparison and remaining gaps

Reference pinned at 0608550b1ce20cae6278d830a67140608e5fdfa7:

- MainWindow/MainWindow.Launcher.cs: HideForLauncher refuses Lockdown, hides to tray, and restores root opacity; ShowHiddenForBoot cloaks native chrome, suppresses activation/taskbar and companion show while executing Loaded; finite 180/260 ms panel fades restore opacity; RequestExit uses the shared engine/audio/overlay/session/save shutdown; the title-bar door delegates to LauncherHost.
- Existing Core/head code already covers launcher boot decisions, handoffs, Lockdown refusal, exit routing and finite panel fades. This unit addresses concealment and a Lockdown race at the boot route.
- The WPF boot cloak uses DWM. Assumption: portable top-level opacity zero before Show is the smallest head equivalent. It hides chrome as well as content and is restored only after routing, but requires native visual verification on Windows and Linux. Headless property tests alone cannot prove no native flash.
- WPF App.xaml.cs holds startup before showing the hidden panel and again in RouteBootSurface; LauncherHost.ShowPanel releases it. This unit connects that state to the existing passive Inbox router. AppUpdater already waits for a visible shell.
- **Missing modal host:** WPF Services/Startup/StartupPresenter.cs is 646 lines, including ordered modal queue/pump, game/launcher parking without spending the five-minute deadline, wake/retry and modal/passive arbitration. Avalonia Platform/StartupLadder.cs has only passive routing. Returning-user What's New / season recap / OG welcome startup callers are still absent (documented in MainShellWindow.FirstRun.cs and MainShellWindow.Marquee.cs). Adapting the presenter, its producers and behavioral tests exceeds the remaining 1,000-line budget; a hold boolean cannot claim these behaviors.
- Reuse the normal shell close-to-tray one-time notification for non-quiet launcher tucks; boot remains quiet and does not consume the latch. The existing OS notification fallback (Windows toast API not yet ported) remains a platform limitation. A sink-based test requests no real notification.
- Missing evidence: native boot visibility, locked-down cold boot and complete modal resume after returning from launcher/game. No P122 dead-caller claim: WPF has live HoldStartupLadder callers in App.xaml.cs and ReleaseStartupLadder in LauncherHost.ShowPanel.

## Verification limits

- Exact base verified in the fallback clone; the original snapshot was clean but on a different commit and its Git directory was read-only. Work proceeded in the explicitly authorized fallback checkout.
- .NET 10.0.402 installed via the official build feed after the default installer's ci.dot.net endpoint returned 403.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors. Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` (rebuilds both dependencies): exit 0, 296 existing warnings, zero errors.
- All 12 distinct test projects in `.github/workflows/build.yml` that target Linux built with exit 0 / zero errors: Core, Core.Settings, Core.Content; Avalonia.CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, and Avalonia.Tests. Generated test bins were removed between projects to avoid copying multiple sets of native runtime assets.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 4,169 passed, five existing Vosk skips, zero failures. Focused LauncherRulesTests / LauncherBootTests / LauncherHandoffTests / StartupQueueCoreTests filter: exit 0, 13 passed, zero skipped or failed.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent from this base.
- Initial validation attempts hit an overlapping Avalonia output-file lock and then disk exhaustion from copied test runtimes; generated outputs were cleaned and sequential builds plus the Core suite rerun passed. The disk-exhausted Core run was not counted as a passing result.
- Avalonia tests are compile-only per the task's measured font-creation limitation; cloud-test-fonts is not present in this base. LauncherWindowTests (including the four new tests) must run in rcheck. No fail-before runtime proof was obtained here; do not treat the new tests as passed.
- No Windows desktop/runtime, native boot or notification tests were run. WPF cross-compilation succeeded: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, exit 0, 555 existing warnings, zero errors; not run. No real desktop/profile was touched by a test.
- The portable concealment choice and the inherited missing modal host are the assumptions/limits, not evidence of full parity.

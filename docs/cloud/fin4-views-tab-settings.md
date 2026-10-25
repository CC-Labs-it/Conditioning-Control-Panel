# fin4: Home gift and inactive cards

- Verified base `56d3fff6a5ee74a9682f0449fb94f16202979704` before editing; initial snapshot differed and was reset.
- Gift box paints the localized offer, reverse face paints today's name, mod art/fallback, and tier/free stamp. Premium sees no price sign. Tier/day, mod and language changes repaint it.
- Hover saves DailyGiftLastRevealDate and retires NEW TODAY across visits/restarts. Cubic reversible 150ms half-turns use 6-degree skew, layout-space phantom-leave suppression and completion reconciliation. The 900ms breath rides BeatLoop. Hide, detach, motion gating and panic stop/settle the ceremony.
- Single cards honor DimWhenInactive, lock/hover/tutorial precedence and muted title. Cached alpha-preserving grayscale art fades over 200ms; split halves repaint independently. Home hover keeps inactive cards dim and suppresses generic lift/pop.
- Added focused headless tests and numbered tester steps. Ledger remains `stub` because several live WPF behaviors remain.
- Read-only parity/trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported. Adapted from WPF at the pinned base using existing Core entitlement, ModArt and BeatLoop seams.

## Baseline WPF gap inventory

References are relative to ConditioningControlPanel at the base SHA above.

| Gap found before implementation | WPF specification | Result |
| --- | --- | --- |
| Mystery offer, localized identity, mod art/fallback, tier/free stamp, premium suppression | MainWindow/MainWindow.Presets.cs:1164-1272 | Implemented |
| Reversible cubic flip, 150ms halves, skew, phantom leave/reconciliation, saved date, NEW TODAY, breath | MainWindow/MainWindow.DashboardFx.cs:277-727 | Implemented; headless behavior validation subject to limits below |
| Single inactive opacity 0.62/title 0.72, lock 0.35 precedence, hover/tutorial/destination exceptions; grayscale | Features/FeatureCard.xaml.cs:517-604; Services/CardMute.cs | Implemented |
| Per-half grayscale and independent state/hover | Features/SplitFeatureCard.xaml.cs:681-775 | Added grayscale; existing independent dim retained |
| Dashboard depth sockets, per-face lift/rotation and active depth | Services/UI/DashboardCardDepth.cs; both feature controls | Missing |
| Weekly intake available-only face; premium/spent/login wordmark; mod art; help, CTA breath, weekly spin/dwell/cancel/retry | MainWindow/MainWindow.UiUpdates.cs:1250-1933 | Missing; Core IntakePass exists, presentation host remains |
| Owned v2 badges and grants-change repaint | Views/Tabs/SettingsTabView.xaml.cs:420-435; Services/Prizes/V2Badges.cs; PrizeGrants.cs | Missing Core ownership host and head badges |
| Wall labels/art; live mod/language/unlock repaint; Deeper mod art | MainWindow/MainWindow.UiUpdates.cs:95-358,889-968; MainWindow.xaml.cs:2712 | Missing apart from gift repaint. XP tile locks are retired in WPF; entitlement presentation is live |
| Wordmark sheen/custom-logo drift; browser sweep/status pulse | MainWindow/MainWindow.DashboardFx.cs:729-922 | Default animated logo already exists; remaining decoration missing |
| Browser pop-out/reparent; bridge lifecycle/enhance matches | MainWindow/MainWindow.Browser.cs | Missing embedded browser host and BrowserEnhancementBridge; disabled controls retained |
| Once-ever note voice before dialog; latch only when playback succeeds | MainWindow/MainWindow.UiUpdates.cs:1943-1976 | Missing PlayNoteClip route and shipped note_newyear.wav; silent note retained |

The combined remaining hosts, presentation and behavior tests exceed the 1000-added-line unit cap: the WPF intake ceremony alone spans hundreds of lines before browser/bridge, depth and ownership ports. This unit prioritizes visible gift and inactive-card gaps. It is not a pure dependency NOOP and makes no P122 dead-caller promotion.

## Verification limits

Results appended after execution. Tests use repository sandbox profiles and the headless dispatcher. No new window or notification host; gift teardown is registered in PanicSurfaces.

## Assumptions

- Keep the row `stub` until every live behavior is reachable.
- Use the combined Core premium seam, covering either supported account provider.
- Undecodable art falls back to opacity dimming, matching WPF.

### Executed checks

- .NET SDK 10.0.402 installed with dotnet-install. The installer's initial ci.dot.net lookup returned 403, then its builds feed download succeeded (exit 0).
- `dotnet build -c Release` passed (exit 0, zero errors) for CCP.Core, CCP.Avalonia, and every test project named by build.yml: Core, Core.Settings, Core.Content, Avalonia main, CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, and ConditioningControlPanel.Tests. WPF tests used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; compiled only, never executed on Linux.
- Final main headless build uses `-m:1`, zero errors. Initial overlapping builds encountered a PDB lock/compiler worker failure; corrected by serial compilation. A missing Task return and test namespace qualification were corrected before the successful build.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 0; 2825 passed, 5 skipped by the existing Vosk prerequisites, total 2830. No test weakened or removed.
- Focused main Avalonia run with `DiffEngine_Disabled=true` and filter `DashboardGiftPresentationTests|SplitFeatureCardFxTests|DashboardClickChoiceTests|PanicSurfacesTests`: exit 1; 4 passed, 15 failed during font creation (Inter / Segoe UI Black), before UI behavior assertions. The stepped-clock test added afterward was compile-checked only. Remaining UI execution, mutation fail-before proof and render-all must run on the supervisor's font-capable runner; no behavior pass claimed here.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127 because that script does not exist on this base. Added the required numbered tester plan anyway.
- `git diff --check`: exit 0. No Windows runtime tests, real desktop windows, Vosk downloads or render-all executed.

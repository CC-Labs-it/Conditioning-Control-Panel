- Restored the upgrade-launch What's New caller, its feature-intro modal gate, and the upgrade-tour CTA. The shell claims a pending hold before Opened, waits 750 ms, retries readiness every 500 ms for up to five minutes, and stamps LastSeenVersion after dismissal. Empty versions stamp silently. Pending offers defer through Core's routing rule; offers during the dialog become Inbox rows without spending their seen flags.
- Added panic cancellation/closure and headless tests for silent versions, launch reachability, queued and open modal gating, timeout, dismissal and panic.
- Replaced the accumulated historical ledger text with one honest stub row and added a numbered tester step.
- Reference: WPF at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Fetched read-only `parity/trunk` (`a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`); no trunk code imported or overwritten.

## WPF gap audit

The card itself already has WPF content, accents, scrollable bullets, Escape/X/dismiss, drag, optional CTA after modal unwind, open-time spending, double-post protection, ten-minute pacing, per-door launch budgets and the tour/update/startup guards. The existing shell reaches daily-free then one-account, play/lab, discord, awareness, studio-rack, haptics, lockdown, blinktrainer, shelistening and the spiral map. Existing tests cover representative doors and gating.

Historical absence claims corrected:

- FirstShowService exists; `MainShellWindow.TabNavigation.cs:293` already returns while it is active.
- Welcome-back and first-run already own IsStartupDialogShowing. Manual What's New/recap deliberately do not, matching WPF.
- Automatic season recap is dead as a modal in the pinned WPF: `MainWindow.Marquee.cs:271-294` only performs silent monthly bookkeeping. There is no recap modal to port for this row. This does not justify marking the whole row wired under P122.
- Launch/provider premium celebrations already use TierCelebration and the presenter; only the live-rise route remains absent.

Remaining live gaps (row stays **stub**):

1. **remotemedia**: WPF `App.xaml.cs:888-966` offers local-source users coaching once per launch at empty-media dead ends. It defers behind startup/update modals, flushes later, and claims the budget only at open. Callers include FlashService, VideoService, WallpaperService and the first-run asset prompt. The head has the media picker but lacks this app-wide offer host and the corresponding production empty-media callbacks. Adding a card on ordinary Assets navigation would teach at the wrong time.
2. **descent-vat**: WPF `MainWindow.ProfileVat.cs:267-305` fires only after a server block arms the jar and the Trainer Card is on screen. Avalonia's ProfileVat remains geometry/readout only; its header names the absent DescentService, polling/visibility wires and faucet. Core has the fold/reader, not the HTTP/lifetime host. The reference faucet alone is 1,145 lines and DescentService another 357, before wiring/tests. A fabricated local-XP fill or an unconditional intro would violate the rollout gate.
3. **possession**: WPF `MainWindow.Lab.cs:690,1100-1140` offers rules at activation when possession is enabled and its separate intro flag is unseen; an Inbox row spends that flag only when opened, and the bark fires immediately. The Avalonia lockdown view explicitly has no running possession director/readout; its target-walk partial still lacks PossessionDirectorHead, cache and target collection. Showing rules for room effects that cannot run would be misleading. That host and bark integration must port together.
4. **premium live rise**: WPF `MainWindow.Patreon.cs:1126-1169,1213-1268` consumes EntitlementTierSync.TierRaised, bypasses the Inbox for an idle live purchase, retries through the presenter when busy, and performs profile-bubble fanfare. The head currently calls the presenter for launch/provider changes; AccountSeed applies the Core entitlement rise rule without an event. The 299-line WPF EntitlementTierSync host, rise ordering, small repeat-tier notice and bubble fanfare still need adaptation and tests.

Completing those hosts faithfully plus this change does not fit the 1,000-added-line unit cap. This unit closes the independent upgrade-modal gap and does not claim the dependent behaviors are wired.

## Assumptions

- Reuse the existing dialog and tutorial engine, with a narrow upgrade host rather than introducing a general startup queue with one caller. Core remains head-neutral.
- Retain Fluent icons already chosen by this head.
- Keep the automatic season recap retired, matching the pinned WPF implementation rather than obsolete ledger prose.

## Verification limits

Validation results are recorded below after execution. Windows-specific code can compile here but cannot run on Linux. No real desktop or user profile is used. No Vosk model download is attempted because the environment documents its 403 limitation.

- SDK: .NET 10.0.402. The original checkout had a different commit and a read-only Git directory; the authorized writable fallback was verified clean at the required base before branching.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia, and all 12 Linux-capable test projects named in build.yml passed, each with zero errors. Builds were retried sequentially after generated native-runtime copies exhausted disk space; only generated outputs were removed.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (3 existing baseline entries; 429 XAML references checked).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3,953 passed, 5 existing Vosk-model skips, 0 failed.
- Focused Avalonia filter `FullyQualifiedName~FeatureIntro|FullyQualifiedName~PanicSurfacesTests`: final exit 0; 20 passed, 0 failed, 0 skipped. Fonts worked in this environment. The initial run found an assertion reading button Content instead of the named TourLabel; that assertion was corrected to check rendered text.
- Regression proof: temporarily removed the returning-launch ArmUpgradeIntro call, rebuilt successfully, then ran RealShellOpenedCallbackReachesUpgradeDialog. It failed with an empty dialog collection (exit 1). Restoring the call and rebuilding gave the 20-pass result above; no probe remains in source.
- `git diff --check`: exit 0. test-plan-coverage.sh is absent on this base, so its check was not available; the numbered WPF/Rows tester step is included.
- Other headless suites were compiled but not executed. No Windows desktop tests, real notifications, manual desktop rendering or live-server feature checks were run. The four remaining dependent hosts above are unverified and the ledger remains stub.
- WPF test project: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false --disable-build-servers -m:1`: final exit 0, 2 warnings, 0 errors. Disabling stale workers resolved an MSBuild child failure after the disk retry. Windows code was compiled, not run.

# fin4-shell-chrome-fx

- Ported tab choreography from WPF at base `20c9f4acff68309193ae82cbc16ba21771b0d873`. ShowTab is the live caller. Finite shared-beat animation preserves timing, direction, motion gates, browser exemptions, target selection, authored transforms and rapid-return hit testing.
- Moved pure ChromeFxNav unchanged into Core for both heads. Added Core decision tests and headless caller/stagger tests plus a numbered manual tester step. No new window, overlay or notification surface: the existing shell remains the panic owner.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Adapted directly from the pinned WPF reference.
- Ledger remains `stub`, never `wired` or `verified`.

## Gap analysis

Before this unit the gaps were AnimateTabIn / SlideTabIn / FadeOutgoingTab / CollapseOutgoingTab / StaggerTabCards / FindStaggerTargets (ShowTab caller), THE BANK's first refusal in AnimateXpDisplay, and forced banner sheens from attention/read-return callers. Tab choreography is now implemented, including cleanup after interruption and close. Nav hover/active state and HeroFx XP fill/odometer were already present.

Remaining live WPF hosts:

- `shell-bank-fx`: BankAccumulator collection/bankable-source policy, XP event ordering/arming, token surface/anchors/landing counter, thud/pop/flash, watchdog, cancellation and unconditional ledger reconciliation. WPF BankFx is 922 lines plus the 215-line accumulator; the Avalonia partial is a placeholder. ProgressionBank already has Awarded and LevelUp and the header is live, so the older stub comments about no event or static XP are stale; there is still no ordered XPChanged-before-Awarded arming path. A hold without delivery and reconciliation would freeze XP.
- Banner attention: WPF BannerFx (412 lines) OnBannerBeatChanged forces a sheen when BannerFxRules requests sparkles. Requires the attention beat/pop/ring/sparkle host and repeat lifecycle.
- `shell-marquee-reads`: WPF MarqueeReads (322 lines) RestoreTicker forces a sheen after an interlude, including cancellation. Requires read cadence/stage/effects and ticker pause/restore host; this head has only its ordinary marquee.

Pinned evidence: `git grep -n SweepBannerSheen 20c9f4acff68309193ae82cbc16ba21771b0d873 -- ConditioningControlPanel/MainWindow` finds ChromeFx itself, Marquee rotation/announcement (already ported), BannerFx:242 and MarqueeReads:264. BankFx TryHoldXpDisplay is live through ChromeFx AnimateXpDisplay. P122 does not apply. Completing these hosts with tests exceeds the 1000-added-line unit budget after tab choreography; the user-visible tab gap is delivered first.

## Verification limits

Final validation results follow below. Windows WPF code cannot run in Linux. No visual parity claim or verified promotion.

## Assumptions

- Keep WPF browser-tab exceptions across platforms.
- Use the shared beat for finite transitions to avoid transform-targeted Avalonia Animation.
- Keep BANK and attention/interlude dependencies explicit instead of adding a hold without release.

Additional cross-row lifecycle gaps in the WPF ChromeFx initializer/funnel: EnsureEventBurstLayer depends on `shell-event-fx`; InitializeHeroFx / ApplyHeroFxLoops depends on the remaining `shell-hero-fx` START charge, ring/heartbeat and XP meniscus hosts; ApplyBannerFxLoops depends on the banner host above. The Dashboard loop funnel already has its own equivalent lifecycle in this head. These are not silently treated as completed by the tab port.

Validation (Release, .NET 10.0.402; `DiffEngine_Disabled=true` for tests):

- `dotnet build <project> -c Release`: exit 0, zero errors for Core, Avalonia, and all 11 Linux test csproj files in build.yml (Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia Consent, Language, CatalogueStartup, Scheduler, Report, GeneralSettings, Snapshot, and Tests).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: final exit 0, 2 warnings, 0 errors. WPF app and test project compiled; Windows code/tests were not run. Initial attempt exhausted generated-output disk space; succeeded after removing already-validated Linux build outputs.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed (424 XAML references checked).
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 2848 passed, 5 pre-existing Vosk skips, 0 failed (2853 total).
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~ChromeTabTransitionsTests|FullyQualifiedName~ChromeAmbientLoopsTests|FullyQualifiedName~ChromeBannerSheenTests|FullyQualifiedName~HeaderLevelLiveTests'`: exit 0; 10 passed, 0 failed/skipped. The existing font setup works in this snapshot.
- Failure proof: with original ShowTab/ChromeFx callers temporarily restored, ChromeTabTransitionsTests failed Full and Reduced with expected opacity 0 versus actual 1; Off passed (exit 1, 2 failed / 1 passed). Implementation restored before final checks. An overlapping intermediate Avalonia build hit an output-file lock; final builds were serialized and tests rerun with --no-build.
- `git diff --cached --check`: exit 0. Test-plan coverage script is absent on this base; numbered coverage was added manually. No real desktop/profile access or visual parity inspection; no render-all run (no P122 claim). Auxiliary suites were compiled, not executed.

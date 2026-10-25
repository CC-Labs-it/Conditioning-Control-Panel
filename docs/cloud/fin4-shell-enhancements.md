# fin4-shell-enhancements

- Added server-authoritative purchasing from purchasable tree and revealed secret cards: confirmation, disabled pending card, refusal dialog, ownership repaint and node/prestige bursts before rebuilding.
- Adapted Core SkillPurchase and purchase tests from read-only parity/trunk snapshot `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (purchase UI last changed at `9ecc48b24d198a6b8ec2a9485536afe4c6224747`). Existing base files were merged, never replaced.
- Wired existing auth recovery, account sync, achievement spend reconciliation and active-session Pink Rush host. A balance refusal waits out one sync cooldown before retrying; failed sync keeps the local wallet.
- Trophy columns now follow trophy_case ownership, including a purchase while the board is attached. Purchase dialogs have an explicit PanicSurfaces entry.
- Retained the single shell-enhancements ledger row as **stub**, and added numbered manual coverage.

## WPF gap audit

Reference pinned at base `85d30ccc4b6cb241d136e8299a0d2e844b88585a`:
`MainWindow.Enhancements.cs:1938-2009,2109-2261`, `MainWindow.EventFx.cs:428-456`,
`Services/Progression/SkillTreeService.cs:80-143,200-231`,
`Services/Settings/ProfileSyncService.cs:3955-4120` and `Services/Progression/SeasonRecapService.cs`.

Before this unit, drawing and analytics panels existed, but tree/secret cards had no purchase caller;
the server purchase service, purchase/prestige celebrations, ownership-driven trophy refresh, lucky toast
and analytics writers were absent. Flash already rolls luck through FlashFxRules, but emits no toast event.

Remaining (not claimed wired):
- Prestige full-chrome sheen (WPF 1.15 seconds, peak 0.16); node and rank bursts now exist.
- Lucky toast host: opt-out/game-surface suppression, reuse/recolour, 200ms fade-in, hold 3s after the last proc, 300ms fade-out/hide with restart protection, primary work-area placement and real close on shutdown. Requires routing both flash and bubble proc events; current FlashFxRules only returns a multiplier.
- Season recording/rollover host: active days, conditioning minutes, feature-use counters and recap snapshots. Analytics still reads historical imports/current zeros where no writer exists.
- SkillUnlocked bark routing. WPF intentionally emits no extra celebration audio; its BarkService voices skill_unlock. This head has no equivalent purchase subscription.
- Existing bonus-chip deviation: skip breakdown entry zero instead of WPF's English-only Base-name test.

The purchase slice plus behavioral tests was prioritized under the 1,000-added-line cap;
the additional recording/toast hosts and their lifecycle tests remain separate work.

## Verification limits

- .NET SDK 10.0.100 installed using the pinned installer after channel discovery returned HTTP 403.
- Initial full Core tests: exit 0, 3,751 passed, 5 existing Vosk-model skips, 0 failures. Final suite after additional tests: exit 1, 3,754 passed, 5 skipped, one unrelated DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls failure (expected Loops, got Clips). That unchanged test passes in isolation.
- Final isolated tests: exit 0, 13 passed (12 purchase/sync tests plus the DTRH retry), 0 failures. Auth tests use an isolated in-memory CoreSecrets provider.
- CCP.Core, CCP.Avalonia and all 12 Linux test projects listed by build.yml compile with 0 errors.
- Core guards: exit 0, all guards passed. test-plan-coverage.sh is absent on this base.
- Focused Avalonia tests were attempted: 5 failed before assertions because Inter glyphTypeface creation failed. Final UI tests are compiled; they require supervisor CI to run. No visual/render-all claim, or fail-before/fix proof for trophy visibility, is made on this environment.
- Windows-targeted WPF tests: build exit 0 with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false, 0 errors; Windows execution is unavailable here.
- Real account purchases, network auth recovery and desktop particle rendering were not exercised; transports are fake and profiles sandboxed in tests.

## Assumptions

- Use the existing MessageDialog with Yes/No captions for WPF's confirmation. English button text matches the reference's intent; localized system button captions are not reproduced.
- Serialize purchases across the tab (including an open question) to prevent duplicate submits.
- Panic dismisses purchase dialogs; an already submitted server transaction cannot be revoked.
- Preserve base drawing/analytics and its documented bonus-chip deviation.

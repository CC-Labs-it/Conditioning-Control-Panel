# fin4-views-tab-enhancements

- Adapted Core SkillPurchase and purchase tests from parity/trunk `4643c25508265bafa446f605d7556ec14e381e59`, and tab purchase UI from `9ecc48b24d198a6b8ec2a9485536afe4c6224747` (inspected trunk tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Existing base files were merged, never overwritten.
- Wired node/secret-card mouse release, hand cursor, localized Yes/No confirmation and permanent note, serialized requests, failure box, receipt adoption, and repaint. Purchase and prestige bursts reuse EventBurstLayer. Purchase dialogs register with PanicSurfaces.
- Reused account auth/merged recovery and SyncPush; added the WPF cooldown-aware sync-before-balance-retry path, spend tracking, initial streak shield/insurance effects, unlock bark and Pink Rush startup. Trophy columns now derive ownership and refresh on purchase.
- Fixed English catalogue inputs to mod vocabulary on names and flavour/description tooltips and confirmation; localized fallback remains available without a mod provider. Regression: all three text cases failed before the fix.
- Replaced the stale repeated ledger history with one honest stub row and numbered manual tester steps.

## WPF gap analysis at base 85d30ccc4b6cb241d136e8299a0d2e844b88585a

The cited tab XAML and wheel handler delegate tree behaviour to MainWindow.Enhancements.cs. Contrary to the stale ledger prose, art, tooltips, glows, hover, secret reveal/cards and four PRO expanders already existed on this base. Missing were click confirmation/purchase, hand cursor, server host and local effects, purchase/prestige feedback, trophy-column refresh, and raw-English mod vocabulary. This branch addresses those except the full-window prestige sheen.

Remaining: the generic full-chrome SweepSheen host is unported; the existing banner sweep is constrained to a different host. The rank burst is present, but this row stays stub. PRO analytics still lack season activity/minutes/feature writers and snapshot production for fresh profiles; porting those requires session/feature tracking and season-rollover work beyond this tab. Grep on the pinned base finds only settings declarations and analytics readers for SeasonActiveDays/SeasonConditioningMinutes/SeasonFeatureUse. Lucky-proc toast belongs to shell-enhancements and its unported proc callers, not this tab's click/scroll surface.

## Verification limits

- `dotnet build -c Release` for CCP.Core, CCP.Avalonia and all 12 Linux workflow test projects: exit 0, zero errors each. Outputs were cleaned between test projects to fit runtime disk capacity.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 2 warnings, 0 errors. Windows tests were compiled but not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: initial run exit 0 (3751 passed, 5 Vosk skips); final run after adding auth/effect tests exit 1 (3752 passed, 5 skipped, one existing BoardTilesTests.A_frame_fits_the_budget timing failure). Isolated timing retry also failed; no tests changed to hide it.
- Focused SkillPurchaseTests: 10 passed; fake HTTP covers receipts, refusals/sync, failed requests, 401 recovery, account switching, immediate effects and monotonic spend.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~EnhancementsTextTests|FullyQualifiedName~EnhancementsPurchaseTests|FullyQualifiedName~EnhancementsTabTests|FullyQualifiedName~EnhancementsAnalyticsTests'`: compiled successfully, exit 1; 3 text cases passed, 5 UI cases blocked by the known Inter glyphTypeface creation failure. UI behaviour and panic dismissal require supervisor execution. Earlier disk-full builds left incomplete XAML artifacts; a clean rebuild removed that failure.
- Text regression before fix: 3 failed, 0 passed; after fix: 3 passed. `git diff --check`: exit 0.
 No live account/server mutation, real desktop, Windows execution, or visual approval was performed. Tests use sandbox profiles and fake HTTP; new Core auth tests use an in-memory secret store. Vosk model download is blocked in this runtime. The test-plan coverage script is absent on this base.

## Assumptions

- Preserve the ledger's stub status until the full-window prestige sheen and analytics producer dependencies are ported.
- Serialize the whole confirmation/purchase path to prevent duplicate dialogs and purchases; WPF disables the purchased card after confirmation.
- Keep Fluent icons and the existing shared FX clock and burst layer.

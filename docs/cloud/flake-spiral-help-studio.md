- Use the production app builder consistently in the affected classes. The first class to initialize the process-wide Avalonia app previously decided whether bundled font mappings were configured. A reordered run failed all 13 cases on Segoe UI Black after another order passed all 13. SharedDeeperThemeTests already used the production builder.
- Publish headless layout/render work before injecting pointer input in the mounted Spiral help, Studio rack, section-strip, shared Deeper themes, and split-card tests. Layout coordinates alone do not synchronize the compositor hit-test scene; the old tests depended on an incidental render tick. Studio now drains the queued BringIntoView before rendering, rather than ticking before the queued work.
- Keep both Spiral language capture/change/restore scopes on the Avalonia dispatcher. Localization change subscribers update controls; the former outer worker-thread restore could race dispatcher work. Retain condition polling for the real help timer, using monotonic elapsed time instead of UTC.
- Park the section strip's real nine-second ambient timer during the exact sheen-count assertion, then retain the existing visibility/tier/attachment lifecycle assertions. A slow runner could previously execute an unsolicited sweep during RunJobs.
- Pin the split card's explicit performance tier by disabling automatic escalation from live overlay counts; restore that setting and the exact previous beat clock in finally. Keep its existing hand-stepped breath assertions.
- Shared Deeper press/toggle tests explicitly move the pointer onto the rendered control before pressing; preserve all original color, opacity, clock, and transition assertions.
- No assertions weakened, tests removed, new serialization, product changes, or ledger promotions. Existing assembly-wide isolation and sandbox profile remain in effect.
- Reference fetched: parity/trunk at a4ee52d40cd347d288dcf7fdaa8445b79f8e503. No trunk code imported; these changes adapt the existing base tests.

## Verification limits

- `dotnet build -c Release`: CCP.Core, CCP.Avalonia, CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, and all eight Linux Avalonia workflow test hosts (Consent, Language, CatalogueStartup, Migration, Scheduler, Report, GeneralSettings, Snapshot): exit 0, zero errors. Existing warnings remain.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,953 passed, zero failed, five existing skips (Vosk integration is unavailable here).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent from this base. No ledger rows changed.
- Before the builder correction, full-class runs demonstrated order dependence: one run passed 13/13 and another failed 13/13 with `Could not create glyphTypeface` for Segoe UI Black in `PremiumSpark.BuildLabel`. This is a bootstrap/font failure, before pointer assertions.
- WPF application and ConditioningControlPanel.Tests cross-build: exit 0, zero errors, using `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Windows code compiled, not run.
- After the builder correction, seeds 1, 2, and 3 passed all 13 full-class cases (39 passes, zero failures/skips). Existing assembly-wide serialization is preserved; separate test processes provide concurrent load with isolated profiles and evidence roots.
- Seeds 4–20 passed the eight specifically named cases from isolated copies of the same built output, using `dotnet vstest ... --TestCaseFilter:<named methods>` and `CCP_TEST_ORDER_SEED=<seed>`.
- Negative control: temporarily changed the first shared round-button hover opacity in Styles.xaml from 0.85 to 0.84, compiled it, and ran the unchanged RoundButtons fact. Exit 1: expected 0.85, actual 0.84. The source was restored immediately afterward; no mutation is included in the commit.
- All 20 seeded runs passed: each of the eight named facts passed 20/20, with zero failures or skips (175 total cases including the extra full-class cases). The restored RoundButtons fact also passed, exit 0; restoration build exit 0, zero errors.
- Full-class `dotnet vstest ... --Parallel --TestCaseFilter:<five classes>`: exit 0, 13 passed, zero failed/skipped. This preserves the existing assembly-wide collection serialization; concurrent processes provide independent load.

| Named test group | Seeded passes per fact |
| --- | ---: |
| SpiralHelpPopoverTests: both Mounted facts | 20/20 each |
| StudioRackWireTests: HapticsRowRightClick | 20/20 |
| SectionStripDepthFxShellTests: HoverLifts | 20/20 |
| SharedDeeperThemeTests: RoundButtons, CheckedIcon, ActionSecondary | 20/20 each |
| SplitFeatureCardFxTests: MotionAndTierGate | 20/20 |

## Assumptions

- Retain the existing assembly-wide DisableTestParallelization policy: adding a collection would not improve isolation. Full-class runs use the repository's existing runner configuration.
- Keep WPF/product behavior unchanged; fix synchronization and inputs owned by tests.

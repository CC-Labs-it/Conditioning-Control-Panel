# chaos-core-a: move the first pure group of Services/Chaos into CCP.Core

Base: `avalonia-port/lang-italian`. Unit A of the Services/Chaos Core move.

## Already in CCP.Core before this unit (untouched)
ChaosBench, ChaosCraftingIds, ChaosCrashSentinel, ChaosLessons, ChaosLifetimeBoons, ChaosMetaState,
ChaosMetaStore, ChaosModels, ChaosNarrativeModels, ChaosRanks, ChaosRevealService, ChaosRunEffects,
ChaosRunEngine, ChaosTuning, ChaosUpgrades, DtrhLoomStore. None of these still exists in the WPF head, so
there are no two copies to route.

## What changed
- `git mv` ConditioningControlPanel/Services/Chaos -> CCP.Core/Services/Chaos (namespace unchanged, so
  WPF callers in BubbleService, OverlayService, BubblePopFeatureControl and DtrhHostService compile as-is
  through the existing ProjectReference/InternalsVisibleTo):
  - `BrainDrainBubble.cs`, `MagnetBubble.cs` (pure bubble rules; `MotionLevel` already lives in Core)
  - `DtrhRunCloseRule.cs`, `DtrhRunPayoutRule.cs` (pure)
  - `DtrhAssetStatsStore.cs`, `DtrhSessionStatsStore.cs`: the only edit is `App.Logger?.Warning` ->
    Serilog `Log.Warning` (WPF's App.Logger is the same Serilog pipeline); paths were already `CorePaths.UserData`.
- Tests `git mv`'d to Tests/CCP.Core.Tests (namespace -> `CCP.Core.Tests`): DtrhRunCloseRuleTests,
  DtrhRunPayoutRuleTests, MagnetBubbleTests, BrainDrainBubbleTests.
- The two MagnetBubble facts that check against `ChaosBubbleVariants` (still WPF: it builds Media colours)
  stay in the WPF test project as `MagnetBubbleVariantsTests.cs`, unchanged.
- New `Tests/CCP.Core.Tests/DtrhStatsStoresTests.cs`: the stores had no tests; it covers TopAssets ranking
  (grabs outrank attention), negative deltas never subtract, and session totals summing while BestCombo maxes.
  It runs in the assembly's CCP_USERDATA_DIR sandbox.
- No embedded resources moved, so no core-guards baseline entry is needed.
- Trunk reference: parity/trunk put the same files in Core in b13ef6048 (BrainDrainBubble),
  4a6a25811 (DtrhAssetStatsStore) and 6054461f4 (DtrhSessionStatsStore). Our base already had the WPF files,
  so I moved those with history instead of importing trunk's copies; trunk's `public` widening and PORTED
  header were not taken.

## Next units (remaining Services/Chaos)
- **chaos-core-b (small App seams):** ChaosBubbleHints (App.Bubbles -> Action seam), ChaosNarrativeHooks /
  ChaosNarrativeDirector / ChaosLessonHooks / ChaosHappyPath (App.Settings -> CoreSettings, App.* providers),
  HtLinkPool (settings + logger).
- **chaos-core-c (DtRH content):** DtrhModContent, DtrhAssetManifest (Fyp.Online dependency), DtrhMetaBridge
  (needs its Dispatcher use behind a seam).
- **chaos-core-d (WPF colour split):** ChaosBubbleVariants (separate the spec table from System.Windows.Media
  colours), then EffectPayload + EffectPayloadFactory (36 App.* calls -> payload host seam).
- **Head-specific, stay per head:** ChaosArt, ChaosStoryCards, ChaosTips (WPF imaging/controls), ChaosNarrator,
  ChaosSfx (NAudio), DtrhSpike, DtrhHostService, CaucusHostService, LoomHostService (WebView2),
  ChaosModeService (shell orchestration). Their pure rule fragments should be peeled off as they get ported.
- Trunk-only Core files with no WPF counterpart on our base (ChaosVideoSegment, DtrhBarkRouter) are feature
  imports, not moves; they belong with the Avalonia DtRH host unit.

## Verification limits
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1274 passed, 5 skipped, 2 failed. The two failures
  (ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted..., LoopbackOAuthTests.GoodState_OnEitherLoopback...
  `[::1]`) fail identically on the untouched base in this container: it runs as root, which defeats the
  read-only folder check, and it has no IPv6 loopback. The moved and new tests: 116/116 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter Chaos`: 11/11 passed.
- Built OK: CCP.Avalonia and every Linux test csproj in build.yml (Core.Settings, Core.Content, Avalonia.Tests,
  Consent, Language, CatalogueStartup, Scheduler, Report, GeneralSettings, Snapshot).
- WPF: `ConditioningControlPanel.Tests` (and the WPF app it references) built with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. It compiled but
  was not run (needs Windows), so `MagnetBubbleVariantsTests` and the WPF callers were compiled, not executed.
- SDK: 10.0.112 from Ubuntu's `dotnet-sdk-10.0` package. The dot.net install host is blocked by egress policy here.

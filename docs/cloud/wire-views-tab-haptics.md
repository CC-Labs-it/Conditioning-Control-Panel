# wire-views-tab-haptics: Haptics page side-art column + previews stop on hide and panic

Ledger row `views-tab-haptics`: stub -> **wired**. The ledger had two `views-tab-haptics` rows: a stale "stub" row from haptics-core and a "wired" row from rows-haptics-tab. They are merged into one row, keeping the evidence from both. The one WPF item still missing from the page was the Tier-1 side-art column.

- **Tier-1 side-art column restored** (`CCP.Avalonia/Views/Tabs/HapticsTabView.axaml`, matching WPF `HapticsTabView.xaml:901`):
  - the 3*/2* split, with MaxWidth 780 on the settings column;
  - the `HapticsSideArt` plate with its bottom scrim;
  - `ApplyFeatureArt` paints features/vibe.png onto the plate, using the mod's copy first (same as the hero and the sync thumbnail).
- **`AdaptiveSideArt` ported to this head** (`CCP.Avalonia/Views/Features/AdaptiveSideArt.cs`). It works like WPF `Features/AdaptiveSideArt.cs`: below `CollapseBelow` it sets the last column to 0, hides that column's children, and lifts the settings column's MaxWidth. All three are restored when the page widens again.
- **Width rule moved to Core.** The pure #1321 hysteresis rule (`ShouldCollapse` / `WidenMargin`) now lives in `CCP.Core/Helpers/SideArtCollapse.cs`. WPF `AdaptiveSideArt` delegates to it, so there is one copy.
- **Imported trunk `5acf94a5f` (HC10), haptics part only.** The DeeperHub and SessionIO parts were left out because they are other lanes.
  - Toy Test and pattern Play now share one page CancellationTokenSource, which ends when the page hides or leaves the tree.
  - `HapticService.PanicStop` (Core, so WPF benefits too) also cancels a per-toy test. That test drives the provider around the mixer, so the mixer's own panic could not stop it.
  - Panic already reaches this through the existing PanicSurfaces "haptics" entry, so no new surface was added.
- **Tests:**
  - `HapticsHeadTests.Tier1SideArtShowsWideAndFoldsBelowTheThreshold` (new);
  - `HapticsHeadTests.PagePreviewStopsOnHideAndToyTestStopsOnPanic` (from trunk);
  - `Tests/CCP.Core.Tests/SideArtCollapseTests.cs` (new).

## Verification limits

- Builds: `dotnet build` succeeded for CCP.Core, CCP.Avalonia, and these test projects: CCP.Core.Tests, Core.Settings, Core.Content, CCP.Avalonia.Tests, Consent, Language, CatalogueStartup, Scheduler, Report, GeneralSettings and Snapshot. The WPF test project also built (`-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`), which compiles the edited WPF `AdaptiveSideArt.cs`. The WPF head itself was not run, because it is Windows-only.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter HapticsHeadTests|PanicSurfaces|HapticsSetup|StudioTab`: 12/12 passed.
- `dotnet test Tests/CCP.Core.Tests`: 2782 passed, 6 skipped, 2 failed. Both failures come from the sandbox, and neither test touches files in this diff:
  - `LoopbackOAuthTests.GoodState_OnEitherLoopback...("[::1]")` fails because there is no IPv6 loopback here.
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` fails because the tests run as root, which defeats the "cannot delete" setup.
- Fail-proven:
  - With `CollapseBelow` removed from the page, the side-art test fails at the narrow width.
  - With the old `PanicStop` restored, the HC10 test's panic assertion fails.
- Render: `--render-all` produced `HapticsTabView.png` (1100x780). At that width the page's centred StackPanel makes the Tier-1 region narrower than 1000px, so the plate is folded and the settings use the full width. WPF's rule would also fold it at that width. The headless test shows the plate at 1200px.
- The other 23 WPF views that use `AdaptiveSideArt` still drop their art column. They stay under ledger row `feat-adaptive-side-art`, which this branch does not edit.
- No real toy or Intiface was used; only the in-process Mock provider.

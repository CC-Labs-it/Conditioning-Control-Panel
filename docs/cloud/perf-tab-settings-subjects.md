- Fix section palette publication on navigation: the eight Color/Brush resource writes each caused a full Avalonia resource walk, including every hidden tab. Publish one complete palette through an application-owned resource provider and skip an unchanged palette. Keep the same keys, colors and brush values.
- Cache only the static image plates in dashboard FeatureCard/SplitFeatureCard controls. Previously every show resampled the full-resolution pictures at HighQuality; text, interaction, glows and ambient animation remain outside the caches. Replace a plate's cache when artwork changes, including changes while hidden; otherwise Avalonia 12.1 can retain the old picture.
- Add focused notification and rendered-pixel regressions. The palette guard failed before the fix (expected one notification, actual eight). The picture guard failed without caches; pixel checks also caught stale hidden-tab artwork before explicit invalidation was added. Check artwork replacement, resizing and split hover clips. Extend existing test isolation to remove the palette provider; no existing assertions changed.
- Read the WPF navigation, tab views, section painting and transition paths. Both heads already retain tab instances; the fix does not defer content, change navigation, remove effects, or introduce an entrance animation to reduce the measured work.
- Import source: fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Base is `aa2dcc1f0413794d7e36abfcaa7272288c4fe6fe`.

## Verification limits

Linux measurements use .NET SDK 10.0.402/runtime 10.0.13, Release, an isolated 1600x1000 dummy Xorg display, and fresh `CCP_USERDATA_DIR` sandboxes. No real desktop or profile was used. Each benchmark discards one warm-up and records 20 cycles. These are single Linux runs, not the five paired Windows runs in the task; they do not establish the Windows median/p95 or memory gates.

Two-tab command: `dotnet run --no-build --project CCP.Avalonia -c Release -- --perf-bench <out.json> --perf-bench-tabs SettingsTab,AvailableSubjectsTab` (exit 0 before and after):

| Metric | Base | Final |
| --- | ---: | ---: |
| SettingsTab median_ms | 173.03 | 107.06 |
| SettingsTab p95_ms | 252.28 | 148.34 |
| AvailableSubjectsTab median_ms | 183.48 | 106.79 |
| AvailableSubjectsTab p95_ms | 234.18 | 169.54 |
| working_set_mb | 740.0 | 721.7 |

The normal full-tab order was also measured under matching `dotnet-trace collect --profile dotnet-sampled-thread-time --format Speedscope` captures (the same benchmark without `--perf-bench-tabs`, both exit 0):

| Metric | Base | Final |
| --- | ---: | ---: |
| SettingsTab median_ms | 205.19 | 179.33 |
| SettingsTab p95_ms | 344.33 | 225.01 |
| AvailableSubjectsTab median_ms | 146.00 | 98.11 |
| AvailableSubjectsTab p95_ms | 165.00 | 146.82 |
| working_set_mb | 891.9 | 887.6 |

Sampled inclusive time under `PaintSectionInk` across the full run fell from 34.26 s to 5.03 s. Temporary per-phase instrumentation put section-chrome synchronization at 28–70 ms before the fix; instrumentation was removed. The different full-order/two-tab values show that preceding tabs and render load matter; neither run is a Windows parity verdict. A temporary no-ambient diagnostic did not establish fog as the dominant cause, so ambient rendering was preserved.

Commands and results:

- `dotnet build <project> -c Release`: CCP.Core, CCP.Avalonia and every Linux test project named in build.yml compiled with zero errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Snapshot.Tests. Existing warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (existing warnings only). WPF and Windows-specific paths compiled but were not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. Initial read-only sandbox invocation could not create temporary files; rerunning with workspace execution permissions passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final exit 0, 3,339 passed, 5 existing Vosk skips, 3,344 total. The first full run had a DtrhRemoteMediaTests back-off assertion race; that class passed all 9 tests in isolation and the complete rerun passed. No Core/test assertions were changed. The Vosk model download is unavailable in this environment.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~TabSwitchArtCacheTests|FullyQualifiedName~TabSwitchPaletteTests'`: exit 0, 3 passed, 0 failed. Both guards were demonstrated red before their fixes. Pixel checks cover changed images while hidden, resizing, and a hovered split clip compared with uncached rendering.
- Existing related classes were attempted with `--no-build`/`--filter`: PerfBenchTests, DashboardFxLoopsTests, SplitFeatureCardFxTests, SectionPaintShellTests, TypeScaleReadabilityTests, FavoritesRailShellTests and SectionTabStripShellTests. The broader selection (including the new classes) exited 1: 11 failed, 3 passed, all failures from the existing font-creation/bootstrap problem. The unchanged base had already reproduced it in PerfBenchTests/DashboardFxLoopsTests (2 failed, 1 passed). New regressions pass in a fresh testhost using the shipped font builder. The existing UI classes therefore remain unvalidated here; the complete UI suite was not run. No assertion was weakened or disabled.

Windows WPF comparison and GPU behavior require the supervisor's Windows benchmark/rcheck. In particular, do not treat the Linux values as proof that SettingsTab/AvailableSubjectsTab meet the supplied Windows thresholds.

## Assumptions

- Keep the existing tab instances and all arrival side effects. Optimize the shared palette publication and the dashboard's static artwork rather than adding preloading, delayed content, network behavior, or new configuration.
- Preserve HighQuality interpolation when a picture cache is rebuilt. Cache only art, so overlays, text, motion gates, hit testing and split interaction retain their existing behavior.

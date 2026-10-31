# Programs / BlinkTrainer switch work (r72)

Checkout verification: `/workspace/Conditioning-Control-Panel`, clean `work` at
`aa9918833722f057079e455167cb3d1b14462340`. Fetched
`CC-Labs-it/Conditioning-Control-Panel` and created the delivery branch from
`7a78d5357b642858568f6005f50cb0b2fc2b85a4` (commit comparison, not branch-name comparison).
That exact base has the rendered-batch PerfBench endpoint but does **not** contain
`MainShellWindow.TabSwitchTiming.cs` or `CCP_TRACE_TAB_SWITCH`; this change adds opt-in tracing.

WPF comparison: `MainWindow.ProgramsTab.cs:509` defers hidden updates; Avalonia
already did that and already skipped unchanged repeat shows. WPF's
`MainWindow.TabNavigation.cs:1017` refreshes Blink settings and rebuilds folder cards
on entry too. Those suspected paths were not the dominant cost locally.
Avalonia section entry rebuilds pill controls and replaces globally referenced
section ink brushes; WPF's resource pipeline does not have Avalonia's shell-wide
DynamicResource rebinding cost. Retaining brushes and realized pill templates
reduces that synchronous work without changing the palette or entry animation.

Changes:
- `CCP.Avalonia/Views/Tabs/ProgramsTabView.axaml.cs:79`: reconcile browse rows in a
  retained observable collection; keep selection and unchanged chapter details.
- `CCP.Avalonia/Views/Tabs/BlinkTrainerTabView.axaml.cs:412`: retain localization
  bindings and unchanged mix/consent paint. At `:463`, retain folder cards by their
  displayed content; rescan every refresh so external file changes remain visible.
- `CCP.Avalonia/Views/Controls/SectionTabStrip.cs:249`: reuse realized section pills;
  language, tier/eligibility changes and explicit Rebuild invalidate the cache.
- `CCP.Avalonia/Views/Windows/MainShellWindow.SectionChrome.cs:182`: mutate retained
  ink brushes, still publishing all four Color resources together once.
- `Tests/CCP.Avalonia.Tests/TabVisualReuseTests.cs:19`: guards for pill/tier,
  brush/color, row/selection/chapter, repeat-show card identity, external file count
  changes and adding another folder without rebuilding its neighbor.
- Three existing headless fixtures now use the production font builder; all their
  assertions remain intact. A bare builder could initialize the process without
  Inter and make the mixed suite fail depending on which fixture ran first.

Historical Linux headless measurements: .NET 10.0.403, Release, full application fonts,
21 alternating switches, first discarded; 20 samples per tab. Same tracing/probe
on base and final. Each cell is **median / p95 milliseconds** (nearest-rank p95).
Trace-file overhead is included. `layout-jobs` includes queued dispatcher work;
`render` forces a headless tick and screenshot capture, **not** PerfBench's desktop
rendered-batch latency. Animated CPU screenshot timings are noisy and regressed;
these data do not establish Windows acceptance.

Source key: **nav** = `CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs`;
**blink** = `CCP.Avalonia/Views/Tabs/BlinkTrainerTabView.axaml.cs`;
**probe** = `Tests/CCP.Avalonia.Tests/TabSwitchWorkTests.cs`.

| Tab / stage | Source | Before | After |
|---|---|---:|---:|
| programs / visibility | nav:175 | 0.473 / 2.108 | 0.419 / 1.568 |
| programs / entrance | nav:179 | 0.062 / 0.151 | 0.056 / 0.093 |
| programs / rail | nav:184 | 0.167 / 0.358 | 0.154 / 0.374 |
| programs / fx | nav:186 | 0.011 / 0.032 | 0.009 / 0.024 |
| programs / section | nav:188 | 15.503 / 33.649 | 11.039 / 19.650 |
| programs / chrome | nav:193 | 0.060 / 0.106 | 0.050 / 0.081 |
| programs / on-shown | nav:195 | 0.074 / 0.151 | 0.066 / 0.086 |
| ProgramsTab / layout-jobs | probe:32 | 62.851 / 79.807 | 53.213 / 56.779 |
| ProgramsTab / render | probe:35 | 312.500 / 801.266 | 323.150 / 860.632 |
| blink-refresh / settings | blink:148 | 0.075 / 0.210 | 0.042 / 0.104 |
| blink-refresh / folders | blink:164 | 0.015 / 0.034 | 0.015 / 0.030 |
| blink-refresh / status | blink:166 | 0.416 / 0.832 | 0.327 / 0.599 |
| blink-refresh / stage | blink:171 | 0.029 / 0.065 | 0.028 / 0.047 |
| blinktrainer / visibility | nav:175 | 0.884 / 1.585 | 0.736 / 1.193 |
| blinktrainer / entrance | nav:179 | 0.041 / 0.080 | 0.039 / 0.109 |
| blinktrainer / rail | nav:184 | 0.136 / 0.376 | 0.141 / 0.251 |
| blinktrainer / fx | nav:186 | 0.010 / 0.026 | 0.013 / 0.042 |
| blinktrainer / section | nav:188 | 17.151 / 53.968 | 11.027 / 17.741 |
| blinktrainer / chrome | nav:193 | 0.065 / 0.228 | 0.042 / 0.065 |
| blinktrainer / on-shown | nav:195 | 0.084 / 0.148 | 0.081 / 0.148 |
| BlinkTrainerTab / layout-jobs | probe:32 | 68.690 / 107.035 | 58.130 / 65.522 |
| BlinkTrainerTab / render | probe:35 | 349.873 / 1038.726 | 361.822 / 1788.135 |

Programs performs zero browse-row mutations on unchanged repeat show/refresh;
Blink performs zero folder-card builds on unchanged repeat show (guarded with a
nonempty folder, not just an empty-library case). There are no new deferrals,
WPF changes, reduced-quality effects or benchmark endpoint/threshold changes.

Historical probe command (7a78d535 measurement tree):
`CCP_TRACE_TAB_SWITCH=1 CCP_TRACE_TAB_SWITCH_FILE=/tmp/ccp-after-isolated.txt dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~TabSwitchWorkTests`.
Baseline: 1 passed, 33.2334 seconds; after: 1 passed, 32.7702 seconds. Historical focused suite: 17 passed, 44.2476 seconds; `git diff --check`: exit 0.

Windows numbers come from the PR's **perf-bench-windows** CI artifact. They were
not measured here. Both median and p95 must satisfy WPF x 1.10 + 20 ms under
`docs/avalonia-perf.html`; local headless stage costs are not a substitute.

Prior trace-output review: console/file sinks are best-effort and disabled after failure.
`TabTraceFailureTests` failed 2/2 on 3ac8b507b with DirectoryNotFoundException; the fixed pre-scope suite passed 19/19 (22.7113 seconds). WPF reference: `MainWindow.TabNavigation.cs:213` has no diagnostic-file dependency.

Final scope correction: restored the complete `74ac6ce3d` snapshot outside this
focused lane, including WPF V1/V2/Rain behavior, its original tests/assets/settings,
and all 19 keys in each of ten languages. This is a corrective commit on top of
8a44247de, with no history rewrite. The earlier inherited-diff exemption was wrong.
The table above is historical evidence from the 7a78d535 measurement tree; Windows
acceptance and final-tree performance remain unverified. Base 74ac6ce3d does not
contain the later perf-bench-windows workflow; obtain that artifact from the
Windows bench integration rather than restoring unrelated CI/WPF benchmark edits.

Final checks: `git diff --quiet 74ac6ce3d 8a44247de -- ConditioningControlPanel Tests/ConditioningControlPanel.Tests CCP.Core Assets .github` exits 1 (fail-proof); the same command without 8a44247de exits 0 on the corrected tree.
Budget guard: reviewed +1277/-6862 = 8139 (FAIL); corrected cumulative +577/-22 = 599 (PASS). All ten language JSON files parse and match base bytes; each regains 19 keys.
`/tmp/ccp-dotnet/dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~TabTraceFailureTests|FullyQualifiedName~TabVisualReuseTests|FullyQualifiedName~ProgramsBrowseTests|FullyQualifiedName~BlinkTrainerTabLiveTests|FullyQualifiedName~SectionTabStripShellTests'` → 15 passed, exit 0, 17.8984 seconds.
`node --test Assets/web/rain-wardrobe/tests/*.test.mjs` → 21 passed, 0 failed; `bash .github/scripts/core-guards.sh` → All Core guards passed.
`/tmp/ccp-dotnet/dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` → exit 0, 571 warnings, 0 errors; WPF suites compiled, not executed on Linux.
CI #3790 reproduction: the unchanged Skia source guard rejected the overlong TabSwitchWorkTests builder chain; local full-shard runs also exposed missing-font setup order and merged-palette corruption (Companion header expected #CBB9FC, got #A3ABFC).
Fix: concise production builder with adjacent UseSkia/UseHeadless in the probe; assembly-level production-font setup on the test dispatcher; reuse only application-owned brush overrides (`TryGetValue`), preserving merged theme defaults. WPF reference: `MainWindow.SectionChrome.cs:194` replaces app brush keys without mutating the merged palette. Existing assertions and CI filters are unchanged; startup and merged-palette assertions were added.
`LD_LIBRARY_PATH=/tmp/ccp-vlc/usr/lib/x86_64-linux-gnu VLC_PLUGIN_PATH=/tmp/ccp-vlc/usr/lib/x86_64-linux-gnu/vlc/plugins PATH=/tmp/ccp-dotnet:$PATH bash .github/scripts/avalonia-tests-sharded.sh --shard 3` → exit 0: 416 passed, 0 failed, 3 pre-existing conditional skips, 419 total (7 m 36 s). Fail-proof before the final probe repair: 415 passed, 1 unchanged source-guard failure, 3 skips; that guard passes after repair. VLC is extracted locally to match CI dependencies, with no native-video exclusion.
`dotnet build Tests/CCP.Avalonia.Tests -c Release -m:1 -nodeReuse:false` → 0 errors; targeted TabVisualReuse/TypeScaleReadability/native-video checks → 7 passed (15.2550 seconds); `avalonia-tests-sharded.sh --expected` → 2086 tests. Windows execution is left to CI; GitHub log API access here returns Forbidden.

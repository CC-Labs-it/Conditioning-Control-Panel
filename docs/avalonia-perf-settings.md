# SettingsTab switch: local stage trace

Base: `dd0b812769dd4e7b99e01a36fe071d9f22d291b2` (r72 shared-cost branch).
Linux cloud, .NET SDK 10.0.100, Release, Skia headless with real drawing.
One warm-up plus 20 samples, alternating progression and settings; three fixed favorites,
empty recents, no active session. The same fixture and probes were used on both versions.
Baseline carries only timing/work-count instrumentation; no optimized behavior.
Stage percentiles are independent and must not be summed.

Windows baseline #3167: Avalonia median 99.7 ms, WPF 18.4 ms (limit about 40 ms).
**Windows numbers come from the PR's `perf-bench-windows` CI artifact.**
These local headless numbers do not establish the Windows median/p95 gate
(Avalonia <= WPF x 1.10 + 20 ms for each). Headless rendering remains the dominant
cost; the Windows target is still subject to that CI result.

## Evidence and change

WPF SettingsTabView.xaml.cs:49 forwards visibility to
MainWindow.TabNavigation.cs:982, which resettles the browser fold and queues startup
cards. Avalonia SettingsTabView.axaml.cs:63 has the equivalent hook; its FX canvas
already composes once. WPF MainWindow.TabNavigation.cs:386 also refreshes the rail,
session lock and intake tile on entry. Inspection did not find a unique Avalonia
visibility hook responsible for the measured synchronous cost: the expensive
repeat work is in the corresponding refresh implementations.

- MainShellWindow.FavoritesRail.cs:75 now reconciles both stacks by destination and
  rendered state instead of clearing/rebuilding them. Reordering retains buttons,
  handlers, art and context menus; only changed/retired rows are replaced/removed.
  Localized label/context/tip, glyph and gate state are checked every refresh.
  Mod changes invalidate art via a revision; subscriptions are removed on close.
  Caches retain only displayed rows in each stack.
- MainShellWindow.TabNavigation.cs:224 re-derives the session lock on Settings entry
  and uses the existing successful-paint guard for unchanged unlocked state.
  Active locks still force repaint, including changed program reasons. This cuts
  the repeated logical-tree sweep of off-screen rack/tabs while unlocked.
- Intake, browser folding, visibility, FX and WPF are unchanged. No work is
  hidden by moving the benchmark endpoint or changing the frame implementation.

## Per-stage costs

All entries are median / p95 in milliseconds, before -> after. Through-render
includes ShowTab, layout, frame scheduling and completion of the rendered batch.
Layout is measured explicitly before awaiting that same endpoint.

| Stage | Before median / p95 | After median / p95 | Source at this commit |
|---|---:|---:|---|
| recent | 0.007 / 0.011 | 0.007 / 0.011 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:147 |
| bark-and-routing | 0.006 / 0.011 | 0.006 / 0.011 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:169 |
| visibility | 0.045 / 0.141 | 0.053 / 0.158 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:179 |
| tab-transition | 0.021 / 0.071 | 0.019 / 0.074 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:182 |
| history | 0.009 / 0.016 | 0.009 / 0.015 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:186 |
| nav-rail | 0.008 / 0.012 | 0.008 / 0.013 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:188 |
| ambient-fx | 0.010 / 0.023 | 0.010 / 0.020 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:190 |
| section-chrome | 0.052 / 0.116 | 0.053 / 0.102 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:192 |
| tooltip | 0.007 / 0.011 | 0.006 / 0.011 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:197 |
| entitlements | 0.070 / 0.173 | 0.064 / 0.094 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:211 |
| settings-favorites | 0.841 / 2.508 | 0.105 / 0.282 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:220 |
| settings-session-lock | 1.859 / 4.308 | 0.002 / 0.003 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:225 |
| settings-intake | 0.017 / 0.049 | 0.013 / 0.020 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:227 |
| settings-intro | 0.009 / 0.016 | 0.004 / 0.006 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:229 |
| tab-hooks | 0.000 / 0.002 | 0.000 / 0.000 | CCP.Avalonia/Views/Windows/MainShellWindow.TabNavigation.cs:199 |
| show-tab | 2.967 / 7.200 | 0.406 / 0.592 | CCP.Avalonia/Views/Windows/MainShellWindow.TabSwitchTiming.cs:48 |
| through-render | 281.898 / 324.255 | 265.118 / 301.450 | Tests/CCP.Avalonia.Tests/SettingsSwitchTests.cs:61 |
| layout | 0.972 / 2.723 | 0.017 / 0.025 | Tests/CCP.Avalonia.Tests/SettingsSwitchTests.cs:59 |

## Regression and reproduction

The expanded FavoritesRailShellTests.VisitPinAndClickTravelThroughTheRealRail
asserts zero additional chip builds and identical visuals on repeat show; it also
checks pinned retention, recent reordering, padlock refresh and click navigation.
On the original path it fails at line 71: expected 2 builds, actual 5.
SessionFeatureLockProgramTests additionally checks changed program reasons on
Settings entry and unlock after a stopped run.

The new timing fixture is opt-in; no machine-dependent latency assertions are
used as unit-test thresholds. Existing tests and the benchmark endpoint are retained.

Commands (SDK installed under /tmp because the image had no dotnet):

```sh
CCP_TRACE_TAB_SWITCH=1 /tmp/ccp-dotnet/dotnet test   Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-restore   --filter 'FullyQualifiedName~SettingsSwitchTests'   --logger 'console;verbosity=detailed'
```

Before/after logs were captured in /tmp/ccp-settings-baseline.log and
/tmp/ccp-settings-after.log. The baseline run also selected the new repeat-show
guard to demonstrate its failure on the original implementation.

The affected-suite command was:

```sh
CCP_TRACE_TAB_SWITCH=1 /tmp/ccp-dotnet/dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SettingsSwitchTests|FullyQualifiedName~FavoritesRailShellTests|FullyQualifiedName~SessionFeatureLockProgramTests|FullyQualifiedName~ProgramTodayCardTests|FullyQualifiedName~DashboardFoldBillboardTests|FullyQualifiedName~PerfBenchFrameTests|FullyQualifiedName~PerfBenchTests" --logger "console;verbosity=detailed" > /tmp/ccp-settings-after.log 2>&1
tail -5 /tmp/ccp-settings-after.log
# Test Run Successful.
# Total tests: 13
#      Passed: 13
#  Total time: 1.0626 Minutes
git diff --check
# no output, exit 0
```

The release build completed with existing nullable, Avalonia and analyzer warnings.
WPF files were not changed. The expanded guard failed on the baseline and passed
on the optimized path; the Windows comparison has not been run in this Linux image.

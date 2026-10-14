# wire-shell-enhancements

- Added all four WPF Tier 6 PRO analytics expanders to the existing Enhancements header: Lifetime Dashboard, Season Rewind, Bestie Records and Brain Drain Report. They require ownership, build content on first expansion, reuse that content on reopening, and support Enter/Space as well as pointer input.
- Ported the activity calendar, minutes bar chart, peak-level sparkline, capped history table (schema-aware values and certified spent column), personal bests and season/lifetime engagement bars. Kept the existing header's single vertical scroller; Fluent icons replace WPF's emoji icons.
- Moved season aggregation, snapshot caps, feature totals/order, best-record selection, time/month formatting, percentile and server-key fallback into Core `EnhancementAnalytics`. WPF calls that same implementation.
- Added focused Core and headless Avalonia tests for historical/live data, schema and caps, ownership gates, lazy construction, keyboard reuse, chart data and certified-column gating. No new timer, window or panic surface.
- No parity/trunk code imported; read-only reference fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Rendering adapted from the WPF reference on base `0e1aa0473e7a5b6120544923e94bb04cc62a7e50`.
- The `shell-enhancements` ledger row remains **stub** with named follow-up `wire-shell-enhancements-b`; no other row changed.

## Verification limits

- `dotnet build CCP.Core`, `dotnet build CCP.Avalonia` and all 11 Linux test projects named by `.github/workflows/build.yml` (`-c Release -m:1 -p:UseSharedCompilation=false`): passed, zero errors. Project build tails are recorded locally in `/tmp/worker6-build-summary.txt`.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (same serial/hardlink properties): passed, zero errors; WPF and Windows test code compiled but not run.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~CCP.Avalonia.Tests.EnhancementsTabTests|FullyQualifiedName~CCP.Avalonia.Tests.EnhancementsAnalyticsTests'`: passed 2/2, zero skipped (final run 21 seconds).
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: **2812 passed, 1 failed, 5 skipped**, total 2818. The failure was unchanged `BoardTilesTests.A_frame_fits_the_budget` (28.70ms during parallel host load); its isolated retry (`--filter FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget`) passed 1/1. The five speech tests skipped because the Vosk model is unavailable; speech runtime is not proved here.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~EnhancementAnalyticsTests`: passed 2/2, zero skipped.
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/worker6-render`: passed, 224/224 views rendered, no binding-error reports.
- `bash .github/scripts/core-guards.sh`: all guards passed (424 XAML references checked).
- All UI/test/renderer execution uses sandbox profiles; `FONTCONFIG_FILE=/workspace/ccp-test-fonts.conf` supplies headless fonts and `DiffEngine_Disabled=true` is set for tests.
- The initial focused run without that font configuration failed to create Inter glyph typefaces. An initial parallel build attempt also hit MSBuild worker/bus errors; a later overlapping Avalonia build hit an obj DLL lock. Final checks use serial project builds and report actual results.
- Visual evidence: `/tmp/worker6-render/EnhancementsTabView.png` from `--render-all` shows the default tree. `/tmp/worker6-analytics.png` from the headless ownership test shows the expanded PRO Lifetime Dashboard and activity calendar; visually inspected.
- Windows runtime behavior cannot execute on this Linux host. Live authenticated purchases are not exercised or implemented in this slice.

## Remaining: wire-shell-enhancements-b

- Authenticated `PurchaseSkillAsync` backend, confirmation/purchase/error dialogs and disabling purchase cards during requests.
- `CelebrateEnhancementPurchase`, prestige rank-up burst, and trophy-case column refresh following purchase.
- Lucky-proc event source plus `OnLuckyProc`, reusable lucky toast realization/placement/hold/close behavior and panic registration for that added surface.

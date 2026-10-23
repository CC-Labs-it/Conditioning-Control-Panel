# fin-views-tab-enhancements

- Add the four Ditzy Data PRO expanders in WPF order, gated individually by ownership,
  collapsed initially and built only on first expansion. Preserve content on collapse,
  the one-time error fallback, colors, spacing, and the header's single vertical scroller.
  Enter/Space supplement WPF's left-button activation; icons use the existing Fluent controls.
- Port Lifetime counters and activity heatmap, Season Rewind charts/table, Bestie Records,
  and Brain Drain engagement bars. Reuse Core snapshots and achievement counters; put
  the WPF percentile calculation in Core. Preserve chart caps, snapshot ordering,
  live-bucket exclusion, schema-1 unknown values, capstone Spent, rounding and empty states.
- Add focused headless tests for each ownership gate, lazy build/reuse/failure, order,
  lifetime fields/heatmap geometry, rewind schema/capstone/charts/deltas/history cap,
  records, feature sums/bars and empty states; add Core percentile boundary tests.
- Replace the duplicated historical prose in the ledger row with current evidence and
  an explicit remaining-gap list. The row remains `stub`.
- Imports: none. Panels were adapted from the frozen WPF reference at
  `888cdbffe4d2ecf3967cee4a4334557ee533e54d` (MainWindow.Enhancements.cs:1063–1761).
  Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`;
  purchasing commit `d72f48cc1b334bb4dd8ac1dc1589688f5190124f` was assessed, not imported.

## Gap analysis and scope

At the pinned base, the cited WPF view's wheel redirect, named fields, login veil,
header/tree/rail and art/FX were present. The live callers in WPF
MainWindow.Enhancements.cs:545–550 and :2010 showed two outstanding feature groups:

1. All four PRO expanders and their analytics/charts. This branch implements these.
2. Purchases on ordinary nodes and revealed secret cards: confirmation copy and permanent
   note, card disablement, server-authoritative receipt/refusal, auth recovery, balance
   sync/retry, local skill effects/events, trophy column refresh and purchase/prestige FX.
   The purchasable cursor belongs with this path and remains absent.

The existing mod-text difference is retained: this head localizes before MakeModAware,
where WPF supplies raw English name/flavour/description when its mod service is present.
The older row's glow/hover/art/secret-card omissions were superseded on this base.
Lucky proc toasts belong to the broader shell-enhancements row, not this view's caller.

Purchasing needs the separate SkillPurchase/ProfileSync port and its application hooks.
The assessed trunk purchase change alone adds 707 lines including focused tests, before
adapting it to this base; adding the full analytics port would exceed this unit's 1,000
added-line cap. This branch prioritizes the four complete visible analytics panels and
honestly retains `stub`; it does not claim a pure-dependency NOOP or dead WPF callers.

## Verification limits

- SDK installed: .NET 10.0.402.
- `dotnet build -c Release` passed (exit 0, 0 errors) for CCP.Core, CCP.Avalonia and
  every Linux test project listed in `.github/workflows/build.yml`: CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests and the Avalonia
  CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler,
  and Snapshot test assemblies. Generated outputs shared native assets with hardlinks
  after the first sweep exhausted temporary disk; the complete sweep then passed.
- WPF test project compiled (exit 0, 0 errors) with `EnableWindowsTargeting=true` and
  `ValidateExecutableReferencesMatchSelfContained=false`. Windows code was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- With `DiffEngine_Disabled=true`, full `dotnet test Tests/CCP.Core.Tests -c Release
  --no-build`: exit 1; 3,553 passed, 5 skipped, 1 failed. The failure is the unchanged
  DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls
  assertion (expected Loops, actual Clips). The Vosk-dependent tests skip because the
  model is unavailable; its download is blocked in this environment. The failed media
  test passed in isolation (exit 0, 1 passed), so the full-suite result remains a
  recorded order/timing failure; no unrelated test or product code was changed.
- Focused Core filter `SeasonRecapMetricsTests|SkillTreeRulesTests`: exit 0;
  12 passed, 0 failed/skipped.
- Avalonia test assemblies compiled; runtime tests were not run under the supplied
  cloud font limitation (cloud-test-fonts is absent). rcheck must run the filter
  `FullyQualifiedName~EnhancementsProTests|FullyQualifiedName~EnhancementsTabTests`.
- `--render-view EnhancementsTabView`: exit 0, 1100x780 image inspected. A disposable
  external harness using RenderProof's real app/font setup opened all four PRO toggles
  over an isolated synthetic profile and rendered each panel: exit 0. Heatmap/counters,
  trend charts/table, records and feature bars were visually inspected. No render-all
  or live desktop/server purchase was run. Inline panels add no separate window,
  overlay or active resource, so no new panic-surface registration is needed.

## Assumptions

- Respect the added-line cap by completing analytics first and carrying the purchase
  feature explicitly; preserve existing localized mod text in this unit.
- Use the existing tab/header scrolling and Core snapshot store; do not invent another
  analytics service, configuration layer or window.

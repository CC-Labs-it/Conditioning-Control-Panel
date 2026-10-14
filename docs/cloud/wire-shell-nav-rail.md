- Ports WPF's vivid diagonal hue bevel to the real section rail. Idle/hover rings are raised (light at top-left), selected rings reverse into a socket lip; opacity remains CC/F2/FF and thickness 3/3.5 px.
- Shares the authored ring constants/math in Core `NavRailPaintRules`, reusing existing `NavStripPaint` HSL/color helpers. WPF `NavRailRules` delegates rather than keeping another color implementation.
- Shares WPF's localization decision in Core `ModAwareLabelRules`: changed, nonempty English mod wording wins; unchanged or absent replacements retain the localized text. WPF `ModAwareLocText.Resolve` delegates.
- The live rail path binds section captions in caps and Back/Search/Web App captions without caps. Mod changes refresh bindings without losing language updates; close disposes bindings and drops already-queued mod callbacks. No new window, playback or perpetual animation is added.
- Focused tests exercise real shell navigation, mod events and localization changes. Existing nav test setup uses the production font builder; assertions are unchanged.
- `parity/trunk` was fetched read-only; no code was imported from it. WPF NavRail/NavRailRules/ModAwareLocText are the behavior specification.
- Ledger row `shell-nav-rail` remains **stub**, with named follow-up **wire-shell-nav-rail-b**: coin disc/dish/socket/lip/outer/inner/specular parts; press travel/release spring/tilt; rail shadow; Misroute effect writer and reroute seam; embedded per-mod art twins; Social Lobby badge producer; Web App move into the Play Games card. Those remaining depth/service/game integrations exceed the 1000-added-line cap together.

## Verification limits

- Negative proof on BASE: `NavRailRemainingTests` failed twice, expected THE NEST but saw HOME, and expected LinearGradientBrush but saw SolidColorBrush. The same tests pass after the implementation.
- Focused Avalonia filter `NavRailRemainingTests|NavRailPaintTests|NavSectionRailTests|NavRehomeShellTests`: **18 passed, 0 failed/skipped**.
- Full Core suite: **2821 passed, 2 failed, 0 skipped**. Failures were untouched scheduling/performance tests: `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` and `BoardTilesTests.A_frame_fits_the_budget` (28.46 ms while workers ran concurrently). No tests or limits were weakened.
- Focused Core rerun of both failures plus `NavRailRemainingRulesTests`: **9 passed, 0 failed/skipped**. Full Core used the shared Vosk model (`CCP_VOSK_MODEL=/workspace/shared/vosk/vosk-model-small-en-us-0.15`).
- `bash .github/scripts/core-guards.sh`: passed.
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/ccp-worker-3/nav-rail/render-all`: exit 0, **224/224** views rendered. `MainShellWindow.png` shows the new rail bevel; fake mod captions are tested headlessly rather than saved in the production render.

- WPF app and test cross-compilation passed with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (512 warnings, 0 errors). That command ran under `flock /workspace/shared/ccp-wpf-build.lock`; WPF `RailPolish11Tests` and `TicketFixes0924Tests` were compiled, not executed on this Linux host.

- Explicit `dotnet build` passed for CCP.Core, CCP.Avalonia and all 11 Linux test projects listed in `.github/workflows/build.yml`: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests. Commands used `-c Release -m:1 /p:UseSharedCompilation=false` and SDK hardlink copy properties; auxiliary builds used `-p:BuildProjectReferences=false` after Core/head builds. Only their generated bin folders were removed after successful compilation to conserve shared disk.
- Windows/native desktop execution was not run; the full Avalonia suite was not run.

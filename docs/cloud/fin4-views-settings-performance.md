- Ported the WPF Gentle visuals switch and conditional fade/speed editors, preserving choice order, culture formatting, nearest-choice seeding, immediate save, and loading guards.
- Applied the effective Reduced motion cap when gentle visuals is enabled, preserving Off and the stored motion selection. Reuses Core GentleVisuals; no new abstraction or window.
- Added a focused headless regression for editor seeding, conditional visibility, selection writes and live motion gating. Bundled Inter avoids dependence on installed Linux fonts.
- Replaced repeated historical parity text with one honest stub row and added numbered tester steps.
- Reference: WPF at pinned base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported from trunk.

## Gap audit and scope

The base already implements auto tier escalation, hardware decoding consumption and second-editor sync, marquee motion gating, lockdown-pulse stopping, ambient controls and DND picker/guards. Historical absence claims for those behaviors are obsolete.

The additional WPF Gentle visuals editors and effective motion cap were absent and are implemented here. Remaining gaps:

- Unified overlay routing: WPF `App.xaml.cs` CompositorEnabled is read by live OverlayService and BubbleService consumers (grep `UseCompositor` in those services at the pinned base). Avalonia only saves UnifiedOverlayHost. WPF compositor host/layers total 4,059 lines, including Windows capture/presentation; Core contains shared layer logic but there is no Avalonia host/routing twin. A complete host plus all routing consumers cannot fit the 1,000-added-line unit cap. P122 does not apply: WPF has live callers.
- Full chrome FX lifecycle: WPF CmbMotionLevel_SelectionChanged calls RefreshChromeFx; Avalonia's ChromeFx file documents the missing initialization and nav/START/XP loop funnel. Section edge, strip, drawer, fold arrow, marquee and lockdown stopping already listen to motion changes. This unit does not invent an empty refresh entry point for effects which have no running lifecycle.
- Gentle visuals safety rollout is incomplete beyond shared Core consumers and the central ambient gate: FlashOverlay still uses its own fade duration, and SectionTabStrip/HelpLoopView still read the raw motion setting. The new editor does not certify every overlay as gentle; completing head-specific safety playback/fade and motion consumers needs a separate surface-by-surface behavior/test pass.
- WPF honors Windows' animation preference; the portable gate still has no OS preference seam. Native Wayland foreground windows remain unavailable; Linux DND uses X11/XWayland, Windows uses user32.

The row remains stub. No new window, notification or overlay is introduced; existing shell panic ownership remains in force. This is not a pure dependency NOOP because the Gentle visuals user path was independently portable.

## Verification limits

- `dotnet build -c Release` passed (exit 0, zero errors) for CCP.Core, CCP.Avalonia and all 12 Linux-compatible test projects listed in build.yml: CCP.Avalonia.{CatalogueStartup,Consent,GeneralSettings,Language,Migration,Report,Scheduler,Snapshot}.Tests, CCP.Avalonia.Tests, CCP.Core.{Content,Settings}.Tests and CCP.Core.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` passed (exit 0, zero errors); WPF and Windows-only code compiled but were not run on Linux. Existing compiler warnings remain.
- `bash .github/scripts/core-guards.sh` passed (exit 0): All Core guards passed. The test-plan-coverage script is absent on the pinned base.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build` passed (exit 0): 3,953 passed, 5 skipped, 3,958 total. Vosk-dependent speech tests skipped because the model is unavailable; no download attempted (known environment restriction).
- Pre-fix regression: after selecting bundled Inter in the test host, the new test failed on the missing ChkGentleVisuals control (exit 1). Initial fontless attempt failed earlier on glyph creation and is not counted as behavior proof.
- Post-fix `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~PerformanceGentleVisualsTests` passed (exit 0): 1 passed, 0 failed. The test uses the existing isolated profile and a real sandbox SettingsService.
- Combined relevant filter (`PerformanceGentleVisualsTests|PerformanceSettingsMotionTests|PerformanceSettingsWiringTests|PerformanceDndPickerTests`, FullyQualifiedName contains each name, `--no-build`): exit 1, 6 failed. Existing classes initialize the process-wide host without bundled font mappings; glyphTypeface creation fails for Consolas/Segoe UI Black before behavior assertions. Do not treat these six as behavior verification; supervisor/GitHub must run them with fonts available.
- `git diff --check` passed. No render-all, real desktop, live media, Windows runtime or full Avalonia suite execution was performed. The new test renders its real controls through headless Skia and never uses the real user profile.

## Assumptions

- Keep the smallest WPF-compatible change: use the existing Core safety rules and settings, and leave large compositor/chrome dependencies explicit rather than simulating them.
- Preserve WPF's first-choice tie behavior and do not round persisted settings when opening the editor.

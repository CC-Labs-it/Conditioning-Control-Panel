# fin2-chaos-estim-overlay

- Base: `a65dbbe2f4935a4797b32efd87513054dd39e1d2`.
- Adapt WPF's hidden-window keep-alive contract: pre-create lightning and cursor halo after the countdown when `e_stim` is equipped; reuse them through strikes/arming and close both on run teardown or panic. No frame clock runs while pre-created windows idle.
- Add WPF's visible-only bolt-then-halo `RaiseActive` surface, using the existing platform overlay seam. It never unhides idle windows. The gameplay-layer video caller remains a dependency.
- Add focused tests for hidden pre-creation, reuse, clocks, refusal, re-stack ordering, boon gating and panic cleanup. Existing `chaos-estim` panic registration covers both windows; no new surface registration is needed.
- Replace the repetitive historical ledger entry with one accurate `stub` row. Existing multi-monitor stage selection and compositor-alpha fade were already present on the base.
- Read-only trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; adaptation uses the frozen WPF reference at the base SHA.

## WPF gap audit

Pinned reference: `a65dbbe2f4935a4797b32efd87513054dd39e1d2`.

1. `Chaos/ChaosEStimOverlay.cs:46` and `:329`: pre-create both hidden native windows. Closed here; `ChaosRunHost.BeginRun` follows `Services/Chaos/ChaosModeService.cs:453` and gates on the equipped boon.
2. `Chaos/ChaosEStimOverlay.cs:100` and halo `RaiseActive`: re-stack bolt then halo. Surface implemented here; the real caller at `ChaosModeService.cs:745-775` still needs the chaos mandatory-video gameplay-layer integration, including its pinning gate and video-start/click callbacks. Adding an unconditional video hook would change WPF's opt-out behavior.
3. `Services/BubbleService.cs:371-441`: genuine charged treat clicks select up to three nearest conductors within 600 DIPs; capstone uses breadth-first conduction (40-hit cap); hits stagger by 70 ms per hop; empty range preserves the charge. The head's field has no charged-pop/conduction feature. Its ordinary bubbles are present; the old assertion that the whole bubble field was missing was stale.
4. `BubbleService.cs:449-479` and `:1753`: free synergy discharges and rabbit discharge callers depend on the unported Electrified Rabbits / Body Buzz effects. These are live WPF paths, not dead code.
5. `ChaosModeService.cs:2765-2797`: E-Stim toy effect arms charges and halo; per-arc callback plays feedback, updates the banner and disarms on depletion. The head has toy buttons and cooldown rules but no E-Stim effect/charge tracking. Wiring only the halo to a button would leave it armed indefinitely without a real charge consumer.

The remaining work crosses charged bubble gameplay, toy effects and chaos mandatory-video integration. Keep the row `stub`; do not apply P122 or claim full parity. No device-output code changed. This is not a pure dependency NOOP: the independent pre-creation and overlay re-stack surface gaps were actionable.

## Verification limits

- SDK: .NET 10.0.100. Channel lookup failed with HTTP 403; the pinned SDK installation succeeded.
- Before implementation, the added lifecycle test failed compilation with 12 missing-API errors (`EnsureCreated`, `RaiseActive`, and test observability). This is compile-level red evidence, not a runtime fail-before proof.
- `dotnet build -c Release` succeeded (exit 0, zero errors) for `CCP.Core`, `CCP.Avalonia`, and all Linux workflow test projects: `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`, `CCP.Avalonia.Tests`, `CCP.Core.Content.Tests`, `CCP.Core.Settings.Tests`, and `CCP.Core.Tests`.
- An initial post-change compile hit MSBuild `MSB4166` (child worker exited prematurely); the subsequent required build succeeded with zero errors.
- `bash .github/scripts/core-guards.sh`: exit 0; all guards passed (3 existing baselined APIs). The first sandboxed attempt could not create temporary files; rerun with write access passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1, 3,952 passed / 1 failed / 5 existing Vosk skips, total 3,958. A full `--no-build` retry produced the same result. Failure: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`, line 185, expected `Loops`, actual `Clips`. Neither Core nor this test was modified.
- Focused Core run (`--no-build --filter 'FullyQualifiedName~ChaosEStimMathTests|FullyQualifiedName~DtrhRemoteMediaTests'`): exit 0; all 12 passed, no skips. The remote-media failure therefore persists in the full-suite context but not in isolation.
- WPF test cross-build: `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 warnings, zero errors. Compiled only; Windows tests were not run.
- `git diff --check`: exit 0.
- Avalonia headless tests were compiled, but not executed under the task's documented font-creation limitation. Supervisor should run `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ChaosEStimOverlayTests` with `DiffEngine_Disabled=true` on the font-capable runner. No test was disabled, removed or weakened.
- No desktop, multi-monitor, mixed-DPI, compositor, or native Windows run was possible here. Windows overlay code was compiled through the Avalonia build, not executed. No new render proof was generated; the existing sample constructor remains unchanged.

## Assumptions

- Match WPF's equipped-boon gate and post-countdown pre-creation timing; do not pre-create for every run.
- Reuse the existing primary-scaling stage mapping and platform refusal policy.
- Keep dependent charged-pop/toy/video integration explicit instead of adding a cosmetic button action without its charge consumer or bypassing the WPF pinning gate.

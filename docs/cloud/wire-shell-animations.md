# Shell animation navigation

- Connect the existing Lockdown plate breath to the real ShowTab funnel: start on arrival, stop and restore on departure, retain the current pulse for repeated/invalid navigation, and stop on window close.
- Replace the animated plate DropShadowEffect with a sibling Border BoxShadow, retaining WPF's pink colors, linear 1.5-second auto-reverse, blur 12–22 and opacity 0.7–1.0. Reduce the code-behind Effect ratchet allowlist accordingly.
- Add a stepped-time navigation regression for entry, exit, repeat/unknown destinations, reduced motion, the performance tier and close; retain the existing curve assertions with equivalent shadow assertions.
- Use the production font builder in the three animation test classes, preserving bundled font registration and mapping Windows-only Segoe names to Inter for these Linux lifecycle tests. Every existing assertion remains.
- Correct the stale ledger evidence: QuestsTabView already owns its visibility-driven season shimmer, and EnhancementsTabView already draws settings-backed Core skill rules rather than sample nodes. No duplicate shell clocks are needed.
- Read-only import source fetched at parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing BeatLoop provenance remains `66ca21fd3`.
- No engine, capture or screen-owning surface is added. WPF's panic path does not stop this decorative tab pulse; its existing navigation/close lifetime remains the owner.

## Verification limits

Run with .NET SDK 10.0.402, `DiffEngine_Disabled=true`, a 1.5 GB managed heap cap, workstation GC, `-m:1`, `-p:UseSharedCompilation=false` and build-copy hardlink flags to fit the parallel worker container.

- `bash .github/scripts/core-guards.sh`: passed the forbidden-API and XAML-reference checks.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~AmbientBeatLoopsTests|FullyQualifiedName~EnhancementsTabTests|FullyQualifiedName~QuestsTabFxTests'`: 13 passed, 0 failed, 0 skipped.
- Regression proof: temporarily removing only `ApplyLockdownPulse(tab)` from the navigation funnel made `LockdownPulseFollowsTabNavigation` fail its pulse-on-entry assertion (expected true, actual false; 1 failed). The hook was restored automatically afterward.
- Restored-hook regression rerun: 1 passed, 0 failed, 0 skipped.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --no-restore`: 2816 passed, 0 failed, 0 skipped. `CCP_REQUIRE_VOSK=1` and `CCP_VOSK_MODEL=/workspace/shared/worker-a-vosk/vosk-model-small-en-us-0.15` required the real speech model.
- Release builds succeeded for CCP.Core, CCP.Avalonia and every Linux test project listed in `.github/workflows/build.yml`:

| Test project | Build |
| --- | --- |
| CCP.Avalonia.CatalogueStartup.Tests | passed |
| CCP.Avalonia.Consent.Tests | passed |
| CCP.Avalonia.GeneralSettings.Tests | passed |
| CCP.Avalonia.Language.Tests | passed |
| CCP.Avalonia.Report.Tests | passed |
| CCP.Avalonia.Scheduler.Tests | passed |
| CCP.Avalonia.Snapshot.Tests | passed |
| CCP.Avalonia.Tests | passed |
| CCP.Core.Content.Tests | passed |
| CCP.Core.Settings.Tests | passed |
| CCP.Core.Tests | passed |

The WPF test build was attempted with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` and stopped while its temporary WPF project compiler was active to relieve shared-container memory pressure. This probe is incomplete; SIGTERM returned exit 0 without a build-success summary, so that exit is not evidence of a completed build. WPF sources are unchanged in this unit, and Windows desktop execution was unavailable.

- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/worker-b-renders` with isolated `CCP_USERDATA_DIR=/tmp/worker-b-render-profile`: exit 0, **224/224 views rendered**. `LockdownTabView.png` shows the sibling pink plate glow; the PNG was inspected. Clock movement and entry/exit are proved by the stepped-time tests, while this render shows the resting visual.

An initial pre-fix filtered test attempt could not run: disk exhaustion caused Core output-copy errors MSB3027/MSB3021. A second build exposed a missing embedded audio JSON in the worker's sparse checkout; the tracked asset tree was restored using hardlinks, without source changes. Neither attempt established a test result.

The Linux container cannot execute WPF/Win32 desktop behavior. Headless tests use the repository's isolated profile setup. Visual output is offscreen evidence only.

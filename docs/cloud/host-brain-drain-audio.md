- Port Brain Drain audio scheduling and folder discovery into Core, using CoreSettings and Serilog.
- Adapt CoreBrainDrain and BrainDrainPlayer from read-only parity/trunk commit `f01bc9bd92f70da0a5e6836873d09db88369ffbf` (fetched trunk tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Existing base files were merged, never replaced.
- Reuse the existing LibVLC voice backend on Linux and Windows through stop/live-gain delegates. No new native backend or UI dependency in Core.
- Wire Studio audio enable/intensity/high-refresh, folder-open and refresh/count; wire engine start/stop, app exit and panic surface `brain-drain-audio`, including SafetyCritical.
- Preserve assets-over-legacy case-insensitive discovery, WPF cadence/probability, play-through, suppression and master-times-own gain. Retire in-flight builds across Stop/Start and synchronous completion.
- Add Core behavior tests, card/panic tests, a tester step and one consolidated parity row (still stub for the visual gaps).

## Verification limits

- `dotnet build CCP.Core -c Release -m:1`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia -c Release -m:1`: exit 0, 0 errors.
- `dotnet build Tests/CCP.Avalonia.Tests -c Release -m:1`: exit 0, 0 errors.
- WPF test build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors; compiled only, Windows execution unavailable.
- Workflow test builds: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Consent.Tests, Avalonia.Language.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Migration.Tests, Avalonia.Scheduler.Tests, Avalonia.Report.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Snapshot.Tests: exit 0, 0 errors. Initial overlapping builds hit intermediate-file/MSBuild errors; serial retries succeeded.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base; manual WPF/Rows tester step added.
- Tests ran with `DiffEngine_Disabled=true`.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3958 passed, 5 skipped, 1 failed: existing remote-media test `Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`, expected Loops, actual Clips. No test was weakened or removed.
- Focused `BrainDrainAudioTests`: exit 0, 6 passed. Mutation proof: removing the publish-generation guard made `StopRetiresInFlightBuildEvenAfterRestart` fail (expected one stale-player disposal, actual zero); restoring it returned green.
- Focused `BrainDrainAudioPanicTests`: exit 0, 2 passed (card caller and panic registry).
- Broader headless filter (new tests, BrainDrainKeepClearRowTests, PanicSurfacesTests): exit 1; 7 passed, 3 failed with existing Inter/Segoe glyphTypeface creation errors. Those window-rendering checks remain unvalidated here.
- Native audible playback, folder shell launch and Windows execution were not run. Linux scheduling/host behavior is tested through fake voices; Windows uses the existing compiled LibVLC backend.

## Assumptions and remaining scope

- Reuse the existing cross-platform native voice seam rather than duplicate WPF NAudio output code.
- Deliver the core audio host plus the engine/card caller. Session-specific named-player callers, avatar notification/media history, the own-volume UI row, and desktop blur/melt/capture remain follow-up work.
- Master volume refresh uses the existing settings binder; BrainDrainVolume is respected on start and by the exposed RefreshVolume seam.

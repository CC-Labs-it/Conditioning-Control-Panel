# fin5-shell-start-stop

- Port the WPF START hero subset into the real shell: mod-tinted charge, idle exhale, press dip/flash/staggered rings and running heartbeat. Reuse the existing shared FrameClock; park on focus, minimize, motion and remote-state gates. No new windows or overlay surfaces: existing shell panic handling remains the owner.
- Repaint attached Settings/dashboard audio controls from model changes, including intensity-ramp ticks and restoration, without saving back during synchronization.
- Add focused headless control assertions and manual steps; retain `stub` with an expanded honest Missing list.
- Source: frozen WPF reference at base `05caa12f51a8a5cd166cda7d5b0b7edf512a9c14`. Fetched `parity/trunk` read-only; no trunk code imported, so no imported trunk commits to credit.

## Gap audit and dependencies

The base row names Brain Drain, START hero FX, wallpaper arming notice/service and ramp master-volume repaint. The latter two independent UI behaviors implemented here are START FX and volume repaint.

BrainDrainService (496 reference lines) owns asset plus legacy clip discovery, intensity timers, non-overlapping audio, cancellation/generation-safe publication, audio arbitration, presence and trigger events; the associated screen melt/blur host is also absent (BrainDrainLayer: 432 lines; BrainDrainCapturePump: 771 lines). WallpaperService (644 reference lines) owns Win32 capture/change/restore, recovery, remote materialization/pool/retry and persistence/pulse rules; the Takeover arming path explicitly defers its notice and service. These are live WPF dependencies, not P122 dead callers. Adapting both hosts, their seams and behavioral tests exceeds the remaining budget; they remain named gaps.

The source audit also found broader omissions concealed by the base row: forced audio-only layered-bed start and conditional stop, systemInitiated exclusions (Relapse/count/video prompt), mandatory-video enhancement prompt, crash sentinel, EMI start/stop events, skill time-of-day tracking and full Stop cleanup (ForceUnduck, interaction queue and fullscreen hosts). These remain explicitly listed instead of declaring the row wired. WPF CheckSchedulerAfterSettingsChange has no live caller and remains omitted as on base.

## Assumptions

- Prioritize the independent user-visible START feedback and volume repaint within this unit; keep dependent host work explicit rather than substituting an inert service.
- Use the existing per-window shared frame source for ambient FX, as required by the Avalonia port; preserve WPF durations and pixel growth without WPF storyboards.
- No new panic-registered surface is needed for decoration on existing shell controls.

## Verification limits

- SDK: .NET 10.0.402.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `dotnet build Tests/<project>/<project>.csproj -c Release` for all 11 Linux test projects in build.yml: exit 0, 0 errors. Projects: CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests, CCP.Core.Tests. The first main headless build found a missing import in the new test; corrected and rebuilt with 0 errors.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings, 0 errors. Compiled only; Windows tests and desktop behavior cannot run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed (424 XAML references). A preliminary read-only invocation could not create its scratch directory; the writable rerun passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 1; 2,828 passed, 5 existing Vosk-model skips, 1 failed out of 2,834. Failure: GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch, Assert.NotNull on the scheduled handle. Focused rerun of GoonClockAndRoundsTests: exit 1, 11 passed, 1 failed. SafeFireAt supplies the exact minimum buffer on the master; elapsed time before ScheduleAt can consume that lead. No Core or Core test source differs from base (`git diff --exit-code <base> -- CCP.Core Tests/CCP.Core.Tests`: exit 0). No tests weakened or removed.
- Pre-fix `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~AudioSettingsTests`: exit 1, 1 passed, 2 failed before the new assertion because PremiumSpark cannot create the Segoe UI Black glyphTypeface. This does not establish a behaviorally failing regression test. Per the environment exception, further StartHeroTests/StartStopIntegrationTests runtime runs were not attempted; they and AudioSettingsTests compile with 0 errors and require the font-capable supervisor runner.
- `git diff --check`: exit 0. Test-plan coverage script is absent on this base; numbered manual steps were added anyway. No Vosk fetch, render-all, real desktop, Windows runtime tests or full Avalonia-suite run attempted.

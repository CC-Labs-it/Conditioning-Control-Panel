# Split feature card wiring

- Ported WPF's per-half face/socket/bevel structure, press and active travel, pointer tilt, release spring, and dashboard-only click inversion. Active rings remain on the fixed regions while the faces move.
- Off art now fades through a cached, immutable grey twin capped at 320px. Dashboard hover keeps an off half muted; other cards reveal its colour. Hiding/unloading snaps the mute back to rest.
- Loaded cards subscribe to MotionLevel/PerformanceMode changes and unsubscribe on unload, so visible dashboard cards re-gate without a tab switch.
- Moved card mute/luma and mosaic depth/click rules to CCP.Core; WPF delegates to the shared implementations. No source was imported from parity/trunk (fetched read-only); the frozen WPF files were the specification.
- Added Core rule tests and headless card tests. Existing Fx assertions remain, with real app font setup and added minimize/rearm and tilt-gate checks. No new panic surface is introduced.

## Verification limits

Commands used .NET 10.0.402 from `/workspace/.ccp-dotnet`, Release configuration, `DiffEngine_Disabled=true`, and serial MSBuild (`-m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false`). Copy-local/output/additional-file hardlink flags were enabled to fit the shared disk.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: passed.
- `dotnet build <project> -c Release` for every Linux test project named in `.github/workflows/build.yml`: passed: Core, Core.Settings, Core.Content; Avalonia, CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot test projects.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: passed on retry, 0 errors (512 warnings). Initial cross-target attempt exhausted disk; the final WPF head and test project compile successfully. Windows tests and the Windows desktop were not run on Linux.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: passed, 2834 passed / 5 skipped / 2839 total. Existing speech-engine tests skipped because the Vosk model is absent. `DashboardDepthAndMuteTests` covers travel priorities, bevel, tilt clamp, dashboard click inversion, mute rules/timing, and luma with preserved alpha.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~SplitFeatureCardDepthTests|FullyQualifiedName~SplitFeatureCardFxTests'`: passed, 4 passed / 0 failed. Real headless pointer input covers each half, click inversion, help carve-out, press/release/exit, grey cache/aspect cap, hide snap, live gates, minimized-window rearm, and tilt gating.
- Before the port, all three new card tests failed against the base card: inverted routing returned `(2,0,0,2)` rather than `(1,1,1,1)`, and grey/face controls were absent. Font/bootstrap and popup sequencing issues in the test fixture were corrected without removing assertions.
- `bash .github/scripts/core-guards.sh`: passed.
- `CCP_USERDATA_DIR=/workspace/worker1-render-profile dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/worker1-render`: 224/224 views rendered. `SettingsTabView.png` shows the three dashboard split cards; `SplitFeatureCard.png` shows the standalone card. The dashboard PNG was inspected.

Headless tests assert states, targets and gates; real desktop transition playback, focus changes, and pointer tilt timing were not exercised. The ledger is wired, not verified.

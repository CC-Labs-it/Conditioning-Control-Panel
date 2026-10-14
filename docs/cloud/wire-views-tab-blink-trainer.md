# Blink Trainer tab: responsive WPF parity

- Blink Trainer now hides its side-art column below 860 DIP and returns it at 908 DIP, matching WPF's 48 DIP hysteresis. Narrow layouts lift the settings columns' width caps; widening restores the authored art width and settings caps.
- Added the Avalonia `AdaptiveSideArt` attached behavior and wired it only on this tab. Extracted its pure collapse decision to Core `AdaptiveSideArtRule`; WPF delegates to that same rule.
- The new real-tab resize regression failed before the fix: `SideArt.IsVisible` stayed true while the wrapping grid was below 860 DIP. Focused tests cover resize, scrollbar-width hysteresis, custom pixel widths, multiple caps, and keeping spanning settings rows visible.
- Updated only ledger row `views-tab-blink-trainer` to wired. Corrected its stale stage-animation note: WPF `Views/Tabs/BlinkTrainerTabView.xaml:210-211` uses plain Images; `MainWindow/MainWindow.BlinkTrainer.cs:534-548` loads and freezes a BitmapImage, with no animated-image hook. The separate WPF session overlays animate via `BlinkTrainerService.BuildTile`; existing session-overlay limitations remain in their own shell/session scope.
- No new window, timer, decoder, or panic-owned surface is introduced. Existing Blink Trainer session panic registration remains applicable, covered by the existing session tests.
- Source was the base branch's WPF implementation. `git fetch origin parity/trunk` ran; no trunk code was imported, so there are no imported trunk commits to credit.

## Verification limits

All commands used .NET SDK 10.0.402 on Linux, `PATH=$HOME/.dotnet:$PATH`, Release configuration, and `DiffEngine_Disabled=true` for tests. Headless tests use the assembly's `CCP_USERDATA_DIR` sandbox. Native LibVLC and a TTF fontconfig configuration were supplied outside the repository; the required Vosk model was available in the shared test cache.

- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release -m:2` with `CCP_REQUIRE_VOSK=1`: **2824 passed, 0 failed, 0 skipped**. Includes eight `AdaptiveSideArtRuleTests` cases.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:2 --filter 'FullyQualifiedName~BlinkTrainerResponsiveTests|FullyQualifiedName~BlinkTrainerTabLiveTests|FullyQualifiedName~BlinkTrainerSessionTests'`: **15 passed, 0 failed, 0 skipped**. Includes two new responsive tests and existing start/stop/countdown/video-preview/panic behavior checks.
- Before the implementation, the real-tab resize test reported **1 failed, 0 passed** at its narrow-layout visibility assertion. Earlier font setup failures were corrected in the new test's setup and do not count as regression proof.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed**.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release -m:2` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release -m:2`: **succeeded**.
- Every Linux test project listed in `.github/workflows/build.yml` built successfully with `dotnet build <project> -c Release -m:2`: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Report, Avalonia.Scheduler, and Avalonia.Snapshot test suites. These auxiliary suites were compiled; only the test runs named above were executed. Disk pressure initially interrupted several builds; after removing only generated auxiliary test binaries, those builds were rerun successfully.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: **succeeded, 512 warnings, 0 errors**. The first attempt hit disk pressure and the next lost an MSBuild worker at `-m:2`; the serialized `-m:1` retry completed. WPF and its tests were cross-compiled only, not executed; generated Windows build outputs were removed after preserving the log.
- `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --render-all /workspace/scratch/wire-views-tab-blink-trainer/render`: **224/224 views rendered, exit 0**. `BlinkTrainerTabView.png` (1100×780) was inspected and had no reported binding error. It shows the wide layout; the resize tests prove the narrow layout and restoration.

No real camera, desktop overlays, Windows runtime, or WPF UI was exercised on Linux. GIF/animated WebP stage behavior was checked against WPF source, not a Windows visual run. Rendering logs and PNGs are outside the branch in `/workspace/scratch/wire-views-tab-blink-trainer/`.

# wire-shell-bank-fx

- Wire completion XP pooling, token flights, per-landing counter ticks, last-landing pop/bar flash/audio, truth release, focus/minimize silence, watchdog and panic cleanup through the existing shell/progression path.
- Move the unchanged WPF BankAccumulator and XPSource enum into CCP.Core; both heads use one policy and unchanged enum ordinals. Link the existing WPF bank rule/flight tests into the Linux Core suite.
- Publish adjusted award amounts and level-up results separately from the base amounts quests already consume. Preserve the base shell, merge its readout/hold hooks, and add only a finite transform track subset that this feature calls.
- Bank/odometer/track adaptation credit: read-only parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. The trunk's duplicate string-based bank policy was not imported; WPF is the behavior specification.
- Update only ledger row `shell-bank-fx` to `wired`; register `bank-fx` in PanicSurfaces and its safety test.

## Verification limits

Commands used .NET 10.0.402, Release configuration, `DiffEngine_Disabled=true`, and `FONTCONFIG_FILE=/workspace/ccp-test-fonts.conf` (system TTF fallback; the host's default WOFF2 family failed Skia). MSBuild copy-hardlink flags saved generated-output space; source behavior and test assertions were preserved.

- Before implementation: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~BankFxTests.CompletionHoldsOnlyTheReadoutWhileTheLedgerAlreadyHasTheAward` failed on the original code: expected `10 / 994 XP`, actual `15 / 994 XP`.
- `dotnet build CCP.Core -c Release` and `dotnet build CCP.Avalonia -c Release`: succeeded, zero errors.
- `dotnet build -c Release` of all 11 Linux workflow test projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests: succeeded. Core.Settings initially hit disk exhaustion; its final explicit retry passed.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: succeeded, 512 warnings, zero errors (8m08s). Windows/WPF runtime behavior was not run on Linux.
- `dotnet test Tests/CCP.Core.Tests -c Release`: 2873 passed, 1 failed, 5 skipped / 2879 total. Untouched `BoardTilesTests.A_frame_fits_the_budget` measured 43.62ms amid parallel builds; the isolated retry also failed. Five Vosk tests skipped without the external model. The full suite is not green.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter 'FullyQualifiedName~BankAccumulatorTests|FullyQualifiedName~BankFlightPlanTests|FullyQualifiedName~BankAwardTests'`: 63 passed, zero failed/skipped.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~BankFxTests|FullyQualifiedName~PanicSurfacesTests'`: 16 passed, zero failed/skipped. Covers real award subscriptions, held readout/live ledger, weather/motion gates, fractional formatting, actual token callbacks, pop timing, panic/minimize/watchdog, level-up and audio override/volume routing. Audio provider was faked; hardware playback was not run.
- `bash .github/scripts/core-guards.sh`: all guards passed (424 XAML references).
- `CCP_USERDATA_DIR=/tmp/ccp-root-bank-render-profile dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/ccp-root-bank-render`: 224/224 rendered, exit zero. `MainShellWindow.png` shows the shell; `/tmp/ccp-bank-flight.png` captures a paused exercised bank frame. Linux headless rendering does not establish desktop feel or Windows behavior.
- All tests/render paths used sandbox profiles. Native desktop input, audible thud playback, and a Windows desktop review remain unrun.

The shell git transport rejected the initial claim with HTTP 403 as ClearlyNotMich. The connected Personal account was the sole account with repository push permission. An empty claim commit/atomic branch creation and final fast-forward-only ref update used that authorized connection; no PR, merge, force-push or queue-branch write was used.

## Changed files

- `CCP.Avalonia/Controls/AmbientFxCanvas.cs`
- `CCP.Avalonia/Helpers/FxTrack.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.BankFx.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.HeroFx.cs`
- `CCP.Avalonia/Views/Windows/PanicSurfaces.cs`
- `CCP.Core/CoreProgression.cs`
- `CCP.Core/Services/BankAccumulator.cs`
- `CCP.Core/Services/Progression/ProgressionBank.cs`
- `CCP.Core/Services/XPSource.cs`
- `ConditioningControlPanel/Services/BankAccumulator.cs` (moved into CCP.Core)
- `ConditioningControlPanel/Services/Companion/CompanionService.cs`
- `Tests/CCP.Avalonia.Tests/BankFxTests.cs`
- `Tests/CCP.Avalonia.Tests/PanicSurfacesTests.cs`
- `Tests/CCP.Core.Tests/BankAwardTests.cs`
- `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
- `docs/avalonia-parity.md`
- `docs/cloud/wire-shell-bank-fx.md`

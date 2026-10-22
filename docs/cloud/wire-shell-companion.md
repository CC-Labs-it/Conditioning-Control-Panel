# wire-shell-companion

- Publish Core companion XP-awarded/drained events after updating settings; reuse existing level/switch events. Both heads now consume the same Core milestone rule and notification wording.
- Subscribe/unsubscribe with the shell lifetime. Ordinary XP repaints only the visible room; levels/switches repaint hero + roster, and drains refresh the header and flash both existing XP overlays.
- Send mod-aware milestone notifications through OsNotifications and play the WPF sound fallbacks/volume through CoreAudio. Replacing a clip, panic and shell close stop the owned sound; the new panic surface is covered by the safety-critical registry test.
- Reuse the existing live mute flag, speech pipeline and quick-menu refresh. The room mute action now refreshes the tube menu without hiding its bubble, and tray Wake speaks the WPF morning greeting.
- Update only the two pre-existing ledger entries sharing the `shell-companion` ID. Trunk was inspected at `a4ee52d40`; no trunk files were imported.

## Verification limits

Environment: .NET 10, Release, `DOTNET_PROCESSOR_COUNT=2`, `DiffEngine_Disabled=true`. New headless tests use bundled Inter/AppFonts options. A task-local fontconfig file that includes `/etc/fonts/fonts.conf` and rejects `/usr/share/fonts/openai-sans/*` was used for supplementary runs/rendering because this image's default WOFF2 font selection failed headless glyph resolution.

- Baseline: the five new CompanionShellEventsTests failed against the original compiled head on stale XP/header, mute-menu state, milestone refresh and missing greeting. The initial harness run failed earlier on fonts; the corrected bundled-font harness reached the behavior assertions.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~CompanionShellEventsTests|FullyQualifiedName~CompanionHeroSyncTests|FullyQualifiedName~TubeQuickMenuTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~CompanionBarkHeadTests'`: 21 passed, 0 failed, 0 skipped (6m13s).
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~CompanionCoreTests --no-build`: 8 passed, 0 failed, 0 skipped.
- Required full `dotnet test Tests/CCP.Core.Tests -c Release`: 2834 passed, 1 failed, 5 skipped. The unchanged `BoardTilesTests.A_frame_fits_the_budget` measured 42.77ms under parallel compiler load. The five existing Vosk tests skipped because the model is unavailable; no tests were changed or excluded from that run.
- `bash .github/scripts/core-guards.sh`: passed all guards (424 XAML references checked).
- Supplementary isolated `CompanionShellEventsTests` run with the task-local fontconfig: 5 passed, 0 failed (4.0203 minutes).
- Core, Avalonia and all 11 Linux test projects named in `.github/workflows/build.yml` built successfully. Builds used `/m:1 -p:UseSharedCompilation=false`; supplementary test assemblies used `-p:BuildProjectReferences=false -p:CopyLocalRuntimeTargetAssets=false` against the already-built references to limit duplicate native assets. An initial head build hit disk-full copies; the clean retry finished with 0 errors. No repository build settings were changed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release /m:1 -p:UseSharedCompilation=false -v:q -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: failed with MSB6006 (`csc` exit 137) after 12m50.80s while the shared environment was at its 16GiB memory / 4-CPU limit. Windows compilation did not complete; no Windows compile or execution is claimed.
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/ccp-worker-1-render`: 225/225 views rendered. Inspected `CompanionTabView.png`; `CompanionHeroCard.png` also shows the affected hero area. These are static layout evidence; the event tests prove state changes and sound/notification requests.
- Additional repository-context `--nav-check`: 2 failures in untouched settings navigation (`OpenDeviceSettings` stayed on Data; the subsequent unknown-section assertion also failed). No navigation code was changed by this unit.
- The isolated Board frame-budget retry also failed (65.09ms). The benchmark was left unchanged.
- Windows runtime behavior, physical audio playback and real OS notification delivery were not run. Audio and notification assertions use the existing recording seams; headless tests use isolated profiles.

- Add `MiscEvidenceTests` in `Tests/CCP.Avalonia.Tests`: exercise the real update dialog, Pink Filter settings handlers and tint brush, Intiface Connect validation, and the shipped default bark through the engine/resolver/head/tube to a fake audio sink.
- Correct the update fill's two-pixel inset to match WPF `Dialogs/UpdateProgressDialog.xaml.cs:25-28`. The updater supplies the inner grid width; WPF uses the outer track width minus six pixels.
- Add a desktop-lifetime tube lookup seam to BarkHead for headless tests. Production still finds the open tube through the desktop lifetime.
- Consolidate duplicate default-voice ledger rows; retain one wired row per requested id, each naming its test. Add numbered WPF comparison steps; no row promoted to verified.
- No trunk code imported. Read-only source fetched: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.

## Verification limits

- SDK: .NET 10.0.402. `DiffEngine_Disabled=true` for test runs. Tests use the assembly's `CCP_USERDATA_DIR` sandbox and bundled font setup; no real desktop, profile, audio output or haptic device.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia, and all test projects referenced in `.github/workflows/build.yml` passed with zero errors. Test projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, and ConditioningControlPanel.Tests. WPF used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; compiled only, not run on Linux. Existing compiler/analyzer warnings remain.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3,953 passed, 5 skipped, 3,958 total. Existing optional Vosk tests skipped because their speech model is unavailable; no tests changed or disabled.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (exit 127); the four rows each have a numbered step with `WPF` and `Rows:` references.
- Progress regression reproduced before the fix: at 37%, expected fill width 130.98, actual 131.72. The same assertion passes after the two-pixel correction.
- Focused command: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~MiscEvidenceTests`. Initial corrected run: exit 0, 4 passed, 0 skipped. Mutation and restored-run results recorded below.
- Native click-through, compositor output, audible voice playback, real Intiface connectivity, Windows runtime and installer download were not run. The headless tests prove settings, validation, speech/audio dispatch and drawing state rather than those platform integrations.

## Assumptions

- The shipped navigation bark is selected from the actual default manifest and isolated from unrelated competing navigation rules; `guaranteed` bypasses its random chance/cooldown. Its production audio resolver and head remain intact.
- Audio uses the existing stoppable audio seam. A small tube lookup seam substitutes for the unavailable desktop lifetime, preserving the real tube speech path.
- Muted egg behavior is checked on an idle tube, independent of an earlier bark's speech gap.
- Pink Filter native overlays remain subject to their existing platform safety checks; the test drives controls and checks the tint brush without opening a native fullscreen overlay.

## Negative controls

The four independent behaviors were temporarily removed in one suite run, then all source files were restored in a `finally` block. Tests themselves were unchanged. Command: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~MiscEvidenceTests`.

| Test | Temporary production change | Observed assertion failure |
| --- | --- | --- |
| UpdateProgressClampsAndPaintsTheDownloadPercentage | Revert the two-pixel fill inset | Expected 130.98, actual 131.72 |
| PinkFilterControlsWriteSettingsAndResetTheRenderedTint | Remove the slider's settings write | Expected opacity 42, actual 10 |
| IntifaceConnectCorrectsTheAddressAndRejectsInvalidInput | Bypass URL normalization | Expected normalized URL, actual typed dot-port |
| ShippedDefaultBarkReachesTheTubeAudioSinkAndMutedEggIsTextOnly | Remove the clip argument from the tube handoff | Expected shipped clip path, actual null |

Mutation run: exit 1, 4 failed, 0 passed, 0 skipped; all four failures occurred at the intended behavior assertions, not setup or font creation.

Restored verification: `dotnet build Tests/CCP.Avalonia.Tests -c Release` exited 0 (0 errors, existing warnings), followed by `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~MiscEvidenceTests`: exit 0, 4 passed, 0 failed, 0 skipped. Core guards passed again. A local audit also confirmed exactly one wired row per requested id, a named test in each evidence column, and numbered WPF/Rows tester steps for all four.

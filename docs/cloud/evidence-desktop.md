- Added `DesktopEvidenceTests` for the four wired rows: haptics help opens an owned wizard and refreshes its address after closing; navigation closes an open tooltip on a hovered rail button; the real mute dialog returns all three answers and keeps speech on Escape/dismiss; the orb parks/resumes with visibility, pause and motion settings.
- Consolidated duplicate `win-emi-mute-prompt` rows, linked named tests in all four evidence columns, and added numbered manual tester steps. All four rows remain `wired`.
- WPF reference lines were checked in this checkout: `MainWindow.Haptics.cs:685-694`, `MainWindow.ToolTipHygiene.cs:120-145`, `EmiMutePromptWindow.xaml.cs:37-57,94-95`, `TakeoverOrb.cs:130-185,327-339`.
- No trunk code imported. `parity/trunk` was fetched read-only; no imported commit credits apply.

## Verification limits

- Base checkout: `39260f198fad67d90bc988c63a00480830e5fefc`; .NET SDK 10.0.402 for final checks. A pinned 10.0.111 install succeeded after a channel lookup returned 403; the channel installer subsequently recovered and installed 10.0.402.
- `dotnet build -c Release` passed for CCP.Core, CCP.Avalonia and Linux workflow test projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests and CCP.Avalonia.Snapshot.Tests. Each completed with zero errors. GeneralSettings was retried with `-m:1` after an MSBuild worker failure. An initial overlapping Avalonia build hit an artifact collision; subsequent serialized builds succeeded.
- `bash .github/scripts/core-guards.sh`: exit 0, both guards pass. `test-plan-coverage.sh` is absent at this base.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~DesktopEvidenceTests`: eight passed, zero failed/skipped. The stronger hovered-button tooltip path also passed (eight cases).
- Negative check 1: removed the shell wizard opening, navigation tooltip close, prompt answer handlers, and orb Pause clock stop; changed the prompt enum's Keep value to remove the default-on-dismiss contract. Build exited 0; all eight cases failed (test exit 1) at their behavior assertions. Sources restored in a finally block. No setup/font failures.
- Negative check 2: removed the page refresh after the wizard, navigation tooltip close and orb reduced-motion gate, with the same prompt mutations. Build exited 0; all eight cases failed (test exit 1). Haptics retained the old address, the nested tooltip stayed open, and reduced motion left the clock running. All production files restored.
- Final restored `dotnet build Tests/CCP.Avalonia.Tests -c Release -m:1`: exit 0, zero errors (existing warnings). Final `dotnet test ... --no-build --filter FullyQualifiedName~DesktopEvidenceTests`: exit 0, eight passed, zero failed/skipped. No production edits remain.
- Full `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1, 3,952 passed, five Vosk skips, one unrelated failure. A second full run returned the same totals: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expects Loops but receives Clips at line 185. Its isolated rerun passed (exit 0, one passed). No Core code changed.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, zero errors. WPF app and tests cross-compiled; not run on Linux. The earlier multi-node attempt failed with MSB4166.
- No real desktop, toy hardware, native compositor, or Windows runtime behavior exercised. Tests use the assembly's sandbox profile and headless platform. Other workflow suites were compiled, not run.

## Assumptions

- A headless owned modal and a changed settings address prove the shell haptics path; device transport remains covered by the existing haptics suites.
- The mute prompt's UI contract is tested through `Ask(owner)`; existing `EmiDeskSummonTests` cover service-level summon/muting and once-per-session behavior.
- Dismissal depends on Avalonia's default dialog result and the existing `Keep = 0` contract, so its negative check changes that enum value temporarily.

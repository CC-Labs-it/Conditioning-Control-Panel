- Restored actual voice-line rows in the Phrase Manager: packaged-mod priority, per-mod install/content-pack union, mod ownership of the baseline, audio-format filtering and alphabetical filename order.
- Moved voice-line enumeration, positional-to-filename ID migration and row construction to `CCP.Core/Services/Companion/CompanionVoiceLines.cs`; WPF calls the same implementation. Disabled/removed IDs retain their existing meaning, and numeric filenames remain stable IDs.
- Connected the existing Companion workshop Manage Phrases button to the existing shell dialog action, matching WPF forwarding. Existing startup `BarkHead` already seeds `CoreBark.AllLines`; the old ledger's missing-bark-engine note was stale.
- Added focused Core catalogue/migration tests and headless Phrase Manager tests for actual voice/bark rows, category/search filtering, bulk persistence and the real workshop button path.
- No trunk code imported. Read-only `parity/trunk` inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapted from the brief's WPF reference files.
- No new playback or panic surface: the manager edits rows; the existing companion/bark engine owns speech and panic behavior, as before.

## Verification limits

Commands used .NET SDK 10.0.402 at `/tmp/ccp-dotnet`, with `-m:1 /p:UseSharedCompilation=false`. Output-copy hardlinks (`CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, `CreateHardLinksForCopyLocalIfPossible`) avoided duplicate media/native files after the shared disk filled. Initial output-copy attempts failed with MSB3021/MSB3026, then succeeded after reclaiming byte-identical duplicates. No source bytes changed during disk deduplication.

- `dotnet build CCP.Core`: succeeded, 48 warnings, 0 errors.
- `dotnet build Tests/CCP.Core.Tests`: succeeded, 118 warnings, 0 errors.
- `dotnet build Tests/CCP.Avalonia.Tests` (includes CCP.Avalonia): succeeded, 50 warnings, 0 errors; final incremental build also succeeded with 0 errors.
- All Linux test projects named by `.github/workflows/build.yml` built successfully: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Consent.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Language.Tests, Avalonia.Report.Tests, Avalonia.Scheduler.Tests and Avalonia.Snapshot.Tests. Consent/GeneralSettings initially failed for disk space and succeeded on retry.
- `bash .github/scripts/core-guards.sh`: all guards passed (424 XAML references checked).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests --no-build`: 2814 passed, 0 failed, 6 existing Vosk-dependent tests skipped (model absent), 2820 total.
- Focused `CompanionVoiceLinesTests`: 4 passed, 0 failed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests --no-build --filter FullyQualifiedName~CompanionPhraseEditorTests --blame-hang-timeout 90s`: 2 passed, 0 failed. Tests use the repository's temporary profile; the cell test uses the actual workshop cell in the actual shell while replacing unrelated Home content.
- Fail proof: both new head tests failed against an isolated output using the worker1 head assembly; its phrase-editor and workshop-cell source files were checked unchanged versus the brief BASE (`git diff --quiet origin/avalonia-port/goongame-core-c -- <both files>`). Failures were the missing `VoiceLine:Alpha` row and the empty owned-dialog collection after clicking Manage Phrases. The final changed assembly passed both tests. No other worker's files were modified.
- Initial mixed head runs exposed absent font registration and asynchronous TextChanged dispatch in the test setup; the final tests use the real app font builder and drain UI jobs after search changes. An early broader `CompanionBarkHeadTests` run was unsuccessful under that setup and is not claimed as passing.
- `CCP_USERDATA_DIR=/tmp/worker6-render-profile dotnet run --no-build --project CCP.Avalonia -- --render-all /tmp/worker6-renders`: 224/224 views rendered. Inspected `CompanionPhraseEditorDialog.png`; its rows are sample data, so the PNG proves visual construction while the behavioral tests prove actual rows and settings.
- WPF app cross-compilation with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` produced ConditioningControlPanel.dll. WPF test-project cross-build succeeded, 509 warnings, 0 errors. No Windows desktop or WPF tests executed on Linux.

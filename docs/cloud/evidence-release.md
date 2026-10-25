- Add `ReleaseEvidenceTests.LinuxTarballPlacesModelWhereSpeechEngineResolvesIt`: run the real tarball script with local publish/download fixtures, extract its archive, and assert runtime model discovery and payload bytes.
- Add `ReleaseEvidenceTests.ContentPackKeepsInstallPathsAndExactDeletionNames`: execute the real PowerShell release functions against a miniature asset/mod tree and check archive paths, bytes, timestamps, and exact installer deletion names.
- Strengthen `RealProfileMigrationTests.SyntheticWpfProfilePreservesSettingsAndUserFilesOnStartupAndShutdown` with non-default volumes and opaque user-owned bytes. Settings formats and production behavior are unchanged.
- Consolidate the three scoped parity IDs to one wired row each, link executable tests, and add numbered tester steps. No row is promoted to verified.
- No trunk code imported. `parity/trunk` was fetched read-only; no imported commit credits apply.

## Verification limits

- Packaging positives: filtered Core run passed 2/2 (exit 0).
- Packaging negatives: temporarily removing the production model-copy command fails the model-discovery assertion (exit 1); temporarily lowercasing production archive entry paths fails the archive assertion (exit 1). Both production files were restored, then the positive run passed.
- The packaging test substitutes publishing and model download; it proves archive placement/discovery, not native Vosk loading. No model download or real microphone run was attempted because the task reports the download blocked.
- The content test executes release helper functions, not the complete multi-gigabyte release command. PowerShell 7 is required on PATH. On Linux, test-only shims reproduce Windows long-path and parent-path semantics. Windows execution remains untested.
- Migration compiles, but the final runtime attempt fails during shell creation: `Could not create glyphTypeface` for `Segoe UI Black` (exit 1), matching the reported cloud font limitation. Its positive and mutation proof remain incomplete. An earlier incomplete build produced a XAML-loading error; rebuilding resolved that without production changes. No real private profile was used, and assertions were not weakened.
- Initial build/test attempts exhausted disk space. Subsequent builds use MSBuild hard-link options and discard generated test outputs between projects. Those failed attempts are not counted as behavior evidence.
- `test-plan-coverage.sh` is absent from the specified base; numbered steps were checked manually.

- Final `dotnet build -c Release`: Core, Avalonia, and all 13 workflow test projects compiled with zero errors. The test projects are Core, Core.Settings, Core.Content, Avalonia.Consent, Avalonia.Language, Avalonia.CatalogueStartup, Avalonia.Migration, Avalonia.Scheduler, Avalonia.Report, Avalonia.GeneralSettings, Avalonia.Snapshot, ConditioningControlPanel, and Avalonia. The WPF test build used `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`; Windows code was compiled but not run.
- Disk workaround build properties: `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true`, `CreateHardLinksForAdditionalFilesIfPossible=true`, `CreateHardLinksForCopyLocalIfPossible=true`. Migration was retried with `-m:1 -nr:false` after a stale worker failure. Final SDK: 10.0.402.
- Final `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests --no-build --no-restore`: exit 0, 3954 passed, 6 existing skips, 0 failed (five unavailable-model Vosk tests and one frame-budget test). The new release tests both passed.
- Final `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The main Avalonia suite was compiled but not run because of the demonstrated font limitation; the relevant isolated migration test was attempted as described above.

## Assumptions

- Preserve the migration suite's separate testhost, since CorePaths is process-immutable; extend its synthetic startup/exit fixture rather than move it into the main Avalonia suite.
- Use deterministic miniature packaging inputs and test-only Windows path shims for Linux; keep actual model loading and full release generation as explicit verification limits.
- Keep all scoped rows wired, with no claim of completed verification.

BLOCKED: Migration behavioral and mutation verification require the cloud font fix or a runner with the required font fallback.

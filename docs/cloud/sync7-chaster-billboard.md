# sync7-chaster-billboard

- Port WPF `58e0c8532`: refresh the visible billboard deck and its dots immediately when a provider changes, jump to an early Live arrival, and retain elapsed hold time when the arrival waits.
- Port WPF `7aac72445` / `bce7c3d35`: mark the raffle's real counted dates on existing lock-sheet squares; keep crosses, show the key only when a dot is present, and use Core's UTC cutoff.
- Port WPF `1876d6b61`: the production Chaster service follows account identity; a page card carries its account epoch and cannot reappear after sign-out or a switch.
- Pure rules already live in Core (`BillboardDeck.Refresh`, `DashboardBillboard.JumpToArrival`, `ChasterRaffle.CountedDates`, `LockCalendar.CountsForRaffle`, Chaster identity epochs); the head calls those rules without copies.
- Read-only `parity/trunk` fetched; no trunk code imported. WPF on this base is the specification.

## Follow-up: sync7-chaster-billboard-next

Row 221 (`ab9a293a9`) remains needs-port: this base has no calendar vector art, its shared vector-art drawing/motion host, or its registration. Restore that coherent art stack and wire the calendar payload's real `mask` before marking the banner row ported. The sheet and billboard parity rows remain stub because their pre-existing visual/provider gaps remain. The featured chess provider itself is the overlapping `sync7-pbp-host-b` lane; this branch tests the shared arrival path with an actual Live card provider.

## Verification limits

No Windows desktop, real account, user profile, or network Chaster service is used by the added tests. No PR was opened.

- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~ChasterBillboardSync7Tests`: baseline 4 failed on missing dots/identity watcher/deck refresh; after the change 4 passed, 0 failed, 0 skipped (2 m 2 s). The tests initialize the production font registration through RenderProof; the first bare-headless bootstrap hit missing font-family errors and was corrected before the behavioral baseline.
- `dotnet test Tests/CCP.Core.Tests`: 2845 passed, 0 failed, 6 skipped (2851 total, 1 m 35 s). The existing optional Vosk/performance cases skip; no test was changed or disabled.
- `bash .github/scripts/core-guards.sh`: passed before and after the head changes. `git diff --check`: passed.
- Early unbounded build was interrupted after compiler contention; one later output-copy attempt exhausted the 32 GiB volume. Retried with `DOTNET_PROCESSOR_COUNT=2`, `-m:1`, `-p:UseSharedCompilation=false`, and hardlink copy properties. Those retries are the results reported here.

- `dotnet run --project CCP.Avalonia --no-build -- --render-all /workspace/scratch/worker4-render`: exit 0, **225/225** views rendered. Inspected `ChasterTabView.png`: the counted-day dot and legend are visible. This full sweep overlapped builds; the final standalone frame and focused test run below are run after all builds to avoid binaries being rewritten while mapped.
- `dotnet build` succeeded for each project below, with `DOTNET_PROCESSOR_COUNT=2`, `-m:1`, `-p:UseSharedCompilation=false`, and `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, `CreateHardLinksForCopyLocalIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, `CreateHardLinksForCopyAdditionalFilesIfPossible` all set to true (disk budget). Existing warnings remain; no build errors.

| Project | Build exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |

- Windows cross-build command: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` with the same resource/copy properties. Bounded after **15 m 36 s**: the frozen WPF app DLL was produced, but the test project was still compiling. Stopped only this checkout's builder/compiler. **The full WPF test-project build did not complete**; no Windows code or desktop was run. WPF sources are unchanged.
- Final focused check after build validation: `dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter FullyQualifiedName~ChasterBillboardSync7Tests`: **4 passed, 0 failed, 0 skipped**, 17 s. No more builds run during the final runtime checks.
- Final Core check: `dotnet test Tests/CCP.Core.Tests --no-build --no-restore`: **2845 passed, 0 failed, 6 skipped**, 49 s. Same existing optional skips.
- Final standalone frame: `CCP_USERDATA_DIR=/workspace/scratch/worker4-final-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-view ChasterTabView /workspace/scratch/worker4-final-chaster.png`: **exit 0**. Inspected the PNG after builds: the real counted-date dot and key are present; no binding errors reported for this view. The icon census comes from pre-existing controls.

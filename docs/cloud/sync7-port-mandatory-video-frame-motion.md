# Mandatory video frame and motion: sync 7

- Adapted WPF `5c828b70f491dcec8e67134f5690eab50ebad2f2`'s replay-after-check switch to the existing Core scheduler and Avalonia settings card. A failed check still reaches progression before an ordinary video ends; strict playback and an active Lockdown retain replay. Surprise replay uses the same gate. Existing verdict timing and mercy thresholds stay intact.
- Reused the base's `MandatoryVideoPresentation.ReplaysAfterCheck`, settings property, and localization keys. No floating infrastructure or unused shared classes were imported.
- Read-only source `origin/parity/trunk` was fetched (tip `a4ee52d40`). No files were imported from that tip; the adapted behavior is credited to the WPF commit above.
- Added scheduler behavior tests and a sandboxed headless settings test; added tester step 9.1. No new surface, so no PanicSurfaces registration is needed. Sync ledgers and parity ledger were not edited.

## Commit disposition

The base has only the full-screen LibVLC host: `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:136` builds screen surfaces, `:149` enables attention globally, and `:300` ends the whole run on a window close. `CCP.Core/Services/Video/MandatoryVideoScheduler.cs:323` refuses a trigger while playing. Neither head nor Core has an `IFloatingVideoSurface`, floating frame, independent floating playback instances, or floating audio focus. These missing host surfaces are prerequisites, not substitutes to invent within this unit.

| WPF commit | Disposition and evidence |
| --- | --- |
| `766bbe480` | **blocked-by-host**: missing floating playback/frame and per-window audio focus, needed for companion creation, Test-add, and sound transfer. Settings/count clamp are already covered by `CCP.Core/Models/AppSettings.cs:2634` and `CCP.Core/Services/Video/MandatoryVideoPresentation.cs:32`. The retired size picker was superseded by listed commit `cd3d38c98`; it is not reintroduced. |
| `e2b510a4f` | **blocked-by-host**: no browser companion/frame mute contract or legacy caption drag to fix. The current shared LibVLC decoder cannot implement independent browser sound ownership. |
| `321ed1d3c` | **blocked-by-host**: no floating frame contract on which to add MotionSpeed or multi-monitor StartMotion. |
| `09fa082e4` | **blocked-by-host**: no floating frame/pointer/motion controller; live throw speed, cross-monitor drag, DPI resize and display-change rehoming require that host. No unused motion class is added. |
| `cd3d38c98` | **blocked-by-host**: independent clip lifetimes, one trigger per instance, Test replacement, and attention suppression require the absent floating instance host. Shared rules already covered by `MandatoryVideoPresentation.cs:54`, `:100`, `:109` (under `CCP.Core/Services/Video/`) and percent/migration/count settings at `CCP.Core/Models/AppSettings.cs:2634`. |
| `e3fb00e04` | **blocked-by-host**: no open floating run to re-arm with a join delay. The quarter-gap/jitter rule is already covered by `CCP.Core/Services/Video/MandatoryVideoPresentation.cs:91`; the full-screen scheduler must not use it. |
| `5c828b70f` | **ported, replay portion**: `CCP.Core/Services/Video/MandatoryVideoScheduler.cs:398`, `CCP.Avalonia/Views/Features/VideoFeatureControl.axaml` (`ChkReplayFailedCheck`) and its code-behind. Tests: `MandatoryVideoSchedulerTests.Replay_switch_ends_ordinary_failures_but_preserves_strict_and_enabled_replays`, `VideoReplaySwitchTests.Replay_toggle_loads_saves_and_follows_external_changes`. **blocked-by-host, floating portions**: Pop up together needs independent clip instances; Keep sound needs frame audio focus. Their settings and window-count rule already exist at `CCP.Core/Models/AppSettings.cs:2677` and `CCP.Core/Services/Video/MandatoryVideoPresentation.cs:62`. |

## Verification limits

- .NET 10 SDK used; `DiffEngine_Disabled=true` for tests.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3,957 passed, 0 failed, 5 existing Vosk skips (model download unavailable).
- Focused `--filter FullyQualifiedName~Replay_switch`: exit 0; all 4 current cases passed, including strict, enabled replay, ordinary off, and Lockdown override. Disabling the production replay guard once produced exit 1: the ordinary-off case found `[Fail]` where no retry message was expected. Guard restored before final suite.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The read-only sandbox initially prevented temporary files; rerun with write access passed.
- Release builds: `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `CCP.Avalonia/CCP.Avalonia.csproj`: exit 0, 0 errors. Every Linux test csproj in `.github/workflows/build.yml` compiled with 0 errors: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Consent.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Language.Tests, Avalonia.Migration.Tests, Avalonia.Report.Tests, Avalonia.Scheduler.Tests, Avalonia.Snapshot.Tests (each prefixed `CCP.`). Final headless compile used `-m:1 -p:UseSharedCompilation=false` after a worker failure; its test callback compile error was corrected before the successful retry.
- WPF test compilation: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true`: exit 0, 2 warnings, 0 errors. Earlier asset-copy staging exhausted disk (exit 134); freeing generated outputs and enabling content hard links resolved it. Compiled only, not run on Linux.
- `git diff --check`: exit 0.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (exit 127); manual tester step 9.1 added anyway.
- Initial parallel verification exhausted disk space through copied native dependencies. Generated test outputs were removed; builds were retried with `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true`, clearing completed test outputs. An initial stale test assembly was detected and rebuilt; the final suite above includes the added Lockdown case.
- Avalonia headless tests are compiled but not run: the task reports font creation failures in this environment, and the font-fix setup is absent on this base. The new test uses the existing sandbox profile and headless dispatcher; no real desktop/profile access is requested.
- Floating behavior cannot be exercised without the host above. No Windows desktop code was run on Linux.

## Assumptions

- Missing floating host is the explicit out-of-scope case in the task. Leave its controls absent rather than expose settings that cannot act.
- Port the independently usable replay switch from `5c828b70f` while reporting that commit's remaining floating portions as blocked.

- Finish `shell-deeper-submissions`: accepted Deeper submissions use a keyed sticky toast instead of a 15-second toast. The catalogue id remains stable across path changes; duplicate live keys collapse; × saves `DismissedNotificationKeys`; View clears the current toast and opens the Deeper library without remembering a dismissal. Queued notifications retain their order and sticky lifetime.
- Restore WPF display-name resolution from the complete library index, including entries hidden by filters; preserve filename fallback when no named entry exists.
- Add focused headless tests for queue/deduplication/no expiry, persisted dismissal across settings reload, throwing action cleanup, acceptance statuses, title/fallback, poll throttle/once-only state, View navigation and badge refresh. Replace the single ledger row's stale merged history with `wired` and precise verification limits.
- Reference: WPF `MainWindow.DeeperSubmissions.cs` and `Services/Notifications/NotificationService.cs` at base `db079bf91474c77a5ea67ec306f6337a7de44b83`. Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing toast service lineage includes `df86ebdd9` and `034b993cf`; the existing hub/badge port includes `e9c279fd1`. Existing base files were adapted in place.

## Gap audit

The ledger's actual remaining gap was WPF's sticky acceptance notification. That requires no expiry timer, a stable `deeper_submission_accepted_` + catalogue-id key, active-key deduplication, pre-host queuing, persisted × dismissal, and action cleanup without permanent dismissal. All are now reached by the existing startup/tab status-poll path. A separate WPF behavior missing from the head was library-title lookup before filename fallback; that is restored against the unfiltered index. This was not a pure dependency-only row.

The merged historical claim that badges were missing was stale: `DeeperTabViewModel.ResolveSubmissionBadge`, submit refresh and poll refresh were already wired on the base. Success/Duplicate recording (default pending status, timestamps, accepted duplicate suppression), authentication checks, startup forced polling, tab-open throttle, status persistence and the one-time `AcceptedNotified` flag already matched the reference. They are reused.

The new toast is a Border inside the existing in-shell NotificationHost, with no new window, overlay, media or capture surface requiring panic registration. WPF's unused programmatic `Dismiss(key)` API was not imported.

## Verification limits

SDK: .NET 10.0.402, installed with the requested dotnet-install command (exit 0).

All builds used `dotnet build <project> -c Release --nologo` with `-p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true`. The WPF test build also used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Generated test outputs were removed after each build to fit the cloud disk; initial attempts that ran out of disk were retried successfully.

| Project | Exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | `0 Error(s)` |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | `0 Error(s)` |

- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --nologo`: exit 0. Tail: `Failed: 0, Passed: 3553, Skipped: 5, Total: 3558` (47 seconds). The five existing Vosk tests skip without the unavailable speech model; no tests were altered or disabled.
- `bash .github/scripts/core-guards.sh`: exit 0. Tail: `checked 429 XAML type references against 1632 app types / 2073 Core types`; `All Core guards passed.`
- `git diff --check`: exit 0. The single ledger id remains unique. Final added-line count is recorded in the commit report.
- Checkout: original snapshot was clean but at a different commit; its fetch failed with read-only `FETCH_HEAD` (exit 255). The authorized clone fallback was verified clean at the exact expected base before creating the unit branch.

The cloud snapshot does not contain the cloud-test-fonts lane: the shared test bootstrap has no font setup, and this environment is documented to fail font creation. Per the task's environment limit, Avalonia tests are compiled but not run here; runtime red/green proof is pending the supervisor's GitHub rcheck. Relevant filter: `FullyQualifiedName~NotificationServiceTests|FullyQualifiedName~DeeperSubmissionStatusTests|FullyQualifiedName~DeeperHubTests|FullyQualifiedName~CatalogueShellTests|FullyQualifiedName~CatalogueSubmitWireTests`. The added integration assertions would reject the old timed toast and filename-only message, but are not claimed fail-proven in this environment.

Windows-specific code and the WPF head cannot be run on Linux. No live catalogue endpoint, desktop notification, camera or real user profile was exercised. Vosk model download is blocked (403); the Core suite's result below records its actual skips.

## Assumptions

- Match WPF's once-only `AcceptedNotified` lifecycle exactly: sticky means no expiry within the session; it does not introduce a durable notification inbox across launches.
- General catalogue preset/session/mod sticky notifications belong to `shell-catalogue-submissions`; this unit wires Deeper acceptance only.
- Keep the existing toast presentation and 180 ms entry / 220 ms exit timings while adding sticky lifetime semantics.

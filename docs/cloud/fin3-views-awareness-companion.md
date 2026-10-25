# Awareness Companion hosting

- Move the original live privacy card to Awareness before Live Pulse, and its original tuning cell into the collapsed fine-tuning drawer before Custom Triggers. Keep the existing per-zone viewmodel and synchronize on hosting/show; repeated navigation creates no copies.
- Route the privacy card’s fine-tuning command, room workshop anchor and hero awareness link to the hosted controls. Exclude the moved tuning cell from the v2 Workshop, retaining the legacy layout when v2 is disabled.
- Transfer refresh-clock ownership to Awareness; hidden tabs stop refreshing. Register `awareness-companion` panic teardown to stop clocks and disarm wipe confirmation.
- Add `AwarenessCompanionHostingTests`, update the existing privacy refresh regression, and add numbered tester steps. Consolidate this ledger ID to one honest `stub` row.
- Source: adapted the WPF reference on the required base (`39260f198fad67d90bc988c63a00480830e5fefc`), especially `AwarenessTabView.Companion.cs:11-28`, `AwarenessTabView.xaml:136,599-601`, and `CompanionRoomView.xaml.cs:271-291`. Fetched read-only `parity/trunk` at `a4ee52d40`; no trunk implementation was imported.

## Verification limits

- Required SDK: .NET 10.0.402. Headless tests use the shipped font setup and a sandbox profile; no real desktop/window-title access.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~AwarenessCompanionHostingTests|FullyQualifiedName~AwarenessPrivacyCardTests|FullyQualifiedName~PanicSurfacesTests'` with `DiffEngine_Disabled=true`: exit 0; **14 passed, 0 failed, 0 skipped**. The new suite alone also passed all 5 tests.
- Six targeted production-removal runs each exited 1 with exactly one failing test: remove privacy hosting → original-zone identity assertion; remove fine-tuning callback → drawer expansion assertion; remove room-anchor navigation → current-tab assertion; remove hero navigation → current-tab assertion; remove hidden-tab refresh stop → stopped-clock assertion; remove panic registration → missing registry entry. Production was restored before the final 14-test green run.
- `dotnet test Tests/CCP.Core.Tests -c Release`: initial exit 1 (3,952 passed, 1 failed, 5 Vosk skips). Unchanged `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected Loops but got Clips; isolated rerun passed. Full `--no-build` rerun: exit 0; **3,953 passed, 0 failed, 5 skipped**.
- `dotnet build CCP.Core -c Release` and `dotnet build CCP.Avalonia -c Release`: exit 0, zero errors. All 12 Linux test projects named in `.github/workflows/build.yml` built with exit 0 and zero errors: CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings and Core.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, **0 errors** (557 existing warnings). WPF compiled but was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- Earlier overlapping builds produced a file-lock failure, resolved by a sequential rebuild. A broader combined run including `CompanionZonePillsTests` encountered the worker’s known Segoe UI font failure. The touched privacy tests now use the shipped font bootstrap; the final focused run above passed. The older zone-pill suite was not successfully exercised.
- The base does not contain `.github/scripts/test-plan-coverage.sh`; numbered coverage was added to the test plan.
- WPF runtime and actual desktop interactions cannot be run on this Linux worker. No live AI or Vosk model download was used.
- Remaining parity: the hosted card still uses the existing legacy observer. WPF v2 projected wire JSON, known-app ledger/history and consent-review activation remain missing. The row stays `stub`, never `verified`.

## Assumptions

- Reuse this head’s existing live per-zone viewmodel rather than introduce WPF’s aggregate room viewmodel merely for hosting.
- A static in-tab host needs panic clock/confirmation teardown, but does not introduce an overlay or notification window.

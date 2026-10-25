- Add the Avalonia modal host over existing Core `StartupQueueCore`: priority/FIFO ordering, keyed deduplication, await-close lifetime, WPF settle/poll/five-minute wait, frozen parked time, and passive offers deferred until drain.
- Wire successful account profile loads to the priority-40 required-name ask. Recheck identity/name when shown; defer session/lock holds to a later load or launch; ask once per account per launch. Save through the existing Core account rename service.
- Move WPF `NeedsDisplayNameRule` unchanged into Core and port its pure tests to the Linux suite. Adapt the existing Avalonia dialog with required mode, sanitized suggestion, validation, save refusal/retry and close-for-next-launch behavior.
- Register startup modals in PanicSurfaces and SafetyCritical; panic drops queued modals and closes the active required-name ask.
- Add ledger row `host-modal-triggers` and numbered tester steps. Part-a is the host plus one caller. Remaining migration: age gate, failed-update report, first-run wizard, welcome-back, What's New, recap, mod picker, enhance nudge and update-available. Their existing paths remain outside this host.
- Source credit: WPF reference at base `39260f198fad67d90bc988c63a00480830e5fefc`. Fetched read-only parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. The hinted `push/*.body` files were absent.

## Verification limits

- .NET SDK 10.0.100 installed using the pinned installer version (channel discovery failed HTTP 403).
- `dotnet build <project> -c Release`: exit 0 for CCP.Core, CCP.Avalonia and all workflow test projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Snapshot.Tests. CatalogueStartup initially hit MSB4166 during overlapping builds; sequential `-m:1` retry passed with zero errors. An overlapping focused test build also hit AVLN9999 file locking; sequential rebuild passed.
- WPF test project: `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, zero errors. Windows code compiled only; Windows tests and desktop behavior were not run on Linux.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release -m:1`: exit 0; 3976 passed, 5 skipped, zero failed. Existing Vosk model-dependent skips remain (download unavailable).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release -m:1 --filter FullyQualifiedName~StartupModalTests`: exit 0, four passed. Tests use the existing sandbox profile and headless dispatcher; no desktop/profile access.
- Negative proof: temporarily reverse the production `Queue.Enqueue` priority sign. Ordering test exits 1: expected `first`, actual `later`. Restore the line and rerun all four startup tests: exit 0.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~PanicSurfacesTests`: exit 0, six passed.
- Combined StartupModalTests/PanicSurfacesTests/InboxFlyoutTests run: exit 1, seven passed and six failed with the known glyphTypeface creation error (Inter / Segoe UI Black; bootstrap order). Separate startup and panic processes passed; combined-font stability and Inbox UI regression need CI. Initial development run also caught the panic intake-first ordering contract and a test missing the deferred TextChanged dispatch; both corrected without weakening assertions.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. Test-plan coverage script is absent on this base.
- No live sign-in/server account, visual side-by-side comparison, full Avalonia suite, or Windows execution. Other workflow test projects were compiled, not executed.

## Assumptions

- Deliver the core host and restored-profile caller as part-a to keep this unit small; migrating every WPF caller and missing surface is follow-up work.
- Existing Core rename errors provide the retry explanation; a separate name-availability probe from WPF is not introduced in this unit.
- Required mode uses a 300 px dialog to fit wrapped body copy and refusal text; legacy rename/delete sizing stays as shipped.
- Panic dismisses this onboarding modal and cancels queued startup offers as a safety improvement; closing it does not sign the account out.

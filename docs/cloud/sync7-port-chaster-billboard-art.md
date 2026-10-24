# sync7-port-chaster-billboard-art

- Audited WPF commit `ab9a293a946143afb3ef4f6ee96872324e5e15a1` against base `39260f198fad67d90bc988c63a00480830e5fefc` using `git show --stat` and the full diff.
- **ab9a293a9: blocked-by-host** for the Avalonia calendar art. The shared Core portion is already covered; no production code was changed or sibling work imported.
- Fetched the read-only `parity/trunk` source, observed at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported, so there are no imported commit credits.

## Already-covered Core portion

| Behavior from ab9a293a9 | Existing evidence on the required base |
| --- | --- |
| Current UTC month's raffle card wins; otherwise use a successful verify count | `CCP.Core/Services/Billboard/EventCards.cs:15` (`RaffleReading.From`) |
| Real counted days are filtered and represented as bits; bit 0 means day 1 | `CCP.Core/Services/Chaster/ChasterRaffle.cs:65`, `:72`, `:92` |
| Count-only and unread readings emit mask zero, preserving count independently | `CCP.Core/Services/Billboard/EventCards.cs:42`, `:48`, `:80` |
| Last raffle card and change event are retained by the service | `CCP.Core/Services/Chaster/ChasterService.Ladder.cs:91`, `:106` |
| Fresh verify reads the card, including after pushes and delayed retry | `CCP.Core/Services/Chaster/ChasterService.Ladder.cs:146`, `:165`, `:250` |
| Page reads publish the same card | `CCP.Core/Services/Chaster/ChasterService.Ladder.cs:208` |
| Billboard adapter observes raffle changes and uses the shared reading | `CCP.Core/Services/Billboard/BillboardAppProviders.cs:329`, `:333` |

## Missing host

The WPF change modifies `BuiltInArt.CalendarData`, `CalendarInfo.IsLit`, and both the today-cell and ordinary-cell rendering in `CalendarArtView`. None of those calendar art types exists in the Avalonia head. `CCP.Avalonia/Controls/Billboard/BillboardArt.cs:12` supplies only a registry; `CCP.Avalonia/Views/Windows/MainShellWindow.DashboardBillboard.cs:113` registers only showcase clip art. `CCP.Avalonia/Controls/Billboard/BillboardCardHost.cs:450` tries the registry and otherwise uses a poster or plain ground. Its header explicitly identifies drawn art views as a later slice.

Required prerequisite: an Avalonia built-in calendar art renderer registered for `CardArt.Calendar`, with its payload parser and drawing lifecycle. A registry and slide container alone cannot implement a correction to nonexistent calendar cells. Per the unit's missing-host rule, this lane does not invent that surface or import the wider built-in art framework.

Once that prerequisite lands, adapt the WPF parser's optional mask and month clamp, preserve its legacy first-N-days fallback for callers without a mask, and make both cell paths use `IsLit(day)`. Locktober already supplies an explicit zero mask when dates are unknown, so its progress bar must retain the count while no date is guessed. Verify sparse dates, day 31, invalid days, explicit zero mask versus absent mask, and the today cell.

## Verification limits

Validation used SDK 10.0.402 and Release configuration with `DiffEngine_Disabled=true` for tests. No new behavior or surface was added: production-line reversal proof and new PanicSurfaces registration do not apply. No ledger rows were added or promoted, and neither the parity document nor sync ledgers were edited. The base has no `.github/scripts/test-plan-coverage.sh`.

## Assumptions

- The instruction to report a missing host applies to the missing calendar art renderer even though the generic billboard container exists.
- Existing Core behavior is left unchanged, as requested for already-covered portions. This document records a blocked unit, not a completed visual port.

## Commands and results

- Checkout verified clean at the exact required base after fetch/reset; local snapshot branch name was `work`.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” The first sandboxed invocation could not create temporary files; rerunning with filesystem write permission passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-restore`: exit 1; 3,952 passed, 1 failed, 5 skipped, 3,958 total. The unchanged base fails `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` at line 185 (expected `Loops`, actual `Clips`). The five existing Vosk tests self-skip without the model; no tests were changed or disabled.
- Avalonia headless tests (including `ChasterLadderTests` and `BillboardAppProvidersTests`) were compiled, not run, per the supplied cloud font limitation. No Windows runtime or desktop tests were run on Linux.
- `git diff --check`: exit 0.

Build command for each project below: `dotnet build <project>/<project-name>.csproj -c Release --nologo`. Each Linux build exited 0 with 0 errors; existing warnings remain.

| Project | Result |
| --- | --- |
| `CCP.Core` | exit 0, 0 errors |
| `CCP.Avalonia` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.Language.Tests` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.Migration.Tests` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.Report.Tests` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests` | exit 0, 0 errors |
| `Tests/CCP.Avalonia.Tests` | exit 0, 0 errors |
| `Tests/CCP.Core.Content.Tests` | exit 0, 0 errors |
| `Tests/CCP.Core.Settings.Tests` | exit 0, 0 errors |
| `Tests/CCP.Core.Tests` | exit 0, 0 errors |

The conditional WPF build also succeeded: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release --nologo -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors. This compiles the Windows reference and tests; they were not run on Linux.

BLOCKED: Avalonia built-in calendar art renderer and payload parser are absent; the remaining visual portion of ab9a293a9 needs that host.

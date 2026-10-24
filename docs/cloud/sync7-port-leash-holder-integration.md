# sync7-port-leash-holder-integration

- Audited the five requested WPF commits against base `39260f198fad67d90bc988c63a00480830e5fefc`; recorded missing hosts below. No runtime changes are possible within this unit's explicit instruction not to invent missing surfaces.
- Preserved the existing Core short-span formatter and merged localization. No sibling feature was imported, and no parity ledger was changed.
- Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; imported no trunk code. The source behavior is credited to the WPF hashes in the table.

## Commit disposition

| WPF commit | Status | Evidence and remaining integration |
| --- | --- | --- |
| `a12bb6c533bc1a5915dbded9ab5cd1c1590b6c76` | blocked-by-host | Missing Avalonia `LeashAskCard`, `LeashSelfCard`, their level pickers and `LeashLevelHelp`. `CCP.Avalonia/Views/Tabs/LeashTabView.axaml.cs:6` explicitly documents the empty-only head; `LeashTabView.axaml:21` hosts only `EmptyPanel`. WPF adds the selected tagline below the ask picker and all three summaries below the self picker; its expanded table remains open across redraws. Importing an uncalled guide would not deliver this behavior. Guide copy already exists at `CCP.Core/Localization/Languages/en.json:8205`. |
| `849a3a90d075583795e849c6408c851cfd7caaf4` | blocked-by-host | Missing Avalonia `LeashHolderCard` and `LeashLook.Chunky`, including the four stacked action buttons. Required adaptation: down-only uniform Viewbox around stacked labels, horizontal padding 5, preserving ordinary button layout. The existing receipts component explicitly says it is unhosted until the holder card lands (`CCP.Avalonia/Views/Controls/Leash/LeashSentList.cs:5`). |
| `394ef991344dc6be85f8acbb301d743563d15a62` | blocked-by-host | Missing holder action buttons, Assign/Punish/Reward sheets, sticker shelf and Let go menu. No Avalonia `LeashHelpSlip` / `LeashHelpButton` exists. WPF's help badges open help independently of the send action; Let go uses a hover card with 250 ms initial delay and 60 s duration. Localization alone is not a working help surface. |
| `f9420ab57545dd1549a3e25a422b16f6d19f1c54` | blocked-by-host | Missing the same holder and slip hosts. Final WPF behavior supersedes the preceding row layout: badge on the icon, leaving name width intact; hover opens and click pins; original Praise chip sizing; slip accents follow the action/row with stamp contrast at least 4.5:1; larger pictograms and sticker shelf colors. Revised no-jargon copy is already present at `CCP.Core/Localization/Languages/en.json:8233`, `:8236`, `:8250`. |
| `7d0e25302c52dd0624df90f84fcfab7da32ceca0` | blocked-by-host (Core already-covered) | `CCP.Core/Services/Leash/LeashUiRules.cs:88` already uses `Span` for Chaster size text; `:91` implements minutes, hours and mixed spans, matching WPF. Missing holder Punish/Reward chip rows prevent integrating the credit label `"-" + Span(s)`. WPF reference tests: `Tests/ConditioningControlPanel.Tests/LeashChipLabelTests.cs:19` and `:23`; no production change is needed in Core. |

## Verification limits

- `bash .github/scripts/core-guards.sh`: exit 0; all guards passed (3 existing baselines; 429 XAML references checked).
- `dotnet test Tests/CCP.Core.Tests -c Release --nologo` with `DiffEngine_Disabled=true`: exit 0; 3953 passed, 5 skipped, 0 failed. The existing Vosk tests self-skipped without the unavailable model; no tests were changed or disabled.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~LeashHeadTests --nologo` with `DiffEngine_Disabled=true`: exit 0; 3 passed, 0 skipped, 0 failed. These existing non-rendering tests exercise the leash poll lifecycle, launcher guard and startup wiring; they do not prove the missing UI.
- `git diff --check`: exit 0.
- `.github/scripts/test-plan-coverage.sh` is absent on this base (invocation exits 127).
- Localization audit against `f9420ab57`: all 32 help-slip entries match in all ten languages. Guide entries match in nine languages; eight Italian guide entries retain the base's equivalent existing wording (base files win).
- Rendering/font-dependent Avalonia suites and Windows execution were not run. No Vosk model download was attempted because the environment reports that endpoint blocked.
No new behavior or surface is introduced, so production-line revert proof, new behavior tests and PanicSurfaces registration are inapplicable. No ledger rows are added or promoted.

### Build commands

`dotnet build <project> -c Release --nologo` completed with exit 0 and 0 errors for:

- `CCP.Core/CCP.Core.csproj`
- `CCP.Avalonia/CCP.Avalonia.csproj`
- `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
- `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
- `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
- `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`
- `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
- `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
- `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
- `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
- `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`
- `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
- `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
- `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`

`dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release --nologo -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; 557 warnings, 0 errors. WPF and Windows-specific code compiled but were not executed on Linux.

Existing compiler/analyzer warnings remain. SDK: .NET 10.0.402.

## Assumptions

- Treat a missing holder/ask/self card as the explicitly permitted blocked-by-host outcome. The existing gate, punishment window, cut confirmation, hold ring and standalone receipt list do not substitute for those cards.
- Keep this unit documentation-only rather than introducing unused controls or re-porting the sibling drawer/card feature.

BLOCKED: Avalonia holder, ask and self cards and their level-picker, sheet and help-slip hosts are absent on the required base.

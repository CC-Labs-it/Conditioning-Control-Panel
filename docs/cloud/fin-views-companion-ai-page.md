- Finish `views-companion-ai-page` as `wired` (never `verified`), removing the stale glyph gap and superseded borrow/give-back history. Exactly one ledger row remains.
- The required base is `888cdbffe4d2ecf3967cee4a4334557ee533e54d`. Its existing `SectionTabStrip.BuildPills` already creates a Fluent `IconGlyph` from `IconMap.ForTab("companionai")`; the map already supplies Bot/Regular. No production code or new panic surface is needed.
- Expand `CompanionAiPageTests` to invoke the real pill and assert its glyph, selected state, exact live hosts, memory order, fresh controls on revisits, and missing-room/no-brain fallback. Preserve the existing expansion, settings refresh, adoption and hide/disarm assertions.
- Add a font-independent `IconMapTests` case pinning the AI pill's established Fluent adaptation and MDL2 mapping. These are regression assertions for behavior already present at the base, not a production fix; no fail-before claim is made.
- Fetched the read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Reference behavior originates in `48f587200` (AI Connection/Memory page), `9db154ab6` (open Behaviour/Triggers), and `5681c132a` (permanent live adoption); navigation glyph reference is in `28c84cbc3`. No trunk code was imported or existing head file overwritten.

## WPF behavior audit

The ledger's effective last row on the base lists only the pill glyph as missing. All page behavior is already reachable on this head; the glyph note predates the shared strip port.

| WPF contract | Existing Avalonia user path |
| --- | --- |
| Companion AI pill opens the page and shows a leading glyph | `MainShellWindow.SectionChrome` routes `SectionTabStrip.TabRequested` to `ShowTab("companionai")`; `IconMap.ForTab` supplies the Fluent icon. |
| Each show refreshes the account before creating name/recap | `MainShellWindow.TabNavigation.OnTabShown` calls `AiPage.OnShown`, which calls `EnsureCurrentAccount` and replaces both controls before the room lookup. |
| Connection is the live drawer, re-synced and expanded | `AiPage.OnShown` synchronizes `EngineRoomVm`, sets `IsExpanded`, and adopts `EngineZone` into `EngineHost`. The existing drawer retains its provider/model/sampler/test handlers. |
| Behaviour then Triggers remain open | The room's existing Workshop parts are adopted directly into `BehaviorHost` then `TriggersHost`, outside the collapsed accordion. |
| Memory sheet shows name, recap, then live diary | `AiPage.axaml` has `NameHost`, `RecapHost`, `MemoryHost` in WPF order; `OnShown` syncs and adopts the room's diary. Existing name/recap rules are shared Core `ConversationRules`; `ConversationPageTests` exercises name saving. |
| Adoption is permanent; repeat show is safe | `CompanionPageHost.Adopt` detaches the existing control once and returns early when the target already owns it. Named room zones remain resolvable on revisits. |
| Leaving disarms an armed diary forget | `AiPage.OnPropertyChanged(IsVisible=false)` disarms the adopted diary. No zone is handed back to the collapsed room. |
| Missing room still has name/recap; absent brain disables name saving and hides empty recap | Both controls are created before the null-room return, as in WPF. A focused fallback test covers this. |

Remaining WPF behaviors lacking in this row: none found. No dependency-only exception or P122 dead-caller exception is used. No render-all proof is claimed.

## Verification limits

SDK: .NET 10.0.402. Every final build below exited 0 with 0 errors; existing warnings remain. Builds used Release. Initial head/main-test builds used MSBuild hardlink-copy flags; the remaining projects used ordinary `dotnet build <project> -c Release`. The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.

| Build target | Exit / result tail |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 / 0 errors |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 / 0 errors |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 / 0 errors |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 / 0 errors |

All test commands exported `DiffEngine_Disabled=true` and used `-c Release --no-build --no-restore`:

| Command | Exit / result tail |
| --- | --- |
| `bash .github/scripts/core-guards.sh` | 0 / All Core guards passed; 429 XAML references checked |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --filter 'FullyQualifiedName~IconMapTests\|FullyQualifiedName~PanicSurfacesTests&FullyQualifiedName!~EachPanicRouteCallsStopAllExactlyOnce'` | 0 / 22 passed, 0 failed, 0 skipped |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` (initial full run) | 1 / 3544 passed, 1 failed, 5 skipped |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --filter 'FullyQualifiedName~DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls'` | 0 / 1 passed |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` (full rerun) | 0 / 3545 passed, 0 failed, 5 skipped |
| `git diff --check` | 0 / no whitespace errors |
| `git diff --exit-code origin/avalonia-port/cloud-base-companion-v2-pages-b -- CCP.Core Tests/CCP.Core.Tests` | 0 / Core implementation and Core tests unchanged |

The initial Core failure was the unmodified remote-media test named above, at its final refill assertion (`Loops` expected, `Clips` returned). It passed in isolation and on the full rerun. No test was weakened, deleted or newly skipped. The five existing Vosk tests skip because the model is unavailable in this cloud environment (download is documented as blocked).

`CompanionAiPageTests` and `ConversationPageTests` compiled with 0 errors but were **not run**: the task explicitly identifies font creation failures in this cloud's UI-test setup until the cloud-test-fonts lane lands. Supervisor GitHub execution is still needed for the new shell/fallback assertions. No UI runtime, render-all, native media, real-account service or Windows execution is claimed. WPF and Windows-specific code compiled but cannot run on this Linux host.

Infrastructure recovery: the snapshot's initial fetch failed on read-only Git metadata, so work used the authorized fallback clone at the exact pinned base. The initial SDK channel lookup received a CI-feed 403; the public release feed installed 10.0.402. A first multi-project build exhausted cloud disk while copying generated runtime files; removing only generated outputs and rebuilding one project at a time completed all targets successfully.

## Assumptions

- Preserve the existing Fluent Bot/Regular adaptation of WPF's MDL2 connection signal. The task requires Fluent icons and the base already maps both identifiers consistently.
- Treat the shared strip's already-landed glyph as resolving the stale ledger gap; avoid a redundant production rewrite.
- Follow the stated cloud font limitation: compile UI tests, leave their execution to the supervisor's GitHub runner, and run font-independent cases locally.

# wire-views-deeper-new-enhancement-dialog

- The HypnoTube/TikTok button now selects the first case-insensitive TikTok title from the live known-video table instead of always using a literal URL. Unavailable/no-match tables retain WPF's fallback, and the existing seen-hint settings write remains intact.
- Extracted this selection rule into Core `HypnotubeDefaultLinks.SelectTikTokTutorialVideo`; both WPF and Avalonia call it, preserving insertion order and best-effort fallback behavior.
- Headless tests exercise the actual HypnoTube button and the existing native-storage seam for both video and audio Browse/Create: remembered folder, picker ownership, filters, selected local path and media type. No real desktop picker is opened.
- Ledger row stays **stub**. Removed misleading comments that claimed CoreTutorial was unseeded or had no event bus: both exist on BASE. Full walkthrough work includes native partial input regions and automatic control/event advancement in the legacy overlay, plus editor continuation; porting that with focused coverage exceeds this lane's 1000-line budget. Merely opening that legacy overlay would obscure Browse/Create, so tutorials remain unwired.
- Read-only `parity/trunk` inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files imported. WPF reference behavior was adapted from BASE `0e1aa0473e7a5b6120544923e94bb04cc62a7e50`.

## Verification limits

Commands used SDK 10.0.402 via `PATH=/workspace/.dotnet:$PATH`, `DiffEngine_Disabled=true`. Headless tests/rendering used the temporary fontconfig selecting system DejaVu/Liberation/Noto fonts because this host's normal fallback chose an unsupported WOFF2 font. Repository test sandboxes and a scoped proxy of Avalonia's existing native-storage interface protect real profiles and the desktop.

- Before the fix, `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~NewEnhancementSourceTests` produced **1 failed, 2 passed**: injected first TikTok URL was ignored and the literal appeared instead. Both existing picker paths already passed.
- After the fix, the same filter produced **3 passed, 0 failed, 0 skipped**.
- `dotnet test Tests/CCP.Core.Tests`: **2812 passed, 0 failed, 6 skipped / 2818 total**. Five existing Vosk tests self-skip without the model; the existing frame-budget test self-skips in Debug. The new selector tests cover case-insensitive insertion order, missing/no-match tables, and the canonical default table. No tests were weakened, deleted or given new skips.
- Explicit `dotnet build` passed for Core, Avalonia and all Linux test projects listed in `.github/workflows/build.yml`: Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: **Build succeeded, 0 errors**. WPF application and test assemblies are compiled, not run on Linux.
- Builds used `-m:1 -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true` to fit the shared disk. Builds of test assemblies were completed before their final test run.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed.**
- `CCP_USERDATA_DIR=/tmp/worker2-newdialog-render-profile dotnet run --project CCP.Avalonia --no-build --no-restore -- --render-all /tmp/worker2-newdialog-render`: **224/224 views rendered**. `NewEnhancementDialog.png` was inspected; its static layout is preserved. The button's dynamic source selection is proved by the headless test rather than that empty-source render.
- Only the relevant Avalonia tests were run, not its entire suite. No live HypnoTube request, real native picker, real desktop or WPF runtime was exercised.

## Remaining: wire-views-deeper-new-enhancement-dialog-b

- Wire all three interactive tutorial buttons to Part 1/Part 2 names using the already seeded TutorialHead and existing CoreTutorialEvents seam.
- Complete the legacy overlay's automatic button/text/selection/slider/event advancement, native partial input regions/click-through and required target preparation so Browse/Create and editor controls remain usable under the card.
- Hide/restore visible tutorial overlays during native file selection; close/unsubscribe Part 1 on Cancel/X; queue Part 2 only after a validated successful Create so empty/failed/cancelled attempts leave no stale continuation.
- Consume the pending Part 2 name once in the editor Loaded path, start after WPF's 800 ms settle delay, stop the delay when the editor closes, and prove each video/audio/HT continuation end to end. Keep only this row's evidence in this lane; editor intro/help work belongs to its own ledger row.

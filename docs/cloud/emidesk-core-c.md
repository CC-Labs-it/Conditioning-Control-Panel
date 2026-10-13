# avalonia-port/cloud-emidesk-core-c: EmiDesk Core move (desk-window rules)

Already in `CCP.Core/Services/EmiDesk` before this unit (earlier units, e.g. 84c18973f, 74a3adead, 7f30f3ff8, bc9e08b33, f669ceb38, 219c5d6d6):
EmiBookCards, EmiBookDeck.*, EmiBookDemos.*, EmiBookLayout, EmiBookText, EmiChrome, EmiCodexChapters, EmiDebug,
EmiDeskChord, EmiDoors, EmiMuteRule, EmiPixel, EmiProps, EmiRingLayout, EmiState, EmiSuggester.

This unit moves the next coherent group, the pure rules the desk window uses to place and keep her on screen:

- `git mv` `ConditioningControlPanel/Services/EmiDesk/{EmiTopmostRule,EmiBubblePlacement,TubeDeskVisibility}.cs` -> `CCP.Core/Services/EmiDesk/`
  (unchanged content and namespace; no App.* use, so no seam needed; `TubeDeskVisibility` stays `internal`, WPF sees it via the existing `InternalsVisibleTo`).
- `git mv` their tests `EmiTopmostRuleTests`, `EmiBubblePlacementTests` from `Tests/ConditioningControlPanel.Tests` to `Tests/CCP.Core.Tests` (now run on Linux).
- New `Tests/CCP.Core.Tests/TubeDeskVisibilityTests.cs` (4 facts): hide/restore lease, no restore when she was hidden, user dismissal cancels the restore, repeated state is a no-op. The rule had no test before.
- No embedded resources, no core-guards baseline change. WPF callers (EmiDeskWindow.Topmost/.Bubble, EmiDeskService.Tube) unchanged.
- Nothing imported from `parity/trunk` (it has no Core copy of these files), so no trunk commits to credit.

## Next units (not moved yet)
- `EmiNames` (pure text rules, but resolves names through WPF `EmiTargets` + `Loc`): needs a Core label provider seam or the ring catalogue moved first.
- `EmiTargets` catalogue data (split the static list from the MainWindow/App launch actions).
- `EmiChains` (chain data + rules; the `Player` uses `DispatcherTimer` and stays in the head) and `EmiNudges` (rules vs `App.EmiDesk` world).
- `EmiLineEngine`, `EmiOffers`, `EmiChannels.*`, `EmiCodex`, `EmiAlive`, `EmiKnock`, `EmiBarkBridge`, `EmiTourNarrator`: rule halves to Core behind provider Funcs.
- Head-only (stay in WPF): `EmiDeskService*`, `EmiFace`, `EmiCardFace`, `EmiBookGlyphs`, `EmiPixelBitmap`, `EmiGifRain`, `EmiSfx`, `EmiVox`, `EmiBook` (dispatcher glue).

## Verification limits
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1384 passed, 5 skipped, 2 failed; the 2 failures are sandbox-only and untouched by this unit
  (`LoopbackOAuthTests ... host: "[::1]"`: no IPv6 loopback here; `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: runs as root, so the "undeletable" folder is deleted).
  Moved/new classes alone (`EmiTopmostRule|EmiBubblePlacement|TubeDeskVisibility`): 13/13 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter EmiDesk`: 9/9 passed.
- `dotnet build`: CCP.Core, CCP.Avalonia and every Tests/* csproj succeeded; `Tests/ConditioningControlPanel.Tests` (and so the WPF app)
  built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- Not run: the WPF app and WPF test suite (Windows only); the Win32 topmost watch that calls `EmiTopmostRule` was compiled, not run.

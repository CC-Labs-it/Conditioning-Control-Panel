# refactor(core): EMI line engine, names, bark mirror and three small desk rules move to CCP.Core

Unit B of the `ConditioningControlPanel/Services/EmiDesk` Core move. Base: `avalonia-port/leash-gate-card`.

## Already in CCP.Core/Services/EmiDesk on the base (not touched)
EmiBookCards, EmiBookDeck.*, EmiBookDemos.*, EmiBookLayout, EmiBookText, EmiChrome, EmiCodexChapters,
EmiDebug, EmiDeskChord, EmiDoors, EmiMuteRule, EmiPixel, EmiProps, EmiRingLayout, EmiState, EmiSuggester.
None of the files moved here had a Core copy, so nothing is duplicated.

## What changed
- `git mv` WPF -> Core (renames, behaviour unchanged): `EmiLineEngine.cs`, `EmiNames.cs`, `EmiBarkBridge.cs`,
  `EmiBubblePlacement.cs`, `EmiTopmostRule.cs`, `TubeDeskVisibility.cs`.
- The `App.*` reads became small Core seams, set once in WPF `App` (next to the CoreBark seam, all lazy):
  - `EmiLineEngine.AskSituationProbe` (was `App.EmiDesk?.AskSituationOk()`), `EmiLineEngine.EffectFeasibleProbe`
    (was `EmiOffers.EffectFeasible`); unset = no offer, exactly as WPF's null desk read.
  - `App.Settings?.Current` -> `CoreSettings.Current` (defaults match the old null fallbacks: spice 2, offers on);
    `UpdateService.AppVersion` -> `CoreReleaseContent.AppVersion` (WPF already seeds it from UpdateService).
  - `EmiNames.TargetLabelProbe` (was `EmiTargets.Find(id)?.Label`).
  - `EmiBarkBridge.DeskFire` (was `App.EmiDesk` / `.Fire`, asked per mirror because the desk comes and goes) and
    `EmiBarkBridge.VideoTitleProbe` (was `App.Video?.LastVideoTitle ?? LastVideoPath`).
- The lines file (`Assets/emi/desk-lines.json`) is a Content file read from disk, not an embedded resource, so no
  csproj or core-guards baseline change was needed.
- Tests: `EmiLineEngineRotationTests`, `EmiBubblePlacementTests`, `EmiTopmostRuleTests` moved (`git mv`) to
  `Tests/CCP.Core.Tests`. New `EmiCoreSeamsTests` pins each seam (unset = no offer / no fire; probe consulted);
  fail-proven by forcing the situation gate open (the ask test went red, restored).
- `EmiMomentIdWiringTests` (WPF) still finds `EmiBarkBridge.cs` by file name through `SourceRoots`, which already
  scans `CCP.Core/`.
- Seam shapes follow trunk `69024b7a3` (parity/trunk, "the moment bus - line engine, names and bark bridge in
  Core"), adapted: trunk copied the files, here they are moved; trunk's `EmiDeskBus` was not imported (nothing
  on this base would call it yet), the bridge's two seams sit on `EmiBarkBridge` itself.

## Next units (remaining in ConditioningControlPanel/Services/EmiDesk)
- emidesk-core-c: `EmiSfx` rules, `EmiVox`, `EmiTourNarrator` (logging/paths only).
- emidesk-core-d: the nudge machine and the knock rules (`EmiNudges`, `EmiKnock`; trunk `bf7785067`, `eeb2773b8`).
- emidesk-core-e: the pure half of `EmiOffers` (effect feasibility/catalogue; trunk `c8fba0005`) and the
  `EmiTargets` catalogue minus its brushes.
- Later (WPF-bound, split first): `EmiChains` player (Dispatcher), `EmiFace` parser, `EmiBookGlyphs`,
  `EmiCodex` (WebView2), `EmiChannels.*`, `EmiAlive`, `EmiCardFace`, `EmiGifRain`, `EmiBook`, `EmiPixelBitmap`,
  `EmiDeskService*`.
- No Avalonia UI in this unit; the Avalonia desk can now wire `EmiLineEngine` / `EmiBarkBridge` in a later unit.

## Verification limits
- `dotnet build`: CCP.Core, CCP.Avalonia and every Linux test csproj in build.yml (Core.Settings, Core.Content,
  Avalonia, Avalonia.Consent/Language/CatalogueStartup/Scheduler/Report/GeneralSettings/Snapshot): all succeeded.
- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests`: 1242 passed, 5 skipped, 2 failed. Both failures are environmental and fail
  identically on the untouched base: `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` (the
  container runs as root, so the "undeletable" folder is deleted) and `LoopbackOAuthTests...(host: "[::1]")`
  (no IPv6 loopback in the container). The EMI classes: 19/19 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter Emi`: 46 passed, 9 skipped, 0 failed.
- WPF: `Tests/ConditioningControlPanel.Tests` (and so the WPF app with the new seam wiring in `App.xaml.cs`)
  COMPILES with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, but
  cannot RUN here (no Microsoft.WindowsDesktop.App runtime on Linux). Not run: the WPF suite, including
  `EmiMomentIdWiringTests`, `EmiKnockMachineTests`, `EmiCodexTests`; the WPF desk was not launched.
- The WPF primer (`ConditioningControlPanel/docs/primers/EMI_DESK_PRIMER.md`) still lists the old paths; left as
  is, the WPF tree is the frozen reference.

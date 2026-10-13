# emidesk-core-d: EMI ring scoring maps and EmiNames into Core

Base: `avalonia-port/emi-line-engine`. Branch: `avalonia-port/cloud-emidesk-core-d`.

Already in Core for this area before this unit: `EmiDoors` (the ring's door table: ids, art, hues,
default order) and the `EmiTarget` record. Both heads' `EmiTargets` already build from it, so the
static catalogue list was split from the launch actions in an earlier unit; what was still WPF-only
was the scoring maps and the naming rules.

- **Tab / rack scoring maps -> `CCP.Core/Services/EmiDesk/EmiDoors.cs`** (`TabTargets`,
  `RackTargets`, with the "why partial" comment). WPF `EmiTargets.NoteTabOpened` /
  `NoteRackOpened` now look up Core; the private WPF copies are gone (one copy of the rule).
- **WPF `EmiTargets.OrderOf` dropped**: nothing in product code called it (the suggester ties on list
  order); its one test assertion now checks `EmiDoors.All[5]`.
- **`EmiNames` `git mv`'d to `CCP.Core/Services/EmiDesk/EmiNames.cs`** (shows as a rename). The only
  head dependency was `EmiTargets.Find(id).Label`; it now reads `EmiDoors` + `Loc.Get("emi_desk_target_<id>")`
  with the same fallback as `EmiTarget.Label`. WPF callers (`EmiBarkBridge`, `EmiDeskService`) are
  unchanged: same namespace, `internal` reached through Core's existing `InternalsVisibleTo`.
  - Deviation from the brief: no label-provider `Func` seam. Trunk `69024b7a3` ("the moment bus -
    line engine, names and bark bridge in Core") moved EmiNames with a `TargetLabelProbe` seam; on our
    tree `Loc` and `EmiDoors` are both Core already, so the seam would have one value-equivalent
    implementation per head. Direct lookup also names doors the Avalonia ring hides (arcademy, fyp...).
    Credit: trunk `69024b7a3` for the move shape.
- **Tests**: the two map invariants (`Tab_and_rack_maps_only_point_at_real_targets`,
  `No_door_is_counted_twice`) moved from WPF `EmiRingCatalogueTests` (reflection on private fields) to
  `Tests/CCP.Core.Tests/EmiDoorsTests.cs`, plus a case-insensitivity test. New
  `Tests/CCP.Core.Tests/EmiNamesTests.cs` covers Feature (door, tab->door, nav-rail name, unknown ->
  null), VideoName, Achievement and XpSource. It pins one shipped quirk rather than fixing it:
  `Feature("settings")` returns the Settings door name because the ring is asked before the
  `tab_dashboard` row (MOMENTS 4.E says "settings" is HOME) - same behaviour in WPF.
- Stale Avalonia comment in `MainShellWindow.Presets.cs` updated (the rack map is in Core now).

## Next units
- Avalonia: score non-ring opens (`ShowTab` -> `EmiDoors.TabTargets`, `OpenStudioModule` ->
  `EmiDoors.RackTargets`, through `EmiState.NoteUsage`), with headless tests.
- Decide the `Feature("settings")` HOME vs Settings-door quirk (owner call), then fix in Core once.
- Continue the EmiDesk move: `EmiChains`, `EmiTopmostRule`, `EmiBubblePlacement` pure rules, and the
  `EmiBarkBridge` payload mapping (trunk `69024b7a3` has a Core bark bridge to adapt).

## Verification limits
- Installed .NET SDK 10.0.112 from Ubuntu apt (the dotnet-install host is blocked by the egress policy).
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1433 passed, 5 skipped, 2 failed. Both failures are environmental
  and in files this unit does not touch: `LoopbackOAuthTests ... host: "[::1]"` (no IPv6 loopback in
  the container) and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so
  the read-only folder can still be deleted). All 7 new tests pass.
- `dotnet build -c Release` succeeded for CCP.Core, CCP.Avalonia, CCP.Core.Settings.Tests,
  CCP.Core.Content.Tests, CCP.Avalonia.{Consent,Language,CatalogueStartup,Scheduler,Report,
  GeneralSettings,Snapshot}.Tests and CCP.Avalonia.Tests.
- `dotnet test Tests/CCP.Avalonia.Tests --filter EmiRing`: 9/9 passed (ring catalogue / wiring / picker).
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: Build succeeded (so the WPF app compiles
  with the moved EmiNames and the EmiTargets edits).
- NOT run: the WPF tests (`EmiRingCatalogueTests`, `EmiCodexTests`) - the test host aborts on Linux
  for lack of the Microsoft.WindowsDesktop.App runtime. WPF runtime behaviour is unverified here.

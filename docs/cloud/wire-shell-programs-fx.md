# wire-shell-programs-fx

- Restore the generated reward-rail halo and day-complete ignite from WPF `ProgramsTabView.xaml:626-698` and `MainWindow.ProgramsTab.cs:779-867`.
- Today's Warm+ halo follows the WPF 1.2-second sine half-cycle: opacity .35-.95, scale .9-1.12. Reduced/Off preserves the static .65 halo; hide, minimise, detach and motion changes stop or re-arm the shared 30 fps beat.
- A completed day's own node gets the separate .9-second flare (opacity 0 -> .95 at .18s -> 0, scale .6 -> 1.35 cubic-out). It survives Reduced, is consumed silently at Off, and cannot replay on a gate change or ordinary rebuild. Existing Core ProgramHeat and the persisted completion key remain the decisions; no engine logic or WPF code was duplicated.
- Add focused gate, phase, cancellation and replay assertions to `ProgramIgnitionTests`; keep all existing tests. The read-only fixture bytes remain unchanged. Its headless setup uses the shipped font builder.
- No trunk code imported. `parity/trunk` was fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.

## Follow-up: wire-shell-programs-fx-b

Keep ledger row `shell-programs-fx` at **stub**. Remaining work: task/seal/graduation spark bursts through the absent shared event-burst shell plumbing, day-complete banner spring, chapter-seal BackEase landing (currently linear), and live program-session progress/clock/sheen (the bar has markup but no live driver). Task-card completion pop also remains absent in the template. Those span the event layer and live session strip; this slice is the coherent reward-rail implementation. These are ordinary program-tab decorations, which WPF panic does not close; the existing program-session panic surface remains responsible for the engine run, and no new safety-critical surface is added here.

## Verification limits

- Regression proof: remove only `ApplyRailPipFx()` calls; 3 new cases fail (halo .65 instead of .35; Full/Reduced ignite remains dark), while Off correctly remains dark. Restore the calls; `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~ProgramIgnitionTests` passes 7/7, no skips.
- `bash .github/scripts/core-guards.sh`: all passed (424 XAML references; 1591 WPF / 1880 Core types).
- `dotnet build <project> -m:1 -p:UseSharedCompilation=false`: Core, Avalonia and all eleven Linux test projects in `.github/workflows/build.yml` succeeded (13 builds total; existing warnings remain).
- `dotnet test Tests/CCP.Core.Tests --no-build` with the pinned model and `CCP_REQUIRE_VOSK=1`: 2815 passed, 0 failed, 1 existing Debug-only skip of the Release frame-budget test.
- `dotnet run --no-build --project CCP.Avalonia -- --render-all /workspace/scratch/worker-d-programs-render-all`: 224/224 rendered, no binding errors found; the isolated showcase manifest refusal is expected. `ProgramsTabView.png` is the browse state. The focused test's real run view at the halo peak is `Tests/CCP.Avalonia.Tests/bin/Debug/net10.0/.pi/program-pips/program-pip-breath.png`, captured and visually inspected. Generated PNGs are not committed.
- The unchanged WPF reference/test target already failed its cross-target attempt in this session with CS1566 reading `ConditioningControlPanel.g.resources` (`OutOfMemoryException`) under the 1.5 GiB compiler heap cap. WPF and Core subtree hashes match that attempt; it is not repeated for this Avalonia-only slice. Windows desktop/native browser execution was not run. No source/test/resource changes were made to bypass that build failure.

Commands use `DiffEngine_Disabled=true`, `DOTNET_GCHeapHardLimit=0x60000000`, `DOTNET_gcServer=0`, `-m:1 -p:UseSharedCompilation=false` and the four MSBuild hardlink-copy properties on this shared host.

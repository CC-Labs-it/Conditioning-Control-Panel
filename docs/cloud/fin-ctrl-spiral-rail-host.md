# fin-ctrl-spiral-rail-host

- Audited `ConditioningControlPanel/Controls/SpiralRailHost.cs` against the existing Avalonia host at pinned base `64ec931211d916628e405e4c4a27b980cdc531c4`.
- Finish this row under P122: WPF retired the medallion's mount; do not resurrect an obsolete rail entry or connect the dormant Avalonia host to a new publisher.
- Preserve the existing native host, Core gates/numerals, shared-clock glyph, tooltip and navigation implementation. No runtime behavior or new surface is introduced, so no new behavior test or panic registration is needed.
- Reference fetch: `git fetch origin parity/trunk` succeeded at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; existing base files win.

## WPF behavior and remaining differences

| WPF behavior | Existing Avalonia behavior / disposition |
| --- | --- |
| Initially collapsed; 56px host, margins 0/6/0/2, hand cursor, centered 40px badge and 32px native glyph | Already implemented; retained dormant in the shell. |
| Gate on enabled flag, live descent block/service, and migration withhold; hide on withdrawal | Core `SpiralRailState.Visible` implements every gate combination; Avalonia `Apply`/`Arm` implements visibility and withdrawal. No production feed, because WPF no longer mounts this host. |
| Loaded calls idempotent `Wire`, subscribes to `Descent.BlockChanged`, and immediately arms; later block/withhold changes re-arm without polling | Remaining implementation difference: Avalonia Loaded calls `Arm`, with no service subscription. This entire instance lifecycle is unreachable in live WPF; adding a publisher would revive retired behavior. |
| Apply stage/progress to the native glyph; tooltip is stage name, newline, flavor | Already implemented. Hidden updates preserve the old tooltip until the next visible block, as in WPF. |
| Dot for stage <= 0, Roman I-VIII, invariant decimal above VIII | Already shared through Core, including the live WPF `SpiralGlyph` caller. |
| Left release opens the Spiral room when the setting is enabled; the destination rechecks its own gates | Already implemented and covered by the existing shell pointer test using an explicitly fed host. The medallion click itself has no live WPF path. |
| Explicit `RefreshGlyphMotion` forwards to the glyph | Implemented, with no caller in either head. Avalonia glyph already uses the shared beat and motion gate. |
| Native drawing, no browser, no independent request/poll | Already implemented. |
| Defensive exception logging around `Arm`, `Wire`, and `OpenMap` | Remaining implementation difference: Avalonia does not wrap these instance routes in the same debug-log catches. These WPF instance paths are retired as well; no new exception wrapper is introduced. |

There are no remaining live WPF behaviors for this row to wire. This is a retired WPF surface, not a dependency-only NOOP on an unported descent publisher. Profile doors and the Spiral room are separate rows and must not be used to revive the removed rail medallion.

## P122 proof at the pinned SHA

Run against `64ec931211d916628e405e4c4a27b980cdc531c4` (not a moving branch):

```sh
git grep -n -E 'new[[:space:]]+SpiralRailHost|<[^>[:space:]]*SpiralRailHost' 64ec931211d916628e405e4c4a27b980cdc531c4 -- ConditioningControlPanel
git grep -n -E '\.RefreshGlyphMotion[[:space:]]*\(' 64ec931211d916628e405e4c4a27b980cdc531c4 -- ConditioningControlPanel
git grep -n -E 'SpiralRailHost|RefreshGlyphMotion' 64ec931211d916628e405e4c4a27b980cdc531c4 -- ConditioningControlPanel ':(exclude)ConditioningControlPanel/Resources/**' ':(exclude)ConditioningControlPanel/docs/**'
```

The first two commands return no matches (exit 1). The exhaustive third search finds the type/constructor/method declarations, comments and documentation, and `SpiralGlyph.cs:197` calling the pure `StageNumeral` helper; no instance caller. `MainWindow.xaml:746-749` explicitly says Circe's tab took the old spiral medallion's slot and mounts `ChasterRailChip`. `MainWindow.ProfileSpiral.cs:105-112` refreshes only the two profile glyphs. The Avalonia shell's existing `SpiralRail` remains collapsed and unfed, matching the retired WPF surface.

## Verification limits

Linux verification uses .NET SDK 10.0.402 and Release configuration. `DiffEngine_Disabled=true` was set for both Core test commands and render-all.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | 48 warnings, 0 errors. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | 172 warnings, 0 errors. |
| `dotnet build <test project> -c Release` for every Linux test project below | 0 each | All 11 projects compile with 0 errors. |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed; 424 XAML references checked. |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0 | Failed 0, passed 2831, skipped 5, total 2836. |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~SpiralRailStateTests` | 0 | Failed 0, passed 20, skipped 0. |
| `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --render-all <temporary-output>` | 0 | 224/224 views rendered; no binding errors reported. |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -nr:false -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 512 warnings, 0 errors. |
| `git diff --check` | 0 | No whitespace errors. |

The 11 compiled test projects are `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and `CCP.Avalonia.Snapshot.Tests` under `Tests/`.

Avalonia tests, including the two existing `SpiralRailHostTests`, were compiled but **not run**, per the task's documented cloud font-creation limit; the cloud-test-fonts lane is absent from this base. Execution is deferred to supervisor rcheck. The five existing Core Vosk tests skipped because the model is unavailable (download is blocked in this environment); no tests were changed, weakened or disabled. Tests and rendering use isolated user-data sandboxes; the render's main-shell frame was inspected and the retired medallion occupies no rail space.

The original snapshot had the wrong HEAD and a read-only Git directory; work used the authorized fresh-clone fallback, checked out at the required base. Concurrent generated test outputs initially exhausted the temporary disk, interrupting builds, render-all and the first Core suite run. Removing only generated output and retrying sequentially produced the successful Linux results above. The first Core guard attempt also lacked temporary-file write access; the authorized writable retry passed.

The first WPF cross-build was interrupted by the same full disk, and the next attempt reported `MSB4166` (child node exited). The final single-worker retry with worker reuse/shared compilation disabled **passed**, compiling the WPF application and test assembly. Windows runtime behavior and WPF tests were **not run** on this Linux worker. No runtime code changed in either head.

## Assumptions

- P122 applies to the retired instance surface even though WPF's live glyph still calls the pure stage-numeral helper. That helper is already shared with Core and tested.
- Preserve the dormant Avalonia mount from the base; deleting it or reviving it would enlarge the runtime diff without closing a live WPF gap.

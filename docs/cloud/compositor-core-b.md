# compositor-core-b: GlyphFallback moves to CCP.Core

Second Core move out of `ConditioningControlPanel/Services/Compositor`.

## Already in CCP.Core before this unit

- `CCP.Core/Services/Compositor/CompositorLayers.cs` (z-order table, moved in 9cf580bcd).

No other file in `Services/Compositor` had a Core or Avalonia copy. `parity/trunk` has no newer
version of the moved file (it matches be689e47c, #717), so nothing was imported from trunk.

## What changed

- `git mv ConditioningControlPanel/Services/Compositor/GlyphFallback.cs -> CCP.Core/Services/Compositor/GlyphFallback.cs`,
  byte-for-byte (100% rename). It is the compositor's per-face text-run split (#615/#717): pure
  SkiaSharp + `LocalizationManager` (already in Core), no `App.*`, no engine, no WPF. Namespace is
  unchanged, so the WPF callers (`SubliminalLayer`, `BubbleLayer`) need no edit; `internal` still
  resolves through Core's existing `InternalsVisibleTo("ConditioningControlPanel")`.
- `CCP.Core.csproj`: `SkiaSharp` 2.88.8 (managed only), the version the WPF head's
  `SkiaSharp.Views.WPF` 2.88.8 already pins, so WPF ships the same SkiaSharp. The Avalonia head keeps
  3.119.4 (via `Avalonia.Skia`), which wins there. Verified in a scratch app: Core's GlyphFallback
  compiled against 2.88.8 runs Split/Measure/DrawCentered correctly on SkiaSharp 3.119.
- `git mv Tests/ConditioningControlPanel.Tests/GlyphFallbackRunTests.cs -> Tests/CCP.Core.Tests/`
  (rename): namespace -> `CCP.Core.Tests`; the WPF render collection is swapped for a
  non-parallel collection, because two tests switch `LocalizationManager`'s language, which every
  Core `Loc` lookup reads. `CCP.Core.Tests` gets `SkiaSharp.NativeAssets.Linux` 2.88.8 so the
  core-linux job loads libSkiaSharp. The tests read the real font table and assert coverage
  invariants, so they hold on Linux fontconfig as on Windows (15/15 pass here).
- SUBLIMINALS_PRIMER: stale `GlyphFallback.Resolve` pointer -> `GlyphFallback.Split` at its Core path.
- No embedded resources and no new core-guards baseline entries.

## Next units (not moved here)

Every remaining file needs a seam first; in rough order:

1. `ILayer.cs` (`IWpfLayer`) + `BaseLayer.cs`: Skia-only contract, but `BaseLayer` takes
   `CompositorEngine` (call `Wake` via an `Action` instead) and reads
   `App.ShouldRenderTargetOnScreen` (a `Func<Rectangle,bool>` seam). Should converge with the
   Avalonia `ILayer`/`IAvaloniaLayer` rather than land as a second contract.
2. Pure layers that only need (1): `BubbleLayer`, `ChaosFxLayer`, `SubliminalLayer`, `FlashLayer`
   (`Services.Flash` dependency), `PinkTintLayer` / `SpiralLayer` (`App.Settings` monitor target and
   `App.Logger` -> CoreSettings + Serilog).
3. `BrainDrainLayer` (`App.Notifications`, `App.GetGlobalScreens`) and `BrainDrainCapturePump`
   (Win32 capture; the rules split from the P/Invoke part).
4. Stay in the WPF head: `CompositorEngine` (WinForms `Screen`, Dispatcher), `ICompositorHost`
   (`System.Windows.Forms.Screen`), `CompositorHostWindow`, `LayeredCompositorHost`,
   `SkiaWpfInterop`, `OverlayCaptureAffinity` (Win32). `DESIGN.md` moves with the engine docs, if ever.

## Verification limits

- Ran (Linux, .NET SDK 10.0.112):
  - `dotnet test Tests/CCP.Core.Tests --filter FullyQualifiedName~GlyphFallback`: 15 passed, 0 failed.
  - `dotnet test Tests/CCP.Core.Tests` (full suite): 1386 passed, 5 skipped, 2 failed. Both failures
    reproduce identically on the untouched base `avalonia-port/game-pbp` and are environmental: the
    container runs as root, so `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` cannot
    make a folder undeletable, and it has no IPv6 loopback for `LoopbackOAuthTests...(host: "[::1]")`.
  - `bash .github/scripts/core-guards.sh`: pass.
  - `dotnet build` of CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR and the Linux test projects in
    `.github/workflows/build.yml`.
  - Scratch app (not committed): Core GlyphFallback on SkiaSharp 3.119 (the Avalonia head's version).
  - `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
    -p:ValidateExecutableReferencesMatchSelfContained=false`: Build succeeded (this compiles the WPF
    head too, so `SubliminalLayer`/`BubbleLayer` resolve the moved Core type).
- Not run: the WPF head and `Tests/ConditioningControlPanel.Tests` (Windows-only; compiled, not run). Fonts on Windows differ (Arial, YaHei); the moved tests
  assert coverage invariants, not family names, and they ran on Windows CI before the move.

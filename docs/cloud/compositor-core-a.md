# refactor(core): move the compositor layer contract and the pure layers to CCP.Core (compositor-core-a)

First unit of the `ConditioningControlPanel/Services/Compositor` Core move. No Avalonia UI.

## Already in Core before this unit
- `CCP.Core/Services/Compositor/CompositorLayers.cs` (z-order table; moved earlier, trunk `9cf580bcd`). No WPF copy remains.
- Nothing else in `Services/Compositor` had a Core copy. `ConditioningControlPanel/Services/Compositor` and
  `CCP.Core/Services/Compositor` are byte-identical on `parity/trunk` (`a4ee52d40`) and on our base, so no trunk code is imported here.

## What changed
- `git mv` to `CCP.Core/Services/Compositor/` (same namespace, show as renames under `git diff -M`):
  - `ILayer.cs` (`IWpfLayer`, the layer contract; only a doc `cref` to the WPF engine became `<c>`).
  - `BaseLayer.cs` - two seams replace the WPF engine and `App`:
    - ctor takes `Action wake` instead of `CompositorEngine` (WPF layers pass `engine.Wake`);
    - `public static Func<int, Rectangle, bool>? ScreenTargetGate`, the head's monitor-target predicate.
      WPF seeds it in `App`'s static ctor with `App.ShouldRenderTargetOnScreen`; unset, every monitor draws
      (the same "err toward showing" the predicate already takes on an enumeration hiccup).
      `PrimaryOnlyUnlessDualMonitor` uses `MonitorTarget.FollowGlobal` (Core) instead of `App.MonitorTargetFollowGlobal` (same value, -1).
  - `PinkTintLayer.cs` - target read from `CoreSettings.Current.PinkFilterTargetMonitor` (default -1 = follow global,
    identical to the old `App.Settings?.Current?... ?? FollowGlobal`).
  - `SubliminalLayer.cs`, `GlyphFallback.cs` - unchanged apart from the ctor.
- `CCP.Core.csproj`: `SkiaSharp 2.88.8` (what `SkiaSharp.Views.WPF` pins, so WPF ships the same Skia; Avalonia resolves its own 3.119.x via Avalonia.Skia).
- WPF: `BrainDrainLayer`/`BubbleLayer`/`ChaosFxLayer`/`FlashLayer`/`SpiralLayer` call `base(engine.Wake)`; `OverlayService`,
  `SubliminalService`, `FirstShowEffects` construct the moved layers with `engine.Wake`; `App` static ctor seeds `ScreenTargetGate`.
- Tests: new `Tests/CCP.Core.Tests/CompositorLayerCoreTests.cs` (wake per activity transition, fill gate gets the pink
  target + screen and defaults open, pink raster colour/alpha clamp + dirty tracking, subliminal flash/clear) - runs on Linux,
  so the Skia natives are proven there (`SkiaSharp.NativeAssets.Linux.NoDependencies 2.88.8` in the Core test project).
- `GlyphFallbackRunTests` stays in `Tests/ConditioningControlPanel.Tests` (reaches Core's internal `GlyphFallback` through the
  existing `InternalsVisibleTo`): it asserts coverage against the machine's real font table (Arial, CJK, emoji faces), which is
  Windows-deterministic but not on a stock Linux runner, so it is not a pure test to move.
- No embedded resources moved, so no core-guards baseline entry.

## Next units (not in this branch)
- **compositor-core-b**: `SubliminalLayer`'s siblings that only need a bitmap seam - `BubbleLayer`, `FlashLayer`, `SpiralLayer`
  (replace `SkiaWpfInterop.ToSKImage(BitmapSource)` / `System.Windows.Rect/Point` with SKImage/SKRect inputs; Spiral's
  `Application.Current.Dispatcher` + `App.Logger` -> Serilog/a post seam).
- **compositor-core-c**: `ChaosFxLayer` (`Screen.PrimaryScreen` -> a primary-bounds provider) and `BrainDrainLayer`.
- **stays WPF / per-head**: `CompositorEngine` (WPF Dispatcher + `CompositionTarget`), `CompositorHostWindow`,
  `LayeredCompositorHost`, `BrainDrainCapturePump`, `OverlayCaptureAffinity` (Win32 P/Invoke), `SkiaWpfInterop`,
  `ICompositorHost` (WinForms `Screen`). An Avalonia compositor head then hosts the Core layers.
- Before Avalonia calls these layers: confirm the moved Skia calls (`SKPaint.TextSize`, `MeasureText`, `DrawText(string,...)`)
  bind against SkiaSharp 3.119 at runtime (obsolete there, not removed), or move Core to the `SKFont` APIs.

## Verification limits
- `dotnet build` (Linux, .NET 10.0.112): CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR, and every Linux test project in
  `build.yml` (Core.Settings, Core.Content, Avalonia.Tests, Consent, Language, CatalogueStartup, Scheduler, Report,
  GeneralSettings, Snapshot): all Build succeeded, no NuGet version conflicts from the new SkiaSharp reference.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1141 passed, 5 skipped, 2 failed - both fail identically on the untouched base in this
  container (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` runs as root, so the read-only folder deletes;
  `LoopbackOAuthTests...("[::1]")` has no IPv6 loopback). New `CompositorLayerCoreTests`: 4/4 passed.
- Compiled, not run: the WPF head and `Tests/ConditioningControlPanel.Tests` (built on Linux with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: 0 errors). Running them,
  `GlyphFallbackRunTests` included, needs Windows. The `ScreenTargetGate` wiring and the `engine.Wake` call sites are Windows
  code that is not exercised here.

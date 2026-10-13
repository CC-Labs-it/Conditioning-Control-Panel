# refactor(core): move the compositor layer contract (ILayer + BaseLayer) to CCP.Core

Unit **compositor-core-c**. Base: `avalonia-port/emi-line-engine`.

## What changed

- **Already in Core for this area** (earlier unit A9, `9cf580bcd`): `CCP.Core/Services/Compositor/CompositorLayers.cs`
  (the z-index table). Nothing else in Compositor had a Core copy, so no rule exists twice after this unit.
- `git mv ConditioningControlPanel/Services/Compositor/ILayer.cs -> CCP.Core/Services/Compositor/ILayer.cs`
  (`IWpfLayer`). Only edit: the doc `<see cref="CompositorEngine"/>` became `<c>CompositorEngine</c>`, since the
  engine stays in the WPF head. The type name is unchanged so no WPF consumer needed an edit.
- `git mv ConditioningControlPanel/Services/Compositor/BaseLayer.cs -> CCP.Core/Services/Compositor/BaseLayer.cs`.
  - Engine seam: the constructor takes `Action wake` instead of `CompositorEngine`. The 7 WPF layers
    (BrainDrain, Bubble, ChaosFx, Flash, PinkTint, Spiral, Subliminal) now pass `base(engine.Wake)`, so behaviour is the same:
    `SetActive` still wakes the parked engine once for each real transition. A constructor argument (rather than a static
    provider) keeps per-engine wiring, so WPF startup needs no new seam assignment.
  - Dropped `PrimaryOnlyUnlessDualMonitor` (the only `App.ShouldRenderTargetOnScreen` caller in BaseLayer). Nothing calls it:
    PinkTintLayer and SpiralLayer call `App.ShouldRenderTargetOnScreen` with their own per-feature target. So the planned
    `Func<Rectangle,bool>` seam would only have wired up dead code, and it is not added.
- `CCP.Core.csproj`: `SkiaSharp` 2.88.8 (managed types only, for `SKCanvas`/`SKRectI` in the contract). It is the same version
  `SkiaSharp.Views.WPF` already resolves in the WPF head, so WPF ships unchanged. Avalonia.Skia's newer SkiaSharp wins in the Avalonia head.
- New `Tests/CCP.Core.Tests/CompositorLayerContractTests.cs` (3 tests): one wake per activity transition, none for a no-op
  set; BaseLayer and interface defaults (main surface, monitor-local, every screen, dirty-every-frame fallback).
  WPF had no pure tests for these two files to move, and no WPF source-scan test reads them by path.
- Trunk credit: nothing was imported from `parity/trunk`. Its `ILayer.cs`/`BaseLayer.cs` are byte-identical to our
  base (they last changed there in `c2fea096e`, `ccb53e8cf` and `83f18eb1c`, which are already in our tree).

## Next units (rest of Services/Compositor)

- compositor-core-d: the pure parts of `CompositorEngine.cs`: layer registry/snapshot, the per-surface dirty fold and
  activation transitions. These go behind an `ICompositorHost`-style seam, and the DispatcherTimer/WPF host stays in the head.
- compositor-core-e: `GlyphFallback.cs` (Skia typeface fallback), after a check for WPF font references.
- compositor-core-f: the layers' render math (Flash/Subliminal/Bubble/PinkTint/Spiral/ChaosFx/BrainDrain). Each one is
  split from its `App.*` reads with provider Funcs.
- Stays in WPF: `CompositorHostWindow`, `LayeredCompositorHost`, `SkiaWpfInterop`, `OverlayCaptureAffinity` (Win32/WPF
  windowing), `BrainDrainCapturePump` (screen capture).

## Verification limits

SDK: 10.0.112 (Ubuntu apt; dot.net's install script is blocked by the sandbox proxy). Linux only.

- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet build -c Release` succeeded with 0 errors for CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR and every Linux test csproj in build.yml
  (CCP.Core.Settings/Content.Tests and CCP.Avalonia.{,Consent,Language,CatalogueStartup,Scheduler,Report,GeneralSettings,Snapshot}.Tests).
  CCP.Avalonia resolves SkiaSharp 3.119.4 (Avalonia.Skia's), not Core's 2.88.8.
- `dotnet test Tests/CCP.Core.Tests -c Release`: 1429 passed, 5 skipped, 2 failed. The 2 failures are environmental and also fail on the
  unmodified base, checked by stashing this change: `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` runs as root, so the delete succeeds,
  and `LoopbackOAuthTests.GoodState_OnEitherLoopback...("[::1]")` fails because the sandbox has no IPv6 loopback. New `CompositorLayerContractTests`: 3/3 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter "Spiral|CornerGif"` (Skia overlays, now with SkiaSharp also flowing from Core): 26/26 passed.
- WPF: `Tests/ConditioningControlPanel.Tests` built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  (0 errors), which compiles the WPF head and the 7 edited layer constructors. It was NOT run (Windows only). The WPF compositor
  (engine wake and real overlay rendering) was not exercised at runtime.

- Extracted the Spiral §9 URL, origin fence, state payload and latest-state ready queue to `CCP.Core/Services/Descent/SpiralEmbedBridge.cs`. WPF's existing `SpiralEmbedView` calls that shared copy.
- Added Avalonia `SpiralEmbedView` over the existing `WebHost` carrier. Only `spiral:ready` releases the last queued state; subsequent states post immediately. Server descent JSON, unknown fields and ISO-shaped strings survive unchanged; absent/unusable raw data keeps the v1 contract.
- Connected the existing Spiral tab's embed lifecycle, successful-navigation splash handoff and failed-load waiting fallback. A failed start never arms the splash watchdog; navigation success stops it. Leaving the tab disposes the browser host and pending bridge state.
- Added focused Core and headless Avalonia tests for protocol, origin fencing, coalescing, raw forwarding, navigation-before-ready, failures and disposal. Kept ledger row `ctrl-spiral-embed-view` **stub** because the real account descent publisher/migration-withhold path is absent on this base.
- No trunk code imported. Read-only `parity/trunk` inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; behavior was adapted from the WPF reference on this brief's base.

## Verification limits

Validation used .NET SDK 10.0.402, `DiffEngine_Disabled=true`, isolated test profiles and the repository's hardlink copy flags. Tests used `DOTNET_ROOT=/workspace/shared/dotnet`; full Core tests required the checksum-checked Vosk model.

- Build: `dotnet build CCP.Core/CCP.Core.csproj -c Release -t:Rebuild` passed (48 warnings, 0 errors). Rebuild replaced an assembly truncated while the shared filesystem was full.
- Build: `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` passed; subsequent workflow test builds also compile the final head.
- Build: all 11 Linux test projects named in `.github/workflows/build.yml` passed: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Report, Avalonia.Scheduler and Avalonia.Snapshot.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore`: 2827 passed, 2 failed, 0 skipped. The unchanged failures were `BoardTilesTests.A_frame_fits_the_budget` and `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` under concurrent compilers. Retrying those two classes after the WPF attempt ended gave 78 passed / 1 failed: the Goon callback passed; the board frame still took 44.60 ms against its unchanged 12 ms budget while other workers compiled. Focused `--filter FullyQualifiedName~SpiralEmbedBridgeTests`: **13 passed, 0 failed**.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter "FullyQualifiedName~SpiralEmbedViewTests|FullyQualifiedName~SpiralRoomShellTests"`: **4 passed, 0 failed** after the new fixture used the shipping `Program.BuildAvaloniaApp` font setup. An earlier bare-builder fixture run had 3 passed / 1 failed because the existing shell test could not resolve Segoe UI Black; no assertions were weakened.
- `bash .github/scripts/core-guards.sh`: passed both guards (424 XAML references; no forbidden Core APIs).
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: **failed** after 16m28s (305 warnings / 1 error), `CS1566` / `OutOfMemoryException` while reading `ConditioningControlPanel.g.resources` under the shared 1.5 GiB GC heap cap. The final WPF app/test assembly did not build; Windows code and WPF tests were not executed.
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/scratch/worker-a-spiral-render`: exit 0, but stopped after `MainShellWindow.png` (127 PNGs) without the render-all summary; this is incomplete visual coverage. Explicit `--render-view SpiralTabView /workspace/scratch/worker-a-spiral-render/SpiralTabView.png` passed; that PNG was inspected and shows the truthful waiting fallback. The live canvas was not rendered.
- Native WebView2/WebKit engines, remote embed handshake, browser process-loss behavior and Windows execution were not run.

## Follow-up: wire-ctrl-spiral-embed-view-b

- Wire the real account descent/block publisher and migration-withhold changes into the tab's state selection and `PostState`; clear/repost on account changes. `Refresh` still passes `hasBlock: false` on this base, so the real map remains unreachable. Do not fabricate a block from local XP.
- Provide the WebKit carrier before document boot: the shared `WebHost` can currently inject only after navigation, so pages that post ready once at boot can miss its carrier.
- Surface native browser process loss and hand the room to its waiting fallback, matching WPF's `ProcessFailed` path.

# sync7-port-gentle-layers

- Adapt WPF `a52c3c1d3215c74a871f0f1d72eb92543e7f53fa` to the existing Avalonia window hosts: pink/spiral fade in and out; the spiral GIF timer uses gentle speed; flash envelopes lengthen and popped flashes use the existing plain-fade state; subliminals lengthen both fades, retain hold time and omit the background card.
- Retain fading windows after their service lists empty, stop the finite fade timer when idle, and register `gentle-fades` after engine shutdown in PanicSurfaces. Panic closes those windows immediately.
- Reuse Core's already-merged helpers. No Core production changes, new compositor, brain-drain renderer, flash media player or pulse service.
- Source credit: WPF commit above (original source `3689dd7f544eac3deedba79f70ba8539a2c98e27`). Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported or existing counterpart overwritten.

## Commit disposition

`a52c3c1d3`: **ported on available window hosts; partially blocked-by-host**.

| Part | Disposition and evidence | Focused test (`GentleLayersTests`) |
| --- | --- | --- |
| Legacy pink/spiral show/hide | Ported: `CCP.Avalonia/Views/Overlays/GentleOverlayFade.cs:23`, `PinkFilterOverlay.cs:116`, `SpiralOverlay.cs:125` | `Overlay_show_hide_and_panic_keep_the_fade_bounded`, `Switching_gentle_off_finishes_a_pending_hide_and_off_shows_unchanged` |
| Spiral GIF speed | Ported: `CCP.Avalonia/Views/Overlays/SpiralOverlay.cs:54`, timer at :133 | `Spiral_delay_scales_and_gentle_pop_overrides_even_None` |
| Flash fade length and plain pop | Ported: `CCP.Avalonia/Views/Overlays/FlashOverlayWindow.cs:247`, `FlashOverlay.cs:387` | `Spiral_delay_scales_and_gentle_pop_overrides_even_None` |
| Subliminal fades/background | Ported: `CCP.Avalonia/Views/Overlays/SubliminalOverlay.cs:149`, :197, :199 | `Subliminal_keeps_hold_but_lengthens_both_fades_and_removes_card` |
| Core layer visibility, math, pink opt-in, flash-exit state | Already covered: `CCP.Core/Services/Compositor/BaseLayer.cs:63`, `ILayer.cs:31`, `GentleFade.cs:12`, `PinkTintLayer.cs:21`, `CCP.Core/Services/Flash/FlashExit.cs:88` | Existing Core implementation retained |
| Compositor SaveLayer/dirty/lifecycle, spiral-layer and brain-drain capture retention | Blocked-by-host: Avalonia has no CompositorEngine, SpiralLayer or BrainDrainLayer. Window overlays above are the existing render path. | Requires those hosts |
| Eased 1-second overlay pulse | Blocked-by-host: no Avalonia equivalent of OverlayService.PulseOverlays (GazeMinigameWindow documents the same missing service). Takeover's separate 30-second pulse is outside this commit. | Requires overlay pulse service |
| Spiral video and flash GIF/clip speed | Blocked-by-host: SpiralOverlay decodes GIFs only; FlashOverlay displays still images/first GIF frame. No spiral video or FlashClipPlayer host exists. | Requires media hosts |

## Verification limits

SDK: .NET 10.0.402. All builds use Release; all test invocations set `DiffEngine_Disabled=true`. The Avalonia suite initializes a `CCP_USERDATA_DIR` sandbox and memory-only secret store.

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors |
| `dotnet build <workflow-test-project> -c Release` for all 12 Linux-compatible test projects listed in build.yml | All 0; 0 errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests; CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests (12 test projects total). |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 557 warnings, 0 errors; Windows/WPF code compiled but not run |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed (rerun with scratch-file access after initial read-only sandbox failure) |
| `dotnet test Tests/CCP.Core.Tests -c Release` | 0; 3953 passed, 5 pre-existing Vosk model skips, 0 failed |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~GentleLayersTests` | 0; 4 passed, 0 failed |
| Production-line revert: remove `&& !s.GentleVisuals` from SubliminalOverlayWindow.Build, run the focused subliminal test | 1 as required; expected 9 children, actual 10. Production condition restored; final positive class run passes. |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~GentleLayersTests\|FullyQualifiedName~FlashPopTests\|FullyQualifiedName~EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered'` | 1; 13 passed, 1 existing FlashPopTests rack UI test failed creating Inter glyphTypeface. The new tests and panic registration test passed. |
| Earlier full PanicSurfacesTests selection | Existing EachPanicRouteCallsStopAllExactlyOnce cannot create Segoe UI Black glyphTypeface. No assertions weakened or tests skipped/deleted. |
| `git diff --check` | 0 |

Full Avalonia suite and native desktop behavior were not run. The environment font issue still blocks the existing shell/rack UI tests; supervisor CI must verify those. No real desktop, notifications, or real user profile was used by the tests. The test-plan coverage script is absent on this base; no sync ledger or parity document was edited.

## Assumptions

- Adapt this commit to existing overlay windows; do not invent the missing compositor, brain-drain, video, animated flash or pulse hosts.
- Panic overrides gentle fades and closes pending windows immediately. A spiral source replacement closes its fading old-frame windows before disposing their cached images.
- Gentle off retains existing timing and visuals. The existing Mix sentinel from FlashExit.Gentle means a plain fade, even when the configured exit style is None.

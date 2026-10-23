# fin-lab-gaze-minigame

- Connect correct-round feedback to the existing Core ModService, matching WPF's live mod lookup and neutral `GOOD` fallback. Keep reward, haptic, pane disposal, jingle and feedback ordering intact.
- Preserve GIF source-frame delays and long holds. When the existing decoder budget samples frames, sum their delays so the loop retains its source duration, including the final partial group. Other decoder callers keep their existing uniform-delay behavior.
- Restore drag arming: retain the original press, wait for platform movement tolerance, require a held left button, and start only one drag per press.
- Add focused GazeMinigameTests for uninitialized/default/themed/custom/missing praise, live switching after a round starts, unequal/zero delays, long holds, loop boundaries, a 123-frame GIF exceeding the frame budget, and horizontal/vertical drag arming with a fake native drag operation. Existing tests continue to cover disposal, late decode and panic.
- Consolidate three duplicate ledger entries into one honest `stub` row. No new surface: existing `gaze-minigame` panic registration owns the window and disposes round images/players.
- WPF reference pinned at base `1b41c94ef066899b6746ffc535978a9d1320f6da`. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; its gaze praise call matches the pinned WPF reference. The praise call is adapted from that reference; no file was overwritten or wholesale imported.

## WPF gap audit

The starting ledger's last entry was `stub`. Inspection of both cited WPF files and the head found:

1. Correct feedback used hardcoded `GOOD GIRL`; WPF calls `GetGazeCorrectMessage()` immediately before displaying feedback, with `GOOD` if the mod service is absent. Fixed by reusing that same service and call order.
2. GIF playback used a uniform first-frame delay. WPF's XamlAnimatedGif uses the source timing and loops forever. Unequal delays/long holds and sampled-loop duration are fixed. The head still retains its deliberate size/frame/cache budget and updates on the 50ms round clock, including grace time; it does not reproduce every source frame at native dimensions and timing.
3. WPF OverlayService.PulseOverlays also boosts an active Brain Drain overlay. This head has no Brain Drain capture/blur/melt renderer, so that part cannot run here. Pink Filter and Spiral already pulse and restore; this change does not add a substitute visual. The WPF pulse is live, so P122 does not apply.
4. The existing head starts pack dragging on press, whereas WPF arms on press and waits for the system drag threshold in PreviewMouseMove. Fixed by retaining the original press for DoDragDropAsync and waiting for movement. Avalonia exposes platform tap tolerance rather than the WPF-specific system drag parameters; that is the closest native cross-platform threshold available.

The remaining differences prevent a `wired` claim. The camera consent/start/calibration gates, asset selection and ordering, countdown, grace/scoring/display-cap decisions, reward options, feedback/results, fullscreen, close and panic paths already have callers in this head. No additional missing service call was found in those paths. Fluent icon and native picker/window API adaptations are retained.

## Verification limits

- .NET SDK 10.0.100 installed. Channel discovery hit a blocked preview endpoint; installing the explicit stable version succeeded.
- Every required build exited 0 with 0 errors (pre-existing warnings remain):

| Project | Build result |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | Exit 0, 0 errors |
| `CCP.Avalonia/CCP.Avalonia.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | Exit 0, 0 errors |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | Exit 0, 0 errors |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | Exit 0, 0 errors |

All used `dotnet build <project> -c Release`. The WPF test project additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; it compiled, but was not run on Linux.

- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,339 passed, 0 failed, 5 skipped, 3,344 total. The existing five Vosk integration tests skip when their model is absent; its download is blocked in this environment. No tests were edited to skip or weaken them.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. The first restricted attempt could not create its scratch files; rerunning with scratch-write access passed.
- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false`: exit 0, 0 errors. An earlier default-parallelism attempt ended with MSB4166 (worker exit); shutting down build servers and using one worker resolved it.
- `git diff --check`: exit 0.

The supplied cloud limitation explicitly defers Avalonia test execution to GitHub unless the font lane has landed. It is absent here. The focused tests are compiled, not run; a runtime fail-before/pass-after claim cannot be made here. GitHub should run with `DiffEngine_Disabled=true`:

```sh
dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~GazeMinigameTests|FullyQualifiedName~SpiralOverlayTests|FullyQualifiedName~PanicSurfacesTests'
```

No real desktop, camera, notification or user profile was used. The tests use the assembly's CCP_USERDATA_DIR sandbox and fake camera source. Windows platform behavior cannot be run on this Linux worker. No WPF source was changed.

## Assumptions

- Preserve the existing decoder budget rather than introducing an unbounded GIF cache into this unit. Keep the row `stub` for the stated fidelity differences.
- For a GIF frame with no positive delay, use a 100ms hold; positive delays retain their metadata value.
- Use Avalonia platform pointer tolerance for drag arming because it has no WPF SystemParameters drag-distance API.

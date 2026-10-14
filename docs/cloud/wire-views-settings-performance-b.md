# wire-views-settings-performance-b

- Lockdown tab navigation now starts/stops the WPF pink border/glow pulse. Motion Reduced/Off stops it
  and restores the static plate; Full permits the next tab visit to re-arm it, as in WPF. Closing the
  shell releases the pulse. The existing 30 fps BeatLoop parks hidden/minimized content.
- The pure Core `LockdownPulse` timeline supplies endpoints/duration to WPF and frame values to
  Avalonia, including WPF's ScRGB color interpolation. No second engine copy or new host was added.
- Replaced the plate's animated DropShadowEffect with a sibling BoxShadow border. Tightened the Effect
  ratchet by removing the shell animation file's exception. Existing shell loop assertions retain the
  timing checks and now assert the sibling glow.
- Added a navigation/settings regression and Core timing tests. Headless fixtures register the exact
  shipped fonts. The navigation regression failed on BASE because nothing called StartLockdownPulse.
- Only ledger row `views-settings-performance` changed; it remains `stub`. Remaining lane
  `wire-views-settings-performance-c`: implement the unified overlay compositor host route. The
  UnifiedOverlayHost checkbox still persists only; WPF Services/Compositor is ~4.5k lines, exceeding
  this slice's 1000-added-line budget. Linux foreground DND still sees X11/XWayland only; native
  Wayland foreground ownership is not exposed by that seam.
- Read parity/trunk as required; no trunk code was imported in this slice. The WPF reference was
  MainWindow.Animations.cs, TabNavigation.cs and MainWindow.UiUpdates.cs motion handler.

## Verification limits

- Before the fix: `dotnet test Tests/CCP.Avalonia.Tests --filter PerformanceLockdownPulseTests` failed
  1/1 on BASE at `Assert.True(shell.LockdownPulsing)` (the unreachable pulse).
- After the fix: `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~PerformanceLockdownPulseTests|FullyQualifiedName~PerformanceSettingsMotionTests|FullyQualifiedName~PerformanceSettingsWiringTests|FullyQualifiedName~AmbientBeatLoopsTests'`: 14 passed,
  0 failed, 0 skipped. `DiffEngine_Disabled=true`; native LibVLC came from rootless apt extraction.
- `dotnet build CCP.Core`, `dotnet build CCP.Avalonia`, and all 11 Linux test csproj listed in
  `.github/workflows/build.yml`: succeeded. Builds used `-m:1 /p:UseSharedCompilation=false` and the
  CopyFilesToOutputDirectory/CopyLocal/AdditionalFiles/CopyAdditionalFiles hardlink flags to fit disk.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet run --project CCP.Avalonia --no-build -- --render-view LockdownTabView /tmp/worker1-perf-lockdown.png`: exit 0, 1100×780 PNG, inspected the plate/glow, no binding error output.
  Used separate temporary CCP_USERDATA_DIR and XDG config/cache directories. Full render-all was not
  rerun for this one-view layout change.
- `dotnet test Tests/CCP.Core.Tests --no-build`: 2797 passed, 0 failed, 1 existing Debug-only
  `BoardTilesTests.A_frame_fits_the_budget` skip, 2798 total. Required real Vosk model supplied via
  `CCP_REQUIRE_VOSK=1 CCP_VOSK_MODEL=/tmp/worker1-vosk/vosk-model-small-en-us-0.15`.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: succeeded,
  509 warnings, 0 errors (7m56s). Used the same build/hardlink flags plus
  `DOTNET_GCHeapHardLimit=0x80000000`. This compiled the changed WPF storyboard and the WPF tests;
  neither was executed on Windows.

Windows runtime, actual desktop compositor behavior and native
Wayland foreground reads cannot be exercised in this headless Linux environment. Cross-compilation
alone does not prove the WPF storyboard executes on Windows. The unified compositor remains unported.

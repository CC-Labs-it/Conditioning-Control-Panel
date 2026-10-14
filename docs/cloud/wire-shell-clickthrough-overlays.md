# wire-shell-clickthrough-overlays

- The live tutorial spotlight now excludes the target rectangle from its native input region; manual steps restore the full input area. Transparent waiting steps accept input only on the tutorial card, so Skip stays usable while background controls receive clicks.
- X11 uses the existing XFixes rectangle entry point. Windows uses a static GDI window region, matching WPF; it does not rely on the bubble overlay's cursor-polled style toggle. GDI temporaries and failed window regions are freed.
- Physical geometry and fractional DPI rounding are shared in Core `SpotlightInputRegion`; the WPF tutorial calls the Core rules. The existing `tour` panic surface closes the overlay by skipping the tour.
- Updated only `shell-clickthrough-overlays` in the ledger. No imports were taken from `parity/trunk` (read-only branch fetched).

## Verification limits

- `bash .github/scripts/core-guards.sh`: passed all guards.
- `dotnet test Tests/CCP.Core.Tests -c Release -p:BuildProjectReferences=false`: passed 2818, skipped 5, total 2823; the skips are missing-Vosk-model speech tests.
- `dotnet build CCP.Core -c Release`, `dotnet build CCP.Avalonia -c Release --no-restore`, and `dotnet build Tests/CCP.Avalonia.Tests -c Release --no-restore`: passed, zero errors. All ten other Linux test projects named in `.github/workflows/build.yml` also built with zero errors (`-p:BuildProjectReferences=false` after the heads were compiled).
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --no-restore --filter 'FullyQualifiedName~TutorialInputRegionTests|FullyQualifiedName~OverlayBackendDispatchTests|FullyQualifiedName~TutorialTourTests|FullyQualifiedName~PanicSurfacesTests'`: passed 21, skipped 0, failed 0.
- Regression proof: temporarily restoring the previous `ClearWindowSpotlightRegion` behavior for spotlight and transparent-wait drawing made both new `TutorialInputRegionTests` fail at `Assert.NotNull`. Restored the fix before the final build and 21-test pass.
- Build commands used `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true` to avoid duplicate native/content files. Tests/render use `FONTCONFIG_FILE=/workspace/ccp-test-fonts.conf`, an external fontconfig file excluding unsupported system WOFF2 fonts; no repository font configuration changed.
- `CCP_USERDATA_DIR=/tmp/worker3-render-profile dotnet run --project CCP.Avalonia -c Release --no-build --no-restore -- --render-all /tmp/worker3-render`: passed, 224/224 views rendered. `/tmp/worker3-render/TutorialOverlay.png` was inspected: the tutorial card, target outline and action row draw correctly. PNGs do not prove native input delivery.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (plus the copy hardlink flags above): passed, 512 existing warnings, 0 errors, elapsed 18m53.98s. WPF head and test assembly compiled; neither was run on Linux.
- Initial overlapping builds were stopped; the focused test build hit disk exhaustion while copying native assets. Generated copies were deduplicated, and subsequent commands use MSBuild's copy hardlink flags. No tracked configuration changed.
- Native X11 input read-back, Windows GDI input routing, mixed-DPI Windows placement and WPF runtime behavior cannot be exercised on this display-free Linux host. Headless tests prove the rectangles sent to the native seam, not OS mouse delivery.
- Existing platform limitations remain: native Wayland requires XWayland; Win32 `RestackAbove` and the previously recorded mixed-DPI sizing risk are outside the new tutorial input path.

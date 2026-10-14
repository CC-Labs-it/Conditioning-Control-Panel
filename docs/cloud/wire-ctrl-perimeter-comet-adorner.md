# wire-ctrl-perimeter-comet-adorner

Base: `avalonia-port/goongame-core-c`. Branch: `avalonia-port/cloud-wire-ctrl-perimeter-comet-adorner`.

- Wired WPF Chaster's six-second link-wait comet to the existing browser-link UI state path, with hide/detach cleanup.
- Wired Launcher's seven-second hover comet and twelve-second running-panel comet; parked on deactivation, minimize, hide and close. Tile rebuilding removes the old hover adorner.
- Replaced the tile's live DropShadowEffect with a sibling BoxShadow Border, preserving hover/rest opacity and blur gates. Removed the now-obsolete Launcher effect-ratchet exemption.
- Retained the existing shared FrameClock, PerimeterCometAdorner rendering and Studio path. No new windows or panic surfaces; existing Launcher close/hide cleanup stops the additions. No new pure rules, no Core or WPF source changes.
- Read WPF `Controls/PerimeterCometAdorner.cs`, `Views/Tabs/ChasterTabView.Fx.cs` and `Windows/Launcher/LauncherWindow.Fx.cs` / `.Choreo.cs` as the spec. Fetched `parity/trunk` read-only; no trunk code imported.
- Updated only ledger row `ctrl-perimeter-comet-adorner` to wired.

## Verification limits

Observed checks (Release, .NET SDK 10.0.402 on Linux):

- Fail-first: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~PerimeterCometHostTests` against BASE with the new test file: 3 failed, 0 passed; all three expected host adorners were absent.
- Fixed-head run: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~PerimeterCometHostTests|FullyQualifiedName~LauncherFxTests'`: 15 passed, 0 failed/skipped. Stepped clocks assert six/seven/twelve-second laps; real pointer hover, host gates and teardown are exercised. Chaster's link-state seam is invoked by reflection; no browser/authentication or real Chaster service was used.
- `bash .github/scripts/core-guards.sh`: all guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release` with official pinned Vosk model required: 2815 passed, 1 failed, 0 skipped (2816 total). Untouched `BoardTilesTests.A_frame_fits_the_budget` measured 25.45 ms against 12 ms while other workers compiled/rendered.
- Isolated `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~BoardTilesTests`: 66 passed, 1 failed; same frame-budget guard measured 19.79 ms under continued shared load. No budget/test was weakened or skipped.
- The initial concurrent build attempts hit memory/disk pressure and were restarted. A clean BASE Avalonia rebuild completed with 172 warnings, 0 errors before the fail-first tests. Recovered generated artifacts and used bounded GC / single MSBuild node / SDK copy hardlinks for later builds.

- A second full Core run also produced 2815 passed, 1 frame-budget failure (28.72 ms), 0 skipped. The final isolated unchanged `BoardTilesTests.A_frame_fits_the_budget` run passed 1/1. These timings are sensitive to the shared worker load; the full-suite failures remain recorded.
- Added a regression for inherited dim/fade on the sibling tile shadow: failed before the correction (expected 0.1925, actual 0.35). After correction the final filter `PerimeterCometHostTests|LauncherFxTests.Play_Holds|LauncherFxTests.Hover_Lifts|AmbientBeatLoopsTests.NoNewEffectInCodeBehind` passed 6/6 with no skips. Existing hover assertions remain and now additionally check the sibling shadow.
- Final `dotnet build -c Release` passed for CCP.Core, CCP.Avalonia and every Linux test project in `.github/workflows/build.yml`: CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings and Core.Tests (13 targets total). Used a single MSBuild node, disabled shared compilation, hardlinked immutable copied dependencies, and `BuildProjectReferences=false` after freshly building the dependencies. Final Core guards also passed.
- Attempted `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true` with bounded memory. The SDK failed with native AccessViolation in `Microsoft.NET.HostModel.ResourceUpdater.CreateAppHost` (exit -6). The Windows test project was not successfully built or run. No WPF source changed.
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/scratch/root-comet-render` rendered 224/224 views. Inspected LauncherWindow.png and ChasterTabView.png. The idle fixture shows the hosts and resting tile shadows; active moving comets are exercised by the stepped-clock tests rather than this idle render.

No Windows runtime, live desktop, browser authentication or actual Chaster service was exercised. No tests or budgets were removed, weakened or newly skipped. This row is wired, not verified.

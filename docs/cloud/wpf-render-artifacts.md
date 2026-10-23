- Add strict, data-driven WPF companion renders for every current verified Avalonia snapshot row:
  `release-content-mod-manager-rows` at 700×500 and `win-layered-audio` at 480×660.
  Both can be constructed headlessly; no current verified snapshot row is excluded.
- Reuse the existing STA/theme harness, disposable profile and offscreen content-host pattern.
  Seed default read state (CCP Default selected, no release service; audio empty/master off/70%),
  assert that state and painted pixels, and decode each saved PNG to check its dimensions.
  No WPF product code changes and no desktop windows are shown.
- Write row-id PNGs under `WPF_RENDER_OUT` (default `TestResults/wpf-render`), run the theory in
  the Windows build job and always upload its folder as the `wpf-render` artifact.
- Document the evidence citation: a specific GitHub Actions run URL plus
  `wpf-render/<row-id>.png` in a baseline commit message.
- Trunk reference fetched and inspected: `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`
  (`parity/trunk`). No trunk code was imported; the existing base files remain authoritative.

## Verification limits

- SDK: .NET 10.0.402. The initial checkout had a different commit and read-only Git metadata;
  the authorized fallback clone was verified at base `6095620b4e54a91477fdff126b61425d83f893d6`.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <project> -c Release` passed (exit 0, 0 errors) for every Linux-compatible
  test project referenced by `.github/workflows/build.yml`:
  `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`,
  `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`,
  `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`,
  `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`,
  `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests` (all under `Tests/`).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors (602 warnings).
  This compiles the WPF head and new theory; Windows execution and actual WPF PNGs require CI.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- YAML parsing, artifact/output path checks, row-id coverage, and actual committed PNG dimension
  checks: exit 0. `git diff --check`: exit 0.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release
  --no-build`: exit 0; 3660 passed, 0 failed, 5 existing Vosk tests skipped (model unavailable).
- Avalonia tests were compiled but not run, per the supplied cloud font-creation limitation;
  the relevant UI regression class is `ModManagerPackDownloadTests`. No Avalonia behavior changed.
  Vosk model download is unavailable in this environment; required-model coverage stays in CI.
- Initial build sweep exhausted disk while copying native dependencies; after removing ignored
  outputs and cleaning each completed test output folder, every required project compiled.
  The first Core guard attempt lacked temporary-file write access; rerunning with write access passed.

## Assumptions

- Mirror the snapshot constructors' current default state, including the unseeded Core mod
  fallback (one CCP Default entry), rather than introduce new pack-download states/baselines.
- Render client content offscreen without native window chrome, as the existing WPF reference
  renderer does; preserve the frozen WPF Customise/Companion layout at the Avalonia baseline size.
- Keep the WPF services unstarted by seeding only read fields, following the existing
  `LayeredAudioWindowTests` settings fixture pattern. A service contract change should fail loudly.

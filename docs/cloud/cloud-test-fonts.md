- Add `.github/scripts/cloud-test-env.sh`, a command wrapper requiring no root or package installation. It creates and removes a private fontconfig configuration/cache, excludes only `/usr/share/fonts/openai-sans/*.woff2`, and keeps the system's existing fallback order. This image then selects its installed Noto Sans. Named/bundled app fonts remain available; command exit status is preserved.
- Add `AppFontsTests.HeadlessSystemFallbackBuildsTextGeometry`, exercising Black-weight text measurement and geometry for the default, Segoe UI composite, unregistered Inter composite, and a missing family. No existing tests or assertions changed.
- Root cause: `/etc/fonts/conf.d/52-orbit-os.conf` strongly prefers OpenAI Sans WOFF2. With SkiaSharp 3.119.4, `SKTypeface.Default` reports OpenAI Sans with **0 glyphs**; loading its WOFF2 file directly returns null. `fc-list` seeing 583 entries therefore does not prove Skia has a usable default. After excluding the unsupported files, Skia loads Noto Sans with **3,317 glyphs**. `FONTCONFIG_*` and `XDG_CACHE_HOME` were unset, `HOME=/home/agent`; the cache warning came from read-only diagnostic sandboxing and disappears with normal write access. Cache writability alone does not fix the font.
- Most headless tests use `AppBuilder.Configure<App>().UseSkia().UseHeadless(...)`, bypassing `Program.BuildAvaloniaApp()` and its `.WithInterFont()` / `AppFonts.Options()` configuration. The shipped builder already supplies bundled Inter as its default, retains Fredoka/mono mappings and emoji fallback, and renders the actual `MainShellWindow` including PremiumSpark here **without** the wrapper. Keep that working setup unchanged rather than change production typography for this image.
- Fetched and inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (font history: `a67e293e0`, `2dbac1078`). No trunk code imported; the base's existing `AppFonts` behavior wins.

## Verification limits

Run from the repository root with .NET SDK 10.0.402 installed under `$HOME/.dotnet`:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DiffEngine_Disabled=true
bash .github/scripts/cloud-test-env.sh dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~AppFontsTests|FullyQualifiedName~Panic|FullyQualifiedName~ChaosPopTextTests'
bash .github/scripts/cloud-test-env.sh bash .github/scripts/avalonia-tests-sharded.sh
bash .github/scripts/core-guards.sh
dotnet test Tests/CCP.Core.Tests -c Release
```

The wrapper targets this Linux cloud image and includes `/etc/fonts/fonts.conf`. It does not install fonts, alter system configuration, change the user's HOME, or set any test skip/require flags. On a system without the excluded files, the rule is inert. Set `CCP_REQUIRE_VOSK=1` only when the Vosk model is present; the model download is blocked here. LibVLC is also absent; existing native-media tests can skip. No new skip condition was added.

- Before the fix: Panic filter **48 passed / 58 failed / 5 skipped** (111 total, exit 1); the new geometry regression **0 passed / 1 failed** with `Could not create glyphTypeface` (exit 1).
- Final wrapper font regression: **1 passed / 0 failed / 0 skipped** (exit 0), using exactly the same test that failed before the fix. The final focused font/Panic/Chaos filter reported **111 passed / 0 failed / 5 skipped** (116 total, exit 0). The full shard run returned exit 1: **1,239 passed / 1 failed / 32 skipped**, with **1,272 of 1,272** listed tests accounted for and **zero glyph-creation errors**.

| Shard | Passed | Failed | Skipped | Total | Exit |
| --- | ---: | ---: | ---: | ---: | ---: |
| 0 | 340 | 1 | 2 | 343 | 1 |
| 1 | 284 | 0 | 8 | 292 | 0 |
| 2 | 315 | 0 | 5 | 320 | 0 |
| 3 | 209 | 0 | 3 | 212 | 0 |
| 4 | 91 | 0 | 14 | 105 | 0 |

- Remaining failure (real rendering/hit-testing): `ContentHitTestTests.ContentButtonsReceiveThePointerAtTheirCentre` — Awareness Tutorial/master controls hit `ScrollContentPresenter` instead of the buttons. This matches the existing test's documented headless compositor-frame symptom; it is not a glyph-creation failure.
- The 32 skips use existing guards for missing Vosk/native media dependencies and OS/container-only SecretStore checks. No test was disabled, deleted, loosened, or given a new skip condition.
- Isolated `ContentHitTestTests` with the unsupported font families excluded and the system Noto Sans fallback: **0 passed / 1 failed / 0 skipped** (exit 1). The Awareness buttons hit `ScrollContentPresenter` instead of themselves, matching the stale-compositor-frame symptom already documented in that test. The same failure also reproduced with a healthy DejaVu fallback, so selecting a different usable default does not resolve this remaining rendering/hit-testing issue.
- Wrapper checks: `bash -n` passed; `fc-match sans-serif` selected `NotoSans-Regular.ttf`; child exit-status preservation was checked with exit 7.
- Shipped app: unwrapped `dotnet CCP.Avalonia/bin/Release/net10.0/CCP.Avalonia.dll --render /tmp/ccp-fonts-app.png` rendered 880×620; `--render-view MainShellWindow /tmp/ccp-fonts-main-shell.png` rendered 1661×1002 including PremiumSpark. Both used temporary `CCP_USERDATA_DIR` and `XDG_CONFIG_HOME` sandboxes and exited 0.
- Release builds: CCP.Core, CCP.Avalonia, and all 11 Linux test projects referenced by `.github/workflows/build.yml` succeeded, **0 errors** each (exit 0). Existing compiler/analyzer warnings remain.
- `bash .github/scripts/core-guards.sh`: all guards passed (exit 0).
- Core suite first run: **3,338 passed / 1 failed / 5 skipped** (3,344 total, exit 1). `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected Loops but got Clips; its class then passed **9/9** in isolation (exit 0).
- Core full-suite repeat: **3,339 passed / 0 failed / 5 skipped** (3,344 total, exit 0). The first remote-media failure is not consistently reproducible; the five existing skips require the missing Vosk model.
- WPF test cross-build: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` succeeded (exit 0), **559 warnings / 0 errors**. Windows code compiled but was not run.

The separate Consent, Language, CatalogueStartup, Scheduler, Report, GeneralSettings, Snapshot, Core.Settings, and Core.Content test assemblies were compiled only, not executed. Windows runtime, desktop integration, and notification delivery cannot be exercised on this headless Linux worker. The new test uses the assembly's existing temporary `CCP_USERDATA_DIR` and dispatcher sandboxes.

- Finish `ctrl-sparkle-wallet`: replace the Press Start 2P text placeholder with WPF's fixed `^_^` face geometry in the original visor rectangle; retain the existing wallet, cards, sprites, balance, localization, wave and reward paths.
- Adapt `WalletEmiFace` from read-only `parity/trunk` commit `29cecb43c79fbd09868f4baea496f95bf0423cce` (fetched tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Use the existing bundled Noto Sans Mono through `AppFonts.Mono`, matching WPF's shipped-font preference. No existing base implementation was overwritten.
- Add focused tests for the real wallet's face type/text/rectangle and for fitted, centred, raised pink ink plus clearing. The wiring test fails on the original TextBlock before the fix.
- Mark the single ledger row `wired`, never `verified`. No new window, popup or animation surface was added, so no new panic registration is needed; the existing wallet and both cards remain discoverable by render-all.

## WPF comparison and assumptions

- Base checked: `888cdbffe4d2ecf3967cee4a4334557ee533e54d`. Read `ConditioningControlPanel/Controls/SparkleWallet.xaml` and `.xaml.cs`, the shell wallet partial and the croupier render test. The ledger's remaining gap is the visor face renderer.
- WPF constructs `EmiFace` at `(320,297)` with size `389x349` and calls `Draw("^_^")` once. That live caller needs the flat geometry path only: 152x137 virtual canvas, 95% ink-fit box, 5px round pink fill/stroke, centred ink bounds, 2% upward lift, clipped/scaled to the visor. All are now reached from `HeaderSparkleWallet` in the real shell.
- Decision: import the wallet-specific flat path rather than the full desk face renderer. Sideways faces, kaomoji, THINKING mode and blinking have no wallet caller at the pinned base; they remain outside this unit. The ledger's “pixel glyph” wording refers to EmiFace's geometry renderer; WPF actually uses Noto Sans Mono, not Press Start 2P, for this face.
- Existing behaviors retained: 29x138 pill; separate help button; localized invite/help text and ordering; exclusive light-dismiss popups, Escape/close/later focus return and VisitRequested; nonnegative N0 balance and accessible name; hover/focus 620ms arm wave; 240ms badge rise, 280ms pulse, four 520ms sparkles, accumulating 1.6s reward window; motion/performance gates and lifecycle cleanup. Existing shell handlers marshal balance and award presentation to the UI thread.
- VisitRequested remains this control's boundary. The shell's Back Room destination belongs to `shell-sparkle-wallet`; this change does not claim completion of that separate feature.

## Verification limits

.NET SDK: 10.0.402. No live desktop or real profile was used. `DiffEngine_Disabled=true` was set for test runs.

| Command | Exit / result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 48 existing warnings, 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 warnings, 0 errors |
| `bash .github/scripts/core-guards.sh` | 0; all guards passed |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0; 3,545 passed, 5 skipped, 0 failed |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~WalletVisorUsesTheEmiGeometryRenderer` before the fix | 1; intended assertion failure: actual face is TextBlock |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~WalletVisorUsesTheEmiGeometryRenderer\|FullyQualifiedName~WalletFacePaintsTheFittedPinkInkAndClears'` after the fix | 0; both new tests passed |
| Same project, `--no-build --filter FullyQualifiedName~SparkleWalletTests` | 1; 2 passed, 3 existing window tests failed on environment font creation (Inter / Segoe UI Black) |
| `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --render-view <view> <output>` for SparkleWallet, SparkleWalletInviteCard and SparkleWalletHelpCard | 0 each; three PNGs rendered, wallet visually inspected; sandbox profile |
| `git diff --check` | 0 |

All workflow-listed test projects were built with `dotnet build <project> -c Release`:

- `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`: exit 0, 0 errors.
- `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`: exit 0, 0 errors.
- `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors.

The Consent project initially read incomplete metadata during an overlapping build; its sequential retry passed with 0 warnings and 0 errors. The first full Core run had one media-selection failure (`Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`, expected Loops, got Clips); its isolated class passed 9/9 and the second full suite passed. No test was modified, weakened or skipped to address either failure.

The five Vosk tests were skipped by their existing missing-model guard; no model download was attempted because the task reports that host blocked. The remaining existing wallet window tests need the cloud-test-fonts lane or the GitHub test runner. The WPF app and WPF tests compiled through Windows targeting but were not run on Linux. No full Avalonia suite, full render-all, live desktop, or Windows runtime check was run; this unit's two new tests and three existing view renders completed successfully.

- Restore the WPF application's shared `Font.Mono` resource (`Cascadia Mono, Consolas, Courier New`). Existing DynamicResource consumers and the Tonight Board can resolve the same application resource; remove the two temporary local copies.
- Map every named family in that WPF monospace chain to the bundled Noto Sans Mono off Windows, alongside the existing generic `monospace` mapping. Windows retains its installed-family selection.
- Add headless tests for resolving/replacing the shared font resource and opening every portable mono mapping as a real fixed-width glyph face.
- No trunk implementation imported. `git fetch origin parity/trunk` was read-only; this slice adapts `ConditioningControlPanel/App.xaml` and the existing head's `AppFonts`.
- Keep `theme-fonts` **stub**. Follow-up lane `wire-theme-fonts-b`: port WPF FontGuard's installed-but-unreadable startup probes with shared chain filtering in Core, and resolve/measure the ledger's remaining synthetic-typeface risk for non-system-emoji fonts. Current system emoji refusal behavior remains covered by `SystemEmojiFontCollectionTests`; this slice does not change that policy.

## Verification limits

- SDK: .NET 10.0.402 through `/workspace/ccp-tools/dotnet`, which limits compiler concurrency and disables hardlink copies. `DiffEngine_Disabled=true` for test runs.
- Regression proof: before the implementation, `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~AppFontsTests` failed both new tests (missing shared resource and missing Cascadia Mono mapping). Final focused filter `FullyQualifiedName~AppFontsTests|FullyQualifiedName~SystemEmojiFontCollectionTests`: **5 passed, 0 failed, 0 skipped**.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --no-build -c Release` with `CCP_REQUIRE_VOSK=1`: **2816 passed, 0 failed, 0 skipped**, duration 1m11s.
- `bash .github/scripts/core-guards.sh`: passed; 424 XAML references checked against 1591 app / 1880 Core types.
- `dotnet run --project CCP.Avalonia --no-build -c Release -- --render-all /tmp/worker3-fonts-render`: **224/224 views rendered**, exit 0. Inspected `MainShellWindow.png` (Tonight Board mono eyebrow) and `UpdateFailedDialog.png` (mono exception box); `SpiralTabView.png` also rendered after removal of its duplicate font resource. No Windows/WPF pixel comparison was run.
- Initial required head-test build failed MSB3030 after generated head outputs were removed during shared disk pressure; its following render failed because the executable was missing. The first CatalogueStartup build failed MSB3191 (ENOSPC creating `obj/.../Avalonia`). A Consent build was canceled during continuing ENOSPC conditions. Sequential retries passed, with generated outputs cleaned only after each command completed.
- All projects below passed `dotnet build <project> -c Release` (0 errors). Core and the head built explicitly; every Linux test csproj named by `.github/workflows/build.yml` was built. Other than full Core and the focused head tests above, the isolated test suites were compiled only.

| Project | Result |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | passed |
| `CCP.Avalonia/CCP.Avalonia.csproj` | passed |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | passed |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | passed |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | passed |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | passed |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | passed |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | passed |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | passed |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | passed |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | passed |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | passed |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | passed |

- Windows font selection compiled in the head but was not executed on Windows. The optional WPF cross-build was not run by this worker: concurrent attempts elsewhere exhausted the shared 32 GB container disk after several minutes. WPF/Windows behavior could not run in this Linux container.
- Publication uses the authorized GitHub Git Data API because the configured git transport account cannot push. Only `avalonia-port/cloud-wire-theme-fonts` is updated, with an expected-head check and `force=false`.

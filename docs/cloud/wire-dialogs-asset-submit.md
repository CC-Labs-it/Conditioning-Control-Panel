# wire-dialogs-asset-submit

Ledger row `dialogs-asset-submit`: the stack base already wired the preset and session share paths (Presets "Share", session rack ☁) and `AssetSubmitWireTests` (3 tests). What was still missing was the dialog's mod branch (`requireDownloadUrl`, MEGA link), which had no caller. This unit adds that caller.

- `CCP.Avalonia/Views/Windows/MainShellWindow.ModCatalogue.cs`: ports WPF `MainWindow.ModCatalogue.cs` `ShareModToCatalogueAsync`, `BuildModCatalogueAsset`, `SafeDirectorySize` and `TryBuildPreviewThumb`. The flow:
  - It refuses with the auth toast when signed out, reusing `CatalogueSignedIn` from PresetIO.
  - It opens `AssetSubmitDialog(requireDownloadUrl: true)` with the manifest tags already filled in.
  - It builds the `ccp-mod/v1` envelope, submits it through Core `CatalogueClient`, records the submission and shows the mod success toast (`catalogue_toast_mod_success`).
- Deviation (preview thumb): WPF uses `BitmapImage(DecodePixelWidth=160)` + `JpegBitmapEncoder(80)`. Here SkiaSharp does the decode, the resize to 160 px wide and the JPEG 80 encode, as `EnhancementsTabView` already does. The 64 KB cap is the same.
- `CCP.Avalonia/Views/Dialogs/ModManagerDialog.axaml.cs`:
  - `BtnShare` is visible for user-installed mods only (WPF ModManagerDialog.xaml.cs:459).
  - The click calls the owner shell's `ShareModToCatalogueAsync`, disables the button during the upload and rebuilds the list so the status badge refreshes (WPF :692).
  - Both `ponytail:` stubs are removed.
- `Tests/CCP.Avalonia.Tests/AssetSubmitWireTests.cs`: new test `ModManagerShareAsksForTheMegaLinkThenSubmitsTheModEnvelope`. It goes from Manage Mods → select a user mod → Share → dialog, and checks:
  - a built-in mod shows no Share button;
  - the tags are pre-filled from the manifest;
  - a non-MEGA link keeps Submit disabled;
  - the POST to `/api/catalogue/mods` carries `ccp-mod/v1`, the creator, the MEGA url, a non-zero size and a 160 px thumb;
  - the submission is recorded;
  - the button is re-enabled afterwards.
  The test uses a fake HTTP handler and never touches the network. It fails before the fix: `Assert.True(share.IsVisible)`, line 176.
- `docs/avalonia-parity.md`: the `dialogs-asset-submit` row now lists the mod path and its test. The base also carried a duplicate stale `stub` copy of this row (left over from a stack merge); I removed it.
- No trunk commits imported: `origin/parity/trunk` still has the same stub for the mod share. The port is taken directly from the WPF reference.
- Panic: nothing new to register. WPF panic does not close the share modal, and the preset/session share is not registered either.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter AssetSubmitWireTests`: 4/4 passed. The new test failed with the two source files stashed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter "AssetSubmitWireTests|ModManager|ModChoice|CatalogueShell|WindowDrop|PanicSurface"`: 35/35 passed.
- `dotnet test Tests/CCP.Core.Tests`: 2777 passed, 6 skipped, 2 failed. Neither failure comes from this change (the branch has no Core or Core-test diff); both come from the container:
  - `LoopbackOAuthTests...(host: "[::1]")`: the container has no IPv6 loopback ("No data available").
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: the tests run as uid 0, so making a folder undeletable has no effect.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- Built on Linux with the .NET 10.0.112 SDK from Ubuntu apt (builds.dotnet.microsoft.com is blocked by the proxy): CCP.Core, CCP.Avalonia and the Linux test projects listed in build.yml.
- The WPF projects were not built or run (Windows-only). WPF was not changed.
- `--render-all` was not run. The Share button only appears once a user-installed mod is selected, which the render's design constructor does not do, so no PNG shows the change.
- The live MEGA/catalogue submit was not exercised against the real server. Only the headless fake-handler test covers it.

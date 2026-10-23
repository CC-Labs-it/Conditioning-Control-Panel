# fin-views-tab-assets

- Add video thumbnails to Library > Assets for local folders and installed encrypted packs.
  Windows uses the WPF shell-image factory (including its icon fallback); other platforms use
  the head's existing shared VLC and frame sink, bounded to 100 px and a five-second wait.
  Decode failures retain the Fluent video placeholder. A successful thumbnail replaces it.
- Reuse the existing four-slot decode gate, folder generation check and 50-entry pack LRU.
  Pack video decrypts use ContentPackStore ownership and are deleted on success or failure.
  The VLC frame is copied on the UI thread into an immutable Bitmap before the decoder is freed.
  No new window, notification or panic surface is introduced; existing preview/help surfaces
  keep their current panic registrations.
- Add focused AssetsTabTests for local loading/success, null/throw failures with retries,
  stale-folder results, pack decrypt cleanup on failure/success and successful cache reuse.
- Replace the duplicated ledger entry's stale gap list; keep `stub` for the remote playback dependency.
- Source review: fetched `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  No trunk code imported: its browser/model differs from the base and its pack strip is disabled
  in the WPF reference. The existing pack browser's prior import credit remains
  `b1423f1eab655953a434b180d00f25537069cb42`. New shell extraction is adapted directly from
  WPF `Helpers/ShellThumbnailHelper.cs` at base `f73c51fdd80d3c1a2e79e5ddf0357eec77e1ba5d`.

## WPF gap inventory and decisions

Reference pinned to `f73c51fdd80d3c1a2e79e5ddf0357eec77e1ba5d`:
`Views/Tabs/AssetsTabView.xaml{,.cs}`, `MainWindow/MainWindow.Assets.cs`,
`MainWindow/MainWindow.AssetsFx.cs`, `MainWindow/MainWindow.Presets.cs`,
`MainWindow/MainWindow.TabNavigation.cs`, and `Helpers/ShellThumbnailHelper.cs`.

| Ledger claim on the base | Finding / disposition |
| --- | --- |
| Local video thumbnails | Live WPF path, missing here; implemented. |
| Encrypted-pack video thumbnails | Live WPF path: decrypt, thumbnail, cache, delete temp; implemented. |
| Assets and browser help popups | Already wired by MainShellWindow.HelpButtons.cs; covered by HelpButtonsTests. |
| Packs help popup | Same attachment already present; button is inside the disabled strip in WPF. |
| Content Packs tree node | Already on this base, including selection, preview and counts; existing AssetsTabTests. |
| Pack refresh/delete, cards, download/activate, creator link, hover, horizontal wheel pan | P122: disabled in WPF; leave hidden. |
| Online source affects flash and video playback | Still missing; depends on remote flash/video playback, outside the view's picker. |

Pinned P122 proof (run against the commit, not an unpinned working tree):

```sh
git grep -n -E 'PacksSectionEnabled|PacksSection|RefreshPacksAsync\(' \
  f73c51fdd80d3c1a2e79e5ddf0357eec77e1ba5d -- ConditioningControlPanel/MainWindow \
  ConditioningControlPanel/Views/Tabs/AssetsTabView.xaml
git show f73c51fdd80d3c1a2e79e5ddf0357eec77e1ba5d:ConditioningControlPanel/Views/Tabs/AssetsTabView.xaml
```

- `MainWindow.xaml.cs:208`: `private const bool PacksSectionEnabled = false`.
- `MainWindow.TabNavigation.cs:577`: the only navigation refresh is gated by that constant.
- `AssetsTabView.xaml:115-123`: both pack-header buttons have `Visibility="Collapsed"`.
- `AssetsTabView.xaml:472,718`: pack strip and splitter are collapsed; repository search finds no
  setter exposing them. Other RefreshPacksAsync calls are inside the hidden card-action chain.
- Thus these controls have handler bodies but no live WPF user route. This excludes them
  from required user behavior; it does not justify exposing the strip in Avalonia.

The online dependency is concrete: `FlashOverlay.NextFlashPath` chooses PackImages or DiskBag,
while `MandatoryVideoScheduler.PickNext` chooses PackVideos or its local queue. Neither reads
MediaSource/RemoteMediaRatio. The WPF VideoService remote-selection leg does. The source picker
already writes these settings; porting consumers is a separate feature. The row remains `stub`.

## Verification limits

All commands used Release configuration and .NET 10. Linux builds also passed
`-p:CopyUseHardlinksIfPossible=true`; disk recovery came from removing generated test output
between projects. The final WPF build used the explicit hardlink flags listed below.
No tracked files were removed.

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 warnings, 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 187 warnings, 0 errors |
| `dotnet build` each Linux workflow test project (list below) | 0 each; 0 errors |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0; 3542 passed, 0 failed, 5 existing Vosk skips (model unavailable) |
| `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --render-all <output>` with isolated CCP_USERDATA_DIR | 0; 252/252 views rendered, including AssetsTabView; inspected its PNG |

Linux workflow test projects compiled: CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests,
CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests,
CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests,
CCP.Avalonia.Snapshot.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests and CCP.Core.Tests.
These are the csproj files under their corresponding `Tests/<name>/` directories.

The initial matrix exhausted disk while copying native dependencies. Removing only generated
outputs resolved it; every Linux target was then rebuilt successfully.
The first Core run exposed three failures during disk exhaustion and a new literal `:no-audio` rule
violation in this helper. Replaced the literal with the existing LibVlcSilence.NoAudioOption;
the unchanged Core tests then passed. This is an observed failing-before/passing-after check.

The WPF test cross-build also passed (exit 0; 2 warnings, 0 errors):

```sh
dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release \
  -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false \
  -p:CreateHardLinksForCopyLocalIfPossible=true \
  -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true
```

Its initial copy phase also exhausted disk; removing the already-verified Avalonia outputs
and using the effective hardlink properties let the retry finish. WPF and Windows COM/GDI
code were compiled, never executed on this Linux runtime.

- .NET SDK: 10.0.402. Initial channel metadata lookup returned 403; installer recovered through
  the supported builds feed and installed successfully.
- Checkout: original snapshot HEAD differed and fetch failed read-only; used the authorized
  fallback clone, verified the exact base, then created only the requested task branch.
- Avalonia behavior tests: compile only in this environment, per the job's measured font
  limitation (cloud-test-fonts is absent). No tests are weakened or skipped in source.
  Supervisor/GitHub must execute AssetsTabTests, AssetsMediaPickerTests, HelpButtonsTests and
  InlineLoopVideoTests using `DiffEngine_Disabled=true dotnet test
  Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter
  "FullyQualifiedName~AssetsTabTests|FullyQualifiedName~AssetsMediaPickerTests|FullyQualifiedName~HelpButtonsTests|FullyQualifiedName~InlineLoopVideoTests"`.
  That command was not run here. No local before/after failure claim is made for unexecuted UI tests.
- Windows COM/GDI extraction compiles on Linux but cannot be run here. Linux native VLC
  extraction also requires system libvlc, which this runtime does not provide.

## Assumptions

- Preserve the disabled WPF pack strip rather than introducing a new activation path (P122).
- Reuse VLC off Windows rather than require a new thumbnail executable. A bounded first frame
  is used there; shell-selected poster frames remain Windows-specific.
- Keep the row honest about the remote-playback dependency instead of promoting it to `wired`.

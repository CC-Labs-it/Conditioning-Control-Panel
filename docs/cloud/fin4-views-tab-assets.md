- Add local and encrypted-pack video thumbnail loading through an isolated Windows shell adapter, preserving WPF's 100 px request and thumbnail-to-icon fallback. Reuse the existing four-slot loader, generation guard and pack cache; always delete temporary decrypted pack videos.
- Add regressions for supported/unsupported local videos, background loading, folder-switch races, pack video decrypt/cache/cleanup. No new window or notification surface; existing preview panic registration is unchanged.
- Replace the duplicated, outdated ledger entry with one honest `stub` row and add numbered tester steps.
- Read-only import source fetched: parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No new code imported from trunk. Existing pack tree credits `b1423f1eab655953a434b180d00f25537069cb42`; it was adapted in place.

## WPF gap analysis

Reference pinned at base `5cdc31ee4412d4ff8a47de9dbfc42cbc39440107`:
- `Views/Tabs/AssetsTabView.xaml.cs` forwards events to `MainWindow/MainWindow.Assets.cs`; both were read with the XAML.
- Local and pack video thumbnails were skipped by the head: now attempted, with shell extraction on Windows and placeholder fallback elsewhere.
- The three help buttons and Content Packs tree node were already implemented on the base; the old row repeated obsolete gaps.
- Remaining hidden strip behaviour: catalogue fetching/fallback, installed and remote preview images/cycling, refresh, delete-all with confirmation, pack download/install/uninstall and activation plus reloads, external/upgrade/Patreon links, creator Discord, wheel-to-horizontal pan, hover animations. Core services already exist; their complete card adapter and tests remain unported. The user-visible video gap was prioritized; row remains stub.
- WPF `MainWindow.xaml.cs` declares `PacksSectionEnabled=false`; `AssetsTabView.xaml` collapses the section/buttons/splitter. `RefreshPacksAsync` also has a download-completion event caller, so this unit does not claim a dead-code exemption or wired status.
- Online flash/video media consumption remains an independent service gap recorded by the original row; this unit changes neither the source picker nor playback services.

## Verification limits

- Required Release builds: CCP.Core, CCP.Avalonia, every Linux test csproj named in `.github/workflows/build.yml`, and `Tests/ConditioningControlPanel.Tests` with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: all compiled with zero errors (exit 0). Initial overlapping builds produced a file-lock/child-node failure; the GeneralSettings retry with `-m:1` passed.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed. The first sandbox invocation could not create temporary files; rerun with write access passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 4272 passed, 5 existing Vosk skips. An earlier run under build load had one board frame-budget failure (12.52 ms); the full rerun passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~AssetsTabTests`: exit 1, all 13 failed at shell construction on the documented font-creation limitation. Bodies were not validated. A temporary production-font diagnostic completed 10 tests then stalled; the host was stopped (exit 1). This harness experiment is not part of the final diff.
- Focused three-behaviour diagnostic with temporary production-font registration: exit 0, all 3 passed (local success/fallback and worker thread, pack decrypt/cache/cleanup including thrown codec error, stale-folder discard). The final source retains the base harness; the temporary font setup was removed.
- `bash .github/scripts/test-plan-coverage.sh`: not available on this base (exit 127). Numbered WPF/Rows tester steps were added manually.
- Negative proof: temporarily restore the original local-video skip and run `--filter FullyQualifiedName~AssetsTabTests.LocalVideos` with the same temporary font setup: exit 1, expected video filenames but received an empty list. Restore the fixed source afterward; the fixed three-behaviour diagnostic above passed.
- `git diff --check`: exit 0.
- Windows shell extraction and the WPF test executable were compiled, not run on Windows. No render-all or live desktop proof; the full headless suite still needs CI validation. CI/Windows validation is required before promotion.


## Assumptions

- Match WPF's Windows shell codec availability; on other OSes retain the existing placeholder rather than introduce a new video decoder dependency.
- Preserve the WPF-hidden pack strip; do not expose incomplete pack management to users.

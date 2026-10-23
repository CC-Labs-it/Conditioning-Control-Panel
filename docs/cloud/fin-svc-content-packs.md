# fin-svc-content-packs

- Audited `svc-content-packs` against frozen WPF at base `8bf304d2f3d7432a0ddcff7fb98de5715080dc73`; retained `stub` with an explicit Missing list. One ledger row remains.
- Dependency-only result: no production or test code changes. The remaining live paths require the unported Bureau host/index/drop surface, Chaos bubble/media effects, or EMI Desk moment bus. Adding a different installer or exposing WPF's hidden cards would change the reference behavior.
- Corrected the stale Discord dependency wording: Discord account/token support exists in `Platform/AccountSeed.cs`; the missing pack-status fallback is an unused WPF API, not an absent account provider.
- Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No imports in this unit; the existing network-service import remains credited to trunk U8 `30e6c8bd6`.

## Behavior inventory

All source locations below refer to the pinned base above, not a moving trunk.

| WPF behavior | Current head / remaining gap |
| --- | --- |
| Startup, orphan discovery/auto-activation, encrypted manifests, installed/active filtering, assets-path refresh, decryption and temp cleanup (`ContentPackService.cs:85-248,955-1244`) | Already supplied by `ContentPackStore`, `App.StartContentPacks`, assets-move rescan and exit cleanup. Active pack files reach the Library tree, thumbnail/preview and per-file/folder opt-outs. |
| Flash images, scheduled video and bubble-count video | Already draw through Core `PackMediaPool`; startup provides the pack-video count. Existing `PackMediaPoolTests`, `ContentPacksWireTests` and `AssetsTabTests` cover these paths. |
| Remote manifest and built-in fallback (`:251-305`); local ZIP validation, recursive image/video discovery, encryption, manifest/settings publication, status callbacks, completed event, preserving the source ZIP and cleanup on failure (`:610-689`) | Service implementation and fake-HTTP/filesystem tests already exist. Missing real caller: Bureau evidence lockers and ZIP intake. WPF matches normalized ZIP filename to catalogue name/ID, refuses unknown/already-installed packs or concurrent intake, forwards progress, rebuilds the image index, and refreshes lockers. Bureau also indexes/decrypts all installed image packs regardless of active state (`BureauIndexService.cs:73-174`). No Bureau host, index or launcher exists on this head. |
| Chaos wash/cascade draws enabled pack images from the shared flash pool (`FlashService.cs:3612-3708`, `ChaosFlashOverlay.cs:320-344`, `ChaosGifCascadeOverlay.cs:71`) | Missing consumer feature: Chaos bubble/media effects. The shared pack pool exists, but `ChaosRunHost` only runs the clock/HUD/countdown/recap; `ChaosHubWindow` FALL IN remains a logged no-op. The future consumer must preserve distinct source picks, opt-outs, null/no-image behavior and temp cleanup. A second pack pool would duplicate existing Core behavior. |
| EMI Desk `contentPackInstalled` after either successful install, target = lowercased pack name (`:588,:680`) | Missing EMI Desk moment bus/`Fire` integration. Head `EmiDeskService` only implements summon/dismiss/presentation/hotkey; its comment explicitly defers the moment bus. No `EmiDeskBus` exists in Core. Both install callers must eventually produce the same beat, after completion; no artificial event seam added for a consumer that cannot run yet. |
| Signed URL download, retry/resume, auth/rate-limit UI, activation/deactivation, uninstall, external link/manual instructions, card preview rotation (`MainWindow.Assets.cs:75-459,1979-2128`) | Network/storage implementations already exist, but card-only UI callers are dead in WPF: const-false navigation gate, collapsed strip and collapsed Refresh/Delete buttons. Keep hidden in Avalonia. Includes currently different dead-path authentication copy and install-status sequencing; no user can reach them in WPF. |
| External download merged-account recovery (`ContentPackService.cs:785`) | Absent in Core pack API; its only WPF caller is the hidden card download handler. Account recovery service exists separately but its head callbacks are unseeded. No reachable WPF gap to wire in this unit. |
| Patreon-first pack status, Discord fallback after absent/failed Patreon (`:831-884`) | Patreon branch exists; Discord fallback absent. Both status methods have no WPF caller outside their own service definitions/internal delegation. No status UI to reproduce. |
| Cached installed/URL card previews (`:1252-1373`, `MainWindow.Assets.cs:213-275`) | Core byte/cache helpers exist; display/rotation belongs to the hidden cards. Library per-file previews already work independently. |
| Focus Training installed-pack buckets (`FocusGameService.cs:35-128`) | Skeleton is constructed/disposed in WPF, but `RefreshBuckets` has no caller; no live bucket-enumeration behavior to port. |

## Pinned caller proof

Reproduce from the repository root (exit 1 means the stated no-match):

```sh
ref=8bf304d2f3d7432a0ddcff7fb98de5715080dc73
git grep -n 'PacksSectionEnabled' "$ref" -- ConditioningControlPanel/MainWindow/MainWindow.xaml.cs ConditioningControlPanel/MainWindow/MainWindow.TabNavigation.cs
# :208 const false; :577 gates the navigation refresh.
git grep -n -E 'PacksSection|BtnRefreshPacks|BtnDeleteDownloadedPacks|BtnPackDownload_Click|BtnPackActivate_Click|Visibility="Collapsed"' "$ref" -- ConditioningControlPanel/Views/Tabs/AssetsTabView.xaml
# :115-123 Refresh/Delete collapsed; :366 strip collapsed; :524/:530 its only card buttons.
git grep -n -E 'PacksSection.*Visibility|BtnRefreshPacks.*Visibility|BtnDeleteDownloadedPacks.*Visibility' "$ref" -- ConditioningControlPanel
# A comment and the collapsed strip declaration; no code enables these controls.
git grep -n -E 'RefreshPacksAsync\(|InstallPackAsync\(|GetExternalPackDownloadUrlAsync\(' "$ref" -- ConditioningControlPanel
# Definitions, hidden-card handlers, completion callback reached from that same strip, and gated navigation.
git grep -n -E 'GetFullPackStatusAsync\(|GetPackDownloadStatusAsync\(' "$ref" -- ConditioningControlPanel
# ContentPackService only: :820 definition, :822 internal delegation, :831 definition.
git grep -n 'RefreshBuckets(' "$ref" -- ConditioningControlPanel
# FocusGameService.cs:35 definition only.
git grep -n -E 'GetAvailablePacksAsync|InstallPackFromLocalZipAsync' "$ref" -- ConditioningControlPanel/Services/Bureau
# BureauHostService.cs:382,470 catalogue; :484 local install.
git grep -n -E 'BureauHostService|BureauIndexService|EmiDeskBus' "$ref" -- CCP.Core CCP.Avalonia
# No matches (exit 1).
```

The whole row cannot use P122: Bureau, Chaos effects and EMI moments are live in WPF. Retain `stub`; no render-all or verified-parity claim. No new surface means no new panic registration or behavior tests.

## Verification limits

- SDK: .NET `10.0.402`.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release --nologo` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release --nologo`: exit 0, `0 Error(s)` (existing warnings).
- `dotnet build <project> -c Release --nologo`: exit 0, `0 Error(s)` for every Linux test project listed in `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Snapshot.Tests` (each under `Tests/<name>/<name>.csproj`). Initial sweep exhausted temporary disk while copying test assets; removed only generated test outputs and repeated the affected builds one at a time, all successfully. No tracked assets removed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release --nologo -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, `512 Warning(s)`, `0 Error(s)`. WPF/Windows code compiled, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0; `All Core guards passed.` Checked 424 XAML type references.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --nologo`: exit 0; `Failed: 0, Passed: 2812, Skipped: 5, Total: 2817`. Includes the existing `ContentPackServiceTests`, `ContentPackStoreTests` and `PackMediaPoolTests`. The five skips are the suite's existing Vosk-model-dependent tests; the model is unavailable in this environment. No tests weakened or changed.
- Avalonia runtime tests (`ContentPacksWireTests`, `AssetsTabTests`) and `--render-all` were **not run**, per the supplied cloud font-creation limitation. The test bootstrap still lacks the cloud-test-fonts setup; compilation above passed. GitHub rcheck must execute them. No P122 promotion relies on an unperformed render.
- Pinned grep checks above: exit 0 with the listed references; the missing-head-dependency search returns expected exit 1. `git diff --check`: exit 0.
- No actual CDN/proxy download, account authentication, native Windows execution or desktop interaction was tested. No new behavior or surface was introduced.

## Assumptions

- Applied the explicit dependency-only exception: document the remaining features instead of creating a replacement installer or porting unrelated feature hosts.
- Kept the existing active-pack Library path and WPF-hidden card controls as they are. A Core API without a real caller does not qualify the row for `wired`.
- Used the authorized fallback clone after the original checkout had the wrong HEAD and a read-only Git directory; its base and remote base both matched the required commit exactly.

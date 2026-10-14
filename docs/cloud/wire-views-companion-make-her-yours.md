# wire-views-companion-make-her-yours

- Wired the existing Community prompts link to WPF's first-five browse dialog: server order, name/author/description, installed-ID filtering, Install/Cancel, installation of the first candidate, and empty/all-installed/installed outcome messages. Installation does not activate a prompt or bypass the existing personality acknowledgement gates.
- Active community readout now uses its installed name, with the WPF id fallback when the file is missing or corrupt.
- Moved `CommunityPrompt` into Core and made its export snapshot use CoreSettings. Added Core `CommunityPromptCatalogue` for cached discovery, offline behavior, installed-file metadata and installation; WPF delegates to the same implementation and browse-message formatting. No interface or new dialog class; WPF panic does not close styled informational dialogs.
- Focused tests use fake HTTP and temporary storage. They cover cache/offline/no-download behavior, installation/read-back, IDs, corrupt files, export-copy isolation, server order/first-five formatting, the real community-button pointer path, cancellation, installation, installed-name fallback, and empty/all-installed outcomes.
- No parity/trunk code imported. Read-only reference fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; source adapted from the WPF files on base `0e1aa0473e7a5b6120544923e94bb04cc62a7e50`.
- Updated only ledger row `views-companion-make-her-yours` to wired; no unported unit behavior remains.

## Verification limits

- Core, Avalonia and all 11 Linux test projects in `.github/workflows/build.yml` built successfully (`-c Release -m:1 -p:UseSharedCompilation=false` and hardlink copy properties). Logs: `/tmp/worker6-community-build-summary.txt`.
- `bash .github/scripts/core-guards.sh`: all guards passed (424 XAML references).
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~CommunityPromptCatalogueTests`: 2 passed, zero failed/skipped.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~MakeHerYoursCommunityTests|FullyQualifiedName~CompanionPersonalityZoneTests'`: final expanded run passed 2/2, zero failed/skipped (82 seconds).
- WPF/Windows test project cross-build (`-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`): passed, zero errors; Windows code compiled but not run.
- Full Core suite: 2812 passed, 1 failed, 5 skipped, total 2818. Unchanged `BoardTilesTests.A_frame_fits_the_budget` exceeded its timing budget at 17.85ms during shared-host work; its isolated retry (`--filter FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget`) passed 1/1. Five speech tests skipped because the Vosk model is unavailable.
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/worker6-community-render`: 224/224 views rendered. `MakeHerYoursView.png` shows the enabled Community prompts link; visually inspected. The dialogs use existing `MessageDialog` chrome.
- Test/renderer profiles are sandboxed, with `FONTCONFIG_FILE=/workspace/ccp-test-fonts.conf` and `DiffEngine_Disabled=true` for tests. Only fake HTTP was exercised; the live community server was not contacted by verification.
- Windows UI/runtime cannot execute on this Linux host. Generated artifacts use local ignored temporary output directories where needed to fit the shared disk.

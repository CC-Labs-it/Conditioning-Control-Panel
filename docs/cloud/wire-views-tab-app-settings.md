# wire-views-tab-app-settings

- Settings is a persistent scrolling page: hiding it does not detach its sections. Reopening it now refreshes General's existing startup registration/video filename path and Devices' existing microphone/camera enumeration path through `IAppSettingsSection`.
- Performance and Notifications reuse their guarded settings repaint on each Settings-door open, matching WPF's visibility-change repaint. Seeding retains the guards against writing stale control values back into settings.
- `AppSettingsRefreshTests` drives the real shell's Settings → Home → Settings route with temporary XDG registration and fake camera/microphone lists. A second test proves repeat refresh and fault isolation. No new dialog, overlay, background loop, or device capture is added.
- Source: WPF reference files on `origin/avalonia-port/goongame-core-c`. `parity/trunk` was fetched/read at `a4ee52d40`; no code was imported from that branch.

## Remaining work: wire-views-tab-app-settings-b

Keep `views-tab-app-settings` at **stub**. WPF's `AppSettingsTabView.CheckPill` follows/parks Account's embedded `PlansView` vault motion through `OnPlansVisibilityChanged`. Avalonia has no embedded `PlansView`/`PlansMode` yet. That requires the Account Plans page and its vault-motion integration before active-section gating can be wired; implementing that whole page exceeds this refresh slice and the branch's 1000-added-line cap. Account backup/export and other per-section omissions remain owned by their existing section ledger rows.

## Verification limits

- Regression baseline, before the four refresh hooks: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~AppSettingsRefreshTests` reported **1 failed, 1 passed**. Failure: reopening Settings retained `old.mp4` when the current startup video was `new.mp4`.
- After fix, the same standalone class with `--no-build` reported **2 passed, 0 failed, 0 skipped**. Tests use the production app builder for its bundled fonts; all filesystem/device changes are sandboxed/faked.
- A combined run of `AppSettingsRefreshTests`, `NotificationsSettingsSectionTests`, `PerformanceSettingsMotionTests`, `WebcamDevicePickerTests` and `NavRehomeShellTests` reported **8 failed, 1 passed** because another class initialized a bare headless builder without production font setup. Failures were glyphTypeface resolution for `Segoe UI Black`/`Consolas`/`Courier New`, before behavior assertions. The existing tests/harness and production font families were not changed.
- Initial test build exhausted shared disk while copying runtime/content files. Only generated bins in this worktree were cleaned; subsequent commands used the SDK's hardlink copy switches. No source assets, tests, or user data were removed.
- `dotnet build <project> -c Release -m:1 /p:UseSharedCompilation=false` passed for `CCP.Core`, `CCP.Avalonia`, and all 11 Linux test projects listed in build.yml: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Consent.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Language.Tests, Avalonia.Report.Tests, Avalonia.Scheduler.Tests and Avalonia.Snapshot.Tests. All completed with 0 errors.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` also passed (0 errors, 512 warnings). The WPF head and WPF tests were **compiled, not run** on Linux.
- Build commands also used `-p:CreateHardLinksForAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true` to share generated copies without altering sources. Auxiliary test bins were removed after build to retain disk space; main Core/Avalonia test bins remain.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed**.
- `DiffEngine_Disabled=true CCP_VOSK_MODEL=/workspace/shared/vosk/vosk-model-small-en-us-0.15 dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: **2816 passed, 0 failed, 0 skipped**.
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/worker2-render`: **224/224 views rendered**, exit 0, with `CCP_USERDATA_DIR=/workspace/worker2-profile` and isolated `XDG_CONFIG_HOME`. The Settings page is `/workspace/worker2-render/AppSettingsTabView.png`; it was inspected. A still render proves construction/layout, while the reopening/hotplug behavior is covered by the tests above.
- Not run: WPF/Windows runtime tests, real OS autostart/session login, real camera/microphone hotplug, or embedded Plans vault motion (not yet implemented).

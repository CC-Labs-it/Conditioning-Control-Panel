# feat(avalonia launcher): "Media and sound" dialog behind the Media pill

Ledger row `win-launcher-media-dialog`: missing -> wired.

- **Core rules**: `ConditioningControlPanel/Services/Launcher/LauncherMediaSettings.cs` moved with `git mv` to `CCP.Core/Services/Launcher/LauncherMediaSettings.cs` (same file as WPF 7.1.5 and trunk; the WPF head now gets it through its `CCP.Core` reference, as `LauncherMelody` did). It accepts known sources only, keeps niches in catalogue order and never empty, resets the Back Room to "auto", and clamps the master volume to 0..100.
- **Core tests**: `Tests/CCP.Core.Tests/LauncherMediaSettingsTests.cs`, 12 cases, imported from parity/trunk `687a604cc`.
- **Avalonia dialog**: `CCP.Avalonia/Views/Windows/LauncherMediaDialog.cs`, imported from parity/trunk `687a604cc` and adapted to this tree:
  - Layout follows WPF `LauncherMediaDialog.xaml` value for value: 560x790, no chrome, a drag title bar and a close button, then the source chips with their hint, the Mixed share slider, the niche chips, the asset preset combo with "Open asset browser", the master volume, the footnote and Done. It uses the same `launcher_media_*` copy keys.
  - Changes apply as they are made; Done only closes. The consent question is the Assets page's own, awaited before anything is written.
  - Adapted: the close glyph is now a Fluent `Dismiss` icon with an accessible name.
  - Adapted: trunk's `AssetSelection.NotifyChanged` does not exist here, so pool invalidation calls the Assets page's own `InvalidateAssetPoolsAfterSelectionChange`, now `internal`.
  - Adapted: `WindowDecorations` is set with a fully qualified name to get around the property and type having the same name.
  - Adapted: chip hover and checked states are styles on Fluent's `PART_ContentPresenter`. Trunk set local brushes, which Fluent's checked accent-blue painted over.
- **Caller**: the Avalonia `LauncherWindow` gets the Media pill (`BtnGear`, WPF `LauncherWindow.xaml:465`, after the speaker). It uses the Fluent `Image` icon instead of WPF's ▣ glyph and opens the dialog modal over the launcher (`ShowDialogSafe`).
  - If the dialog fails to open, it falls back to the panel's App Settings tab, as WPF does.
  - "Open asset browser" brings the panel up on the Assets tab.
  - Under Lockdown the click is refused, matching the ModPill.
- **Tests**:
  - `Tests/CCP.Avalonia.Tests/LauncherMediaDialogTests.cs`:
    - WPF copy, live settings and Done.
    - Consent no and yes, the Back Room reset and the ratio slider.
    - The last niche stays on, and the master slider writes the volume.
  - `LauncherWindowTests.MediaPill_OpensTheMediaDialog`: the pill opens the dialog, owned by the launcher.
- **render-all**: it picks up the dialog automatically (it is in the Views namespace and has a parameterless constructor). `--render-view LauncherMediaDialog` drew it at 560x790.
- **Docs**: the `docs/avalonia-parity.md` row is now `wired` with this evidence, and "gear/media dialog" is dropped from the `win-launcher` row's missing list.
- **Deviations (ponytail)**:
  - Picking a preset does not hand off to the panel (WPF `ApplyAssetPresetFromLauncher`). The Assets page repaints its picker and folder when it is next shown.
  - The asset-browser link has no hover underline.

## Verification limits

- `dotnet build`: `CCP.Core`, `CCP.Avalonia`, `CCP.LinuxSmoke`, `Tests/CCP.Core.Settings.Tests`, `Tests/CCP.Core.Content.Tests` and `Tests/CCP.Avalonia.{Consent,Language,CatalogueStartup,Scheduler,Report,GeneralSettings,Snapshot}.Tests` all build with 0 errors. `Tests/ConditioningControlPanel.Tests` builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; it was built, not run.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1438 passed, 2 failed, 5 skipped.
  - The 2 failures are `LoopbackOAuthTests...(host: "[::1]")` (no IPv6 loopback in this container) and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (the container runs as root, which can delete the folder).
  - Both fail the same way on the untouched base `avalonia-port/emi-line-engine`.
  - `LauncherMediaSettingsTests`: 12 of 12 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter LauncherMediaDialogTests|LauncherWindowTests|LauncherFxTests`: 41 of 41 passed, headless, `DiffEngine_Disabled=true`, CCP_USERDATA_DIR sandbox.
- The full CCP.Avalonia.Tests suite was not run; only the launcher classes were.
- Not run live: no desktop session here. The dialog was only rendered offscreen and driven headless. The WPF head was compiled through its test project but not run (Windows-only).

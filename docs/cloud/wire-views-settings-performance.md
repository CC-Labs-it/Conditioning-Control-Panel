# wire-views-settings-performance (slice; row stays `stub`)

Settings ▸ Performance toggles that only saved a value now drive behaviour on the Avalonia head.

- **Auto performance mode**: the tier rule moved to Core (`CCP.Core/Services/UI/PerformanceTierRule.cs`,
  thresholds 8 / 16 verbatim from WPF `PerformanceProfile`); WPF `PerformanceProfile.CurrentTier` now calls it.
  Avalonia `AmbientFxCanvas.Env.CurrentTier` uses it with `HeavyElementCount` = live `FlashOverlay` windows +
  ambient bubbles, so a heavy screen drops to Balanced / Performance and sheds ambient loops, as on WPF.
- **Force video hardware decoding**: the mandatory-video player decodes in software (and adds
  `:avcodec-hw=none`) unless the user opted in, as WPF `VideoService.cs:3494/3861`. Before this it always
  used hardware decoding. The option string is Core `MandatoryVideoScheduler.SoftwareDecodeOption`, which WPF
  now calls too.
- **HW-decode box sync**: `ChkVideoHwDecode` follows the second editor (Settings ▸ Monitors), as WPF
  `PerformanceSettingsSection.xaml.cs:68`. Adapted from parity/trunk `eab61edb3`.
- **Motion level, shell chrome half**: a motion change also re-reads the window edge lift, the section strip's
  sheen clock, the favourites handle glow and the fold-arrow breath. This is the part of WPF
  `CmbMotionLevel_SelectionChanged` that the self-listening loops did not cover
  (`MainShellWindow.SectionChrome.cs` `InitializeMotionChrome`/`OnMotionChrome`).
- **Do-not-disturb on Windows**: `Platform/Win32Windows.cs` reads the user32 foreground window and lists
  processes with a main window for the picker. App seeds Core `DndGuard` with it on Windows and with X11
  elsewhere. Adapted from parity/trunk `119ec005f` / `2dbac1078` (`Platform/DoNotDisturbGuard.cs`), trimmed to
  the two reads because Core `DndGuard` already owns the cache and the decision.
- Ledger row `views-settings-performance` updated. It stays `stub`, with follow-up lane
  `wire-views-settings-performance-b`:
  - `ChkUnifiedOverlay` (UnifiedOverlayHost) routes WPF's CompositorEngine (~4.5k lines), which has no twin
    on this head yet.
  - The lockdown pulse is not stopped on motion Off, because nothing on this head starts it.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter PerformanceSettingsWiringTests`: 3/3 pass. All 3 fail with
  the three wirings removed (motion chrome hook, heavy-element count, settings hook).
- Related Avalonia classes (Performance*, HomeLayoutSync6, MandatoryVideo*, SectionTabStrip, NavRail,
  Bubble*, FlashOverlay*, TierBadge, AmbientFx*, PanicSurfaces, DashboardFx, MonitorsSettings): 43 passed,
  2 skipped. The LibVLC real-clip test skips here because there is no libvlc.
- `dotnet test Tests/CCP.Core.Tests`: 2785 passed, 6 skipped, 2 failed. Both failures also fail on the
  untouched base in this sandbox: `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted` (root ignores
  read-only directories) and `LoopbackOAuthTests ... [::1]` (no IPv6 loopback).
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- Builds: CCP.Core, CCP.Avalonia, every Linux test csproj in build.yml, and Tests/ConditioningControlPanel.Tests
  (with `EnableWindowsTargeting`) all succeeded. That last build includes the WPF edits (PerformanceProfile,
  VideoService).
- Compiled but not run: `Platform/Win32Windows.cs` (user32), the WPF head's edits, and the mandatory-video
  decoder path with a real LibVLC.
- No `--render-all`: nothing static changed. The change is motion clocks, tier choice and decoder flags.

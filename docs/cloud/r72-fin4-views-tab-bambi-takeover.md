# Takeover chant re-cut

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad. Old lane: 07ed62e9cd17.
Only views-tab-bambi-takeover is advanced. Bubble overlay changes are excluded.
The row remains stub because wallpaper and other documented gaps remain.

| Behaviour | Old lane evidence | Re-cut evidence | WPF specification |
| --- | --- | --- | --- |
| Explicit toggle, hint, live volume | CCP.Avalonia/Views/Tabs/BambiTakeoverTabView.axaml.cs:601-612 | Same file:604-615 | ConditioningControlPanel/Views/Tabs/BambiTakeoverTabView.xaml.cs:145-175 |
| Pair order, beat, gap, bounded retry, mute and stale callbacks | CCP.Core/Services/MantraChantLoop.cs:21-106 | Same file:22-107 | ConditioningControlPanel/Services/MantraChantService.cs:45-50,231-287,333-368 |
| Transport and volume | CCP.Avalonia/Platform/MantraChantHost.cs:16-63 | Same file:17-64 | ConditioningControlPanel/Services/MantraChantService.cs:144-159,300-319,337-355 |
| Startup disarm, exit disposal | CCP.Avalonia/App.axaml.cs:454,1186 | Same file:519,1272 | ConditioningControlPanel/App.xaml.cs:3500-3503,6529 |
| Panic disarm | CCP.Avalonia/Views/Windows/PanicSurfaces.cs:46 | Same file:52 | ConditioningControlPanel/App.xaml.cs:1880; Services/MantraChantService.cs:109-121 |
| Entitlement lapse stop | CCP.Avalonia/Views/Windows/MainShellWindow.Patreon.cs:143 | Same file:140 | ConditioningControlPanel/MainWindow/MainWindow.Patreon.cs:196 |

Already on the base: CCP.Avalonia/Views/Tabs/BambiTakeoverTabView.axaml.cs:56-62
(Start/Stop, Test, Test voice, Devices, model folder), :241-249 (chant settings and hint),
:475-625 (consent, controls and settings). WPF tab:120-175,183-259; shell
MainWindow.Autonomy.cs:143. These were not re-ported.

Part B, remaining after checking the base:
- Wallpaper host, keep-off restore, empty-library warning, credit/link. WPF
  ConditioningControlPanel/Views/Tabs/BambiTakeoverTabView.xaml.cs:236-259;
  Avalonia tab code:541-553 still saves settings only.
- Companion permissions destination and hidden Force Start. WPF tab:53,183-186.
- Web Video: CCP.Avalonia/Views/Windows/MainShellWindow.Autonomy.cs:95;
  WPF ConditioningControlPanel/Services/AutonomyService.cs:1226.

The old lane also listed countdown, event audio and diagnostic Test as missing.
They are already present on the base: CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.TakeoverBar.cs:85,
CCP.Avalonia/Views/Windows/MainShellWindow.Autonomy.cs:84,276. WPF references:
ConditioningControlPanel/MainWindow/MainWindow.Autonomy.cs:456,600;
ConditioningControlPanel/Services/AutonomyService.cs:1797. No changes needed.

Base startup recovery and all existing panic stops are preserved. No WPF files,
localization keys, Scrolller code or progress ledger are changed.

Validation uses .NET 10.0.403. Core: 4648 passed, 6 inherited skips.
The 13 focused Core chant cases pass after seven independent mutations
(beat, mute, stop, retry bound, gap clamp, volume, clip order) each failed.
Headless UI tests use the suite dispatcher, process-state isolation and the
existing QuestsDepthTests bundled Inter mapping for Segoe families. The cloud
image cannot resolve Segoe UI Black; no product font or assertion was changed.
Live audio output and a desktop session were not exercised.

Seven independent headless UI mutations also failed at assertions: missing
Start, Stop, live ApplyVolume, panic disarm, persisted-flag disarm, Dispose and
entitlement-lapse Stop. All mutations were restored. The affected and isolation
filter is TakeoverTabUiTests|PanicSurfacesTests|PremiumGatesTests|TestIsolationTests.

Final restored headless run: 23 passed, 0 failed, 0 skipped. All three builds,
core-guards.sh, avalonia_docs.py and git diff --check pass. The full Avalonia
test assembly was compiled; only the affected and isolation filter was run.

Review P2: live tube mute now governs loop parking and volume, with settings
fallback (CCP.Avalonia/Platform/MantraChantHost.cs:23,34; WPF
ConditioningControlPanel/Services/MantraChantService.cs:185-193,231-253).
The focused tab test minimizes/restores the shell with an attached avatar,
checks zero volume and disposed audio, and verifies fresh-pair resume.
Review validation: both mute-gate reversions failed independently, then restored.
Core: 4648 passed, 6 inherited skips; affected/windowing/isolation: 28 passed.

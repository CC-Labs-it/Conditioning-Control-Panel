# fin4-shell-autonomy

- Base verified: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Branch: `avalonia-port/cloud-fin4-shell-autonomy`.
- Added the avatar countdown's live path: Core reports the fraction of the actual scheduled interval; the existing tube scales its fill from the left every 100 ms while Takeover runs, hides without opt-in/random scheduling, and stops/unsubscribes on close. No shell animation loop or new overlay.
- Announcements resolve the active mod's generated event clip through the existing Core content ladder and speech player. Missing clips remain text-only. The generator slug is shared with WPF.
- Test shows WPF's diagnostic snapshot before executing. Panic registers/closes the diagnostic surface and invalidates its awaited continuation. The stopped case includes status and the original entitlement/start guidance.
- Replaced duplicated historical ledger prose with one honest `stub` row, focused automated coverage and numbered tester steps.
- Imports: none. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; adapted the pinned base's WPF reference and reused this head's services. WPF files are unchanged.

## WPF gap audit and remaining scope

All references below are pinned to base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`.

- `MainWindow/MainWindow.Autonomy.cs`: switches, consent, entitlement/Lockdown refusal, Start/Stop, cadence/idle controls, wallpaper setting controls, voice hints/test/input configuration, resume and countdown setting already have head handlers. The countdown setting lacked a display consumer; fixed here. Wallpaper keep/picker/duration still only save settings: no host applies/reverts wallpaper or warns about an empty library.
- `Services/AutonomyService.cs:173-211`: Test diagnostics were absent; fixed here. `:1758-1773`: matching event audio was absent; fixed here. `AvatarTube/AvatarTubeWindow.Avatar.cs:43-126`: countdown display/ticker was absent; fixed here.
- Web Video needs `Services/Browser/BrowserMediaService.cs` (554 reference lines), browser bridge/fullscreen ownership, repeat avoidance in `AutonomyService.cs:1187-1297`, and `Services/Video/Browser/BrowserVideoWindow.cs` (226 reference lines). A normal external-browser launch would omit ownership, bounded duration, heartbeat, grace/restore and panic semantics. No such shortcut was added.
- Wallpaper needs `Services/Video/WallpaperService.cs` (852 reference lines): original capture/stale recovery, serialized OS writes, local and remote pools/temporary leases, pending activation and cache age handling. Takeover adds replaceable timed revert versus keep, Stop handback and empty-library feedback. Together with Web Video and behavioral tests this exceeds the 1000-added-line unit cap. The ledger retains these missing behaviors instead of claiming wired or installing partial OS behavior.
- Mind Wipe is already seeded by LibVLC startup (`CCP.Avalonia/App.axaml.cs:702`, `Platform/MindWipePlayer.cs:56`); the historical “never today” claim was stale. Spoken Mantra and announcer banner are also already live.
- Brain Drain and Spiral pulses are dead in WPF's weighted picker. Grep proof: WPF `Services/AutonomyService.cs:930-951` delegates to `AutonomyScheduler.Candidates`; pinned `CCP.Core/Services/Autonomy/AutonomyScheduler.cs:98-128` has no candidate additions for BrainDrainPulse, SpiralPulse or BubbleCount. Bubble Count's existing forced entry stays intact. No dead pulse implementation was added.
- Force Start's handler exists, but its only control is `Visibility="Collapsed"` in WPF `Views/Tabs/BambiTakeoverTabView.xaml:761`. The head keeps the same hidden control. No public gate-bypassing debug path was introduced.
- Other features' time-based achievement credit belongs to their own service/overlay rows; Takeover quest minutes were already wired.

## Verification limits

- Added lines versus the required base: 354 (cap 1000).

- SDK: .NET 10. Channel discovery initially returned HTTP 403; installing stable 10.0.100 succeeded (channel installer subsequently completed to 10.0.402).
- `dotnet build -c Release` (exit 0 for every project, zero errors): CCP.Core, CCP.Avalonia, CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests (test projects under Tests).
- WPF test build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 warnings, zero errors. Compiled only; Windows code cannot run here.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3955 passed, 5 existing Vosk skips, 3960 total. Focused AutonomySchedulerTests/CompanionEventAudioTests: exit 0; 16 passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~TakeoverTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0; 17 passed, zero skipped. The Takeover bootstrap now uses the shipped font setup (as PanicSurfacesTests already did); these classes were actually run using the assembly's isolated userdata sandbox.
- Regression red phase: compiling the new countdown test against the old implementation failed with CS1061 (missing NextRandomFireFraction). The first implemented run exposed exact-full fraction rounding; storing the actual scheduled interval fixed it without weakening assertions.
- Initial overlapping builds caused a stale app assembly during a headless run; sequential rebuilding fixed that. A repeat run then exposed order-dependent glyphTypeface failures when Takeover bootstrapped Avalonia without shipped fonts; applying the existing panic tests' font configuration to each Takeover bootstrap fixes that environment dependency. An early Core run also hit remote-media selection and frame-budget failures. Both passed alone on the unchanged base, its full suite passed (3953 passed, 5 existing skips), and the final changed full suite passed. No tests were skipped, removed or weakened to get green.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The requested test-plan-coverage.sh is absent on this base (invocation exit 127); numbered WPF/Rows steps were added manually.
- Not run: Windows desktop behavior, full Avalonia suite, real audio playback, render-all and visual timing on a real desktop. No new Windows-specific implementation was added.

## Assumptions

- Prioritize independent visible behavior within this unit; keep `shell-autonomy` at `stub` until browser-media and wallpaper hosts land.
- Leave WPF's hidden Force Start and non-candidate pulse actions hidden/unselected, preserving its live path.
- Reuse existing MessageDialog and speech surfaces; the diagnostic participates in the panic registry.

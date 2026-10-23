# fin-shell-profile-bubble

- Restore WPF's quadratic ease-out on the gold ring's 450-1200 ms fade; preserve the 120 ms rise and hold through 450 ms.
- Restore WPF's sine ease-out on the tier badge's final 525-700 ms settle; retain the shared one-shot frame clock, 8 s timer, earlier keys and motion gates.
- Add font-independent production-track regression tests and keep the ledger row `stub` with the remaining dependency explicit. No new surface needs panic registration.
- Source: frozen WPF at base `72796641344e4706b7f884abd6cbf53623d70b39`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported from trunk in this unit. Existing tier/beat implementation already credits trunk `56ab3f850` / `66ca21fd3` in its source.

## WPF audit

Compared `ConditioningControlPanel/MainWindow/MainWindow.ProfileBubble.cs` and `MainWindow.ProfileBubbleTierFx.cs` with their Avalonia twins, the shell XAML, HeroFx and existing ProfileBubble tests. Followed the spiral calls into WPF `MainWindow.ProfileSpiral.cs` and the head's stub, and checked the live Spiral Room gates.

| WPF behavior | Result on this branch |
| --- | --- |
| Face priority: shared Discord photo, equipped preset bust, deterministic initials/gradient, signed-out neutral question mark; photo cache and failed-load fallback; account/tier repaints | Already implemented on base. |
| Menu name/tier, localized level/XP rail and clamped fraction, reachable achievement totals, sign-in/logout caption and action | Already implemented on base, reusing Core and the existing shell paths. |
| 100 ms hover delay, 250 ms close grace, popup hover cancellation, dismissal on window state/deactivation/outside press, direct Profile click and Profile/Achievements/Settings doors | Already implemented on base. Portable edge-aligned popup placement replaces WPF custom placement. |
| Public-profile sharing URL from menu and Trainer Card, signed-out disabled CTA, failed-browser clipboard/prompt and sandbox refusal | Already implemented on base. |
| UI-thread reactions: XP pulse (900 ms throttle, 1.10/260 ms), level-up (1.35/560 ms + 45-particle burst + ring), achievement (1.18/380 ms + ring) | Already implemented on base; ring's fade easing corrected here. |
| Flash tilt (-12/9/-5/0 degrees at 90/220/340/480 ms; 2.5 s throttle), subliminal shimmer (0.55 dip, 300 ms each way; 4 s throttle), motion gates | Already implemented on base. |
| Gold ring: 120 ms rise, hold to 450 ms, quadratic ease-out to zero at 1200 ms | Base faded linearly; corrected here. |
| Tier: 8 s background timer, one 700 ms wobble, hidden/minimized/motion gates; 72 px left hover copy at -4/-46, 260 ms BackEase amplitude 0.45, menu restacking and cleanup | Already implemented on base except the final sine ease-out; corrected here. Native stacking still requires Windows validation. |
| Spiral row, glyph, summary, live block/motion updates and close-then-room door | Missing; depends on `shell-profile-spiral`, detailed below. |

### Remaining dependency

At the pinned base, WPF `MainWindow.ProfileBubble.cs` calls `WireProfileSpiral`, `RefreshProfileMenuSpiral` and `UnwireProfileSpiral`. WPF `MainWindow.ProfileSpiral.cs` implements:

- Row visibility only when `App.Descent.Current` is present and `App.DescentMigration.SpiralWithheld` is false. The rail enable flag is deliberately irrelevant; the header always represents the current user.
- `BlockChanged` subscription so a block arriving or withdrawing while the menu is closed repaints the row, plus refresh on menu open and live XP ticks; symmetric cleanup.
- `SpiralGlyph.Apply(block)` and glyph motion refresh; localized `Day {0} · <stage name>` (not the obsolete Roman numeral mentioned in the XAML comment).
- Clicking the row closes the menu, then calls `ShowTab(SpiralRoom.TabKey)`. The room rechecks its gates on entry; the door itself deliberately does not reject a stale click. WPF's old standalone map and first-light plate pulse are retired.

Evidence commands (all repository-relative, pinned to `72796641344e4706b7f884abd6cbf53623d70b39`):

```sh
git grep -n -E 'WireProfileSpiral|RefreshProfileMenuSpiral|UnwireProfileSpiral' 72796641344e4706b7f884abd6cbf53623d70b39 -- ConditioningControlPanel/MainWindow/MainWindow.ProfileBubble.cs
git grep -n -E 'SpiralWithheld|BlockChanged|BuildSpiralSummary|ShowTab|ProfileMenuSpiral_Click' 72796641344e4706b7f884abd6cbf53623d70b39 -- ConditioningControlPanel/MainWindow/MainWindow.ProfileSpiral.cs
git grep -n -E 'class DescentService|class DescentMigrationService' 72796641344e4706b7f884abd6cbf53623d70b39 -- CCP.Core CCP.Avalonia
git grep -n -E 'ProfileMenuSpiral_Click|hasBlock: false|spiralWithheld: false' 72796641344e4706b7f884abd6cbf53623d70b39 -- CCP.Avalonia/Views/Windows/MainShellWindow.ProfileSpiral.cs CCP.Avalonia/Views/Tabs/SpiralTabView.axaml.cs
```

The service-definition grep has no matches (exit 1): Core contains models, parsing and countdown helpers, but no live Descent service or migration coordinator. The final grep finds the empty click handler and the room's hardcoded absent-block state. The menu row remains collapsed. A fabricated local block or always-visible navigation would bypass WPF's server/migration gate. This is a live WPF dependency, not a P122 dead-caller case; the row cannot be `wired`.

## Verification limits

SDK: .NET 10.0.402. All test commands used `DiffEngine_Disabled=true`. The original snapshot had a different HEAD and a read-only Git directory; the authorized fallback clone was pinned to the expected base before changes.

`dotnet build <project> -c Release` succeeded for each project below (exit 0, zero errors; existing warnings remain). The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.

| Project | Exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | `0 Error(s)` |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | `0 Error(s)` |

- Before the easing fixes: `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~ProfileBubbleTimingTests` exited 1: **4 failed, 12 passed**. The three ring fade samples were 0.75/0.5/0.25 instead of 0.5625/0.25/0.0625; the tier settle midpoint was -12.9 instead of -12.527208 degrees. Behavior-neutral extraction of the production animation factory/angle function preceded this red run.
- After the fixes: the same filter exited 0: **16 passed, 0 failed, 0 skipped**. Repeated against the final build with `--no-build`, also 16 passed.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` exited 0: **2811 passed, 0 failed, 5 skipped, 2816 total**. The five existing Vosk integration tests self-skip without the model; no tests or skip conditions were weakened. The supplied environment limitation says the model download returns 403.
- `bash .github/scripts/core-guards.sh` exited 0: **All Core guards passed**; checked 424 XAML references against 1591 app types / 1880 Core types. The script needs writable temporary scratch space; the read-only attempt failed before it was rerun successfully with that access.
- `git diff --check` exited 0.
- Initial multi-project builds exhausted the small cloud disk while copying duplicate runtime assets. Removing only this task's generated test outputs and cleaning each completed output allowed every required build to succeed.

The existing font-dependent `ProfileBubbleTests` / `ProfileBubbleParityTests` were compiled, not run, following the supplied cloud font limitation. The shared-clock integration test now checks the sine-settle midpoint; that assertion awaits the supervisor's GitHub run. The new timing tests ran without fonts or windows. They sample the production animation tracks; they do not prove compositor output, native popup ordering, or a visual match.

Windows/WPF code compiled but was not executed on Linux. No native desktop interaction, notifications, real user profile, browser opening, render-all, or WPF visual comparison was performed. No new surface was introduced, so no panic registry entry was added. The remaining spiral behavior cannot be exercised without its unported live services.

## Assumptions

- Exact WPF easing is part of this row's behavior, so these independent gaps are fixed even though the remaining spiral work is blocked.
- The two existing shell decorations introduce no window, popup, notification or new panic surface.
- Keep the spiral service/ceremony port in its own unit; do not fake account state or expand this branch into `shell-profile-spiral`.

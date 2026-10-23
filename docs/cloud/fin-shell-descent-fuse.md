- Finish `shell-descent-fuse`: the live App startup and sync countdown now reach both missing WPF behaviors. The row is `wired`, without a claim of visual verification.
- Initial gap inventory against WPF at `db079bf91474c77a5ea67ec306f6337a7de44b83`:
  - Neutral palette dimming: four 11% steps toward `#0A0514`, six-hour boundaries, alpha/rounding, startup catch-up, phase/tick repaint only on a changed step, active-mod derivation, unchanged accents, kill-switch restoration and migration restoration. Core already owns the through-zero hold and twelve-hour release.
  - Companion phase lines: Whisper/Clock/Dimming/Vigil/Terminal copy, mod petname substitution, retry while no tube exists, once per phase, no sound/AI badge, countdown-only Terminal silence and reschedule reset. Core already owns the policy; the head never seeded its Speaker.
- Adapt WPF `Services/Descent/DescentFuseChrome.cs` into the Avalonia color helper and apply it at the existing palette writer. No new palette cache, animation, configuration or Core dependency. Repaint on the existing phase/tick path and on migration acknowledgement (including after countdown ticks have stopped).
- Seed Speaker at countdown startup through the existing tube `GigglePriority`. Reuse its `tube-speech` panic surface; no new window or notification surface was added. Closed shells detach countdown/settings handlers and retire their spark breath.
- Add focused startup, color arithmetic, real shell palette/mod-switch, companion-copy/retry/silence/reschedule and panic tests. Keep the existing live-fuse tests intact.
- Source credit: WPF references above plus `MainWindow/MainWindow.xaml.cs` (neutral-only palette block) and `App.xaml.cs` (Speaker initialization), all pinned to the base commit. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk implementation was imported or overwritten.

## Verification limits

- Checkout verified and corrected from the initial snapshot to the exact expected base; clean before work. Local feature branch created from that commit; remote base tracking ref fetched for the line-cap comparison.
- SDK: .NET 10.0.402. Standard install succeeded after the optional CI feed returned 403 and the installer fell back to the public build feed.
- Before fix: focused startup regression failed with null Speaker. Two window regressions failed before assertions with the known cloud `Could not create glyphTypeface` / `Segoe UI Black` environment error. These are not claimed as behavior fail-proofs.
- After fix: eight font-independent checks passed (startup regression, WPF rounding/alpha/clamping, all phase-copy/silence/reschedule rules, and the existing missing-companion retry test). Window tests compile, but were not rerun after the font failure; their final runtime behavior and visual appearance require the supervisor's GitHub check. Existing window-based fuse tests were likewise not run here.
- Windows desktop behavior cannot run on this Linux worker. No live desktop, real profile, network presence measurement, or real speech/audio output was exercised.

Commands used `-c Release` and `DiffEngine_Disabled=true` for tests:

| Command | Exit/result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj` | 0; 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj` | 0; 0 errors |
| `dotnet build` each Linux test csproj in `.github/workflows/build.yml` | All 12 exited 0; 0 errors each (Core, Core.Settings, Core.Content, Avalonia, Consent, Language, CatalogueStartup, Migration, Scheduler, Report, GeneralSettings, Snapshot) |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 554 existing warnings, 0 errors; compiled WPF app and tests, not run |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --no-build` | 0; 3553 passed, 0 failed, 5 existing Vosk tests skipped because the model is unavailable (known download restriction); no tests changed or disabled |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --filter '<focused filter below>'` | 0; 8 passed, 0 failed, 0 skipped; final surface tests compiled |
| `bash .github/scripts/core-guards.sh` | 0; all Core guards passed (429 XAML references checked) |
| `git diff --check` | 0; no whitespace errors |

Focused filter: `FullyQualifiedName~DescentFuseFinishTests.StartupSeeds|FullyQualifiedName~DescentFuseFinishTests.DimmingMatchesWpf|FullyQualifiedName~DescentFuseFinishTests.ReusedCorePolicy|FullyQualifiedName~DescentFuseShellTests.CompanionLine`.

The initial full `DescentFuseFinishTests` run exited 1 (null Speaker plus two environment font errors). The final window cases and other Avalonia suites were compiled only; full Core and the focused font-independent tests were run. Render-all/live visual validation was not attempted after the font failure.

## Assumptions

- This unit covers the cited shell header/chrome and companion phase lines. Catch-up/ignition show openers and post-zero retry remain the separately tracked `win-descent-fuse` unit; its row and code are unchanged.
- Observe the existing `DescentMigrationCompleted` setting to restore the palette on this head. This supplies WPF's restore behavior for the live sync acknowledgement without importing the separate WPF migration service.
- Use the existing open-tube lookup for speech; an absent tube returns false so Core retries. Keep WPF's scripted, silent priority delivery and existing Core timing/copy policy.

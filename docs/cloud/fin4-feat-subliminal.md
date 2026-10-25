- Added a passive shared stage with per-screen owner leases for solid subliminal cards, finite 50 ms fades, classic fallback on native refusal/focus/video, and cleanup through the existing engine/panic surface. Other overlay families still need migration to share that stage.
- Wired video Freeze before pre-roll (800 ms), attention/bubble-count suppression, deferred audible Reset at teardown (90% roll, 1–2 s), consume-once behavior and Stop cancellation. Shared orchestration stays in Core.
- Ported panel/editor silent-voice hints and Fluent mute marks using the playback resolver, refreshed on mod changes; restored the bundled Fredoka choice.
- Connected actual whisper playback duration to the bark voice floor (+250 ms, extend-only), preserving WPF safety/guaranteed/text-only attribution exceptions.
- Replaced the contradictory historical ledger row with current evidence and an honest Missing list; added numbered tester steps. The row remains **stub**, not verified.
- No trunk code imported. Inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation follows the pinned base's WPF reference and reuses existing Core/head code.

## Gap inventory and assumptions

The required base is `700e3f57ed097afe0e52f53158e095c80f0ac741`. Its existing focus support superseded the old ledger claim that focus never activates. The remaining named gaps were solid-mode host ownership and video-deferred Reset. Inspection additionally found missing silent-voice warnings, bundled Fredoka selection, and whisper/bark coordination: contrary to the old row, BarkHead is now seeded. These are addressed here.

Remaining: cross-family compositor ownership/z-order. Chaos, image flashes and tint still own independent surfaces. Completing that requires migrating those consumers and their rendering/native-order contracts, rather than calling a subliminal-only stage a fully unified compositor. The WPF host and compositor engine alone span 850 lines, before the three existing head consumers (890 lines) and their tests. This broader port is left explicit under Missing rather than silently claiming wired. Closed font-picker preview remains the documented head divergence; per-row preview and card font work.

Assumptions: keep the required commit as the base even though the remote branch name resolves to an older ancestor; keep existing closed-picker styling; use per-screen stages for correct per-monitor DPI; preserve the classic path while video is live. No new infinite animation, transform animation, effect, or mutable bitmap transfer is introduced.

## Verification limits

- .NET 10.0.100 installed successfully after channel discovery returned HTTP 403.
- Both app projects needed for Linux (CCP.Core and CCP.Avalonia), all test projects named by build.yml, and the WPF test project with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false were compiled. A catalogue test build hit MSB4166 once and passed a serial retry. An overlapping head build hit an output-file lock and was rerun serially.
- Core full suite: 4,120 passed, 5 existing skips, zero failures (exit 0); final serial confirmation recorded below. Existing Vosk/performance skips remain; tests were not weakened.
- Required focused Avalonia classes: 27 passed, zero failures/skips (exit 0), with DiffEngine_Disabled=true. A broader grouping also including CompanionBarkHeadTests produced 28 passes and 3 font-creation failures (Segoe UI Black/Arial); no tests were weakened. The required grouping bootstraps the shipped font configuration and passes.
- Negative controls: replacing deferred Reset with immediate Reset, removing silent marks, and closing the host at the first lease release produced 3/3 assertion failures (exit 1); all mutations restored.
- Core guards passed. test-plan-coverage.sh is absent on this base (attempt exit 127); numbered WPF/Rows steps were added manually.
- Windows WPF tests compiled but were not run. Native focus, click-through, z-order, real audio, real video and physical multi-monitor behavior were not exercised. Render-all was not run.
- The fetched base branch points to `cc00d8e3922e85ca61b8f65b2c853cbff166affb`, an older ancestor of the required base. Therefore the literal remote-ref line-cap command includes inherited stack changes and exceeds 1000 before this unit. The unit-only count against the mandated commit is recorded below; the older branch was not changed.

## Commands and result tails

- `dotnet build <project> -c Release -m:1`: final per-project results below. WPF adds `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests -c Release`: `Passed! Failed: 0, Passed: 4120, Skipped: 5` (exit 0).
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~Subliminal|FullyQualifiedName~TextOverlayTests|FullyQualifiedName~PanicSurfacesTests'`: `Passed! Failed: 0, Passed: 27, Skipped: 0` (exit 0; final invocation reused the compiled output with `--no-build`).
- `bash .github/scripts/core-guards.sh`: `All Core guards passed.` (exit 0).
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (exit 127).
- Negative-control run: `Failed: 3, Passed: 0` (expected exit 1), proving the regressions are detected.
- `git diff --check`: clean (exit 0).

No Windows runtime or real-desktop claims are made. Full row completion remains blocked by the cross-family compositor migration; the literal remote-ref line cap is independently blocked by the older remote base ref.

Final build matrix (all use Release, serial MSBuild):

| Project | Exit |
| --- | --- |
| `CCP.Core` | 0 |
| `CCP.Avalonia` | 0 |
| `CCP.Avalonia.CatalogueStartup.Tests` | 0 |
| `CCP.Avalonia.Consent.Tests` | 0 |
| `CCP.Avalonia.GeneralSettings.Tests` | 0 |
| `CCP.Avalonia.Language.Tests` | 0 |
| `CCP.Avalonia.Migration.Tests` | 0 |
| `CCP.Avalonia.Report.Tests` | 0 |
| `CCP.Avalonia.Scheduler.Tests` | 0 |
| `CCP.Avalonia.Snapshot.Tests` | 0 |
| `CCP.Avalonia.Tests` | 0 |
| `CCP.Core.Content.Tests` | 0 |
| `CCP.Core.Settings.Tests` | 0 |
| `CCP.Core.Tests` | 0 |

Final WPF targeting build: exit 0, 0 errors (compiled, not run). Final Core confirmation: 4,120 passed, 5 existing skips, 0 failures (exit 0).

Added lines against required commit: 549; against fetched older remote ref: 36,679 (36,130 inherited additions, 549 from this unit).

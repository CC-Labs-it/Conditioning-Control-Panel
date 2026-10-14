- Wire the existing pack-card template's pointer enter/leave events to the existing CardSheenAdorner, following WPF MainWindow.AssetsFx.OnPackCardHover: one card at a time, radius 8, transitions gate and stale-leave protection.
- Stop and remove the sheen on pointer leave, effective ancestor hide, card removal and tab unload. Keep the packs strip collapsed as in WPF.
- Add three real-shell AssetsTabTests for hover switching/leaving, ancestor/tab hiding, MotionLevel.Off and rebuilding cards. Add a small production-source-linked headless test host for repeatable focused proof; it isolates only AmbientFxCanvas environment inputs and checks its outer card markup matches production.
- Keep only the shell-assets-fx ledger row stub. Follow-up `wire-shell-assets-fx-b`: replace the seeded cards with ContentPackService-backed cards/previews/actions, complete the full-shell checks and capture live hover evidence. The placeholder catalogue is outside this coherent FX slice.
- No imports. The first read-only parity/trunk fetch failed during GitHub authentication outage; the later fetch succeeded at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038. Existing WPF and head helpers supplied this adaptation.

## Verification limits

Environment: Linux, SDK initially absent. SDK 10.0.402 was installed, then 10.0.111 for a bounded comparison. Managed compilation and VSTest repeatedly stalled. Final commands use SDK 10.0.111, DOTNET_PROCESSOR_COUNT=2, DOTNET_TieredCompilation=1, DOTNET_gcServer=0, -m:1 and -p:UseSharedCompilation=false. GC heap cap is 1 GB for the focused source host and 2 GB for the full head. DiffEngine_Disabled=true; runtime profile is /workspace/worker1-fx-profile and the Vosk model is /workspace/shared/vosk/vosk-model-small-en-us-0.15.

- `bash .github/scripts/core-guards.sh` passed: 424 XAML references, 1591 app types, 1880 Core types; no forbidden Core API regression.
- Initial `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` restored Core, Avalonia and the test project but did not finish compilation. Pre-fix full-shell PackCard test attempts also failed to reach execution. Those attempts prove no assertion outcome.
- Required `dotnet build` attempts for Core, Avalonia, CatalogueStartup, Consent, GeneralSettings, Language and Report were bounded at 90 seconds; Core required timeout escalation (exit -9), the others exited 124. Scheduler, Snapshot, Core.Content, Core.Settings, Core.Tests and ConditioningControlPanel.Tests were subsequently attempted once with 60-second bounds and exited 124. The main Avalonia test project was attempted separately as above. WPF compile flags were EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false. No required whole-project build pass is claimed.
- An additional standalone Core warm build (300 seconds) and final Avalonia head build with a 2 GB heap cap (300 seconds) exited 124. No compiler errors or successful compilation were emitted in those attempts.
- Focused proof: the linked production controller/adorner/visibility/frame clock compiled in an isolated headless host. Its original BASE outer card template lacks pointer handlers. The direct xUnit v3 run `dotnet Proof.dll -method PackFxProof.PointerMovesOneSheenAndLateLeaveDoesNotStopTheNewCard` failed on Assert.Single (empty adorner collection): total 1, failed 1, errors 0, skipped 0, 6.327 seconds. Earlier fixture-font failures and VSTest timeout are not counted as regression proof. The retained branch host uses the same production sources plus a fixture-consistency assertion.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false` and the corresponding Avalonia test command with `--filter FullyQualifiedName~AssetsTabTests` each exited 124 after 60 seconds without reaching test execution. The Core suite and the three new real-shell tests remain unrun; no test was removed or weakened.
- `dotnet run --project CCP.Avalonia -c Release --no-restore -p:UseSharedCompilation=false -- --render-all /workspace/worker1-render` exited 124 after 60 seconds. No PNG or live visual proof was produced. The standard render keeps the pack strip hidden, as WPF does.
- No Windows execution is possible on Linux. No new panic surface is introduced: this change only decorates an existing card and owns no window/session/output.

Focused retained host commands and results:

```sh
dotnet build Tests/CCP.Avalonia.PackCardFx.Tests/CCP.Avalonia.PackCardFx.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
# Build succeeded; 0 warnings, 0 errors; 00:03:50.84.
dotnet Tests/CCP.Avalonia.PackCardFx.Tests/bin/Release/net10.0/CCP.Avalonia.PackCardFx.Tests.dll
# First run: total 5, failed 1 (missing fixture output file); all 4 lifecycle tests passed.
```

Avalonia removes its XAML resource from default None items, so the fixture-consistency assertion exposed a packaging error. Changed the project to an explicit Content item. That incremental build reached compilation but exited 124 at its 180-second bound before output tasks finished; no successful complete build after this metadata correction is claimed. To execute the unchanged assertion against the already-built assembly, copied the exact fixture markup into this host's own output, then reran:

```sh
cp Tests/CCP.Avalonia.PackCardFx.Tests/AssetsTabView.axaml Tests/CCP.Avalonia.PackCardFx.Tests/bin/Release/net10.0/AssetsTabView.axaml
dotnet Tests/CCP.Avalonia.PackCardFx.Tests/bin/Release/net10.0/CCP.Avalonia.PackCardFx.Tests.dll
# Total 5, Errors 0, Failed 0, Skipped 0, Not Run 0; 47.798 seconds; exit 0.
```

These five results cover production template consistency plus pointer switching/stale leaves, effective ancestor hide/motion refusal, card removal, and tab unload with the real shared clock. They are focused source-host evidence, not execution of the full application or the three new full-shell tests. All original tests remain intact. No PR or merge was created.

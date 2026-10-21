- Restores WPF `MainWindow.SubjectsFx` roster entrance (10 DIP rise, 220 ms fade / 260 ms rise, 40 ms stagger capped at slot 6) through the real Available Subjects view lifecycle. A Normal-priority realization/retry catches first binding; collection refreshes do not replay it.
- Animates item containers so a Taken card retains its bound 0.6 opacity. A finite interaction clock stops on completion, hide and unload.
- Restores Connect left-press / release / leave / capture-loss squish (0.97 scale, 80 ms QuadraticEaseOut), including Reduced motion; Off snaps. Hidden/detached controls reset to neutral.
- Adds focused headless `SubjectsFxTests` for stagger timing/cap, dimmed card binding, no replay on rebind, press/release/leave, Off and lifecycle cleanup. Tests use the existing sandbox profile and a stepped clock.
- The existing sample roster and service-action stubs are unchanged; those belong to the separate Available Subjects view/service units. This brief completes the shell FX row only.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk source imported. Existing files win. No new domain logic or panic surface was introduced.

## Verification limits

- All 14 required project builds passed with zero errors: `CCP.Core`, `CCP.Avalonia`, and every test csproj named in `.github/workflows/build.yml`:
  `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`,
  `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`,
  `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`,
  `CCP.Avalonia.Snapshot.Tests`, and `ConditioningControlPanel.Tests`.
  Each ran `dotnet build <project> -c Release -p:OutDir=/workspace/scratch/worker4-matrix-bin/ -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true`.
  The WPF test project additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; its tail was `512 Warning(s), 0 Error(s)`.
- `bash .github/scripts/core-guards.sh`: `All Core guards passed.`
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore`: **2816 passed, 0 failed, 0 skipped**, 1 m 15 s.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --no-restore --filter FullyQualifiedName~SubjectsFxTests`: **3 passed, 0 failed, 0 skipped**, 16 s.
  The original two motion regressions were first run against the unimplemented base and failed on absent press scale and entrance opacity (2 failed, 0 passed), before the implementation was installed. The Social-door test additionally caught the hidden page's unrealized containers; realizing from the window root fixed it.
- Final runtime checks used each project's normal output under the repository. A separate shared OutDir was used for matrix builds to fit disk; it mixes WPF/head native package versions and cannot serve as the runtime folder. `CCP.Core.Tests` was rebuilt in its normal output, and final head/Core project assemblies were copied into the existing isolated Avalonia outputs after matrix completion. No test source or assertion was weakened.
- `CCP_USERDATA_DIR=/workspace/scratch/worker4-render-profile XDG_CONFIG_HOME=/workspace/scratch/worker4-render-xdg dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/scratch/worker4-render`: **224/224 views rendered**, exit 0.
  `/workspace/scratch/worker4-render/AvailableSubjectsTabView.png` was visually inspected: it captures the staggered arrival and preserves the Taken card's dimming. The image is a workspace artifact, not committed.
- WPF application/test assemblies were cross-compiled, **not executed** on this Linux host. Other workflow test assemblies were compiled only; the full Avalonia suite was not run. No live directory/claim service, real desktop, Windows input, or device was exercised. New headless tests use the existing sandbox profile and production font setup.

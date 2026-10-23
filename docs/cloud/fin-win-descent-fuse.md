# fin-win-descent-fuse

- Finish `win-descent-fuse` using the task's P122 rule for behavior dead in WPF.
  The base already implements every live behavior of this row. No application,
  Core, reference, test, or panic-registration code changes are needed.
- Replace the stale room-audio follow-up in the single ledger row with pinned
  caller proof and the verification limits below; status is `wired`, never `verified`.
- Import provenance: fetched `parity/trunk` read-only at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; imported no code or behavior.
  All conclusions below use the requested base
  `a879e6f806efd9b2f08eea4299eeffa18f2dc23b`.

## Reference audit

Read both `ConditioningControlPanel/Windows/DescentFuseWindow.xaml` and its
code-behind, then followed `Services/Descent/DescentShowDirector.cs`,
`DescentMigrationService.cs`, `DescentRoomSfx.cs`, and `Services/MotionFx.cs`.
The markup's room, vignette, text, order, dimensions and colors are already
adapted in the Avalonia counterpart; the stage painter has its own ledger row
(`ctrl-descent-fuse-stage-visual`).

Every remaining behavioral difference in this window is behind the retired opener:

| WPF behavior absent or different in the Avalonia show seam | Reachability at the pinned base |
| --- | --- |
| One crack sting on the first Crack frame; reduced motion stays silent. Audio switch and master-volume gate; ordered `descent_crack`, `descent_zero_flash`, `freeze_trigger` fallback; volume scaled by 0.55 and clamped; missing assets/errors stay silent. | Sole `PlayCrack()` caller is inside `FrameCrack`; no live show can reach it. Fog-entry/splash cues belong to the spiral room, not this window. |
| Live constructor holds migration offers, releases at bloom and on every close; ceremony-open polling, nonactivating Topmost reassertion and director focus handoff. | Constructor is unreachable; `IsCeremonyOpen` is constant false. Avalonia retains local hold bookkeeping and timeout/resync rendering, without a ceremony service. |
| Effective reduced motion also caps Full for Windows animation effects off and GentleVisuals. Avalonia takes the raw motion setting once. | Constructor is unreachable; retained only for render/test shows. |
| WPF line/window/catch-up fades are linear; backdrop rise alone uses quadratic ease-out. Avalonia uses quadratic ease-out for all fades. WPF explicitly clears opacity animations and detaches its timer handler on close. | Only dormant show lifecycle/frames; production never starts that clock or fade. |
| WPF checks dispatcher availability/shutdown before opening or framing; uses an IsLoaded owner and explicit render-priority timer teardown. Avalonia uses a visible owner (required by Show(parent)), its timer API, and catches frame/open failures. | Only the opener body after the retirement return and dormant frames; no live entry bypasses retirement. |
| WPF Ideal text formatting and HWND constant-alpha fade/nonactivating Z-order semantics. | WPF-specific rendering APIs behind the same retirement return; no live surface requires a new platform seam. |

Existing live behavior is preserved: all show kinds refuse to open; a live zero,
startup catch-up and owed late catch-up mark nothing and start no show-close retry.
An account already answered is guarded. Late zero with no crack owed still retries
through the existing SyncPush once per minute for at most 15 attempts, stopping
when disarmed or answered. Panic already registers `descent-fuse` and closes any
render/test show. No new surface is introduced.

## P122 caller proof

Run these source searches from the repository at the pinned base:

```sh
git grep -n -E 'new DescentFuseWindow|DescentFuseWindow\.Open\(|ShowsRetired|DescentRoomSfx\.PlayCrack\(' a879e6f806efd9b2f08eea4299eeffa18f2dc23b -- ConditioningControlPanel
git grep -n 'IsCeremonyOpen => false' a879e6f806efd9b2f08eea4299eeffa18f2dc23b -- ConditioningControlPanel/Services/Descent/DescentMigrationService.cs
```

Executable matches (comments omitted, paths relative to `ConditioningControlPanel/`):

```text
Services/Descent/DescentShowDirector.cs:155: DescentFuseWindow.Open(DescentShowKind.Live)
Services/Descent/DescentShowDirector.cs:231: DescentFuseWindow.Open(DescentShowKind.CatchUp)
Windows/DescentFuseWindow.xaml.cs:173: internal const bool ShowsRetired = true;
Windows/DescentFuseWindow.xaml.cs:177: if (ShowsRetired)
Windows/DescentFuseWindow.xaml.cs:196: var window = new DescentFuseWindow(kind);
Windows/DescentFuseWindow.xaml.cs:318: DescentRoomSfx.PlayCrack();
Services/Descent/DescentMigrationService.cs:66: public bool IsCeremonyOpen => false;
```

The constructor is private (`:105`); its sole construction site follows the
unconditional retirement return (`:180`). No ignition opening call exists in WPF.
Avalonia has the same retirement return. Its `OpenShow` bypass and internal render
constructor are test/render seams, with no live caller bypassing `Open`.
Thus room audio is dead reference behavior, rather than a dependency blocking
this row. P122 permits `wired` with this pinned proof and render-all.

## Verification limits

- SDK: .NET `10.0.402`. All commands use Release configuration;
  `DiffEngine_Disabled=true` was exported for test/render commands.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- Built every Linux test csproj named in `.github/workflows/build.yml`: all 12
  exited 0 with 0 errors. Core: Tests, Settings, Content. Avalonia: Tests,
  CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report,
  Scheduler, Snapshot. Existing compiler/analyzer warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML
  references, 1632 app types, 2071 Core types). The initial read-only sandbox
  attempt could not create temporary files; reran successfully with write access.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --no-restore`:
  exit 0; **3541 passed, 0 failed, 5 skipped**, total 3546. Existing Vosk tests
  self-skip without the model; its download is blocked in this environment.
  No tests were changed, disabled or filtered out of this run.
- `dotnet CCP.Avalonia/bin/Release/net10.0/CCP.Avalonia.dll --render-all <output-dir>`:
  exit 0, **252/252 views rendered** with an isolated `CCP_USERDATA_DIR`.
  Inspected `DescentFuseWindow.png` (1280 x 720): room, sparks, standing line;
  no binding errors reported for that view. This is P122 rendering proof, not
  desktop/Windows verification. Generated images/logs stay outside the branch.
- Disk exhaustion interrupted initial test builds and the first render process.
  Removed only generated test outputs, rebuilt sequentially, and reran rendering;
  the results above are the successful complete runs.
- Avalonia tests were **compiled, not run**, following the task's measured cloud
  font limitation; the cloud-test-fonts test bootstrap is absent on this base.
  The existing behavior coverage is `DescentFuseDirectorTests`,
  `DescentFuseShellTests.ZeroOpensNoLiveShow_RetiredLikeWpf`,
  `AccountSeedTests.FuseBloomAndPostShowRetryUseTheExistingSync`, and
  `CoreStandInTests.DescentFuseSurfacesShowCoreDescentFuseCopy`. Supervisor/rcheck
  should run the following on the font-capable runner:

```sh
export DiffEngine_Disabled=true
dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~DescentFuseDirectorTests|FullyQualifiedName~DescentFuseShellTests.ZeroOpensNoLiveShow_RetiredLikeWpf|FullyQualifiedName~AccountSeedTests.FuseBloomAndPostShowRetryUseTheExistingSync|FullyQualifiedName~CoreStandInTests.DescentFuseSurfacesShowCoreDescentFuseCopy'
```

- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`:
  exit 0, 0 errors, 554 existing warnings. The WPF application and tests compiled
  on Linux; no WPF tests or Windows runtime code were executed.
- No new runtime behavior was added, so no new behavioral test or panic surface
  is required. Existing coverage and registration are retained unchanged.

## Assumptions

- Apply P122 to the retired show's differences and preserve the existing
  preview choreography.
- Keep spiral-room audio, countdown heartbeat and stage-painter work in their
  existing feature/ledger scopes; this unit concerns the fuse window.

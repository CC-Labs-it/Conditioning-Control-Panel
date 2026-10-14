# wire-shell-app-startup

- Seed the same two shared companion mod hooks that WPF seeds in `App.xaml.cs:317/327`: lazily notify the current `CompanionBrain` on a mod switch and invalidate `BambiSprite`’s stable prompt on pack content changes. Startup constructs the brain after mod initialization, so the callback reads the current brain when invoked.
- Add sandboxed `StartModsTests.ModSwitchPurgesOldBarkEchoesFromTheBrainCreatedAfterStartup` cases using the real mod service, brain/session and prompt builder. They check preservation of user turns, removal of previous-persona bark echoes, the new persona boundary, and eviction of a cached prompt after same-id text replacement. Restore both hooks in `CoreModsSnapshot` to avoid static test leakage.
- Keep `shell-app-startup` **stub**. Follow-up lane `wire-shell-app-startup-b` covers global chat/camera/quick-recal hotkeys; remaining mod hooks for DTRH/Arcademy host close, resource-path cache, portrait availability, voice-line index and event accent; and the remaining WPF service startup paths (including speech/wake-word startup, EMI Desk, Arcademy hosting, haptics playback-time coupling and descent speech). These broader head services exceed this coherent startup hook slice.
- No code imported from `parity/trunk`; the read-only fetch failed while GitHub authentication was unavailable. Existing Core logic and the frozen WPF startup are the specification. No WPF or Core production files changed.

## Verification limits

- `bash .github/scripts/core-guards.sh`: **passed**, with all tracked WPF/Core/head source and XAML materialized in the sparse checkout. Tail: `checked 424 XAML type references against 1591 app types / 1880 Core types`; `All Core guards passed.`
- The environment initially had no SDK. SDK 10.0.402 compilation stalled; the first ordinary SDK 10.0.111 baseline regression was bounded at 600 seconds and timed out before tests executed. A subsequent 1 GB heap attempt emitted CCP.Core but timed out before Head/tests. These cancellations/timeouts are not passing builds or regression proof.
- Final commands use SDK 10.0.111, `DOTNET_PROCESSOR_COUNT=2`, `DOTNET_TieredCompilation=1`, `DOTNET_GCHeapHardLimit=0x80000000`, `DiffEngine_Disabled=true`, `CCP_VOSK_MODEL=/workspace/shared/vosk/vosk-model-small-en-us-0.15`, and `-m:1 -p:UseSharedCompilation=false`, preserving analyzers and test gates. Later short attempts and the corrected harness also use `DOTNET_gcServer=0` (workstation GC).
- Method regression proof uses a temporary project outside the branch: `dotnet build /workspace/worker6-startup-proof/Proof.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false`, then `dotnet /workspace/worker6-startup-proof/bin/Release/net10.0/CCP.Avalonia.Tests.dll -noLogo`. `StartMods` was extracted verbatim from BASE and from final App source, and the corrected added theory body plus `CoreModsSnapshot` were copied verbatim. It references this worker’s emitted Core DLL and the same Core package versions. Scaffolding supplies App’s Mods/Brain fields, sandbox-profile lookup and a cleanup-only ResetReleaseContent; the complete Avalonia App is not compiled by this harness.
- Corrected controlled-persona BASE run: build succeeded with 0 warnings/0 errors in 17.66 s; direct runner **2 failed, 0 errors, 0 skipped, 0 not run**, 3.695 s. Actual old bark echo remained; cached prompt did not contain `fresh pack persona`. Corrected final method: build succeeded with 0 warnings/0 errors in 43.31 s; direct runner **2 passed, 0 failed, 0 errors, 0 skipped, 0 not run**, 14.321 s. Both runs use exactly the final theory fixture: a custom `startup pack persona` text replaced without changing the mod id or fingerprint inputs. The earlier Bambi-string fixture also failed after wiring because media titles are intentionally excluded from rewriting; it was corrected and is not counted as cache regression evidence.
- A full Head baseline with the two hooks removed timed out at 600 s under the 2 GB cap before any test execution (Core emitted). Full Head baseline red proof therefore comes from the method harness only. The final required matrix results follow; an emitted dependency DLL is not a successful project build.

All matrix commands below use `-c Release -m:1 -p:UseSharedCompilation=false`. The WPF build additionally uses `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; the final Avalonia test uses `--filter FullyQualifiedName~StartModsTests`. Projects were enumerated from `.github/workflows/build.yml` (including the consent assembly). Timeouts are exit 124 and do not imply a test failure.

| Operation / project | Bound | Observed result |
| --- | --- | --- |
| `build CCP.Core/CCP.Core.csproj` | 120 s | Timed out; restore output only, no requested project/test completion. |
| `build CCP.Avalonia/CCP.Avalonia.csproj` | 300 s | Timed out; Core DLL emitted, no requested project/test completion. |
| `build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 120 s | Timed out; Core DLL emitted, no requested project/test completion. |
| `build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 120 s | Timed out; Core DLL emitted, no requested project/test completion. |
| `build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 12 s | Stopped own active attempt after 12 seconds to bound remaining matrix at 60 s; no build/test result. |
| `build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 60 s | Timed out; Core DLL emitted, no requested project/test completion. |
| `build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 60 s | Timed out; restore output only, no requested project/test completion. |
| `build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 60 s | Timed out; restore output only, no requested project/test completion. |
| `build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 60 s | Started with 60 s bound; worker interruption left no captured exit or success. Core DLL emitted; no test-project build completion observed. |
| `build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 60 s | Timed out; Core DLL emitted, no requested project/test completion. |
| `build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 60 s | Build succeeded; 10 warnings, 0 errors; 00:00:41.38. |
| `build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 60 s | Build succeeded; 0 warnings, 0 errors; 00:00:45.12. |
| `build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 60 s | Timed out; Core DLL emitted, no requested project/test completion. |
| `build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 60 s | Timed out; restore output only, no requested project/test completion. |
| `test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 60 s | Timed out; Core DLL emitted, no requested project/test completion. |
| `test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 60 s | Timed out; Core DLL emitted, no requested project/test completion. |

- Windows desktop execution is unavailable on this Linux worker; the WPF test compile attempt is recorded above. No render-all run: the change seeds existing nonvisual startup hooks and adds no visual surface.

No full Core or full/filtered Avalonia test results were observed. Core compiled as a dependency of the two successful Core test-project builds, but the explicit Core build attempt timed out. The complete Head and WPF test builds did not finish here; this slice is compiled and exercised at method level only.

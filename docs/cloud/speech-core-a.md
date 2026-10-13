# refactor(speech): Core move, unit A - model folder + mic resolution

Unit A of moving `ConditioningControlPanel/Services/Speech` into `CCP.Core/Services/Speech`.

## Inventory (Services/Speech on this base)

| WPF file | State in CCP.Core before this unit | This unit |
|---|---|---|
| `SpeechService.cs` | Engine already in Core as `SpeechEngine` (Vosk, grammar, scoring, `ResolveModelDir`); WPF keeps only the NAudio `IMicSource` | Model root routed to Core; device resolution routed to Core |
| `SpeechModelFolder.cs` | Not in Core; Avalonia re-implemented the "create it first" rule inline | **Moved** (`git mv`) |
| `SileroVadGate.cs` | Not in Core | Next unit (needs sherpa-onnx in Core) |
| `SherpaWakeService.cs` | Not in Core | Next unit (needs sherpa-onnx + `IMicSource` capture) |

`parity/trunk` has no newer work on these files (its Speech commits - 8d9c88403, cc2a08ca6,
64edd576c, f3776529c, d41efc49b - are already on this base), so nothing is imported from it.

## What changed

- `git mv ConditioningControlPanel/Services/Speech/SpeechModelFolder.cs -> CCP.Core/Services/Speech/`.
  `Root` is now `SpeechEngine.DefaultModelRoots[0]` (same path as before: `<install>/Resources/Models/vosk`),
  logging goes through Serilog `Log` (WPF sets `Log.Logger` at startup), and `Open()` is dropped from Core:
  opening a folder is a head concern. WPF `MainWindow.OpenSpeechModelFolder` now does
  `Ensure(root)` + `ExplorerLauncher.OpenFolder(dir)` - the exact body `Open()` had.
- Avalonia `MainShellWindow.OpenSpeechModelFolder` uses `SpeechModelFolder.Ensure` instead of its own
  inline `CreateDirectory` copy (one rule, both heads). A failed create now logs Warning (WPF level) rather than Debug.
- WPF `SpeechService.ModelRoot` routes to `SpeechModelFolder.Root` instead of repeating the path.
- New Core rule `SpeechEngine.ResolveInputDevice(devices, savedIndex, savedName)`: saved name, then saved
  index, else null (OS default). WPF `SpeechService.ResolveDeviceNumber` (also used by `SherpaWakeService`)
  and Avalonia `PulseMicSource.ResolveSource` both delegate to it - they were two copies of the same #441b rule.
  - Edge: WPF now matches against the picker's names (`EnumerateInputDevices`, which shows a blank WaveIn
    name as `Device N`), i.e. the same names the picker saves. Previously a blank-named device could never match by name.
- Tests: `git mv Tests/ConditioningControlPanel.Tests/SpeechModelFolderTests.cs -> Tests/CCP.Core.Tests/`,
  pointed at `SpeechEngine.ResolveModelDir` / `DefaultModelRoots`; three new facts for `ResolveInputDevice`
  (name beats stale index, gone name falls back to index, nothing/none-left/"System default" -> OS default).
- No embedded resources moved, so no core-guards baseline entry is needed.

## Next units

- **speech-core-b**: `SileroVadGate` to Core - add `org.k2fsa.sherpa.onnx` (managed) to CCP.Core, keep the
  win-x64 runtime package in the WPF head; model root via `AppContext.BaseDirectory` + `CorePaths.UserData` like Vosk.
- **speech-core-c**: `SherpaWakeService` to Core - replace its two `WaveInEvent` captures with `IMicSource`,
  settings via `CoreSettings`, `App.Logger` -> Serilog; the calibration sweep (pure threshold choice) gets Core tests.
- **speech-avalonia-wake**: Avalonia wiring of the sherpa wake engine over `PulseMicSource` (linux-x64 runtime package).

## Verification limits

- `dotnet test Tests/CCP.Core.Tests` (Linux, .NET 10.0.112): 1236 passed, 5 skipped (Vosk model not present),
  2 failed - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` (container runs as root, so the
  folder CAN be deleted) and `LoopbackOAuthTests.GoodState_OnEitherLoopback_...(host: "[::1]")` (no IPv6 loopback).
  Both fail identically on the base `avalonia-port/possession-core-a`; unrelated to this unit.
  `--filter Speech`: 23 passed, 5 skipped (the Vosk end-to-end tests, model absent).
- `dotnet test Tests/CCP.Avalonia.Tests --filter TakeoverTabUiTests|PulseMic|SheListening`: 5/5 passed
  (covers the models-folder button now going through `SpeechModelFolder.Ensure`).
- `bash .github/scripts/core-guards.sh`: all guards passed.
- Built on Linux: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR and every Linux test project in build.yml.
- **Compiled, not run**: the WPF head and `Tests/ConditioningControlPanel.Tests` build with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, but cannot run here.
  `SpeechService.ResolveDeviceNumber` (WaveIn) and `MainWindow.OpenSpeechModelFolder` (Explorer) are Windows code.

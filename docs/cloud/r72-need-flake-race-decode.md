# Race decode cancellation re-cut

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`. Initial snapshot was clean at `aa9918833722f057079e455167cb3d1b14462340`; restored and verified the required base, then worked in the requested filtered clone `/tmp/ccp`.
Old lane: `avalonia-port/cloud-base-r72-flake-race-decode`, `171647cf2320`; inspected only its own diff `e9eb9cd94c81..171647cf2320` (85 additions, 4 deletions).

| Behavior | Disposition and source evidence |
| --- | --- |
| Reject cancellation before the head call and before accepting its PCM | Ported verbatim. Old and new `CCP.Core/Services/Race/TrackPcm.cs:40,47`. WPF `ConditioningControlPanel/Services/Chaos/CaucusHostService.cs:900,907-908` checks before analysis and after decoding. |
| Cancel native wait and reject completion racing with cancellation, after stopping the muxer | Ported. Old `CCP.Avalonia/Views/Games/RaceTrackPlayer.cs:269,277-280`; new same path `:271,279-282`. WPF `ConditioningControlPanel/Services/Race/NAudioTrackDecode.cs:55-57` checks between blocks; host `CaucusHostService.cs:907-908` rejects cancelled completion. |
| Reject cancellation from synchronous terminal progress before reading PCM | Ported verbatim. Old and new `RaceTrackPlayer.cs:237-239`. WPF host `CaucusHostService.cs:907-908` is the post-decode contract; WPF decoder terminal progress is `NAudioTrackDecode.cs:66`. Real host progress is asynchronous; this test exercises the synchronous decoder seam. |
| Pre-cancelled native decode | Already on base `RaceTrackPlayer.cs:232,251`, new `:232,254`; old lane `:232`. Kept both base checks. WPF `NAudioTrackDecode.cs:57`, `CaucusHostService.cs:900`. |
| Playback safety lifecycle | Covered separately by open PR #3286, `avalonia-port/cloud-r72-fin4-game-race`, per its published scope: `GameWindow.Race.cs:95,155,198,210`. WPF `CaucusHostService.cs:383,435,558-592,1108-1115`. No overlapping code imported. |

Focused tests reused from the old lane:

- `Tests/CCP.Core.Tests/RaceCoreSeamsTests.cs:60`: before and after head cancellation, original token, and provider call count. Fail-proof against base production: both cases failed. The full passing suite includes both cases.
- `Tests/CCP.Avalonia.Tests/RaceTrackPlayerTests.cs:78`: pre-cancelled missing source, original token, no progress. Passed against base, proving already-present behavior.
- Same file `:92`: cancels only on the caller thread's final Report(1), then checks cancellation and original token. Fail-proof against base production: failed with "No exception was thrown"; the pre-cancel test passed. Native LibVLC loaded from local Debian packages, with CI=true so missing LibVLC cannot skip.

No WPF source, safety, Scrolller, localization keys or test isolation rules changed. Core retains no head types. The parity row at `docs/avalonia-parity.html:212` stays stub. No live parity claim.

Validation (SDK 10.0.403 in `/tmp/ccp-dotnet`, all commands from `/tmp/ccp`):

| Command | Result tail |
| --- | --- |
| `dotnet-install --channel 10.0` | Missing SDK; channel lookup tried blocked ci.dot.net. Retried released `--version 10.0.403 --azure-feed https://builds.dotnet.microsoft.com/dotnet`: installed successfully. |
| `dotnet build CCP.Core -m:1 -p:UseSharedCompilation=false -v:q` | 0 warnings, 0 errors, 1.13 s |
| `dotnet build CCP.Avalonia -m:1 -p:UseSharedCompilation=false -v:q` | 201 warnings, 0 errors, 44.69 s |
| `dotnet build Tests/CCP.Avalonia.Tests -m:1 -p:UseSharedCompilation=false -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -v:q` | 144 warnings, 0 errors, 20.93 s |
| `dotnet test Tests/CCP.Core.Tests --filter FullyQualifiedName~DecoderRejectsCancellationBeforeAndAfterTheHead -v:q` before fix | Failed 2, passed 0; baseline failure proof (`/tmp/ccp-core-red.log`). |
| `dotnet test Tests/CCP.Core.Tests --no-build -v:minimal` after Core fix | Failed 0, passed 4637, skipped 6, total 4643, 47 s (`/tmp/ccp-core-run.log`). |
| `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~DecodeRefusesCancellationAtCompletion&#124;FullyQualifiedName~DecodeRefusesPreCancelledTokenBeforeOpeningTheFile' -v:minimal` before native fix | Failed 1 (no exception), passed 1, skipped 0 (`/tmp/ccp-native-red.log`). |
| `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~RaceTrackPlayerTests&#124;FullyQualifiedName~TestIsolationTests' -v:minimal` after fix | Failed 0, passed 18, skipped 0, 3 s (`/tmp/ccp-native-final.log`). |
| `bash .github/scripts/core-guards.sh` | All Core guards passed; 429 XAML references, 1605 app types, 2269 Core types. |
| `python3 .github/scripts/avalonia_docs.py` | avalonia_docs self-check OK |
| `git diff --check` | Clean |

Both Avalonia test commands used `CI=true`, `LD_LIBRARY_PATH=/tmp/ccp-vlc/root/usr/lib/x86_64-linux-gnu`, `VLC_PLUGIN_PATH=/tmp/ccp-vlc/root/usr/lib/x86_64-linux-gnu/vlc/plugins` and `timeout 120`. Real LibVLC 3.0.23 runtime packages were extracted under /tmp; no system install or desktop run. The pipe in each filter is part of the quoted filter, not a shell pipeline. The six Core skips are the existing five Vosk/model cases and Debug BoardTiles budget case.

An initial overlapping build attempt hit a partially written Core reference assembly. Stopped it and reran the builds serially as above; no production workaround or test weakening. A build-and-test attempt produced no test summary, so the explicit full `--no-build` run above establishes the result.

Part B: none from this lane's own diff remains. Other race gaps in the existing parity row are outside this cancellation slice.

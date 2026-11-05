# Sync7 engine fixes C re-cut

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`, clean local `work` after the requested fetch/reset. Initial snapshot was `aa9918833722f057079e455167cb3d1b14462340`. Origin fetch identifies CC-Labs-it/Conditioning-Control-Panel. No WPF files changed, no new language keys.

Old lane inspected: `c2bc2f5aaa47..21d8e61f616b`, fetched from `avalonia-port/cloud-base-r72-sync7-engine-fixes-c`. Reused its ledger, watcher, pool cleanup, identity retention, Mantra gain wiring and focused tests. Preserved base completion timer cleanup and panic handling.

| Behaviour | WPF spec | Result on Core/Avalonia |
| --- | --- | --- |
| Keep refused deletes; retry every Track/Release and cleanup, rows 63/68/78 | `ConditioningControlPanel/Services/Media/MediaTempFileLedger.cs:42`, `:62`, `:78`; `Services/Flash/FlashService.cs:4404`; `Services/Video/VideoService.cs:8553`; `Services/BubbleCountService.cs:1000` | Ported: `CCP.Core/Services/Media/MediaTempFileLedger.cs:42`, `:62`, `:78`; `Services/Content/PackMediaPool.cs:35`, `:100`, `:107`; failed deletion keeps source identity at `Services/Content/ContentPackStore.cs:392`. |
| Mantra drone initial and live master gain; tones use master gain; unsubscribe on stop, win-mantra and rows 71/78 | `ConditioningControlPanel/Windows/MantraWindow.xaml.cs:362`, `:369`, `:406`, `:417`, `:440` | Ported: `CCP.Avalonia/Views/Windows/MantraWindow.axaml.cs:485`, `:501`, `:519`, `:524`, `:531`; shared watcher `CCP.Core/Services/Audio/MasterVolumeWatch.cs:34`. Player-local gain preserves the base ramp and avoids changing the application mixer. |
| Completion timer cannot end the next session | WPF `Windows/MantraWindow.xaml.cs:301` starts a delayed close | Already stricter on base: Avalonia `Views/Windows/MantraWindow.axaml.cs:534` at base, `:543` after port; regression `Tests/CCP.Avalonia.Tests/MantraWindowSessionTests.cs:279`. No duplicate timer change. |
| Overlap | No relevant WPF behaviour in fetched PR | `refs/pull/3413/head` resolves to `c48b97d394a0`, Vault pricing only. Fetched engine-fixes-b branch at `df91f8b7f526` changes session/panic handling only. Neither covers these gaps. GitHub API metadata lookup returned HTTP 403. |

Tests: `Tests/CCP.Core.Tests/MediaTempFileLedgerTests.cs:11` and `:41`; `MasterVolumeWatchTests.cs:11`; `PackMediaPoolTests.cs:141`; `Tests/CCP.Avalonia.Tests/MantraWindowSessionTests.cs:410`. The Mantra test covers initial attenuation, new tone gain, live mute, close and unsubscription. Existing tests use the production font setup from the old lane; assertions and test coverage retained.

Core fail-proofs: remove below-cap Track retry, Release retry and watcher exception isolation: 3 failures, 1 pass. Remove failed-delete source identity retention: 1 failure, 3 passes. Restore base PackMediaPool: 1 failure, 3 passes. Restored code: 11 passed, 0 failed. Logs: `/tmp/ccp-fail-ledger-watch.log`, `/tmp/ccp-fail-identity.log`, `/tmp/ccp-fail-pool.log`, `/tmp/ccp-restored-core.log`.

Validation commands and result tails are recorded below after execution.

Part B: RemoteMediaCache integration remains outside this slice: WPF `ConditioningControlPanel/Services/Fyp/Online/RemoteMediaCache.cs:183` calls `Temps.Release`; no corresponding Core/Avalonia cache host exists. Native audio output and Windows file locks were not exercised. The pool test exercises Unix permission refusal. No safety or Scrolller code changed.

Command results (SDK installed at `/tmp/ccp-dotnet`, version 10.0.403):

| Command | Result tail |
| --- | --- |
| `bash /tmp/ccp-dotnet-install.sh --channel 10.0 --quality GA --install-dir /tmp/ccp-dotnet --azure-feed https://builds.dotnet.microsoft.com/dotnet` | Installation finished successfully. Default feed discovery had returned 403 for ci.dot.net. |
| `dotnet build CCP.Core` | 50 warnings, 0 errors. |
| `dotnet build CCP.Avalonia` | Final sequential build: 0 warnings, 0 errors. Earlier concurrent build had an output-file lock and was rerun. |
| `dotnet build Tests/CCP.Avalonia.Tests` | 345 warnings, 0 errors. Subsequent `dotnet test` compiled the updated test setup. |
| `dotnet test Tests/CCP.Core.Tests` | Passed: 4639, skipped: 6, failed: 0, total: 4645. Existing skips retained. |
| `dotnet test Tests/CCP.Core.Tests --no-restore --filter 'FullyQualifiedName~MediaTempFileLedgerTests\|FullyQualifiedName~MasterVolumeWatchTests\|FullyQualifiedName~PackMediaPoolTests'` | Restored after mutations: passed 11, failed 0. |
| `dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter 'FullyQualifiedName~MantraWindowSessionTests'` | Passed 8, failed 0. Initial bare-font host failed glyphTypeface creation; production setup fixes that without weakening assertions. |
| `bash .github/scripts/core-guards.sh` | All Core guards passed. |
| `python3 .github/scripts/avalonia_docs.py` | avalonia_docs self-check OK. |

Mantra fail-proofs: independently remove drone master multiplication (expected 26, actual 41), live subscription (expected 0, actual 26), tone master multiplication (expected 0.0375, actual 0.15), and stop unsubscription (delegate remains). Each mutant compiles and the focused test fails once. Logs: `/tmp/ccp-mantra-{drone,live,tone,unhook}.log`. All source changes restored before final validation and commit.

Final restored Mantra run (`dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~MantraWindowSessionTests'`): passed 8, failed 0, skipped 0. Log: `/tmp/ccp-mantra-restored.log`. Final guards and docs self-check pass; both edited HTML tables retain one row per line. All dotnet commands above use `/tmp/ccp-dotnet/dotnet`.
Diff against base including this record: 474 added, 41 removed, 515 changed lines across 12 files.

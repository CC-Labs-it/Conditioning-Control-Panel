- Move the live Just Drop receipt drawer into CCP.Core with WPF delegation for receipt labels. The drawer keeps no order file, uses the device-token header, caches for 90 seconds per account, and stays empty without credentials or network access. Focused tests use a fake HTTP handler.
- Render the real Session Takeaway drawer: three recent shortcuts, an overflow tray containing every receipt, copy-only taste links, and the server-gated shop. Replays reuse the single Just Drop window, start in host fullscreen, minimize the visible panel, and restore only the minimize they made when closed or panicked.
- Add the persistent fullscreen strip and host-focus Escape path, rent Topmost only while active, expose Just Drop in the EMI ring behind the server flag, and record shop opens in the existing EMI usage store.
- Apply WPF's autoplay/no-throttle Chromium arguments through WebHost's per-instance WebView2 environment event and dedicated browser_data_justdrop profile (matching WPF; environments with different flags cannot share the same data folder). Other native adapters ignore those arguments.
- Keep only ledger row `shell-just-drop` at `stub`. Follow-up `wire-shell-just-drop-b`: native document-start carrier and Escape injection, native fullscreen-element interception, live WebKitGTK authentication-header proof, and receipt hover sheen/live language-change repaint. NativeWebView exposes no document-start or fullscreen-element seam; the strip alone does not prove page-owned fullscreen containment.
- Base: `avalonia-port/companion-transcript` at `1423ff908e605aace2fdf58fcad5683f1d8e4749`. Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files imported. Behavior is adapted from the WPF references on the base.

## Verification limits

- SDK observed: .NET 10.0.402 on Linux.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --no-restore`: 2836 passed, 6 skipped, 0 failed, 2842 total. Five skips require the unavailable Vosk model; the sixth is the opt-in board-tiles performance benchmark. No tests were removed or disabled.
- `bash .github/scripts/core-guards.sh`: passed both guards (424 XAML references checked).
- The first WPF test-project build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` failed during NuGet restore because the shared overlay ran out of space. Hash-identical immutable asset hardlinks recovered space; no tracked content changed from the disk mitigation.
- Windows browser flags and real fullscreen behavior cannot be run on this Linux headless host. Headless tests use the WebHost fallback, and no sandbox accesses the live shop or real desktop/profile. Live WebKitGTK header stamping remains unproven.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --no-build --filter 'FullyQualifiedName~JustDrop'`: 19 passed, 0 failed/skipped. An isolated temporary copy with only the account-cache key removed failed the unchanged device-door test (`Expected: 3`, `Actual: 2` HTTP calls). The repository implementation was not mutated for that proof.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger 'console;verbosity=normal' --filter 'FullyQualifiedName~JustDropWindowTests|FullyQualifiedName~EmiRingWiringTests|FullyQualifiedName~PanicSurfacesTests'`: 17 passed, 0 failed/skipped, `Test Run Successful`. The initial run had 8 failures because the old test bootstraps omitted the production font aliases; related fixtures now use `Program.BuildAvaloniaApp`, preserving all assertions. Clipboard assertions await the headless clipboard rather than blocking its UI thread.
- `CCP_USERDATA_DIR=/tmp/ccp-worker3-renderall-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/ccp-worker3-renderall`: exit 0, `225/225 views rendered`. `PresetsTabView.png` shows the normal empty drawer. A temporary render-only friend assembly also rendered and visually inspected `/tmp/ccp-worker3-render/JustDropHost.png` (persistent fullscreen strip + honest no-engine fallback) and `JustDropReceipts.png` (synthetic test-only receipt + gated shop); both rendered with 0 binding errors. The PNGs and harness live in `/tmp`, not in the branch; synthetic receipts are never supplied by production code.

Linux build checks used `dotnet build <project> -m:1 -p:UseSharedCompilation=false`, with `-p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true` enabled to avoid duplicating native dependencies/assets. Test-project builds used `-p:BuildProjectReferences=false` after Core and Avalonia were built separately. `DOTNET_PROCESSOR_COUNT=2` limited compiler contention. Suites other than the Core and focused Avalonia tests described above were compiled, not executed.

| Project | Result |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | exit 0; 0 errors |
| `CCP.Avalonia/CCP.Avalonia.csproj` | exit 0; 0 errors |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | exit 0; 0 errors |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | exit 0; 0 errors |

- Final production-seam check: `dotnet build CCP.Avalonia/CCP.Avalonia.csproj --no-restore -m:1 -p:UseSharedCompilation=false` and `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --no-restore -m:1 -p:BuildProjectReferences=false -p:UseSharedCompilation=false` (plus the hardlink properties above) both exited 0 with 0 errors after the dedicated WebView2 profile change.
- After the final profile seam: `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --no-build --logger 'console;verbosity=normal' --filter 'FullyQualifiedName~JustDropWindowTests|FullyQualifiedName~IconGuardTests'`: 12 passed, 0 failed/skipped; `Test Run Successful`.
- WPF retry: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (plus hardlink-copy properties). Restore succeeded, but compilation was stopped under the shared 16 GiB memory / 4 CPU limit after prolonged C# phases. This is an incomplete build, not a Windows compile pass; WPF tests were not run.

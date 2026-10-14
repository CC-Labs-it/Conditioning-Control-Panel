# wire-dialogs-username-picker-b

- Rechecked the WPF references: UsernamePickerDialog has one caller in AccountService.HandlePostAuthV2Async. That method is reached only through BtnQuickPatreonLogin_Click / BtnQuickDiscordLogin_Click; no XAML or code subscribes either handler. WPF still supplies no live UI path to port. The active name-registration prompt remains LoginDialog's username panel.
- Add a per-dialog HttpClient transport seam. The public constructor keeps the same shared client, endpoint, 500 ms debounce, parsing, status messages/colours and cancellation guards. No new rule is copied or introduced; Core and WPF are unchanged.
- Extend UsernamePickerDialogTests with real UI/HTTP lifecycle checks: rapid edits query only the latest trimmed and URL-escaped name; availability enables Confirm and closes true with the trimmed result; taken, non-success and malformed responses disable Confirm; stale responses and replies after Close cannot overwrite state. Fake HTTP handlers replace the network and every test uses the existing profile sandbox.
- Bootstrap the related headless tests with the shipped bundled fonts without a desktop/X11 backend; every existing assertion is preserved.
- Keep ONLY ledger row dialogs-username-picker **stub**, and name the blocked host work as follow-up lane **wire-dialogs-username-picker-c**. No panic surface was added: the dialog remains unhosted on both production heads.
- No trunk code imported. `origin/parity/trunk` was fetched read-only; this base wins for existing files.

## Verification limits

- `source /tmp/ccp-shared-native-env.sh`; .NET 10.0.402, sandboxed profiles and real Vosk model/rootless native libraries.
- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~UsernamePickerDialogTests`: passed 7/7, failed 0, skipped 0. No live endpoint was contacted.
- `dotnet build CCP.Core/CCP.Core.csproj`, `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`, and all 11 Linux test projects listed in `.github/workflows/build.yml`: passed, 0 errors. Commands use `-m:1 /p:UseSharedCompilation=false` and all three output/copy-local/additional-files hardlink flags to share unchanged package binaries.
- `bash .github/scripts/core-guards.sh`: passed; 424 XAML references checked.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` with the same serial/hardlink flags: passed, 509 warnings, 0 errors; elapsed 7m52s. This compiles the WPF head and tests without running Windows code.
- `CCP_REQUIRE_VOSK=1 dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --no-build` with the same flags: passed 2784, failed 0, skipped 1, total 2785; elapsed 1m50s. The skip is the pre-existing opt-in `BoardTilesTests.A_frame_fits_the_budget` performance test; no test was changed or newly skipped. Real Vosk model loaded.
- The focused username suite was rerun after the final explicit disabled-Confirm assertion: passed 7/7 again, failed 0, skipped 0; elapsed 6s. `git diff --check` passed.
- Not visual: XAML, drawing and layout are unchanged, so no render-all was run for this follow-up. Windows/WPF runtime tests were not run on Linux; real provider login and the hardcoded live name endpoint were not exercised.

## Follow-up lane: wire-dialogs-username-picker-c

Owner decision remains required: WPF must first gain a supported live caller (subscribe its existing quick-login handlers, or route HandlePostAuthV2Async from LoginDialog). Then port that exact host path, account-provider handoff and panic/close semantics to Avalonia. Inventing an Avalonia-only trigger would violate the brief.

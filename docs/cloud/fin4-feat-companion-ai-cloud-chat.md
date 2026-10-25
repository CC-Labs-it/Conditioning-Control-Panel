# fin4: companion AI cloud chat

- Finish the last ledger version's BYO key gap: the existing Engine Room one-way key input now writes a dedicated `companion_openai` secret through the head's existing store. The account `apikey` slot stays separate. Only `secret:companion_openai` is saved in companion settings; explicit empty input clears the secret and reference.
- Seed the OpenAI-compatible transport's existing key-reader delegate at desktop startup. Both diagnostics and chat use it. No Core transport duplication, new host, dialog, overlay or panic registration is needed.
- Read legacy WPF settings using SecureStringHelper's exact null-entropy, current-user DPAPI format on Windows. Preserve its non-base64 plaintext fallback; unreadable base64 blobs return no key on Linux or after cryptographic failure. Never write plaintext as a fallback.
- Use a post-initialization TextChanged handler, matching WPF PasswordChanged, so opening an empty key box cannot clear an existing secret. Add loopback behavior tests through the actual drawer covering the input, saved-settings privacy, Sync, opening with an existing key, diagnostic authorization, multi-turn authorization, explicit clear and legacy failure/fallback behavior. Consolidate the two ledger entries into one wired row and add numbered tester steps.
- Import credit: no trunk code imported. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; existing base implementations won throughout.

## WPF gap analysis

Base: `5cdc31ee4412d4ff8a47de9dbfc42cbc39440107`. The last ledger entry supersedes the earlier one. Its remaining gaps were (1) a secure-store read path for BYO keys and (2) legacy encrypted-blob compatibility. The input setter was also inert, so finishing the reader alone would leave no user path. All three are implemented together.

References: `MainWindow/MainWindow.CompanionRoom.cs:312` (one-way store and save), `Views/Controls/Companion/Runtime/EngineRoomRuntimeVm.cs:128` (Sync suppression and empty getter), `Services/Auth/SecureStringHelper.cs` (legacy decoding), and `App.xaml.cs:656` (transport seam). Cloud proxy, personality prompt assembly, quota, protocol, text hygiene and companion-link index already live in Core. Moderation session/log/counter seeding and the local setup offer already exist on the base and remain exercised by the focused suites. Local/OpenAI transports and the Ollama wizard are already ported; no dependency host remains for this row. Login navigation and Live actions UI belong to the separate drawer row.

## Assumptions

- Follow WPF's legacy plaintext read compatibility, while all new writes use the secure store. An unreadable Windows blob on Linux requires re-entry.
- Use a distinct secret slot: CoreSecrets.ApiKey stores the cloud account API key, not the BYO provider key.
- The secret reference is an Avalonia settings representation; the frozen WPF head does not resolve it. Legacy WPF-to-Avalonia reads are supported; switching a newly saved reference back to WPF requires re-entering the BYO key there.

## Verification limits

Verification results are recorded below. Real cloud accounts, an installed Ollama service, Linux Secret Service persistence and Windows DPAPI cannot be exercised by the loopback tests. No real desktop or keyring is touched; the headless profile uses the existing memory-only sandbox store. Windows code compiles on Linux but requires Windows runtime checks. The whole unrelated render-all suite is not part of this small existing-surface change.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <test-project> -c Release` for all 12 Linux test projects in `.github/workflows/build.yml`: exit 0, 0 errors (CatalogueStartup retried with `-m:1` after a transient worker failure).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 553 warnings, 0 errors. Compiled only; WPF and native Windows DPAPI round-trip tests were not run on Linux.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0; 4,272 passed, 5 existing Vosk/model-dependent skips, 0 failures. No tests weakened or disabled.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:1 --filter 'FullyQualifiedName~LocalProvidersTests|FullyQualifiedName~CompanionCloudModerationTests|FullyQualifiedName~CompanionLocalSetupOfferTests'`: exit 0; 15 passed, 0 skipped, 0 failed. The reported font limitation did not affect this suite.
- Regression proof: restored the base's inert EngineRoomVm and unseeded SecretStore temporarily, kept the new test, and ran only DrawerKeyReachesDiagnosticsAndChatAndClearStopsSending. It failed (exit 1) at the expected reference assertion: expected `secret:companion_openai`, actual empty string. Restored the implementation and reran the final focused suite successfully.
- `bash .github/scripts/core-guards.sh`: exit 0; both guards pass. `git diff --check`: exit 0. `test-plan-coverage.sh` is absent from this base (attempt exits 127); numbered WPF/Rows tester coverage added manually.
- Drawer initialization regression proof: temporarily restored the base drawer markup/code with the live setter. The real-control test failed (exit 1): expected existing key `sk-existing`, actual null immediately after drawer construction. Restored the post-initialization handler; the focused suite passes with the existing key preserved, typed key sent, and cleared key never sent.

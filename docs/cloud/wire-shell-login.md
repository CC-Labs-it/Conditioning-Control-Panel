# wire-shell-login

- Added WPF's post-login Discord achievement-sharing offer. Only the panel-owned Yes response enables sharing; No/X preserves the choice. The question starts before awaiting the background cloud profile, matching WPF.
- Games (including in-panel Descent), a visible launcher and a hidden/minimized panel defer to one keyed Inbox row. Explicit Inbox Open asks even while the game remains. The row is filed directly because this head's passive presenter does not yet sample games/launcher state.
- The existing Share Achievements checkbox now writes/saves the local choice, so users can revoke sharing as the question promises. Acceptance repaints that checkbox and the Profile sharing summary.
- Shared pure routing lives in CCP.Core as `AchievementSharePromptDecision`. WPF retains its `AchievementSharePromptRule` facade and `GameOnScreen` probe, including the ticket-test API. The existing seven WPF pure routing cases are also linked into the Linux Core suite through a test-only type alias; no original tests were removed or weakened.
- Added eight headless cases through the real successful-login continuation: Yes/No, persistent opt-in and revocation, X, slow profile load, game/Inbox routing, hidden-panel dedupe, no authenticated Discord and already-enabled skip. Tests use sandboxed profiles, memory-only secrets and fake profile responses.
- Updated only ledger row `shell-login`; it remains **stub** with the named follow-up below. No code imported from parity/trunk (read-only source fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`).

## Verification limits

Commands used SDK 10.0.402 (`PATH=/workspace/dotnet:$PATH`) with `DiffEngine_Disabled=true` for tests. Final results:

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: 48 warnings, **0 errors**.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: 172 warnings, **0 errors**.
- All eleven Linux test projects named in `.github/workflows/build.yml` built successfully: Core, Core.Settings, Core.Content, Avalonia, Avalonia.Snapshot, Avalonia.Language, Avalonia.CatalogueStartup, Avalonia.Scheduler, Avalonia.Report, Avalonia.Consent and Avalonia.GeneralSettings. Matrix builds used `-m:1 -nr:false`, hardlink copy flags and a shared per-worker `OutDir` to fit the runner; references used the already-built Core/head. Final touched test assemblies were rebuilt in their normal output paths.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: **511 warnings, 0 errors**. The WPF facade and its existing ticket regression tests compile; Windows code/tests were not run on Linux.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: **Passed: 2823, Failed: 0, Skipped: 0, Total: 2823**, 47 seconds. Includes the seven shared routing cases.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~LoginAchievementOfferTests --logger 'console;verbosity=detailed' --blame-hang-timeout 2m --blame-hang-dump-type none`: **8 passed**, 30.4 seconds. All builds finished and final project DLL/PDB files were copied independently into test bins before execution.
- Baseline proof against the original app: the first four new UI cases produced **3 failed / 1 passed** (no Yes/No offer and no deferred Inbox row; the already-enabled skip passed).
- `bash .github/scripts/core-guards.sh`: **All Core guards passed** (424 XAML references checked).
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /workspace/scratch/worker3-final-render`: **224/224 views rendered**. `LoginDialog.png` shows the entry; `MessageDialog.png` is the shared question layout's default sample. A temporary external harness invoked the existing renderer with the actual question constructor/labels and wrote `DiscordAchievementOffer.png` (460x225) in the same directory; no harness code is added to the repository.
- Real provider/browser consent and live cloud endpoint behavior were not exercised in this sandbox.
- Added lines vs `origin/avalonia-port/goongame-core-c`: 401 (cap 1000).

## Follow-up: wire-shell-login-b

The known-fields push remains restricted to unified_id, XP, level, descent_epoch and achievements. Port each additional field's server read/adopt behavior together with its push: stats, skills, consents, cosmetics and acks. This cross-feature work exceeds this unit's line cap and must not be replaced by speculative server keys. The provider-link caller should reuse the same new offer when that unit is ported. The sharing question is an ordinary owned MessageDialog, which WPF's panic does not close; it introduces no game, audio or engine surface.

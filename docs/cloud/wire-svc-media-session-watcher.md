# Media-session watcher: shared lifecycle slice

- Move WPF's 3-second now-playing poll lifecycle into Core `MediaSessionWatcher`; the existing WPF `SmtcMediaWatcher` delegates to it and retains only Windows acquisition/session I/O.
- Preserve idle construction, one acquisition shared across rapid pause/resume, one read in flight, cached-manager restart, quiet unavailable/read-failure behavior, current-sample clearing and timer disposal.
- Reject an in-flight result after Stop, Dispose or a complete Stop/Start cycle. The old WPF poll only checked disposal, so a late result could repopulate a paused watcher's sample.
- Move blank-title/artist/state normalization into the Core `MediaSample` factory; WPF calls it before querying an unusable session's playback/timeline details.
- Add controlled acquisition/read tasks and fake timers in `MediaSessionWatcherTests`; they exercise the real lifecycle without reading a desktop, media player or user profile.
- Use the production Avalonia builder in the two existing awareness regression fixtures, so the shipped bundled-font configuration is present; every assertion is retained.
- Consulted read-only parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no unused head implementation was imported.

## Follow-up: wire-svc-media-session-watcher-b

The ledger row remains **stub**. BASE has the media interface/policies, but no Avalonia observer or media-session backend. The trunk Core observer source alone is 1013 added lines before platform probes, application composition and tests, so full wiring exceeds the 1000-line cap.

Port the observer and its real consumer path, register the existing awareness consent/pause/stop/panic lifetime, and add the Linux MPRIS and Windows SMTC head readers using this shared loop. The current slice starts no new Avalonia service/surface; WPF's existing observer still owns Stop/Dispose.

## Verification limits

Run with .NET SDK 10.0.402, `DiffEngine_Disabled=true`, workstation GC, `-m:1`, `-p:UseSharedCompilation=false` and hardlink build-copy flags to fit the parallel container.

- Core guard checks passed: forbidden APIs and XAML type references.
- Initial focused `MediaSessionWatcherTests`: 15 passed, 0 failed/skipped. A further disposed-timer exception case was then added and included in the final full suite.
- Regression proof: temporarily restoring the old WPF publication check (`!disposed` only) made both pause/read-completion cases fail with an old sample. The full generation/start guard was restored.
- Final `dotnet test Tests/CCP.Core.Tests -c Release`: **2832 passed, 0 failed, 0 skipped**, including the 16 new cases. `CCP_REQUIRE_VOSK=1` and `CCP_VOSK_MODEL=/workspace/shared/worker-a-vosk/vosk-model-small-en-us-0.15` required the real speech model.

- All **13 Linux buildable projects** in `.github/workflows/build.yml` built successfully: Core, Avalonia and all 11 Linux test projects. Commands were `dotnet build <csproj> -c Release` with the flags above.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~AwarenessAiRoutingTests|FullyQualifiedName~AwarenessTubeReactionTests'`: **4 passed, 0 failed, 0 skipped**. The initial bare-app test configuration failed all four on missing glyph typefaces; switching to the production builder registered the bundled fonts and the unchanged assertions passed.
- Actual modified Windows `SmtcMediaWatcher.cs` compiled in an isolated temporary Windows-targeted project referencing Core, with the existing WPF friend-assembly name, `net10.0-windows10.0.19041.0`, Windows SDK 10.0.26100.84 and `win-x64`. `dotnet build /tmp/worker-b-smtc-compile/SmtcCompile.csproj -c Release -t:Compile -p:BuildProjectReferences=false`: **Build succeeded, 0 warnings, 0 errors**. This proves the real WinRT API/source contract; it does not execute SMTC.
- The isolated project's full packaging target failed because the Linux SDK lacks `Microsoft.Build.AppxPackage.dll`; its compile target above passed. The full WPF test-project build with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false` was attempted separately; stopped after a resource-limited three-minute probe in the generated WPF compiler (workstation GC, 1.5 GiB heap cap). Its log has no successful full-build result: **incomplete**, not a pass. No WPF tests were run.

This is a nonvisual Core/WPF lifecycle slice; render-all is not applicable. The container cannot execute Windows SMTC or WPF desktop behavior. Tests never start a real media backend.

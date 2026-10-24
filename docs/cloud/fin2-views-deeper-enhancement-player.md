- Add WPF player keyboard transport: Space, 5/30-second seek, volume steps, repeat suppression and text-input exclusion. Audio reuses the existing local transport; video seeks and volume use the existing guarded script channel.
- Add mute/restore semantics: muted zero is not persisted; manually changing volume ends mute.
- Add Escape popup/idle/session behavior, the three-second stop gesture, localized progress, strict-lock checks and cleanup on focus loss, stop, panic and close.
- Add timeline hover time and edge clamping without changing playback position.
- Add focused headless tests in the interaction partial of `DeeperPlayerAudioTests`; retain the existing `deeper-player-audio` panic surface. No new native surface is introduced.
- Keep exactly one ledger row, honestly marked `stub`; this branch is partial progress, not full parity.
- Source comparison: WPF reference at `b6c9a65df77b8d2b91f8231b43be8837a7d1ab53`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## WPF gap audit

Compared all three cited reference files (XAML, code-behind, Mission3 partial) with the Avalonia player and its Audio partial. Existing discovery, editor routing, waveform drawing, event filtering and transport code remain authoritative in this head.

| WPF behavior absent at the base | Outcome |
| --- | --- |
| Space toggle, repeated-key suppression and text-input exclusion | Implemented for keys routed through the player window. |
| Left/Right seek 5 s, Shift 30 s; media bounds | Implemented, reusing audio seek and video script paths. |
| Up/Down volume 5 and M mute/restore; no persisted muted zero | Implemented, including video volume script and manual-slider unmute. |
| Escape closes popup; idle closes; active/paused session survives tap | Implemented with the existing Core strict-lock state. |
| Hold Escape 3 s, 50 ms progress, 1.5 s lost-repeat timeout | Implemented; focus loss, panic, stop and close cancel the timer. |
| Mini-timeline hover m:ss, edge clamping and mouse-leave hide | Implemented with a sibling overlay; existing capture-loss scrub cleanup retained. |
| Decode/cache waveform peaks | Missing. WPF `AudioWaveformCache` depends on MediaFoundationReader; the head has no portable decoder/cache. Drawing alone was already ported. |
| Bind/unbind enhancement engine to audio/video clock; engine events feed log | Missing. `EnhancementHostService`, `EnhancementEngine` and playback time-source integration remain WPF-only; loading JSON is not execution. |
| Webcam-needed pre-play banner, enable/dismiss, consent, toggle, state labels, session-owned close cleanup | Missing player integration. The portable `WebcamTracker` and consent dialogs exist, so this is not a pure dependency NOOP. |
| Browser zoom, Ctrl-wheel, injected-document handlers, HTML5 fullscreen notification and keyboard relay | Missing. Requires extending the existing WebHost/native bridge; CSS zoom does not reproduce WPF browser zoom. |
| F shortcut, fullscreen host lifecycle, dual-monitor reparent/exit, fullscreen Escape routing | Missing with native fullscreen integration. No replacement shell-wide fullscreen behavior is claimed. |
| Dark title bar | Missing Windows platform integration. Native decorations retained. |
| Persist/restore player window bounds with visible-screen guard | Missing. |
| Publish measured duration once per local file; suspend idle UI timer | Missing. |
| Library resolver provider, embedded promotion/deduplication, sticky source hint and 6 s promoted toast | Missing head library lifecycle/promotion integration. Core resolver is already called. |
| Open editor at event rule id | WPF itself ignores `ruleId`. Not counted as a live gap. |

Proof for the last item at the pinned base: `git grep -n 'ruleId\|JumpToEditorForCurrentEnhancement' b6c9a65df77b8d2b91f8231b43be8837a7d1ab53 -- ConditioningControlPanel/Views/Deeper/EnhancementPlayerWindow.Mission3.cs` shows the callers and the implementation, whose comment explicitly says the rule id is unused. This is not a P122 promotion: live gaps remain and render-all was not run.

The remaining work includes live player integration as well as unported engine/native dependencies. This is neither a pure dependency NOOP nor a `wired` row. No speculative engine/decoder abstractions were introduced in this bounded interaction change.

## Verification limits

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0; final head was also rebuilt through its test project after the last source change.
- `dotnet build Tests/<project>/<project>.csproj -c Release`: exit 0 for every Linux-capable test project named in build.yml: CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests and CCP.Core.Tests. Final interaction-test compilation: 0 errors (existing warnings remain).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: final exit 0, 2 warnings, 0 errors. Windows application and tests compiled, never run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; Failed 0, Passed 2811, Skipped 5, Total 2816. Existing Vosk tests self-skip without the unavailable model; no tests were changed to skip.
- Avalonia execution intentionally not attempted under the supplied cloud font-creation limitation: the cloud-test-fonts lane is absent on this base. The relevant filter for supervisor execution is `FullyQualifiedName~DeeperPlayerAudioTests|FullyQualifiedName~DeeperRowsTests|FullyQualifiedName~PanicSurfacesTests`, with `DiffEngine_Disabled=true`. The new tests are compile-validated, not runtime-validated; no fail-before/pass-after proof is claimed for mute persistence.
- Native audio, native browser behavior, real camera, desktop/window notifications, and render-all were not run. Tests added here use fake audio and the existing headless/profile sandbox.
- Initial batch builds exhausted disk by copying runtime payloads into each test output. Deleted only generated output directories, then built serially with cleanup between projects. The WPF build also exhausted disk copying output; after freeing generated Avalonia binaries, its retry passed. Transient NuGet 503 responses resolved on retry.
- `git diff --check`: exit 0. No Core, WPF-reference, package, credential or secret files changed.

## Assumptions

- Match WPF timing, copy and order where the portable head can execute them. Keep native fullscreen and browser-focused key relay explicitly missing instead of substituting window maximize or CSS zoom.
- Track down/up pairs because Avalonia KeyEventArgs has no WPF IsRepeat. Clear the set when the window deactivates.
- A live local transport (including paused) or a navigated video counts as an active session for Escape; the head has no enhancement host running state yet.
- Use the authorized writable clone fallback after the snapshot fetch failed on read-only Git metadata. Do not modify the independent snapshot.

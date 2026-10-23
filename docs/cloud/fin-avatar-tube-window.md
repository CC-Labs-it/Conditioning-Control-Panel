# fin-avatar-tube-window

- Restore the tube's last look per mod at startup; repaint art, caption, glass and layout on a mod change. Reuse Core `ModAvatarLooks`; opening or falling back does not record a deliberate pick.
- Wire the title arrows and Companion card's Look combo to one selector. Preserve WPF 7.1.5 order (1,2,3,4,7,5,6, then custom sets by unlock level), supported-set filtering, custom labels, and single-emote-mod pinning. Deliberate picks save globally and per mod, notify the bark engine, and switch the companion only after the 200 ms fade-out. A superseded/closed swap cannot repaint or switch the companion.
- Add detached Ctrl+wheel and Up/Down zoom through the existing 25% step, 50–150% limits, persistence and screen clamp. Attach resets the displayed scale to 100%, as before.
- Use existing Core `MakeRoomPlacement`/`MakeRoomState` to move/narrow a normal shell when neither side fits. Respect its minimum width, fullscreen/maximized state, lockdown and user takeover. Restore on detach/hide/close; a later user move discards the restore.
- Add focused headless behavior tests and retain the existing float-input assertion under WPF's maximized-shell exception. Existing `tube-speech` panic registration remains the owner of this surface; no additional window or notification is introduced.
- Adapt selection behavior from read-only `parity/trunk` commit **a4ee52d40cd347d288dcf7fdaa8445b79f8e5038**, `AvatarTubeWindow.AvatarSets.cs`. Existing head files win; trunk's duplicated look-storage helpers were replaced with the Core implementation already on this base. WPF reference pinned at **affe27c56fd7d658cafb8052720bdcbec1816d8a**.
- Replace the repeated historical ledger text with one current `stub` row and an explicit remaining-work inventory. This unit does **not** claim full row parity.

## WPF comparison and remaining gaps

The cited `.xaml`, `.xaml.cs`, Avatar, ChatInput, CirceEmotes, DescentFuse, Reactions, Speech and Windowing partials are the reference. The old ledger combines several generations of notes. The following inventory replaces those notes; related implementation details are grouped under their user-visible behavior.

| Area | Missing at the pinned base | Outcome / remaining work |
| --- | --- | --- |
| Look selection (`Avatar.cs:214,395,850-995`; constructor `:138-166`) | Startup validation and per-mod look, supported/custom list, arrow navigation, picker door, custom labels, persistent selection and companion coupling; mod-change art refresh | Implemented for the existing static-avatar path, including registry/mod-local single-emote detection. **Missing:** portrait-manifest skin choices/titles/previews and emote idle-clip preview. |
| Animated avatars (`Avatar.cs:273-393`, `:1006-1468`; `CirceEmotes.cs`) | Legacy GIF playback/pause/resume/refresh; emotive portraits and skin buckets; pose crossfade/return-to-idle; line-id/mood emotes, weighted idle, clip queues/talk scheduling, nonverbal/click reactions, per-set scale/offset/audio lead-in and stall/error recovery | **Missing:** all playback/choreography above. The selection work does not turn still images into animated avatars. WPF's static pose timer also fades to 0.3 for 150 ms; this head still swaps directly. |
| Docking (`Windowing.cs:820-1200,2256-2453`) | Make-room; mirrored right-dock frame, measured alpha insets/flip axis; Ctrl+scroll and arrow zoom | Make-room and zoom implemented. **Missing:** mirrored art and measured mod-art geometry, including transparent-art scrubbing; stock insets remain. Real WM drag is implemented with `BeginMoveDrag` but still needs desktop verification. |
| Window lifecycle/movement (`Windowing.cs`, constructor) | Fullscreen detection, native ownership/z-order persistence, activation/focus recovery, hidden/minimized animation suspension, chaos-run visibility/topmost behavior, bubble/possession glides and return-home, compositor-host anchoring, Midnight cosmetic glass | **Missing:** advanced behavior above. Basic dock/follow/detach/save/clamp and shell minimize/maximize routes already exist. Own-STA mode has no Avalonia counterpart (one UI thread). |
| Ambient motion (`Windowing.cs:733-817`) | Floating bob, portrait breathing/wobble, speech vibration and drifting mist | **Missing.** Any future ambient port must use the shared clock, not a new infinite shell animation. |
| Speech presentation (`Speech.cs:421-1259,2915-3034`) | Typewriter profiles and duration compensation; static-pose/audio lead-in; link/known-video processing and chat-history links; mod-specific bubble styling/size; independent unclipped speech window placement | **Missing:** these features. Queue gaps, hover latch, policy/AI badges, thinking/listening, clip interruption and panic are already implemented. The old note that no giggle/voice plays is stale: `PlaySpeechAudio` and `PlayGiggleSound` are live. |
| Speech/audio producers (`Speech.cs:1571-2830`) | Two-second greeting with absence/streak/lapse/name handling; idle giggles/idle-voice gates; trigger-phrase timer; random floating bubbles/pop audio; category phrase audio; voice-line selection/refresh, uninterrupted note clips and audio pause/resume; alternate fallback sounds and collapse egg | **Missing:** these producers and their speech/emote coupling. Existing queued barks remain text-only where WPF's queue does too. |
| Click/chat (`ChatInput.cs:47-179,189-357,747-865`) | Click squash, four-click animation recovery, fifty-click collapse, avatar-click achievement and occasional pop sound; activity-comment/awareness-v2 route; chat bark echo and season-recap/achievement emits; robust input foreground/focus recovery | **Missing:** these behaviors. Chat send/brain/moderation, shortcut, log, ask chips and awareness legacy AI/fallback already exist. The ledger's “no AI/no double bounce” awareness note is stale. The old talking-static-pose note is also stale: the pinned WPF `PoseTimer_Tick` only rotates/fades, with no talking-pose mapping. |
| Quick menu (`ChatInput.cs:867-1566`) | Remote-controller emote swap/send and Pause browser/WebHost mute | **Missing:** both. Local engine/trigger/takeover/personality/mute/attach/detach/scale/dismiss actions and remote feedback bubble already exist. The trigger switch saves its flag but still has no trigger timer. |
| Service reactions (`Reactions.cs:287-637`) | Arbiter awareness-line door; video start/end, games, lock-card AI, flash/audio/click, subliminal, bubble pop/miss, achievements/player level/companion level/switch (including 600 ms greeting debounce), mind-wipe/brain-drain and engine-stop reactions | **Missing:** these tube subscriptions/reaction paths. Companion switching itself is now reachable through look selection; companion-event caption refresh/greetings remain absent. Existing bark engine hooks do not establish parity for every WPF tube reaction. |
| Takeover/Fuse (`Avatar.cs:38-126`; `DescentFuse.cs:38-163`) | Autonomy countdown ticker; phase-driven candle with reduced-motion static flame and flicker cleanup | **Missing:** both. Their XAML surfaces are still dormant. |

Several gaps have live services available already; this is not a pure dependency NOOP and P122 does not apply. Full animation/speech/reaction parity exceeds the 1,000-added-line unit budget. The ledger stays `stub` until those paths are implemented and reachable.

## Verification limits

Tests use the existing `CCP_USERDATA_DIR` module sandbox and memory-only SecretStore. The new fixture explicitly uses bundled fonts; older test fixtures without that setup may still encounter the known cloud font failure.

| Command / scope | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 warnings, 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors (existing warnings) |
| `dotnet build` of every Linux-compatible workflow test project | 0 for all 12; 0 errors. CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings and Core.Tests. Shared build output avoids duplicating large assets. |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 0 errors; WPF app compiled transitively. Windows code compiled but was not run. |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed (429 XAML references). |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release` | 0; 3,545 passed, 0 failed, 5 existing Vosk skips (model unavailable; download blocked in this environment). |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~AvatarTubeFinishTests\|FullyQualifiedName~AvatarTubeWindowingTests'` | 0; 14 passed, 0 failed, 0 skipped. |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ModLocalEmotePinsItsSetUnlessOtherLooksAreDeclared` | 0; 1 passed, 0 failed. Added after the preceding assembly compiled; all 15 relevant tests passed across the two final runs. |
| Regression proof on the pinned base | Exit 1: 4/4 focused tests failed on disabled picker, ignored remembered look, missing mod refresh and zoom. The make-room test separately failed before its implementation. |

The shared-output test launches initially aborted before executing tests because runtime dependencies were omitted by the common-output build optimization; final tests use ordinary project outputs. Other Avalonia classes were compiled but not run. No manual desktop/WM drag, mixed-DPI desktop, audio hardware, Windows runtime, or full render-all check was run. No WPF source was changed. Remaining parity gaps are listed above, not treated as verification success.

## Assumptions

- Keep the existing static-avatar renderer while exposing the WPF selection path; animated/portrait behavior remains explicitly missing rather than importing a second animation engine in this unit.
- Use Avalonia visual opacity transitions for the WPF 200 ms out/in fade. Companion coupling occurs at the fade-out boundary; cancellation and a generation guard preserve the final pick.
- Make-room uses the current head's physical-pixel docking geometry and shared Core planner, preserving the WPF minimum-width floor and user-takeover rule.

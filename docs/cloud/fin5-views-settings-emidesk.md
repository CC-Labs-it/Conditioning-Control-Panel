# FIN5: EMI Desk ring moments

- Required base verified: `2d75ec66fe2107434d0bdcdbde63d804f22a6212`. Branch names were not used to validate the snapshot.
- Connect the real ring to the existing Core line/offer engine: `ringOpen` before the open counter, `ringDismissed` on an unpicked fold, `suggestionIgnored3x` every third consecutive dismissal, and `ringPick` after unlocked navigation and usage scoring with `target` and `pickIsTop`.
- This makes the shipped `pinTop:{target}` asks reachable by picking the first unpinned card; it is no longer necessary to replace a pet ask's effect to exercise that path. No new window, host, clock, abstraction, or dependency. Existing offer panic cancellation is unchanged and tested.
- Add four focused regression cases. Before the fix: 4 failed / 0 passed (missing asks); after the fix: focused offer and ring wiring classes 16 passed / 0 failed.
- Keep the single parity row `stub`, correct the formerly unreachable pin tester step, and add numbered ring gesture coverage.
- Source is the frozen WPF reference at the required base, specifically `Windows/EmiDesk/EmiDeskWindow.Ring.cs` (ToggleRing/OnRingClosed) and `Services/EmiDesk/EmiTargets.cs` (Pick). Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## Gap inventory and scope

The settings control's own seven editors already save and feed the available runtime on this base. The WPF references cited by the row lead to these remaining runtime gaps:

- Glass setting: no reader in Avalonia. WPF `EmiDeskWindow.Glass.cs` supplies idle checks, bedtime/hold/ring/video gates, glitch transitions, channel selection, painter lifetime, interactions and teardown. Its six `EmiChannels*.cs` files supply the actual channel content/skins. These total over 3,400 reference lines before adaptation and tests; a complete host cannot reasonably fit this 1,000-added-line unit alongside the other gaps.
- Offer effects: `spiral`, `video`, `rain`, `tour:`, `book:`, `packs`, `assets` still refuse at draw time. Some underlying surfaces already exist, but their offer-specific gates, actions and originating moments remain unconnected. No fake chips were enabled.
- Ambient/counter bus: WPF `WireAppEvents`, `OnClockTick`, `EmiBarkBridge`, usage from non-ring navigation, locked-card reactions, pin onboarding/nudges, session/feature events, local-night/idle/long-run clock beats and release-hold hooks remain incomplete. This includes the bedtime-bearing moments, so the FIN4 bedtime implementation still has no authored user path. Ring-open asks requiring `topUnpinned` plus a target are not supplied that context by this change (nor by WPF ToggleRing's bare Fire).
- Voice/bubble/animation: WPF `EmiVox.cs` Blipese synthesis and lifetime, book-side bubble placement, `EmiChains` body-frame playback and idle beats remain absent.
- Summon/service: backSoon/weekend/bedtimeBroken, knock hand-off, bookOffer, empty-library follow-up, onboarding timers, tube suppression/handoff and live-talking-feature mute probes remain incomplete. WPF `ChooseGreetMoment`, `MaybeOfferBookOnSummon`, `StartNudges`, `WireAppEvents`, and tube partials specify these. Existing X11 chord also cannot observe input focused in native Wayland windows.

This is a partial implementation, not a pure dependency NOOP or a P122 dead-caller promotion. The ledger must remain stub. Assumption: prioritize the smallest live path that exposes already-ported offer effects, without approximating the glass/voice hosts or changing authored odds/cooldowns.

## Verification limits

- .NET SDK 10.0.100 installed using the explicit version after channel discovery returned HTTP 403.
- `dotnet build -c Release` for CCP.Core, CCP.Avalonia, and all 11 Linux test projects named in `.github/workflows/build.yml`: exit 0, zero errors. Existing warnings remain.
- Before implementation: the four new regression cases failed on missing asks, exit 1. After implementation: offer/ring wiring classes passed 16/16, exit 0.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~EmiDeskOfferTests|FullyQualifiedName~EmiRingWiringTests|FullyQualifiedName~EmiDeskSettingsSpeechTests|FullyQualifiedName~EmiDeskChordTests|FullyQualifiedName~EmiRingPickerTests'`: exit 0, 26 passed, zero failures/skips. `DiffEngine_Disabled=true`.
- WPF test project cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 existing warnings, zero errors; compiled only, not run.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`, with `DiffEngine_Disabled=true`: exit 0, 2,823 passed, zero failed, five existing Vosk tests skipped because the model is unavailable. Vosk-dependent recognition remains untested.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. `git diff --check`: exit 0.
- Windows runtime behavior cannot be exercised on this Linux worker. Other test assemblies were compiled only. No real desktop or user profile is used by the headless suites; their existing test profile bootstrap supplies a sandbox. No new surface needs panic registration; the existing offer surface's panic test passed.
- `test-plan-coverage.sh` is absent on this base; numbered WPF/Rows coverage was updated manually. No render-all run or real X11/Wayland interaction was performed.

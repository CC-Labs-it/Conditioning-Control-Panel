# Documentary museum

Full Three.js Gooning Museum, revised after the owner's November 10 feedback. Six room kits follow the script: rose/velvet Subject intake, cyan switchback, midnight reflex laboratory, pink descent, mint Wake Up conservatory and a plain service wall at the end. Layered wall panels, detailed light fixtures, upholstered alcoves, screen housings and physical exhibits replace the initial plain gallery. Thirty-eight large personal-media displays fill selected niches, with dense pairs in the opening gallery. Pull-down projection screens present the documentary inside the room. The floorboard comparison and its slow-motion replay share one physical screen.

The three sculptures and their pedestals reuse Backroom's knight.glb, queen.glb and rook.glb. The texture set is about 14.5 MB, with 2K terrazzo, velour, leather and rubber maps plus the earlier scanned surfaces. Sources and transformations are recorded in assets/documentary/textures/sources.json. Painted and brushed surfaces also use deterministic micro-normal and roughness maps. Conservatory planters use curved botanical leaves with vein maps, varied stems, textured soil and stone troughs; nearby benches have separate timber slats. Museum geometry, textures and story illustrations load from local assets; personal-media screens use the player's selected source.

## Preview

Run from the fuzz directory:

    node dev/tab-doc-preview-server.mjs --root ../ --port 8897 --feed http://127.0.0.1:8817

Open http://127.0.0.1:8897/fuzz/dev/tab-doc.html

Drag with mouse or touch to look around. Click/tap the physical lever, or press Space/Enter, to pump. The lever has a generous stable hit area; scenery clicks do not pump. Center view resets the head direction. The narrated ride unlocks the platform after 1.4 seconds of the opening recording, then moves while the narrator continues. **Ride preview > Leave platform** remains available for manual inspection. The visible **Connect Scrolller images** button, also available as **Ride preview > Use Scrolller**, connects this visit through the existing authenticated local feed host. The host still owns consent, approved channels, allowance and expiry. No feed request starts before that explicit selection. **Use own files** and **No pictures** switch sources and release the remote visit. The adapter binds only to loopback and streams media without saving it. In the game, the screens use the player's existing selected Scrolller or own-file source.

Preview doors: ?at=lever, ?at=145 (first drop), ?at=floorboard, ?at=363 (second descent), ?at=wakeup, ?at=stage. Narrated chapter doors enter at 92m, 268m and 370m; the old narrated ?at=303 and ?at=410 links normalize to the appropriate chapter start. Other numeric distances remain diagnostic positions and do not reconstruct a partial recording. Add lite=1, reduced=1, gentle=1, plan=prime or hours=3 as needed. The normal preview includes recorded narration, subtitles and music. voice=0 disables narration; music=0 disables music. manual=1 and test=1 default to silent for deterministic drives, with voice=1 and music=1 overriding those defaults. CC toggles subtitles.

Distance follows the 580m rail, including its real slopes and bends. Built-in narration sets a smooth forward pace from the playing recording's media clock. The cart approaches each spoken visual cue, slows through the reflex comparison and keeps moving between galleries. Pumping adds bounded energy without outrunning the next image. There are no stationary narration holds and movement never pauses or seeks the voice. Without the built-in narration controller, manual pumping and the existing powered connecting sections remain available. Reduced motion caps speed and disables banking, anticipation and speed FOV. Explicit external holds and Wake Up always win over automatic movement.

## Game integration

This module leaves the ending registry and shop row to their owning lane. Use these properties on the ending selected by that owner:

    import { tabDocumentary, DOCUMENTARY_SHOW_MAX_MS } from './ui/documentary/index.js';
    const ending = {
      ending: tabDocumentary,
      showMaxMs: DOCUMENTARY_SHOW_MAX_MS,
    };

The real game host was checked by temporarily replacing the outside handler inside an isolated test browser. No registry change was saved. Wake Up, Escape and panic work through the existing host. New run resolves into the ordinary recap.

## Narration and illustrations

Finished original recordings are packaged in assets/documentary/voice. Narration uses the game's voice mixer, one clip at a time, with a 1.5-second gap after each natural ending. Chapters begin at 0m, 92m, 268m and 370m. No distance gate pauses or seeks inside a recording, and the cart does not wait for a whole chapter to finish at a screen. New run queues its closing response behind the active recording; Wake Up pauses immediately and panic disposes immediately. Hover reactions are at least five minutes apart and limited to two played reactions per run.

Subtitles preserve the approved script for all 217 recordings, in short phrases with responsive line wrapping. The four ride recordings use acoustic word alignment from their delivered audio. The other 213 recordings retain estimated phrase timing. Captions, projector slides and cart approach anchors share the same resolved phrase boundaries and follow the media clock. Timing provenance is reported as mixed overall and aligned or estimated per caption. Captions use the locally packaged Fredoka font at weight 600, with larger desktop text and a 22px phone layout. Missing clips remain silent while finite visual timing continues for their measured duration; no replacement speech is generated.

Voice attacks fade over 120ms. Release fades are limited to measured quiet tails with a safety margin, so they do not lower final spoken syllables. The packaged files hash-match their delivered originals, and duration metadata agrees within 0.5ms. Many originals have no quiet tail; this does not establish whether words are missing, and playback cannot restore missing source audio. No recording was regenerated or modified in this pass.

The background playlist reuses Breathe In, Idle Cycle and Sealed from the existing licensed lo-fi runtime pack. It loops throughout the ride and the parked ending, fades at track boundaries, and ducks under speech. Only one music stream is active at once. Those MP3s remain part of the existing ignored runtime audio pack rather than a duplicate committed asset set.

Story slides use realistic documentary images, with an androgynous technical radiography of the Subject in electric blue on soft cyan for the introduction. The habitat monitors carry opaque black censor bands over their generic page content. Nine slides cover the Subject, handcar, cheetah, mantis shrimp, room, floorboard, matching spreadsheet room, daylight and conversation chairs. Projectors stand at 12m, 112m, 310m, 418m and 504m. The entire reflex comparison and "Again, in slow motion" stay on the 310m screen; there is no second replay housing. The later conversation image uses the 504m screen so the cart can continue past daylight. Use ?at=floorboard or ?at=268 to review the reflex chapter from its beginning. The first three parchment exhibit samples remain separate supporting art. Missing final illustrations use purpose-built diagrams; images replace them at assets/documentary/art/<slot>.webp. The final plain service wall remains free of story projection cards.

Decoded slide changes use a bounded 220ms crossfade with slight lateral movement and a quiet mechanical click. Screen unrolling has a short motor cue. Both sounds are finite procedural effects routed through the existing SFX mixer, with no separate audio context or permanent sound loop. Initial image preloads and stale decode completions do not trigger cues. Gentle uses opacity-only transitions and quieter effects; reduced motion commits slides and screen position immediately. Pause, hidden-page suspension and panic release active effects. Physical speaker cones respond to narration activity, return to rest during silence and stop moving in Gentle or reduced motion.

The three-hour post-ride sequence uses wall time and audio ended events, with no permanent render loop. Regular browser-tab hiding allows narration to continue. If the host explicitly suspends audio on focus loss, the mixer remains authoritative: speech resumes when the host releases it and the stage clock keeps wall time. New run can play its short recorded response before resolving to recap; panic is immediate. Terms read-back requires exact accepted clause IDs. The current game stores counts, so those blocks and their linking lines are skipped rather than claiming the player accepted every recorded clause.

The last view is a plain wall with a physical AD button, a small New run control, a plant, a water cooler and a bulb. There is no title or advertising card. The elapsed clock appears on the wall after five minutes. Subscribers have no AD control. Gentle and reduced motion disable the bulb's irregular dimming.

The built-in recording controller owns timing by default. An external recording lane can replace it:

    tabDocumentary(ctx, {
      narration: false,
      fallback: false,
      connect(tunnel) {
        tunnel.on('ready', () => { /* start the recorded cold open */ });
        tunnel.on('cue', cue => { /* play the recording for cue.id */ });
        // Send these at the corresponding recording offsets:
        // tunnel.beat('booth.stamp');
        // tunnel.beat('booth.unlock');
      },
    });

The returned tunnel has ready, read-only state, on, addCues, setPace, hold, beat, captions, narration, music and dispose. Events include ready, zone, band, exhibit, cue, pump, stage, stage-action, beat and dispose. Set narration:false and music:false for a silent integration. setPace(provider) returns a release callback and installs a provider on the existing physics tick; state.pacing reports whether it is active. projector.show accepts a story id (booth, lever, reflex, wakeup, conversation) plus optional slot/title/line; the legacy replay id aliases reflex. projector.hide retracts the selected screen.

Visual beats: booth.stamp, booth.unlock, floor.creak, floor.snap, floor.replay, floor.freeze, floor.release, stage.pitch, stage.buttons, stage.booth with {state:'a'|'b'|'c'}, and stage.caption with {text}. The stage.buttons beat starts the elapsed timer after the recorded pitch. Built-in narration pacing suppresses the old floor.snap hold. External manual lanes can still hold the floorboard replay and release it with floor.release.

Run pictures remain display-only DOM media, aligned to the camera with portal occlusion. The 38 larger personal screens share an active-media budget of eight on desktop and four in lite mode. Scrolller keeps strict-origin referrers and visible attribution. Video limits remain two on desktop and one in lite mode. Reduced motion and Gentle use stills. Media source changes and disposal release leases.

## Checks

    node --test --test-concurrency=2 tests/documentary-tunnel.test.mjs ui/documentary/narration.test.mjs ui/documentary/narration-pacing.test.mjs ui/documentary/subtitles.test.mjs ui/documentary/projector-effects.test.mjs ui/documentary/music.test.mjs dev/tab-doc-preview-server.test.mjs
    node dev/tab-doc-drive.mjs

The browser drive expects the server above and Playwright. Set PW_ROOT to the package.json beside an installed Playwright package, CHROME_EXE to Chrome, and optionally SHOTS to a screenshot directory.

Checks cover physics, true track distance, winding/sloped alignment, automatic sections, braking, reduced motion, external cue holds, DOM media and stage clock. Focused narration checks cover acoustic caption/slide timing, continuous cart profiles, bounded pumping, non-overlapping speech, finite projector effects and immediate pause/panic cleanup. The browser drive covers desktop/touch dragging, lever-only pumping, Wake Up, replay, panic, stage controls and cleanup. Three-hour idle clock advancement leaves node count flat and the docked scene has no rendering callback. Dragging redraws on input; a deliberate lever press briefly animates the parked lever.

Rendering is capped at 60 fps desktop and 30 fps lite, with bounded geometry pools and one shared texture set. Desktop browser testing is not a physical phone GPU benchmark.

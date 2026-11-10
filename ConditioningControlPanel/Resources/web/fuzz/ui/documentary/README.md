# Documentary museum

Full Three.js Gooning Museum, revised after the owner's November 10 feedback. Six room kits follow the script: rose/velvet Subject intake, cyan switchback, midnight reflex laboratory, pink descent, mint Wake Up conservatory and a plain service wall at the end. Layered wall panels, detailed light fixtures, upholstered alcoves, screen housings and physical exhibits replace the initial plain gallery. Large personal-media displays fill selected niches. The story uses larger pull-down projection screens inside the room, including the floorboard replay, instead of a card over the player's view.

The three sculptures and their pedestals reuse Backroom's knight.glb, queen.glb and rook.glb. The texture set is about 14.5 MB, with 2K terrazzo, velour, leather and rubber maps plus the earlier scanned surfaces. Sources and transformations are recorded in assets/documentary/textures/sources.json. Painted and brushed surfaces also use deterministic micro-normal and roughness maps. Runtime loads only local assets.

## Preview

Run from the fuzz directory:

    node dev/serve.mjs 8897

Open http://127.0.0.1:8897/fuzz/dev/tab-doc.html

Drag with mouse or touch to look around. Click/tap the physical lever, or press Space/Enter, to pump. Scenery clicks do not pump. Center view resets the head direction. The opening recording unlocks the platform when it finishes; **Ride preview > Leave platform** skips that wait. **Ride preview > Choose personal media** lets the standalone preview display local files. In the game, the screens use the player's existing selected Scrolller or own-file source.

Preview doors: ?at=lever, ?at=145 (first drop), ?at=floorboard, ?at=363 (second descent), ?at=wakeup, ?at=stage. Numeric distances work. Add lite=1, reduced=1, gentle=1, plan=prime or hours=3 as needed. The normal preview includes recorded narration. voice=0 is silent; manual=1 and test=1 default to silent for deterministic drives, with voice=1 overriding that default.

Distance follows the 580m rail, including its real slopes and bends. Powered lifts and downhill acceleration run only in the connecting sections, then brake before scripted exhibits. This owner-requested behavior supersedes the original pump-only rule for those connections. Reduced motion caps speed and disables banking, anticipation and speed FOV. Holds and Wake Up always win over automatic movement.

## Game integration

This module leaves the ending registry and shop row to their owning lane. Use these properties on the ending selected by that owner:

    import { tabDocumentary, DOCUMENTARY_SHOW_MAX_MS } from './ui/documentary/index.js';
    const ending = {
      ending: tabDocumentary,
      showMaxMs: DOCUMENTARY_SHOW_MAX_MS,
    };

The real game host was checked by temporarily replacing the outside handler inside an isolated test browser. No registry change was saved. Wake Up, Escape and panic work through the existing host. New run resolves into the ordinary recap.

## Narration and illustrations

Finished original recordings are packaged in assets/documentary/voice. Narration uses the game's voice mixer, one clip at a time, and stops on Wake Up or panic. Opening and exhibit holds release from recorded clip endings. Phrase-level visual offsets are estimates because the recording delivery has no word-alignment file. Missing clips remain silent and release their hold after their measured duration. Missing final illustrations use purpose-built diagrams; approved images can replace them at assets/documentary/art/<slot>.webp.

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

The returned tunnel has ready, read-only state, on, addCues, hold, beat, captions, narration and dispose. Events include ready, zone, band, exhibit, cue, pump, stage, stage-action, beat and dispose. Set narration:false for a silent integration. projector.show accepts a story id (booth, lever, reflex, replay, wakeup) plus optional slot/title/line; projector.hide retracts the selected screen.

Visual beats: booth.stamp, booth.unlock, floor.creak, floor.snap, floor.replay, floor.freeze, floor.release, stage.pitch, stage.buttons, stage.booth with {state:'a'|'b'|'c'}, and stage.caption with {text}. The stage.buttons beat starts the elapsed timer after the recorded pitch. Keep the floor hold through the replay, then send floor.release.

Run pictures remain display-only DOM media, aligned to the camera with portal occlusion. Scrolller keeps strict-origin referrers and visible attribution. Video limits are two on desktop and one in lite mode. Reduced motion and Gentle use stills. Media source changes and disposal release leases.

## Checks

    node --test --test-concurrency=2 tests/documentary-tunnel.test.mjs ui/documentary/narration.test.mjs
    node dev/tab-doc-drive.mjs

The browser drive expects the server above and Playwright. Set PW_ROOT to the package.json beside an installed Playwright package, CHROME_EXE to Chrome, and optionally SHOTS to a screenshot directory.

Checks cover physics, true track distance, winding/sloped alignment, automatic sections, braking, reduced motion, cue holds, DOM media and stage clock. The browser drive covers desktop/touch dragging, lever-only pumping, Wake Up, replay, panic, stage controls and cleanup. Three-hour idle clock advancement leaves node count flat and the docked scene has no rendering callback. Dragging redraws on input; a deliberate lever press briefly animates the parked lever.

Rendering is capped at 60 fps desktop and 30 fps lite, with bounded geometry pools and one shared texture set. Desktop browser testing is not a physical phone GPU benchmark.

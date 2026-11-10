# Documentary museum

Full Three.js environment for the approved November 10 museum concept: scanned stone, brick, plaster, wood and mosaic materials; arched galleries and alcoves; brass fittings; cabinets, benches, cases, a grass specimen and a working handcart.

The three sculptures and their pedestals reuse Backroom's knight.glb, queen.glb and rook.glb. The new texture set is about 3.9 MB. Its six CC0 sources and transformations are recorded in assets/documentary/textures/sources.json. Runtime loads only local assets.

## Preview

Run from the fuzz directory:

    node dev/serve.mjs 8897

Open http://127.0.0.1:8897/fuzz/dev/tab-doc.html

Click, tap, Space or Enter pumps the cart. The booth locks for 40 seconds in the silent preview; **Ride preview > Leave platform** skips that wait.

Preview doors: ?at=lever, ?at=floorboard, ?at=wakeup, ?at=stage. Add lite=1, reduced=1, gentle=1, plan=prime or hours=3 as needed.

## Game integration

This module leaves the ending registry and shop row to their owning lane. Use these properties on the ending selected by that owner:

    import { tabDocumentary, DOCUMENTARY_SHOW_MAX_MS } from './ui/documentary/index.js';
    const ending = {
      ending: tabDocumentary,
      showMaxMs: DOCUMENTARY_SHOW_MAX_MS,
    };

The real game host was checked by temporarily replacing the outside handler inside an isolated test browser. No registry change was saved. Wake Up, Escape and panic work through the existing host. New run resolves into the ordinary recap.

## Narration and illustrations

This build is silent. Recorded narration and the 23 approved wall illustrations are separate deliveries. Missing art temporarily uses existing game illustrations. Put approved images at assets/documentary/art/<slot>.webp; slot names and captions are in the manifest.

The recording lane can own timing:

    tabDocumentary(ctx, {
      fallback: false,
      connect(tunnel) {
        tunnel.on('ready', () => { /* start the recorded cold open */ });
        tunnel.on('cue', cue => { /* play the recording for cue.id */ });
        // Send these at the corresponding recording offsets:
        // tunnel.beat('booth.stamp');
        // tunnel.beat('booth.unlock');
      },
    });

The returned tunnel has ready, read-only state, on, addCues, hold, beat, captions and dispose. Events include ready, zone, band, exhibit, cue, pump, stage, stage-action, beat and dispose.

Visual beats: booth.stamp, booth.unlock, floor.creak, floor.snap, floor.replay, floor.freeze, floor.release, stage.pitch, stage.buttons, stage.booth with {state:'a'|'b'|'c'}, and stage.caption with {text}. The stage.buttons beat starts the elapsed timer after the recorded pitch. Keep the floor hold through the replay, then send floor.release.

Run pictures remain display-only DOM media, aligned to the camera with portal occlusion. Scrolller keeps strict-origin referrers and visible attribution. Video limits are two on desktop and one in lite mode. Reduced motion and Gentle use stills. Media source changes and disposal release leases.

## Checks

    node --test --test-concurrency=2 tests/documentary-tunnel.test.mjs
    node dev/tab-doc-drive.mjs

The browser drive expects the server above and Playwright. Set PW_ROOT to the package.json beside an installed Playwright package, CHROME_EXE to Chrome, and optionally SHOTS to a screenshot directory.

Verified: nine model/clock/DOM boundary tests; 50 desktop and phone browser checks; real host loading, pumping, pause, resume, panic and recap; own-file images; simulated remote attribution, referrers, video limits and lease release; startup failure cleanup. Three-hour idle clock advancement leaves node count flat and the docked scene has no rendering callback. A deliberate lever press can briefly animate the parked lever.

Rendering is capped at 60 fps desktop and 30 fps lite, with bounded geometry pools and one shared texture set. Desktop browser testing is not a physical phone GPU benchmark.

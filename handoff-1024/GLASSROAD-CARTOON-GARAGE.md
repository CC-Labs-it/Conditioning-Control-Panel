# Racing Thoughts cartoon garage and screen transitions

Owner request, 2026-10-24: animated loading, more saturated colors, cartoon 3D with black outlines; center EMI inside customization and remove abrupt screen changes.

Isolated branch: `codex/glassroad-cartoon-garage`, above `codex/glassroad-garage-menu` at `384f1462f`. Preview: http://127.0.0.1:18892/. The game branch and concurrent Keep, Mainframe and Boudoir work are untouched.

- Garage models receive per-instance cel materials, a four-step light ramp, stronger paint chroma and black inverted hulls. Hulls share original geometry and skeleton bindings, add no vertex buffers, ignore pointer rays and leave source assets, face pixels, translucent lights and locked silhouettes intact. Race and track materials retain their existing behavior.
- The loading cover stays opaque throughout the full shader warm-up. A bouncing EMI kart, moving checker strips and striped progress animate over a teal, pink and yellow palette. Host Gentle and OS reduced motion stop decorative motion.
- Customization uses the union of actual indexed, skinned bounds across all nine warmed outfits. It fits and centers the rotating rider inside the free rectangle measured from the controls, including phone layouts. No hardcoded world-space horizontal offset remains.
- A reversible 0.62-second transition eases the camera and EMI as the carousel retreats. DOM panels share the same progress value. Calm uses an 0.8-second fade with the camera switch hidden at zero opacity. Large frame deltas cannot skip the whole transition. Inputs are guarded while the scene moves.
- Options, controls and online cards retain the garage backdrop instead of cutting to the race world. Entry fades and consistent ink-edged cards complete the menu flow.

Validation: 913 Node tests pass. Chrome WebGL2 and WebGPU each pass 18 cover/layout/menu/start checks. Firefox WebGL2 with Gentle passes garage, Snow customization, options and race start without browser errors. The real skinned model clears the viewport and controls, and pointer rotation plus Seafoam/checker customization pass in the motion capture.

Private evidence, before screenshots, browser harnesses and a 7.27-second motion recording: `C:/Users/Admin/.codex/visualizations/2026/10/23/01a16986-767c-7342-b038-2405bd2c4de2/cartoon-pass/`. The review decision is in `iteration_review.json`; the recording is `garage-motion.mp4`. None of these captures belongs in the public runtime tree.

Integration: retain the previous PR stack through #2801 and merge this tip as a descendant. Keep the small `boot.js` garage-render selection change when reconciling concurrent track work; do not replace its full file. No deploy was performed.

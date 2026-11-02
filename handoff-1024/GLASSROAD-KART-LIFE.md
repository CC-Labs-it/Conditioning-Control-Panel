# Kart and EMI life pass, October 24

Owner requested animated moving pieces, expressive EMI performance and richer model finishes, using agents.

## Delivery

- Branch `codex/glassroad-kart-life`, preview port 18892. All-eight inspection viewer: port 18894.
- Base is game `5ea35f9bf` plus the concurrent garage GPU fix `d7bb9cbdf`. No track branches were merged, reset or rewritten.
- Three draft reviews: #2798 tagged assets, #2799 mechanism runtime, #2800 EMI and garage integration. Stack sits above #2769, the garage GPU fix. Merge only the tip into the game branch or retarget each dependent before merging a parent. Never merge the stack only into its intermediate branches.
- Eight vehicles, 20 semantic assemblies, two to four additional material draws per vehicle. Pendulum reaches 8968 triangles after adding its missing bearing, bracket and crown axle; other accepted forms stay unchanged except Reverie's corrected pearl links.
- Actual game and garage use the same mechanism updater. EMI has held attention/greeting phases, spring antenna follow-through and contact-preserving additive acting. Finishes use shared physical materials; no new raster textures.
- Public payload is the runtime GLBs, small metadata and renderer code only. Private sources, native masters and review media: `C:/Projects/glassroad-art/kart-collection-2026-10-23/life-2/`.

## Runtime contract

- Vehicle scalar `_LIFE` becomes `_life` in GLTFLoader. Zero is static; 1..5 identify rigid assemblies. `art/models/vehicle-mechanisms.json` gives model-space pivots, axes and spin/swing/pulse/press parameters. Y up, -Z forward, metres. Spin rate is radians/sec; other rates are cycles/sec.
- `createVehicleLife` cuts complete rigid triangles, transforms them through the existing inverse bind and parents them to their actual bone. It adds no bones or clips. Shared cached static cuts own cloned GPU buffers, released only after the last instance. Do not dispose geometry sharing source BufferAttributes.
- `createEmiLife` is constructed on a fresh rest-pose clone. Every frame: `beforePose()`, mixer update, then life update. R4 cup joint axes differ from model axes; preserve its cached inverse rest frame when calculating palm/sole-plane motion.
- The authored 26 clips own major actions. Secondary body/limb offsets stand down during them. Preserve exact approved face pixels and accepted outfit fits.
- Both systems consume existing frame delta and racer state. No independent timers or physics/network changes. Motion off restores rest; Gentle calms motion. Distant mechanisms freeze their current angle. Phone rival vehicle fallback stays unchanged.
- EMI enamel uses the existing interleaved garage mask. Do not add another GPU vertex buffer: the accepted skinned mesh already needs seven before that mask.

## Validation and evidence

- 904/904 race tests pass, including actual R4 contact heights over 480 frames, exact restoration and all eight tagged GLBs under two real clips.
- More than 100,000 world-vertex comparisons pass. Mechanism pivot residual is below 0.002 mm. Semantic IDs remain attached to the same source triangles after compression.
- Six vehicles retain every triangle attribute. Two permit only named connector repairs, recorded in the private source edit ledger.
- Chrome garage/customization/race interaction checks: 16 pass. WebGPU garage boot and Firefox WebGL2 garage-to-race pass without exceptions. All-eight front/rear evidence and an 18.6-second reel are in private `life-2/review/` and `life-2/racer-life-reel.webm`.
- Corrected Pendulum/Reverie native masters import, save and reopen in Blender 5.2.1, 15 joints intact.
- No deployment or game-branch merge from this pass. Keep and Mainframe sessions remain separate and must retain their own track/water/media/camera changes when combined later.

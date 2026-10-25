/* render/post.js - RenderPipeline (r183+ name for PostProcessing): scene pass, the item screen effect
 * (render/screenfx.js, CONTRACT-B1), bloom at the tier's resolution, then one tiny composite: edge tint,
 * vignette, rescue white fade, finish swell.
 * Bloom reads the raw scene and is added AFTER the screen effect, so rails and the racing line glow
 * through every effect (never fully blind). Every number that moves here comes from FxState, which
 * already asked photosafe and Gentle; fx.screen (fx/effects.js) drives the screen effect.
 * Polish 2 (perf): MSAA lives on the scene pass only (Q.antialias, 4 samples); the canvas gets the one
 * composite quad and needs none. compileAsync() builds the scene pass pipelines for its own target.
 */
import { RenderPipeline, Color } from 'three/webgpu';
import { pass, uniform, screenUV, smoothstep, mix, vec3, vec4, clamp } from 'three/tsl';
import { bloom } from 'three/addons/tsl/display/BloomNode.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';

// the SCREENFX lane's module; a missing or broken one leaves the plain scene (the race still runs)
const screenMod = await import('./screenfx.js').catch((e) => { console.warn('[glassroad/post] no screenfx: ' + (e && e.message)); return null; });

const rgb = (hex) => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };
const NONE = Object.freeze({ kind: 'none', t: 0, durS: 0, k: 0, strength: 0, prevKind: 'none', prevK: 0,
  params: Object.freeze({ gentle: false, corner: false, dimOnly: false, blurMul: 1, flicker: false, cover: 0, seed: 0, by: -1 }) });

export function createPost(three, scene, camera) {
  const pipe = new RenderPipeline(three);
  const u = { strength: uniform(LOOK.bloomStrength), white: uniform(0), flash: uniform(0), edge: uniform(0), side: uniform(0), submerged: uniform(0) };
  const scenePass = pass(scene, camera, { samples: Q.antialias ? 4 : 0 });
  const col = scenePass.getTextureNode('output');
  let screen = null;
  try { screen = screenMod ? screenMod.createScreenFx(three) : null; } catch (e) { console.warn('[glassroad/post] screenfx failed: ' + (e && e.message)); }
  const base = screen ? screen.node(col) : col.rgb;
  let bloomNode = null, builtBloom = null;

  function build() {
    if (bloomNode) { bloomNode.dispose(); bloomNode = null; }
    let out = base;
    if (Q.bloom) {
      bloomNode = bloom(col, u.strength, LOOK.bloomRadius, LOOK.bloomThreshold);
      bloomNode.setResolutionScale(Q.bloomScale);
      out = out.add(bloomNode.rgb.mul(screen ? screen.bloomGate() : 1));
    }
    const vig = smoothstep(LOOK.vignetteIn, LOOK.vignetteOut, screenUV.sub(0.5).length().mul(Math.SQRT2));
    const sideW = clamp(screenUV.x.sub(0.5).mul(u.side).mul(2).add(1), 0, 1);
    out = mix(out, rgb(LOOK.edgeTint), vig.mul(u.edge).mul(sideW).mul(0.6));
    out = out.mul(vig.mul(LOOK.vignetteDark).oneMinus());
    out = mix(out, vec3(1, 1, 1), u.white).add(rgb(LOOK.flashTint).mul(u.flash));
    // A steady coolant tint eases with depth; no strobe, blur or loss of the driving corridor.
    out = mix(out, out.mul(vec3(0.48, 0.82, 1.02)).add(vec3(0.006, 0.035, 0.055)), u.submerged.mul(0.8));
    pipe.outputNode = vec4(out, 1);
    pipe.needsUpdate = true;
    builtBloom = Q.bloom;
  }
  build();

  return {
    pipe,
    antialias: Q.antialias,
    /** Reuse the same scene-pass targets, effect nodes and composite pipeline for the next course. */
    setScene(nextScene,nextCamera){
      scene=nextScene;camera=nextCamera;scenePass.scene=nextScene;scenePass.camera=nextCamera;
    },
    detachScene(){scene=null;camera=null;scenePass.scene=null;scenePass.camera=null;},
    /** Compile every visible scene object for the scene pass target (no draw). */
    async compileAsync() {
      const target = three.getRenderTarget(), mrt = three.getMRT();
      try {
        three.setRenderTarget(scenePass.renderTarget);
        three.setMRT(scenePass.getMRT());
        // The pipeline quad renders at depth +1, its scene pass at +2. Match that
        // cache context without changing the shared recursion counter across awaits.
        await three.compileAsync(scene, camera, null, null, three._callDepth + 2);
      } finally {
        three.setRenderTarget(target);
        three.setMRT(mrt);
      }
    },
    /** Call after a quality step. */
    retier() { if (builtBloom !== Q.bloom) build(); else if (bloomNode) bloomNode.setResolutionScale(Q.bloomScale); },
    update(fx, dt = 0, aspect = 16 / 9, view = {}) {
      if (screen) screen.update((fx && fx.screen) || NONE, dt, aspect, view);
      if (!fx) return;
      u.strength.value = LOOK.bloomStrength * (1 + (fx.bloomBoost || 0) * LOOK.bloomBoostGain);
      u.white.value = fx.whiteFade || 0;
      u.flash.value = fx.flashLevel || 0;
      u.edge.value = fx.edgeGlow || 0;
      u.side.value = fx.edgeSide || 0;
      u.submerged.value = fx.submerged || 0;
    },
    render() { pipe.render(); },
    dispose() { if (bloomNode) bloomNode.dispose(); if (screen) screen.dispose(); pipe.dispose(); },
  };
}

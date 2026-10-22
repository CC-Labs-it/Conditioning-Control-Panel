/* render/post.js - RenderPipeline (r183+ name for PostProcessing): scene pass, bloom at the tier's
 * resolution, then one tiny composite: edge tint, vignette, rescue white fade, finish swell.
 * Every number that moves here comes from FxState, which already asked photosafe and Gentle.
 */
import { RenderPipeline, Color } from 'three/webgpu';
import { pass, uniform, screenUV, smoothstep, mix, vec3, vec4, clamp } from 'three/tsl';
import { bloom } from 'three/addons/tsl/display/BloomNode.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';

const rgb = (hex) => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };

export function createPost(three, scene, camera) {
  const pipe = new RenderPipeline(three);
  const u = { strength: uniform(LOOK.bloomStrength), white: uniform(0), flash: uniform(0), edge: uniform(0), side: uniform(0) };
  const col = pass(scene, camera).getTextureNode('output');
  let bloomNode = null, builtBloom = null;

  function build() {
    if (bloomNode) { bloomNode.dispose(); bloomNode = null; }
    let out = col.rgb;
    if (Q.bloom) {
      bloomNode = bloom(col, u.strength, LOOK.bloomRadius, LOOK.bloomThreshold);
      bloomNode.setResolutionScale(Q.bloomScale);
      out = out.add(bloomNode.rgb);
    }
    const vig = smoothstep(LOOK.vignetteIn, LOOK.vignetteOut, screenUV.sub(0.5).length().mul(Math.SQRT2));
    const sideW = clamp(screenUV.x.sub(0.5).mul(u.side).mul(2).add(1), 0, 1);
    out = mix(out, rgb(LOOK.edgeTint), vig.mul(u.edge).mul(sideW).mul(0.6));
    out = out.mul(vig.mul(LOOK.vignetteDark).oneMinus());
    out = mix(out, vec3(1, 1, 1), u.white).add(rgb(LOOK.flashTint).mul(u.flash));
    pipe.outputNode = vec4(out, 1);
    pipe.needsUpdate = true;
    builtBloom = Q.bloom;
  }
  build();

  return {
    pipe,
    /** Call after a quality step. */
    retier() { if (builtBloom !== Q.bloom) build(); else if (bloomNode) bloomNode.setResolutionScale(Q.bloomScale); },
    update(fx) {
      if (!fx) return;
      u.strength.value = LOOK.bloomStrength * (1 + (fx.bloomBoost || 0) * LOOK.bloomBoostGain);
      u.white.value = fx.whiteFade || 0;
      u.flash.value = fx.flashLevel || 0;
      u.edge.value = fx.edgeGlow || 0;
      u.side.value = fx.edgeSide || 0;
    },
    render() { pipe.render(); },
    dispose() { if (bloomNode) bloomNode.dispose(); pipe.dispose(); },
  };
}

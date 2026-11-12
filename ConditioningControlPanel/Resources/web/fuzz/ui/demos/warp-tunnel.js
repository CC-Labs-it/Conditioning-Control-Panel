/* Demo for the Warp Tunnel (tunnel): the unlock's own shader (ui/unlocks/basics/warp-tunnel.js) behind a little page,
 * a bright ring and a faster fall on every press. Replaces the generated rectangle demo (owner 2026-11-08), so this
 * module loads basics.js first and overwrites its entry. Silent, as every demo is. */
import './basics.js';
import { DEMOS } from './kit.js';
import { createTunnelGL } from '../unlocks/basics/warp-tunnel.js';

DEMOS.tunnel = (k) => {
  k.page('dark');
  const cv = k.el.ownerDocument.createElement('canvas');
  cv.width = 240; cv.height = 150;
  cv.style.cssText = 'position:absolute;left:0;top:0;width:240px;height:150px;';
  k.el.appendChild(cv);
  const gl = createTunnelGL(cv);
  k.counter({ v: 1250000, y: 14 });
  const b = k.btn({ w: 90, h: 34, x: 75, y: 92, skin: 'pink' });
  const st = { t: 0, spin: 0, speed: 0.5, rings: [] };
  const f = k.finger(170, 130);
  k.every(1500, () => f.tapEl(b, () => { k.press(b); st.speed = Math.min(1.6, st.speed + 0.35); if (st.rings.length < 4) st.rings.push(9); }));
  k.frame((dt) => {
    st.speed += (0.5 - st.speed) * Math.min(1, dt * 0.8);
    st.t += dt * st.speed;
    st.spin += dt * 0.012;
    st.rings = st.rings.map((z) => z - dt * z * 1.7).filter((z) => z > 0.25);
    gl?.draw({ t: st.t, spin: st.spin, alpha: 0.95, cx: 0.5, cy: 0.72, a: [1, 0.41, 0.71], b: [0.2, 0.13, 0.42], d: [0.05, 0.02, 0.1], rings: st.rings });
  });
};

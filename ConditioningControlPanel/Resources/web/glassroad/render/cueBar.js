import { Group, Mesh, BoxGeometry, PlaneGeometry, Color, Vector3, Quaternion, Matrix4 } from 'three/webgpu';
import { fadeSet, labelTexture, beatKick, ease, unit } from './cueKit.js';
import { S } from '../sim/state.js';

// BAR: a sky-wide progress bar hung far ahead over the road, like the sky it travels with the player and
// turns smoothly with the road's heading (no camera motion). Values come from every BAR row of the same
// variant: install steps on its blips; reboot fills steadily between keyframes and completes at its end.
const AHEAD_M = 260, UP_M = 46, WIDTH_M = 170, HEIGHT_M = 13;
/** The bar value at songS from its keyframe rows (sorted by start). */
export function barValue(rows, songS, variant) {
  const keys = rows.filter(r => r.kind === 'BAR' && (r.params?.variant || 'install') === variant).sort((a, b) => a.startS - b.startS);
  if (!keys.length || songS < keys[0].startS) return null;
  const base = keys[0], linear = variant === 'reboot';
  const pts = keys.map(k => [k.startS, unit(k.params?.value ?? 0)]);
  if (linear) pts.push([base.endS, 1]);
  let i = 0; while (i + 1 < pts.length && songS >= pts[i + 1][0]) i++;
  const [t0, v0] = pts[i], next = pts[i + 1];
  if (!next) return v0;
  if (linear) return v0 + (next[1] - v0) * unit((songS - t0) / (next[0] - t0));
  // Install holds each value and steps to the next on its blip, with a short eased fill.
  const prev = i ? pts[i - 1][1] : v0;
  return prev + (v0 - prev) * ease((songS - t0) / .5);
}

export function createCueBar(scene, track, { make = () => document.createElement('canvas') } = {}) {
  const object = new Group(); object.name = 'cue-bar'; object.visible = false; scene.add(object);
  const box = new BoxGeometry(1, 1, 1), plane = new PlaneGeometry(1, 1), set = fadeSet();
  const shell = new Mesh(box, set.make({ color: '#061309' })), rim = new Mesh(box, set.make({ color: '#52e256' }));
  const fillMat = set.make({ color: '#52e256' }), fill = new Mesh(box, fillMat);
  const label = labelTexture(make, 1024, 128), text = new Mesh(plane, set.make({ map: label.texture }));
  object.add(rim, shell, fill, text);
  // A hologram over the course: it reads through tunnel roofs and walls rather than vanishing behind them.
  for (const m of set.mats) m.depthTest = false;
  [rim, shell, fill, text].forEach((mesh, i) => { mesh.renderOrder = 20 + i; });
  rim.scale.set(WIDTH_M + 3, HEIGHT_M + 3, .6); shell.scale.set(WIDTH_M, HEIGHT_M, 1); shell.position.z = .2;
  text.scale.set(WIDTH_M * .62, WIDTH_M * .62 / 8, 1); text.position.set(0, HEIGHT_M / 2 + 12, .3);
  const f = { p: [0, 0, 0], t: [0, 0, 1], n: [0, 1, 0], b: [1, 0, 0], w: 0 };
  const want = new Vector3(), wantQ = new Quaternion(), m4 = new Matrix4(), bx = new Vector3(), ny = new Vector3(), tz = new Vector3();
  const lit = new Color('#52e256'), hot = new Color('#d8ffdc');
  let alpha = 0, value = 0, variant = null, placed = false, shown = 0;
  const anchor = new Vector3();
  function update(rows = [], all = [], { songS = NaN, song = null, kart = null, dt = 0, paused = false, gentle = false } = {}) {
    if (paused) return;
    const step = Math.min(.1, Math.max(0, dt || 0));
    // The longest live BAR row is the bar's span; keyframe rows only move its value.
    const span = rows.filter(r => r.kind === 'BAR').sort((a, b) => (b.endS - b.startS) - (a.endS - a.startS))[0] || null;
    const ended = span && songS >= span.endS, target = span && !ended ? unit(span.alpha) : 0;
    alpha += (target - alpha) * Math.min(1, step / (ended && !gentle ? .08 : gentle ? .6 : .3));
    if (span) variant = span.params?.variant || 'install';
    object.visible = alpha > .004;
    if (!object.visible || !kart) { if (!object.visible) placed = false; return; }
    const v = barValue(all, songS, variant); if (v !== null) value = v;
    track.frameAt(kart[S.s], f);
    bx.fromArray(f.b); ny.set(0, 1, 0); tz.fromArray(f.t); tz.y = 0; tz.normalize(); bx.crossVectors(ny, tz).negate();
    want.set(kart[S.posX], kart[S.posY], kart[S.posZ]).addScaledVector(tz, AHEAD_M).addScaledVector(ny, UP_M);
    m4.makeBasis(bx, ny, tz.clone().negate()); wantQ.setFromRotationMatrix(m4);
    if (!placed) { object.position.copy(want); object.quaternion.copy(wantQ); placed = true; }
    else { const k = Math.min(1, step / .9); object.position.lerp(want, k); object.quaternion.slerp(wantQ, k); }
    anchor.copy(object.position);
    const kick = gentle ? 0 : beatKick(song, songS, 5) * .6;
    fill.scale.set(Math.max(.01, WIDTH_M - 4) * value, HEIGHT_M - 4, 1.1);
    fill.position.set(-(WIDTH_M - 4) / 2 + (WIDTH_M - 4) * value / 2, 0, .4);
    fillMat.color.copy(lit).lerp(hot, kick);
    object.scale.setScalar(1 + kick * .02);
    shown = Math.round(value * 100);
    const name = span?.params?.label || (variant === 'reboot' ? 'REBOOT' : 'INSTALLING');
    label.paint(name + shown, (ctx, w, h) => {
      ctx.fillStyle = '#52e256'; ctx.font = '700 92px ui-monospace, Consolas, monospace'; ctx.textBaseline = 'middle';
      ctx.textAlign = 'left'; ctx.fillText(name, 12, h / 2); ctx.textAlign = 'right'; ctx.fillText(shown + '%', w - 12, h / 2);
    });
    set.opacity(alpha);
  }
  return { object, update, anchor, get value() { return value; },
    clear() { object.visible = false; alpha = 0; placed = false; value = 0; },
    get stats() { return { visible: object.visible, value: shown, variant }; },
    dispose() { object.removeFromParent(); set.dispose(); label.dispose(); box.dispose(); plane.dispose(); } };
}

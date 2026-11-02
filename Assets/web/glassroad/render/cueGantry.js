import { Group, Mesh, BoxGeometry, PlaneGeometry, Color } from 'three/webgpu';
import { wordTheme } from './wordTheme.js';
import { placeOnFrame, fadeSet, labelTexture, beatKick, ease, unit } from './cueKit.js';

// GANTRY: a count arch across the road, crossed on its spoken number. Posts stand outside the road edge,
// the beam and number clear any kart by metres. Lit and pulsing on the beat until you pass, then it goes dark.
const CAPACITY = 6;
export function createCueGantries(scene, track, { make = () => document.createElement('canvas') } = {}) {
  const object = new Group(); object.name = 'cue-gantries'; scene.add(object);
  const theme = wordTheme(track), box = new BoxGeometry(1, 1, 1), plane = new PlaneGeometry(1, 1), slots = [];
  const lit = new Color(theme.accent), dark = new Color('#26222c'), glowOn = new Color('#ffffff');
  for (let i = 0; i < CAPACITY; i++) {
    const set = fadeSet(), root = new Group(); root.visible = false; object.add(root);
    const frameMat = set.make({ color: theme.ink }), glow = set.make({ color: lit.clone() });
    const label = labelTexture(make, 256, 256), face = set.make({ map: label.texture });
    const posts = [-1, 1].map(() => new Mesh(box, frameMat)), beam = new Mesh(box, frameMat), strip = new Mesh(box, glow);
    const sign = new Mesh(plane, face);
    root.add(...posts, beam, strip, sign);
    slots.push({ root, posts, beam, strip, sign, label, glow, set, id: null });
  }
  function paint(slot, text) {
    slot.label.paint(text, (ctx, w, h) => {
      ctx.fillStyle = theme.ink; ctx.beginPath(); ctx.arc(w / 2, h / 2, w * .47, 0, Math.PI * 2); ctx.fill();
      ctx.lineWidth = 12; ctx.strokeStyle = theme.accent; ctx.stroke();
      ctx.fillStyle = '#ffffff'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
      ctx.font = `900 ${text.length > 2 ? 90 : 170}px system-ui, sans-serif`; ctx.fillText(text, w / 2, h / 2 + 8);
    });
  }
  function update(rows = [], { songS = 0, song = null, paused = false, gentle = false } = {}) {
    if (paused) return;
    const wanted = rows.filter(r => r.kind === 'GANTRY' && r.frame && r.alpha > .001);
    for (const s of slots) if (s.id && !wanted.some(r => r.id === s.id)) { s.root.visible = false; s.id = null; }
    const kick = gentle ? 0 : beatKick(song, songS);
    for (const row of wanted) {
      const s = slots.find(v => v.id === row.id) || slots.find(v => !v.id); if (!s) continue;
      if (!s.id) { s.id = row.id; paint(s, String(row.params?.label ?? '')); }
      const f = row.frame, half = Math.max(4, f.w / 2) + 1.6, top = 9.5;
      placeOnFrame(s.root, f); s.root.visible = true;
      for (const [k, post] of s.posts.entries()) { post.position.set((k ? 1 : -1) * half, top / 2, 0); post.scale.set(1.1, top, 1.1); }
      s.beam.position.set(0, top, 0); s.beam.scale.set(half * 2 + 1.1, 1.3, 1.3);
      s.strip.position.set(0, top - .75, .7); s.strip.scale.set(half * 2, .35, .2);
      // Passed (or missed) arches go dark; the one you are about to cross pulses on the beat.
      const passed = row.hitS != null || songS > row.startS + .35, off = passed ? ease(((songS - (row.hitS ?? row.startS)) || 0) / .35) : 0;
      s.glow.color.copy(lit).lerp(glowOn, passed ? 0 : kick * .6).lerp(dark, off);
      const size = 6.5 * (1 + (passed ? 0 : kick * .08)) * (1 - off * .25);
      s.sign.position.set(0, top + 3.6, .7); s.sign.scale.set(size, size, 1);
      s.set.opacity(unit(row.alpha) * (1 - off * .55));
    }
  }
  return { object, update,
    clear() { for (const s of slots) { s.root.visible = false; s.id = null; } },
    get stats() { return { active: slots.filter(s => s.root.visible).length, capacity: CAPACITY }; },
    dispose() { object.removeFromParent(); for (const s of slots) { s.label.dispose(); s.set.dispose(); } box.dispose(); plane.dispose(); } };
}

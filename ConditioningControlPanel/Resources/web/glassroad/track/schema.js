/* track/schema.js - validation for Track JSON v1 (plan 3.1, CONTRACT.md). No three.js.
 * validate(json) -> { ok, errors: string[], warnings: string[] }. Errors make a track unloadable;
 * warnings (node spacing outside 8..15 m, odd but legal values) do not.
 */

export const FLAGS = Object.freeze(['start', 'rail', 'railL', 'railR', 'open', 'magnet', 'boost', 'jump', 'glide', 'pit', 'flow']);
export const SURFACES = Object.freeze(['glass', 'grip', 'haze']);
export const SPACING_M = Object.freeze([8, 15]);

const isNum = (v) => typeof v === 'number' && Number.isFinite(v);
const isInt = (v) => Number.isInteger(v);
const isStr = (v) => typeof v === 'string' && v.length > 0;

/** @param {any} t parsed track JSON @returns {{ok: boolean, errors: string[], warnings: string[]}} */
export function validate(t) {
  const errors = [], warnings = [];
  const err = (m) => errors.push(m), warn = (m) => warnings.push(m);
  if (!t || typeof t !== 'object' || Array.isArray(t)) return { ok: false, errors: ['track is not an object'], warnings };

  if (t.format !== 'glassroad-track') err('format must be "glassroad-track"');
  if (t.version !== 1) err('version must be 1');
  if (!isStr(t.id) || !/^[a-z0-9-]+$/.test(t.id)) err('id must be lowercase letters, digits, dashes');
  if (!t.name || !isStr(t.name.en)) err('name.en missing');
  if (typeof t.loop !== 'boolean') err('loop must be boolean');
  if (!isInt(t.laps) || t.laps < 1 || t.laps > 9) err('laps must be an integer 1..9');
  if (t.units !== 'm') err('units must be "m"');

  const nodes = Array.isArray(t.nodes) ? t.nodes : null;
  if (!nodes || nodes.length < 4) { err('nodes must be an array of at least 4'); return { ok: false, errors, warnings }; }
  const N = nodes.length;
  const inRange = (i) => isInt(i) && i >= 0 && i < N;

  nodes.forEach((n, i) => {
    const at = 'nodes[' + i + ']';
    if (!n || typeof n !== 'object') { err(at + ' not an object'); return; }
    if (!Array.isArray(n.p) || n.p.length !== 3 || !n.p.every(isNum)) err(at + '.p must be 3 numbers');
    if (!isNum(n.roll) || Math.abs(n.roll) > 60) err(at + '.roll must be a number in -60..60 degrees');
    if (!isNum(n.w) || n.w < 4 || n.w > 40) err(at + '.w must be 4..40 m');
    if (!isNum(n.speed) || n.speed < 0.3 || n.speed > 1.5) err(at + '.speed must be 0.3..1.5');
    if (!SURFACES.includes(n.surface)) err(at + '.surface must be one of ' + SURFACES.join('|'));
    if (!Array.isArray(n.flags)) { err(at + '.flags must be an array'); return; }
    for (const f of n.flags) if (!FLAGS.includes(f)) err(at + ' unknown flag ' + f);
    if (new Set(n.flags).size !== n.flags.length) err(at + ' duplicate flags');
    const railish = n.flags.filter((f) => f === 'rail' || f === 'railL' || f === 'railR');
    if (n.flags.includes('open') && railish.length) err(at + ' open and rail flags together');
    if (n.flags.includes('rail') && railish.length > 1) err(at + ' rail with railL/railR');
    if (n.flags.includes('jump') && railish.length) warn(at + ' jump node carries a rail (ignored over the gap)');
  });
  if (errors.length) return { ok: false, errors, warnings };

  const last = t.loop ? N : N - 1;
  for (let i = 0; i < last; i++) {
    const a = nodes[i].p, b = nodes[(i + 1) % N].p;
    const d = Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]);
    if (d < 0.5) err('nodes ' + i + ' and ' + ((i + 1) % N) + ' coincide');
    else if (d < SPACING_M[0] || d > SPACING_M[1]) warn('node spacing ' + d.toFixed(2) + ' m after node ' + i);
  }
  if (!nodes[0].flags.includes('start')) warn('node 0 has no start flag');

  // sections: contiguous cover of [0, N)
  if (!Array.isArray(t.sections) || !t.sections.length) err('sections must be a non-empty array');
  else {
    let expect = 0;
    t.sections.forEach((s, i) => {
      const at = 'sections[' + i + ']';
      if (!isStr(s.id)) err(at + '.id missing');
      if (!isInt(s.from) || !isInt(s.to) || s.from !== expect || s.to <= s.from || s.to > N) err(at + ' from/to must continue at ' + expect + ' and end by ' + N);
      expect = s.to;
      for (const k of ['act', 'palette', 'sky']) if (!isStr(s[k])) err(at + '.' + k + ' missing');
      if (!isNum(s.bandSpeed) || s.bandSpeed < 0 || s.bandSpeed > 4) err(at + '.bandSpeed must be 0..4');
    });
    if (expect !== N) err('sections must end at node count ' + N);
  }

  if (!Array.isArray(t.checkpoints) || t.checkpoints.length < 2) err('checkpoints must have at least 2 entries');
  else {
    if (t.checkpoints[0] !== 0) err('checkpoints[0] must be 0');
    t.checkpoints.forEach((c, i) => {
      if (!inRange(c)) err('checkpoints[' + i + '] out of range');
      else if (i && c <= t.checkpoints[i - 1]) err('checkpoints must increase');
    });
  }

  const sp = t.spawn;
  if (!sp || !inRange(sp.node)) err('spawn.node out of range');
  else if (!Array.isArray(sp.grid) || !sp.grid.length) err('spawn.grid must be a non-empty array');
  else sp.grid.forEach((g, i) => {
    if (!Array.isArray(g) || g.length !== 2 || !g.every(isNum)) err('spawn.grid[' + i + '] must be [x, z]');
    else if (Math.abs(g[0]) > nodes[sp.node].w / 2) err('spawn.grid[' + i + '] off the road');
  });

  const list = (k, check) => {
    if (!Array.isArray(t[k])) { err(k + ' must be an array'); return; }
    t[k].forEach((e, i) => {
      const at = k + '[' + i + ']';
      if (!e || !inRange(e.node)) err(at + '.node out of range');
      else check(e, at);
    });
  };
  list('items', (e, at) => { if (!isNum(e.x) || !isStr(e.kind)) err(at + ' needs x and kind'); });
  list('boosts', (e, at) => {
    if (!isNum(e.x) || !isNum(e.w) || e.w <= 0 || !isNum(e.len) || e.len <= 0) err(at + ' needs x, w > 0, len > 0');
  });
  list('jumps', (e, at) => {
    if (!isNum(e.airLen) || e.airLen <= 0 || !isNum(e.height)) err(at + ' needs airLen > 0 and height');
    if (!nodes[e.node].flags.includes('jump')) err(at + ' node ' + e.node + ' has no jump flag');
  });
  list('hazards', (e, at) => { if (!isStr(e.kind) || !isNum(e.x)) err(at + ' needs kind and x'); });
  list('media', (e, at) => {
    if (e.side !== 'L' && e.side !== 'R') err(at + '.side must be L or R');
    if (!isNum(e.w) || !isNum(e.h) || e.w <= 0 || e.h <= 0) err(at + ' needs w, h > 0');
    if (e.kind !== 'still' && e.kind !== 'clip') err(at + '.kind must be still or clip');
    if (!isInt(e.slot) || e.slot < 0) err(at + '.slot must be an integer >= 0');
  });
  for (let i = 0; i < N; i++) {
    if (nodes[i].flags.includes('jump') && !(t.jumps || []).some((j) => j.node === i || (j.node < i && nodes.slice(j.node, i + 1).every((n) => n.flags.includes('jump')))))
      warn('nodes[' + i + '] jump flag with no jumps entry');
  }

  if (!t.ai || typeof t.ai !== 'object' || !Array.isArray(t.ai.lines) || !Array.isArray(t.ai.pace)) err('ai must have lines[] and pace[]');
  else {
    t.ai.lines.forEach((l, i) => { if (!l || !inRange(l.node) || !isNum(l.x)) err('ai.lines[' + i + '] needs node and x'); });
    if (!t.ai.pace.every(isNum)) err('ai.pace must be numbers');
  }
  const bt = t.beat;
  if (!bt || typeof bt !== 'object') err('beat must be an object');
  else {
    if (bt.bpm !== null && (!isNum(bt.bpm) || bt.bpm <= 0)) err('beat.bpm must be null or > 0');
    if (!isNum(bt.offsetMs)) err('beat.offsetMs must be a number');
    if (!Array.isArray(bt.map)) err('beat.map must be an array');
  }
  const lm = t.limits;
  if (!lm || !isNum(lm.fallY) || !isNum(lm.edgeGraceM) || lm.edgeGraceM < 0 || !isNum(lm.edgeGraceS) || lm.edgeGraceS < 0)
    err('limits needs fallY, edgeGraceM >= 0, edgeGraceS >= 0');
  else if (lm.fallY >= Math.min(...nodes.map((n) => n.p[1]))) err('limits.fallY must be below the lowest node');

  return { ok: errors.length === 0, errors, warnings };
}

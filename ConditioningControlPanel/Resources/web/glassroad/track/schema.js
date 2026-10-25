/* track/schema.js - validation for Track JSON v1 (plan 3.1, CONTRACT.md). No three.js.
 * validate(json) -> { ok, errors: string[], warnings: string[] }. Errors make a track unloadable;
 * warnings (node spacing outside 8..15 m, odd but legal values) do not.
 */

export const FLAGS = Object.freeze(['start', 'rail', 'railL', 'railR', 'open', 'magnet', 'boost', 'jump', 'glide', 'pit', 'flow']);
/* Water wave 1: 'stream' (a road of moving water, optional `flow: [along, lat]` m/s) and 'pool' (still water). */
export const SURFACES = Object.freeze(['glass', 'grip', 'haze', 'stream', 'pool']);
/** The float the road mesh carries per vertex (track/mesh.js `surface` attribute): the index in SURFACES. */
export const surfaceId = (s) => Math.max(0, SURFACES.indexOf(s));
/** Flow bounds (m/s): |along| <= FLOW_MAX[0], |lat| <= FLOW_MAX[1]; lat positive = road right, the query's lateral sign. */
export const FLOW_MAX = Object.freeze([12, 6]);
export const SPACING_M = Object.freeze([8, 15]);
/** Track `items` kinds known to the engine (B1): an item box row member, a pickup bubble. Others warn. */
export const ITEM_KINDS = Object.freeze(['box', 'bubble']);

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
    if (!isNum(n.w) || n.w < 4 || n.w > 240) err(at + '.w must be 4..240 m');
    if (!isNum(n.speed) || n.speed < 0.3 || n.speed > 1.5) err(at + '.speed must be 0.3..1.5');
    if (!SURFACES.includes(n.surface)) err(at + '.surface must be one of ' + SURFACES.join('|'));
    if (n.flow !== undefined) {
      if (!Array.isArray(n.flow) || n.flow.length !== 2 || !n.flow.every(isNum)) err(at + '.flow must be [along, lat] numbers');
      else {
        if (n.surface !== 'stream') err(at + '.flow is only legal on a stream node');
        if (Math.abs(n.flow[0]) > FLOW_MAX[0] || Math.abs(n.flow[1]) > FLOW_MAX[1]) err(at + '.flow outside |along| <= ' + FLOW_MAX[0] + ', |lat| <= ' + FLOW_MAX[1]);
      }
    } else if (n.surface === 'stream') warn(at + ' stream node with no flow');
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
  list('items', (e, at) => {
    if (!isNum(e.x) || !isStr(e.kind)) { err(at + ' needs x and kind'); return; }
    if (Math.abs(e.x) > nodes[e.node].w / 2) err(at + ' off the road');
    if (e.ds !== undefined && (!isNum(e.ds) || e.ds < 0 || e.ds > 40)) err(at + '.ds must be 0..40 m');
    if (!ITEM_KINDS.includes(e.kind)) warn(at + ' unknown kind ' + e.kind);
    if (nodes[e.node].flags.includes('jump')) err(at + ' sits over a jump gap');
  });
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

  // polish 4 drop shortcuts (optional): a rail gap on one side over nodes from..to-1, a forward landing stretch
  if (t.routes !== undefined) {
    if (!Array.isArray(t.routes)) err('routes must be an array');
    else t.routes.forEach((r, i) => {
      const at = 'routes[' + i + ']';
      if (!r || !isStr(r.id)) { err(at + '.id missing'); return; }
      // polish 6: 'gap' is a sideways hop across to a road alongside (the sim keeps its speed)
      if (r.kind !== 'drop' && r.kind !== 'gap') err(at + '.kind must be drop or gap');
      if (r.side !== 'L' && r.side !== 'R') err(at + '.side must be L or R');
      if (!inRange(r.from) || !isInt(r.to) || r.to <= r.from || r.to > N) { err(at + ' needs from < to within the nodes'); return; }
      if (!r.land || !inRange(r.land.from) || !inRange(r.land.to) || r.land.to < r.land.from) { err(at + '.land needs from <= to'); return; }
      const ahead = ((r.land.from - r.from) % N + N) % N;
      if (ahead <= r.to - r.from || ahead >= N / 2) err(at + '.land must lie ahead of the gap, within half a lap');
      if (!r.pad || !isInt(r.pad.node) || r.pad.node < r.land.from || r.pad.node > r.land.to || !isNum(r.pad.x) || Math.abs(r.pad.x) > nodes[r.pad.node].w / 2)
        err(at + '.pad must sit on the landing stretch');
      else if (r.pad.ds !== undefined && (!isNum(r.pad.ds) || r.pad.ds < 0 || r.pad.ds > 15)) err(at + '.pad.ds must be 0..15 m');
      if (r.open !== undefined) {
        const span = Math.hypot(...[0, 1, 2].map((k) => nodes[r.to % N].p[k] - nodes[r.from].p[k]));
        if (!r.open || !isNum(r.open.ds) || !isNum(r.open.len) || r.open.ds < 0 || r.open.len < 2 || r.open.ds + r.open.len > span + 1)
          err(at + '.open needs ds >= 0 and len >= 2 inside the open stretch');
      }
      const shut = r.side === 'L' ? 'railL' : 'railR';
      for (let k = r.from; k < r.to; k++) {
        const fl = nodes[k].flags;
        if (fl.includes('rail') || fl.includes(shut) || fl.includes('jump')) err(at + ' node ' + k + ' must leave the ' + r.side + ' side open');
      }
    });
  }

  if (t.forks !== undefined) {
    if (!Array.isArray(t.forks)) err('forks must be an array');
    else {
      const ids = new Set();
      t.forks.forEach((fork, i) => {
        const at = 'forks[' + i + ']';
        if (!fork || !isStr(fork.id) || ids.has(fork.id)) { err(at + ' needs a unique id'); return; }
        ids.add(fork.id);
        if (!inRange(fork.from) || !inRange(fork.to) || fork.to <= fork.from) { err(at + ' needs main node indices from < to without wrapping'); return; }
        if (!Array.isArray(fork.points) || fork.points.length < 4 || fork.points.length > 256) { err(at + '.points needs 4..256 points'); return; }
        for (const key of ['label', 'kind']) if (fork[key] !== undefined && !isStr(fork[key])) err(at + '.' + key + ' must be a string');
        fork.points.forEach((p, j) => {
          const where = at + '.points[' + j + ']';
          if (!p || !Array.isArray(p.p) || p.p.length !== 3 || !p.p.every(isNum)) { err(where + '.p must be 3 numbers'); return; }
          if (!isNum(p.w) || p.w < 4 || p.w > 120) err(where + '.w must be 4..240 m');
          if (p.roll !== undefined && (!isNum(p.roll) || Math.abs(p.roll) > 60)) err(where + '.roll must be -60..60 degrees');
          if (p.surface !== undefined && !SURFACES.includes(p.surface)) err(where + '.surface is unknown');
          if (p.flags !== undefined && (!Array.isArray(p.flags) || p.flags.some(f => !['rail', 'railL', 'railR', 'open'].includes(f)) || new Set(p.flags).size !== p.flags.length ||
            (p.flags.includes('open') && p.flags.length > 1) || (p.flags.includes('rail') && p.flags.length > 1))) err(where + '.flags must describe ordinary rails or open sides');
          if (p.flow !== undefined && (p.surface !== 'stream' || !Array.isArray(p.flow) || p.flow.length !== 2 || !p.flow.every(isNum) || Math.abs(p.flow[0]) > 12 || Math.abs(p.flow[1]) > 6)) err(where + '.flow requires a bounded stream current');
          const previous = fork.points[j - 1]?.p;
          if (previous?.length === 3 && previous.every(isNum) && Math.hypot(...p.p.map((v, k) => v - previous[k])) < .5) err(where + ' coincides with its previous point');
          if ((j === 0 || j === fork.points.length - 1) && Math.hypot(...p.p.map((v, k) => v - nodes[j === 0 ? fork.from : fork.to].p[k])) > .01) err(where + ' endpoint must match its main node');
        });
      });
    }
  }

  if (t.courts !== undefined) {
    if (!Array.isArray(t.courts)) err('courts must be an array');
    else {
      const ids = new Set();
      t.courts.forEach((c, i) => {
        const at = 'courts[' + i + ']';
        if (!c || !isStr(c.id) || ids.has(c.id)) { err(at + ' needs a unique id'); return; }
        ids.add(c.id);
        if (!inRange(c.from) || !isInt(c.to) || c.to <= c.from || c.to > N) { err(at + ' needs from < to within nodes'); return; }
        if (!Array.isArray(c.holes)) { err(at + '.holes must be an array'); return; }
        const holes = new Set();
        for (const h of c.holes) {
          if (!h || !isStr(h.id) || holes.has(h.id)) { err(at + ' holes need unique ids'); continue; }
          holes.add(h.id);
          if (!inRange(h.node) || h.node < c.from || h.node >= c.to || !isNum(h.x) || !isNum(h.w) || h.w <= 0 || !isNum(h.len) || h.len <= 0 ||
              (h.ds !== undefined && (!isNum(h.ds) || h.ds < 0))) err(at + ' hole ' + h.id + ' needs an in-court node, x, w > 0, len > 0 and ds >= 0');
        }
      });
    }
  }

  // stones wave: fields of stepping stones (track/courts.js buildFields); a stone is anchored like a hole
  if (t.fields !== undefined) {
    if (!Array.isArray(t.fields)) err('fields must be an array');
    else {
      const ids = new Set();
      t.fields.forEach((c, i) => {
        const at = 'fields[' + i + ']';
        if (!c || !isStr(c.id) || ids.has(c.id)) { err(at + ' needs a unique id'); return; }
        ids.add(c.id);
        if (!inRange(c.from) || !isInt(c.to) || c.to <= c.from || c.to > N) { err(at + ' needs from < to within nodes'); return; }
        if (!Array.isArray(c.lanes) || !c.lanes.length || !c.lanes.every(isNum) || c.lanes.some((x, k) => k && x <= c.lanes[k - 1])) err(at + '.lanes must be sorted numbers');
        if (!Array.isArray(c.stones)) { err(at + '.stones must be an array'); return; }
        if (c.platforming !== undefined && typeof c.platforming !== 'boolean') err(at + '.platforming must be boolean');
        const stones = new Set();
        for (const h of c.stones) {
          if (!h || !isStr(h.id) || stones.has(h.id)) { err(at + ' stones need unique ids'); continue; }
          stones.add(h.id);
          const hex = h.shape === 'hex', maxW = c.platforming ? 64 : 32, maxLen = c.platforming ? 80 : hex ? 40 : 32;
          if (h.swing !== undefined && (!hex || !h.spin || !isNum(h.swing) || h.swing <= 0 || h.swing > .25)) err(at + ' bounded swing requires a spinning hex in 0..0.25 radians');
          if (h.spin !== undefined && (!hex || !isNum(h.spin) || Math.abs(h.spin) > 0.12)) err(at + ' stone spin requires a hex and at most 0.12 rad/s');
          if ((h.shape !== undefined && !hex) || !inRange(h.node) || h.node < c.from || h.node >= c.to || !isNum(h.x) || !isNum(h.w) || h.w < 3 || h.w > maxW || !isNum(h.len) || h.len < 3 || h.len > maxLen ||
              (h.ds !== undefined && (!isNum(h.ds) || h.ds < 0))) { err(at + ' stone ' + h.id + ' needs an in-field node, x, valid shape, w in 3..' + maxW + ', len in 3..' + maxLen + ' and ds >= 0'); continue; }
          if (Math.abs(h.x) + h.w / 2 > nodes[h.node].w / 2) err(at + ' stone ' + h.id + ' leaves the road footprint');
        }
      });
    }
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

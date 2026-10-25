/* ============================================================================
 * ui/open-intro.js - the card the page opens on, about 3 s, then the lobby.
 *
 * The ads' end card rebuilt on a 2D canvas over everything: the King's Walk
 * card's gear train with the name wound up through it, the chess ad's pink
 * Fredoka letters, its 1858 serif line and its "Mostly." stamp. The clock and
 * every pose live in ui/open-intro-plan.js (pure, tested in node).
 *
 * It never holds the page: boot carries on underneath (the door, the host's
 * frames, the spectate open), and the card just lifts off whatever is there.
 * Any click, touch or key takes it out fast. Escape too, and that Escape is
 * kept here: it never reaches boot's leave or the door's back step, so the
 * page cannot close under the intro. ui/host-escape.js listens first, so a
 * kept press the panel posts later is still matched to this one.
 *
 * Levels: full, reduced (a calm card fade, nothing turns) and off (Options,
 * "Opening intro"). Gentle visuals play the full card slower (prefs().introSpeed).
 * ==========================================================================*/

import { onHostMessage, isHosted } from '../bridge.js';
import {
  OPEN, openLevel, openPlan, hurried, letterPose, gearTrain, lineState, stampState, cardState, backdrop,
  clicksBetween, cuesBetween, openOver, smooth,
} from './open-intro-plan.js';

const TITLE = 'Piece by Piece', LINE = "It's still chess.", STAMP = 'Mostly.';
const PINK = '#ff5fa2', HOT = '#ff2d8a', CREAM = '#fbf1e2', INK = '#150a20', BG = '#1A1A3E';
const TAU = Math.PI * 2;
const FACE = 'PbpFredoka';
const game = (px) => `700 ${Math.round(px)}px ${FACE}, 'Segoe UI', system-ui, sans-serif`;
const serif = (px) => `italic 500 ${Math.round(px)}px 'Playfair Display', Georgia, 'Times New Roman', serif`;
const WAIT = 0.4;   // seconds the first frame may hold for the host's settings and the font

function gearPath(g, teeth, rRoot, rTip, phase) {
  const step = TAU / teeth;
  g.beginPath();
  for (let i = 0; i < teeth; i++) {
    const a = phase + i * step;
    for (const [r, an] of [[rRoot, a], [rRoot, a + step * 0.22], [rTip, a + step * 0.34], [rTip, a + step * 0.66], [rRoot, a + step * 0.78]])
      g.lineTo(Math.cos(an) * r, Math.sin(an) * r);
  }
  g.closePath();
}

/** One brass wheel: twelve teeth, four windows, a dark hub (the King's Walk card's gear). */
function drawGear(g, x, y, R, phase, alpha) {
  if (alpha <= 0.003) return;
  g.save(); g.translate(x, y); g.globalAlpha = alpha;
  gearPath(g, 12, R * 0.86, R * 1.06, phase);
  const gr = g.createLinearGradient(0, -R, 0, R);
  gr.addColorStop(0, '#ffe29a'); gr.addColorStop(0.45, '#e2a544'); gr.addColorStop(1, '#8a5a1c');
  g.fillStyle = gr; g.shadowColor = 'rgba(0,0,0,.55)'; g.shadowBlur = R * 0.25; g.shadowOffsetY = R * 0.08; g.fill();
  g.shadowColor = 'transparent'; g.lineWidth = Math.max(1.5, R * 0.07); g.strokeStyle = '#3a2208'; g.lineJoin = 'round'; g.stroke();
  g.rotate(phase);
  g.fillStyle = '#2a1608'; g.beginPath();
  for (let k = 0; k < 4; k++) {
    const a0 = k * TAU / 4 + 0.22, a1 = (k + 1) * TAU / 4 - 0.22;
    g.moveTo(Math.cos(a0) * R * 0.3, Math.sin(a0) * R * 0.3); g.arc(0, 0, R * 0.66, a0, a1); g.arc(0, 0, R * 0.3, a1, a0, true); g.closePath();
  }
  g.fill();
  g.fillStyle = '#3a2208'; g.beginPath(); g.arc(0, 0, R * 0.1, 0, TAU); g.fill();
  g.restore();
}

/** One letter of the name: ink rim, the pink-to-violet fill, a pink glow (the ads' card). */
function drawLetter(g, ch, x, y, px, sx, sy) {
  g.save(); g.translate(x, y); g.scale(sx, sy);
  g.font = game(px); g.textAlign = 'center'; g.textBaseline = 'alphabetic';
  g.lineJoin = 'round'; g.lineWidth = px * 0.14; g.strokeStyle = INK; g.strokeText(ch, 0, 0);
  const gr = g.createLinearGradient(0, -px * 0.78, 0, px * 0.05);
  gr.addColorStop(0, '#ffe3f4'); gr.addColorStop(0.28, '#ff9fd0'); gr.addColorStop(0.62, PINK); gr.addColorStop(1, '#b45cff');
  g.shadowColor = 'rgba(255,60,160,.75)'; g.shadowBlur = px * 0.28; g.fillStyle = gr; g.fillText(ch, 0, 0);
  // no white highlight stroke: on Fredoka it traces the seams where the contours overlap (AD-PLAYBOOK, text)
  g.restore();
}

export function createOpenIntro({
  prefs = () => ({}), sfx = () => null, root = document.body, holdAt = null, win = window, doc = document,
} = {}) {
  const p0 = prefs() || {};
  const levelOf = (p) => openLevel({ enabled: p0.openIntro !== false, reduced: !!p.reducedMotion, gentle: p.introSpeed || 0 });
  if (levelOf(p0) === 'off') return null;

  const cv = doc.createElement('canvas');
  cv.className = 'pbp-open';
  cv.setAttribute('aria-hidden', 'true');
  root.appendChild(cv);
  const g = cv.getContext('2d');
  if (!g) { cv.remove(); return null; }

  // The name's own face, carried in the page (assets/fonts): the card waits a moment for it, never long.
  let fontReady = false;
  try {
    const face = new FontFace(FACE, `url(${new URL('../assets/fonts/fredoka-latin.woff2', import.meta.url).href})`, { weight: '300 700' });
    doc.fonts.add(face);
    face.load().then(() => { fontReady = true; lay = null; }, () => { fontReady = true; });
  } catch { fontReady = true; }

  const letters = [...TITLE].map((ch, i) => ({ ch, i })).filter(l => l.ch !== ' ');
  let plan = null, level = null, speed = 1;
  let t = 0, skipAt = null, waited = 0, settingsSeen = !isHosted, wantHurry = false;
  let lay = null, W = 0, H = 0, dpr = 1, raf = 0, last = 0, done = false, outCue = false;

  function setLevel() {
    const p = prefs() || {};
    const next = levelOf(p);
    if (next !== level) {
      // a card already on its way when the host says "reduced" stays up and fades out calmly;
      // a calm card already up never turns into the clockwork
      if (!level || t <= 0.2) { level = next; plan = openPlan(lay.at, level); }
      else if (next === 'reduced') { level = next; plan = openPlan(lay.at, level); t = OPEN.reduced.in; }
    }
    speed = level === 'full' && p.introSpeed > 0 ? p.introSpeed : 1;
    if (wantHurry) plan = hurried(plan, t);
  }

  /** Size the card to the window: the name fits 82% of the width, never taller than a fifth of the height. */
  function layout() {
    dpr = Math.min(2, win.devicePixelRatio || 1);
    W = win.innerWidth || 1; H = win.innerHeight || 1;
    cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr);
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.font = game(100);
    const w100 = g.measureText(TITLE).width || 600;
    const px = Math.max(24, Math.min(H * 0.2, W * 0.82 * 100 / w100, 230));
    g.font = game(px);
    const w = g.measureText(TITLE).width;
    const xs = letters.map(l => {
      const a = g.measureText(TITLE.slice(0, l.i)).width, b = g.measureText(TITLE.slice(0, l.i + 1)).width;
      return (a + b) / 2;
    });
    const lpx = px * 0.3, mpx = px * 0.36;
    g.font = serif(lpx); const lw = g.measureText(LINE).width;
    g.font = game(mpx); const mw = g.measureText(STAMP).width;
    const base = H * 0.5 + px * 0.12;   // the line and the stamp fit under the name at any width: they are smaller
    lay = {
      px, w, base, x0: W / 2 - w / 2, xs, lpx, mpx, lw, mw,
      at: letters.map((l, i) => ({ ch: l.ch, at: xs[i] / w })),
      lineY: base + px * 0.62, R: px * 0.3,
    };
    if (plan) plan = openPlan(lay.at, level);
    if (plan && wantHurry) plan = hurried(plan, t);
  }

  function draw() {
    if (!lay || win.innerWidth !== W || win.innerHeight !== H) layout();
    const L = lay;
    g.setTransform(dpr, 0, 0, dpr, 0, 0);
    g.clearRect(0, 0, W, H);
    // the dark: the page's own navy, darker at the edges
    const bk = backdrop(plan, t, skipAt);
    if (bk > 0.002) {
      g.globalAlpha = bk;
      g.fillStyle = BG; g.fillRect(0, 0, W, H);
      const v = g.createRadialGradient(W / 2, H / 2, Math.min(W, H) * 0.2, W / 2, H / 2, Math.hypot(W, H) * 0.6);
      v.addColorStop(0, 'rgba(12,10,30,0)'); v.addColorStop(1, 'rgba(8,6,20,.85)');
      g.fillStyle = v; g.fillRect(0, 0, W, H);
      g.globalAlpha = 1;
    }
    const card = cardState(plan, t, skipAt);
    if (card.a <= 0.002) return;
    g.save();
    g.globalAlpha = card.a;
    // the OUT: up and a little bigger as it goes; the stamp's knock on the way in
    const s = 1 + card.kick + card.lift * 0.06;
    g.translate(W / 2, H / 2 - card.lift * H * 0.06); g.scale(s, s); g.translate(-W / 2, -H / 2);
    // a pink bloom behind the name, swelling as it lands
    const tl = Math.min(t, skipAt ?? t);
    const glow = plan.level === 'reduced' ? 0.45 : smooth((tl - OPEN.land + 0.25) / 0.3) * (0.45 + 0.4 * Math.exp(-Math.max(0, tl - OPEN.land) * 4));
    if (glow > 0.01) {
      const gl = g.createRadialGradient(W / 2, L.base - L.px * 0.35, 0, W / 2, L.base - L.px * 0.35, L.w * 0.62);
      gl.addColorStop(0, `rgba(255,95,162,${0.26 * glow})`); gl.addColorStop(1, 'rgba(255,95,162,0)');
      g.fillStyle = gl; g.fillRect(0, 0, W, H);
    }
    // the gear train: meshed wheels on one pitch along the axle line, a little wider than the name
    const gt = gearTrain(plan, t, skipAt);
    const R = L.R, pitch = R * 1.93, nG = Math.ceil((L.w + L.px * 0.3) / pitch), gy = L.base + R * 0.25;
    // letters show only above the axle line while the train is up
    g.save();
    if (gt.k > 0.003) { g.beginPath(); g.rect(-W, -H, W * 3, gy + H + (tl > OPEN.land ? (1 - gt.k) * L.px : 0)); g.clip(); }
    plan.letters.forEach((l, i) => {
      const pose = letterPose(l, t, plan.level, skipAt);
      if (pose.hidden) return;
      drawLetter(g, l.ch, L.x0 + L.xs[i], L.base + pose.drop * L.px * 0.9, L.px, pose.sx, pose.sy);
    });
    g.restore();
    if (gt.k > 0.003) {
      const gx0 = W / 2 - (nG - 1) * pitch / 2, gyy = gy + (1 - gt.k) * R * 2.6;
      for (let i = 0; i < nG; i++) drawGear(g, gx0 + i * pitch, gyy, R, (i % 2 ? -1 : 1) * gt.spin + (i % 2 ? TAU / 24 : 0), Math.min(1, gt.k * 1.6));
    }
    // the line, in the 1858 serif, wiped in left to right; then the stamp in the game's voice
    const ln = lineState(plan, t, skipAt), st = stampState(plan, t, skipAt);
    const lx = W / 2 - (L.lw + L.lpx * 0.4 + L.mw) / 2;
    if (ln.k > 0.002) {
      g.save();
      g.beginPath(); g.rect(lx - 4, L.lineY - L.lpx * 1.4, (L.lw + 8) * ln.k, L.lpx * 2.2); g.clip();
      g.globalAlpha *= Math.min(1, ln.k * 1.4);
      g.font = serif(L.lpx); g.textAlign = 'left'; g.textBaseline = 'alphabetic';
      g.shadowColor = 'rgba(8,2,14,.6)'; g.shadowBlur = L.lpx * 0.5; g.fillStyle = CREAM;
      g.fillText(LINE, lx, L.lineY + (1 - ln.k) * 8);
      g.restore();
    }
    if (st.a > 0.002) {
      g.save(); g.translate(lx + L.lw + L.lpx * 0.4 + L.mw / 2, L.lineY - L.lpx * 0.3); g.rotate(st.rot); g.scale(st.scale, st.scale); g.globalAlpha *= st.a;
      g.font = game(L.mpx); g.textAlign = 'center'; g.textBaseline = 'middle'; g.lineJoin = 'round';
      g.lineWidth = L.mpx * 0.16; g.strokeStyle = INK; g.strokeText(STAMP, 0, 0);
      g.shadowColor = HOT; g.shadowBlur = L.mpx * 0.4; g.fillStyle = PINK; g.fillText(STAMP, 0, 0);
      g.shadowBlur = 0; g.fillStyle = '#ffd0e8'; g.fillText(STAMP, 0, -L.mpx * 0.03);
      g.restore();
    }
    g.restore();
  }

  function play(name, opts) { try { sfx()?.play?.(name, opts); } catch { /* sound is optional */ } }

  function sounds(a, b) {
    if (skipAt != null || plan.level !== 'full') return;
    if (clicksBetween(plan, a, b) > 0) play('tick');
    for (const c of cuesBetween(plan, a, b)) play(c === 'land' ? 'land' : 'promote', c === 'land' ? { height: 1.2 } : undefined);
    if (!outCue && b >= plan.outAt) { outCue = true; play('cardClose'); }
  }

  function frame(now) {
    raf = 0;
    if (done) return;
    const dt = Math.min(0.25, Math.max(0, (now - (last || now)) / 1000));   // a hitch jumps the card on, never stretches it
    last = now;
    if (holdAt != null) t = holdAt;
    else if (skipAt != null) t += dt;                    // a skip goes out at its own pace, gentle or not
    else if (!(settingsSeen && fontReady) && waited < WAIT) waited += dt;   // the first frame holds, briefly
    else { const a = t; t += dt * speed; sounds(a, t); }
    if (holdAt == null && openOver(plan, t, skipAt)) { finish(); return; }
    draw();
    raf = win.requestAnimationFrame(frame);
  }

  /** Take it out fast, from wherever it is. */
  function skip() {
    if (done || skipAt != null) return;
    skipAt = t;
    cv.style.pointerEvents = 'none';
  }

  /** A spectate open: the name lands, then the card goes, with no line and no stamp. */
  function hurry() {
    wantHurry = true;
    if (plan) plan = hurried(plan, t);
  }

  function finish() {
    if (done) return;
    done = true;
    if (raf) win.cancelAnimationFrame(raf);
    for (const fn of offs) fn();
    cv.remove();
  }

  // Any key skips; every key is kept while the card is up (the player cannot see what it would hit),
  // Escape above all: it must never reach boot's leave or the door's back step from here.
  const onKey = (e) => {
    if (done) return;
    if (holdAt == null) skip();
    e.stopImmediatePropagation();
    // nor does its default happen (Enter on a door button under the card); the browser's own chords still work
    if (e.key === 'Escape' || !(e.ctrlKey || e.metaKey || e.altKey || /^F\d+$/.test(e.key))) e.preventDefault();
  };
  const onPress = (e) => { if (holdAt == null) skip(); e.stopPropagation(); };
  win.addEventListener('keydown', onKey, true);
  cv.addEventListener('pointerdown', onPress);
  const offs = [
    () => win.removeEventListener('keydown', onKey, true),
    () => cv.removeEventListener('pointerdown', onPress),
    // the host's settings decide the level (reduced, gentle); its spectate open shortens the card
    onHostMessage((m) => {
      if (!m || m.type !== 'pbp:settings') return;
      settingsSeen = true;
      if (m.spectateMatchId) hurry();
      // preferences.js folds this frame in first (it listened first); read the result next tick
      Promise.resolve().then(() => { if (!done) setLevel(); });
    }),
  ];

  layout();
  setLevel();
  draw();
  raf = win.requestAnimationFrame(frame);

  return {
    skip,
    hurry,
    active: () => !done,
    state: () => (done ? null : { level, t: +t.toFixed(3), total: +plan.total.toFixed(3), speed, skipped: skipAt != null }),
    dispose: finish,
  };
}

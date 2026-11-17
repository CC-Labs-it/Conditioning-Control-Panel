/* sheet/fx.js - the buy moment and the payout, ported from the owner's mockup (fly, flyOut, sparkAt): stars leave the
 * wallet and land on the item (a spend), or puff out of the gift box and stream into the wallet (a gain), each landing
 * a gold ring and a few motes, with sheet/sound.js bells. In sheet mode the wallet is the host's, just above the page,
 * so the stars aim at the top right edge. Motion off: no flight (the caller rings one bell). Less or Gentle: half the stars.
 * at / ink / confetti serve sheet/juice.js (the OWNED stamp's hit, a stamp press, the coupon); Gentle gets no confetti.
 * Uses Web Animations; a page without them (the tests' fake DOM) gets no flight and the same outcome. */

export function createFx({ root, doc, base = './', motion = 'full', gentle = false, sound, wallet, walletAt = () => null, onWallet = () => {}, onStars = null }) {
  // THE LAST LEG (CONTRACT "Sheet additions", wallet-stars): the page cannot draw over the host's bar (its HWND ends at
  // the sheet's top edge), so with a host wallet it also hands the host every star's whole path, once, as the flight
  // starts. The host draws the same path in its own layer (the part over the page is hidden under the page), counts
  // its chip as the stars land (a gain) or leave (a spend), and the page sends no wallet-land for that flight.
  // onStars(plan) answers true when the host took it.
  const handOff = (dir, amount, ease, tracks) => {
    if (typeof onStars !== 'function' || !walletAt() || !tracks.length) return false;
    const h = host(), r1 = (v) => Math.round(v * 10) / 10, r3 = (v) => Math.round(v * 1000) / 1000;
    const stars = tracks.map((t) => ({ d: Math.round(t.d), ms: Math.round(t.ms),
      k: t.k.map(([o, x, y, s, rot, a]) => [r3(o), r1(x + h.left + 13), r1(y + h.top + 13), r3(s), Math.round(rot), r3(a)]) }));
    try { return onStars({ dir, n: Math.max(0, Math.round(amount)), e: ease, stars }) === true; } catch (e) { return false; }
  };
  const can = (n) => motion !== 'off' && n && typeof n.animate === 'function' && typeof n.getBoundingClientRect === 'function';
  // In a sheet the host says where its wallet star is (init.walletAt / wallet-at, page CSS pixels) and hears the
  // stars reach or leave it (onWallet(k), k = share so far, 1 = the last): its chip counts and kicks.
  const hostWallet = (h) => { const p = walletAt(); return p && Number.isFinite(p.x) && Number.isFinite(p.y) ? { x: p.x - h.left - 13, y: p.y - h.top - 13 } : null; };
  const few = motion === 'less' || gentle ? 0.5 : 1;
  const el = (tag, cls) => { const n = doc.createElement(tag); n.className = cls; return n; };
  const host = () => root.getBoundingClientRect();
  function centre(node) {
    const h = host();
    if (!node || node.hidden || !node.getBoundingClientRect().width) return hostWallet(h) || { x: h.width - 70 - 13, y: 4 };   // the host's wallet, above the sheet
    const a = node.getBoundingClientRect();
    return { x: a.left + a.width / 2 - h.left - 13, y: a.top + a.height / 2 - h.top - 13 };
  }
  const walletStar = () => { const w = wallet(); return w && !w.hidden ? w.querySelector('img') || w : null; };
  /** Where stars leave from and land: the host's real wallet chip when it said where (a sheet), else the page's own. */
  const walletPoint = () => hostWallet(host()) || centre(walletStar());
  function star() { const s = el('img', 'flystar'); s.src = `${base}star.png`; s.alt = ''; root.append(s); return s; }
  function sparkAt(x, y, big) {   // where a star lands: a gold ring and a few motes
    const r = el('i', 'ping' + (big ? ' big' : '')); r.style.left = x + 'px'; r.style.top = y + 'px'; root.append(r); setTimeout(() => r.remove(), 800);
    for (let k = big ? 12 : 4, i = 0; i < k; i++) {
      const m = el('i', 'mote'), a = Math.random() * 6.283, d = (big ? 26 : 12) + Math.random() * (big ? 30 : 14);
      m.style.left = x + 'px'; m.style.top = y + 'px'; root.append(m);
      m.animate([{ transform: 'translate(0,0) scale(1)', opacity: 1 }, { transform: `translate(${Math.cos(a) * d}px,${Math.sin(a) * d + 6}px) scale(.2)`, opacity: 0 }],
        { duration: 360 + Math.random() * 300, easing: 'cubic-bezier(.1,.7,.3,1)', fill: 'both' }).onfinish = () => m.remove();
    }
  }
  /** The centre of a node, in the sheet's own coordinates (for the juice's ink rings and confetti). */
  function at(node) { const h = host(), a = node.getBoundingClientRect(); return { x: a.left + a.width / 2 - h.left, y: a.top + a.height / 2 - h.top }; }
  /** An ink ring where a stamp hits: one ring, scaling out and fading (Gentle: a fade in place). small = a stamp-card dot. */
  function ink(x, y, small) {
    if (!can(root)) return;
    const r = el('i', 'j-ink' + (small ? ' small' : ''));
    r.style.left = x + 'px'; r.style.top = y + 'px'; root.append(r);
    const s = gentle ? [1, 1] : motion === 'less' ? [0.6, 1.4] : [0.4, small ? 1.6 : 2.2];
    r.animate([{ transform: `scale(${s[0]})`, opacity: 0.85 }, { transform: `scale(${s[1]})`, opacity: 0 }], { duration: gentle ? 420 : small ? 460 : 620, easing: 'cubic-bezier(.15,.7,.3,1)', fill: 'both' }).onfinish = () => r.remove();
  }
  /** A few confetti bits in a colour (the shelf's), thrown up and falling with a turn. Gentle and off: none. */
  function confetti(x, y, colour, count) {
    if (!can(root) || gentle || !(count > 0)) return;
    for (let i = 0; i < count; i++) {
      const c = el('i', 'j-conf' + (i % 3 === 0 ? ' alt' : '')), a = -Math.PI / 2 + (Math.random() - 0.5) * 2.6, d = (40 + Math.random() * 70) * (motion === 'less' ? 0.5 : 1);
      if (colour) c.style.setProperty('--c', colour);
      c.style.left = x + 'px'; c.style.top = y + 'px'; root.append(c);
      const ux = Math.cos(a) * d, uy = Math.sin(a) * d, spin = (Math.random() - 0.5) * 720;
      c.animate([
        { transform: 'translate(0,0) rotate(0deg) scale(.6)', opacity: 1 },
        { transform: `translate(${ux}px,${uy}px) rotate(${spin * 0.5}deg) scale(1)`, opacity: 1, offset: 0.38 },
        { transform: `translate(${ux * 1.25}px,${uy + 46 + Math.random() * 30}px) rotate(${spin}deg) scale(.9)`, opacity: 0 },
      ], { duration: 900 + Math.random() * 400, delay: Math.random() * 60, easing: 'cubic-bezier(.2,.6,.4,1)', fill: 'both' }).onfinish = () => c.remove();
    }
  }
  return {
    sparkAt, at, ink, confetti,
    /** A spend: stars leave the wallet in a dense stream, arc high and land on the item, each with a small pop where it
     * lands (the owner: "slower, more dense, a small animation when they land"). Answers how long that takes (0 = none). */
    flyOut(toEl, amount) {
      if (!can(toEl) || !can(root)) { if (amount > 0) onWallet(1); return 0; }   // no flight: the number just changes
      const s0 = walletPoint(), box = toEl.getBoundingClientRect(), e0 = centre(toEl);
      const n = Math.max(8, Math.round(Math.max(16, Math.min(32, Math.round(amount / 20))) * few));
      let longest = 0;
      // Out of the host's chip the arc stays low: the bar is short and the window's top edge cuts a high one.
      const low = !!hostWallet(host());
      const tracks = [], go = [];
      for (let i = 0; i < n; i++) {
        const size = i % 4 === 0 ? 0.85 : 0.34 + Math.random() * 0.3, dx = (Math.random() - 0.5) * box.width * 0.7, dy = (Math.random() - 0.5) * box.height * 0.6, spin = (Math.random() - 0.5) * 480;
        const ex = e0.x + dx, ey = e0.y + dy, side = (Math.random() - 0.5) * 160;
        const ax = s0.x + (ex - s0.x) * 0.3 + side, ay = Math.min(s0.y, ey) - (low ? 8 + Math.random() * 14 : 40 + Math.random() * 60), bx = s0.x + (ex - s0.x) * 0.7 + side * 0.4, by = ay + (ey - ay) * 0.35;
        const dur = 1250 + Math.random() * 450, delay = i * Math.min(52, 1100 / n) + Math.random() * 30; longest = Math.max(longest, dur + delay);
        const k = [[0, s0.x, s0.y, size * 0.3, 0, 0], [0.12, s0.x + (ax - s0.x) * 0.25, s0.y + (ay - s0.y) * 0.25, size * 1.05, spin * 0.1, 1],
          [0.42, ax, ay, size * 1.1, spin * 0.4, 1], [0.7, bx, by, size, spin * 0.7, 1], [0.9, ex, ey, size * 0.8, spin, 1],
          [1, ex, ey - 4, size * (gentle ? 0.8 : 1.4), spin, 0]];   // the last: the landing, a little pop
        tracks.push({ d: delay, ms: dur, k });
        go.push({ i, ex, ey, k, dur, delay });
      }
      // A spend: the host's chip counts down as its stars leave (it took the flight), else at once as before.
      if (!handOff('out', amount, [0.45, 0, 0.3, 1], tracks) && amount > 0) onWallet(1);
      sound.air(0.7, 0.045, false);
      for (const { i, ex, ey, k, dur, delay } of go) {
        const s = star();
        s.animate(k.map(([offset, x, y, sc, rot, opacity]) => ({ transform: `translate(${x}px,${y}px) scale(${sc}) rotate(${rot}deg)`, opacity, offset })),
          { duration: dur, delay, easing: 'cubic-bezier(.45,0,.3,1)', fill: 'both' }).onfinish = () => {
          s.remove(); if (i % 2 === 0 || i === n - 1) sparkAt(ex + 13, ey + 13, false); sound.tick(1 - i / n);
          if (i % 5 === 4 && toEl.isConnected && !gentle) toEl.animate([{ scale: '1' }, { scale: motion === 'less' ? '1.006' : '1.012', offset: 0.35 }, { scale: '1' }], { duration: 220, easing: 'ease-out' });
        };
      }
      return longest;
    },
    /** A gain: stars puff out of `fromEl`, mostly upward, then stream into the wallet; onLand(k) after each (k = 0..1). */
    fly(fromEl, amount, onLand = () => {}) {
      if (!can(fromEl) || !can(root)) { sound.bell(sound.NOTES[3], 0.12, 1); onLand(1); onWallet(1); return 0; }
      const s0 = centre(fromEl), e0 = walletPoint();
      const n = Math.max(6, Math.round(Math.max(10, Math.min(30, 8 + Math.round(amount * 0.9))) * few));
      let landed = 0, longest = 0;
      const tracks = [];
      for (let i = 0; i < n; i++) {
        const size = i % 4 === 0 ? 0.78 + Math.random() * 0.2 : 0.34 + Math.random() * 0.3;   // a few lead stars, the rest fine dust
        const a = -Math.PI / 2 + (Math.random() - 0.5) * 3.6, burst = 34 + Math.random() * 70, bx = s0.x + Math.cos(a) * burst, by = s0.y + Math.sin(a) * burst * 0.8;
        const sway = (Math.random() - 0.5) * 150, mx = bx + (e0.x - bx) * 0.55 + sway * 0.35, my = by + (e0.y - by) * 0.55 - Math.abs(sway) * 0.3, spin = (Math.random() - 0.5) * 540;
        const dur = 1150 + Math.random() * 420, delay = i * 44 + Math.random() * 40; longest = Math.max(longest, dur + delay);
        tracks.push({ d: delay, ms: dur, k: [[0, s0.x, s0.y, size * 0.3, 0, 0], [0.2, bx, by, size * 1.15, spin * 0.2, 1],
          [0.62, mx, my, size, spin * 0.6, 1], [1, e0.x, e0.y, size * 0.55, spin, 1]] });
      }
      // A gain: the host's chip counts up as its own copy of each star lands (it took the flight), else on wallet-land.
      const hosted = handOff('in', amount, [0.42, 0, 0.3, 1], tracks);
      sound.air(0.55, 0.05, true);
      for (const { d: delay, ms: dur, k } of tracks) {
        const s = star();
        s.animate(k.map(([offset, x, y, sc, rot, opacity]) => ({ transform: `translate(${x}px,${y}px) scale(${sc}) rotate(${rot}deg)`, opacity, offset })),
          { duration: dur, delay, easing: 'cubic-bezier(.42,0,.3,1)', fill: 'both' }).onfinish = () => {
          s.remove(); landed++;
          if (landed === n) { sparkAt(e0.x + 13, e0.y + 13, true); sound.chord(); } else { if (landed % 2) sparkAt(e0.x + 13, e0.y + 13, false); sound.tick(landed / n); }
          onLand(landed / n);
          if (!hosted && (landed === n || landed % 3 === 0)) onWallet(landed / n);
        };
      }
      return longest;
    },
  };
}

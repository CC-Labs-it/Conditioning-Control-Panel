/* sheet/looks.js - the screen-effect looks drawn on canvas, ported from the owner's mockup (prize-counter-v4.html): card
 * backs, the Deck card front, word styles with their own motion, fog colours (and the two-tone swirl), the whisper that
 * leaves from behind a flipping card. Pure drawing on a 2D context; time goes in, nothing is kept here. */

export const FOGS = { pink: '#ff5fa8', lilac: '#a98bff', mint: '#5fe0b8', amber: '#ffb347' };
export const WORDS = ['let go', 'sink', 'good', 'deeper', 'soft', 'obey', 'blank'];
const TAU = Math.PI * 2;
export const ease = (x) => { x = Math.max(0, Math.min(1, x)); return 1 - Math.pow(1 - x, 3); };
export const hueOf = (id) => { const s = String(id).split('.').pop() + '..'; return (s.charCodeAt(0) * 37 + s.charCodeAt(1) * 61) % 360; };

export function drawBack(g, kind, x, y, w, h) {
  g.save(); g.beginPath(); g.rect(x, y, w, h); g.clip();
  g.fillStyle = kind === 'plain' ? '#ff5fa8' : kind === 'lace' ? '#f6e9f0' : kind === 'ink' ? '#f1efe6' : '#17202b'; g.fillRect(x, y, w, h);
  if (kind === 'ink') { g.strokeStyle = '#17202b'; g.lineWidth = 1; for (let i = -h; i < w; i += 6) { g.beginPath(); g.moveTo(x + i, y + h); g.lineTo(x + i + h, y); g.stroke(); } }
  else if (kind === 'check') { const s = Math.max(6, w / 6); g.fillStyle = '#ff5fa8'; for (let cy = 0; cy * s < h; cy++) for (let cx = 0; cx * s < w; cx++) if ((cx + cy) % 2 === 0) g.fillRect(x + cx * s, y + cy * s, s, s); }
  else if (kind === 'lace') { g.fillStyle = '#d98bb0'; for (let ly = 5; ly < h; ly += 9) for (let lx = 5 + ((ly / 9) % 2) * 4.5; lx < w; lx += 9) { g.beginPath(); g.arc(x + lx, y + ly, 1.8, 0, TAU); g.fill(); } g.strokeStyle = '#d98bb0'; g.lineWidth = 1.5; g.strokeRect(x + 3, y + 3, w - 6, h - 6); }
  g.restore(); g.strokeStyle = '#ffffff80'; g.lineWidth = 1; g.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1);
}
/** A Deck card's face: a picture stand-in (a sky, a sun, hills) in one hue. */
export function drawFront(g, x, y, w, h, hue) {
  const gr = g.createLinearGradient(x, y, x + w, y + h); gr.addColorStop(0, `hsl(${hue},55%,62%)`); gr.addColorStop(1, `hsl(${hue + 40},50%,38%)`);
  g.fillStyle = gr; g.fillRect(x, y, w, h);
  g.fillStyle = '#ffffff8c'; g.beginPath(); g.arc(x + w * 0.72, y + h * 0.3, Math.min(w, h) * 0.11, 0, TAU); g.fill();
  g.beginPath(); g.moveTo(x, y + h); g.lineTo(x + w * 0.38, y + h * 0.5); g.lineTo(x + w * 0.62, y + h * 0.78); g.lineTo(x + w * 0.8, y + h * 0.6); g.lineTo(x + w, y + h); g.closePath(); g.fillStyle = '#14101973'; g.fill();
}
/* Local or system fonts only: Segoe Print for chalk, Consolas for the typewriter, Rockwell for the marquee. */
export function wordFont(kind, px) {
  if (kind === 'chalk') return `600 ${px * 1.15}px "Segoe Print", "Comic Sans MS", cursive`;
  if (kind === 'type') return `500 ${px * 0.82}px Consolas, "Cascadia Mono", "Courier New", monospace`;
  if (kind === 'marquee') return `700 ${px * 0.9}px Rockwell, "Rockwell Extra Bold", Georgia, serif`;
  return `700 ${px}px "Segoe UI", system-ui, sans-serif`;
}
export const wordColour = (kind) => (kind === 'marquee' ? '#ffd24a' : kind === 'chalk' ? '#fdf7e8' : '#ffffff');
/** One word in its style. k = seconds into a 4 s life (the style's own motion: typed, written on, rising, echoing, lit). */
export function drawWord(g, kind, text, cx, cy, px, k, strength) {
  if (kind === 'marquee') text = text.toUpperCase();
  const out = k < 2.8 ? 1 : k < 3.5 ? 1 - (k - 2.8) / 0.7 : 0, A = strength == null ? 0.92 : strength;
  if (out <= 0) return;
  g.save(); g.font = wordFont(kind, px); g.textBaseline = 'middle'; g.fillStyle = wordColour(kind); g.textAlign = 'left';
  const full = g.measureText(text).width, x0 = cx - full / 2;
  if (kind === 'type') {
    const shown = text.slice(0, Math.ceil(text.length * Math.min(1, k / 1.0)));
    g.globalAlpha = A * out; g.fillText(shown, x0, cy);
    if (k < 2.8 && Math.floor(k * 3) % 2 === 0) g.fillRect(x0 + g.measureText(shown).width + 2, cy - px * 0.38, px * 0.08, px * 0.76);
  } else if (kind === 'chalk') {
    g.translate(cx, cy); g.rotate(-0.035); g.translate(-cx, -cy);
    g.beginPath(); g.rect(x0 - 4, cy - px, (full + 8) * Math.min(1, k / 1.1), px * 2); g.clip(); g.globalAlpha = A * out; g.fillText(text, x0, cy);
  } else if (kind === 'marquee' || kind === 'drift') {
    let x = x0;
    for (let i = 0; i < text.length; i++) {
      const ch = text[i], wch = g.measureText(ch).width;
      if (kind === 'marquee') { const lit = k > 0.1 + i * 0.13, allAt = 0.1 + text.length * 0.13, blink = k > allAt + 0.15 && k < allAt + 0.27; g.globalAlpha = A * out * (lit ? (blink ? 0.45 : 1) : 0.2); g.fillText(ch, x, cy); }
      else { const e = ease((k - i * 0.07) / 0.55); g.globalAlpha = A * out * e; g.fillText(ch, x, cy + (1 - e) * px * 0.55 - (1 - out) * px * 0.4); }
      x += wch;
    }
  } else if (kind === 'echo') {
    g.textAlign = 'center';
    for (let i = 1; i <= 2; i++) { const fr = (k * 0.45 + i * 0.5) % 1; g.save(); g.translate(cx, cy); g.scale(1 + fr * 0.4, 1 + fr * 0.4); g.globalAlpha = A * out * (1 - fr) * 0.35; g.fillText(text, 0, 0); g.restore(); }
    g.globalAlpha = A * out * Math.min(1, k / 0.3); g.fillText(text, cx, cy);
  } else { g.textAlign = 'center'; g.globalAlpha = A * out * Math.min(1, k / 0.5); g.fillText(text, cx, cy); }
  g.restore();
}
/** Fog blobs drifting: one colour, or the two-tone swirl (cols = several). */
export function drawFogBlobs(k, cols, t, w, h, alpha = 0.55) {
  for (let i = 0; i < 5; i++) {
    const bx = w / 2 + Math.sin(t * (0.3 + i * 0.07) + i * 2.1) * w * 0.22, by = h * 0.45 + Math.cos(t * (0.25 + i * 0.05) + i * 1.3) * h * 0.18, rr = Math.max(w, h) * (0.3 + 0.06 * Math.sin(t * 0.4 + i));
    const bl = k.createRadialGradient(bx, by, 0, bx, by, rr); bl.addColorStop(0, cols[i % cols.length]); bl.addColorStop(1, 'rgba(0,0,0,0)');
    k.globalAlpha = alpha; k.fillStyle = bl; k.fillRect(0, 0, w, h);
  }
  k.globalAlpha = 1;
}
/** The whisper: small behind the card as it turns, then past it and thinning out toward you. front = draw the near half. */
export function drawWhisper(g, word, age, x, y, cw, ch, front, style = 'classic') {
  const dur = 1.9;
  if (age > dur || (age >= 0.75) !== front) return;
  const s = 0.55 + ease(age / dur) * 0.95, a = age < 0.3 ? age / 0.3 * 0.6 : age < 1.1 ? 0.6 : 0.6 * (1 - (age - 1.1) / 0.8);
  const txt = style === 'marquee' ? word.toUpperCase() : word;
  g.save(); g.translate(x + cw / 2, y + ch / 2); g.scale(s, s); g.font = wordFont(style, Math.max(9, ch * 0.15)); g.textAlign = 'center'; g.textBaseline = 'middle';
  if (age > 1.1) { const sm = (age - 1.1) * 14; g.globalAlpha = a * 0.5; g.fillStyle = '#ff9cc8'; g.fillText(txt, -sm, 0); g.fillStyle = '#9ce8ff'; g.fillText(txt, sm, 0); }
  g.globalAlpha = a; g.fillStyle = wordColour(style); g.fillText(txt, 0, 0); g.restore();
}

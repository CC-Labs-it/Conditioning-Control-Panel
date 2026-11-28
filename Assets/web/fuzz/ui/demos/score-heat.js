import { DEMOS } from './kit.js';
import { BRAND } from '../../data/brand.js';
import { createScoreMotion, paintScoreHeat, paintScoreInk, SCORE_FIRE_HEADROOM } from '../unlocks/basics/score-heat.js';

// A single tap, a short run of taps, then a payout. The gaps give the
// same spring used by the live counter time to breathe between gestures.
const beats = [
  {at: .18, units: 1, taps: 1},
  {at: .82, units: 1, taps: 1},
  {at: .98, units: 1, taps: 1},
  {at: 1.13, units: 1, taps: 1},
  {at: 1.27, units: 1, taps: 1},
  {at: 1.41, units: 1, taps: 1},
  {at: 1.55, units: 1, taps: 1},
  {at: 1.69, units: 1, taps: 1},
  {at: 1.83, units: 1, taps: 1},
  {at: 2.46, units: 100, taps: 0},
];
const loopSeconds = 5.8;

DEMOS.score_heat = kit => {
  const doc = kit.el.ownerDocument;
  const win = doc.defaultView;
  const style = win.getComputedStyle(doc.documentElement);
  const palette = {
    a: style.getPropertyValue('--niche').trim() || '#a58bff',
    b: style.getPropertyValue('--niche-2').trim() || '#ff80b9',
  };
  const dpr = Math.min(2, win.devicePixelRatio || 1);
  kit.page('#241c30');
  const line = kit.add(0,0,`width:100%;height:100%;display:grid;place-items:center;`);
  const counter = doc.createElement('span');
  counter.style.cssText = 'position:relative;isolation:isolate;display:inline-flex;align-items:baseline;gap:.22em;color:#fff6e9;font:700 27px/1.1 ShopRound,"Segoe UI",sans-serif;font-variant-numeric:tabular-nums;transform-origin:50% 78%;';
  const amount = doc.createElement('span'), currency = doc.createElement('span');
  const foreground = 'position:relative;z-index:1;-webkit-text-stroke:.057em #15111a;paint-order:stroke fill;';
  amount.style.cssText = foreground;
  currency.textContent = ' '+BRAND.currency;
  currency.style.cssText = foreground + 'font-size:.68em;color:color-mix(in srgb,#fff6e9 40%,' + palette.b + ');';
  const canvas = doc.createElement('canvas');
  canvas.setAttribute('aria-hidden','true');
  canvas.style.cssText = 'position:absolute;z-index:0;left:50%;top:50%;transform:translate(-50%,-50%);pointer-events:none;';
  canvas.style.marginTop=(-SCORE_FIRE_HEADROOM/2)+'px';
  counter.append(canvas,amount,currency);
  line.append(counter);
  const g = canvas.getContext('2d');
  let width = 0, height = 0, total = 1200;
  let digits = [];
  function writeScore() {
    const text = total.toLocaleString('en-US');
    if (amount.childElementCount !== text.length) {
      amount.replaceChildren();
      digits = [];
      for (const char of text) {
        const glyph = doc.createElement('span');
        glyph.style.cssText = '--score-ink-dye:color-mix(in srgb,var(--niche) var(--score-ink-tint,50%),var(--niche-2));color:color-mix(in srgb,#fffbef,var(--score-ink-dye) var(--score-ink-strength,0%));';
        amount.append(glyph);
        if (char !== ',') digits.push(glyph);
      }
    }
    for (let i=0; i<text.length; i++) {
      const glyph = amount.children[i];
      if (glyph.textContent !== text[i]) glyph.textContent = text[i];
    }
    const w = counter.offsetWidth + 120, h = counter.offsetHeight + 32 + SCORE_FIRE_HEADROOM;
    if (width === w && height === h) return;
    width = w; height = h;
    canvas.width = Math.round(w*dpr); canvas.height = Math.round(h*dpr);
    canvas.style.width = w+'px'; canvas.style.height = h+'px';
    g?.setTransform(dpr,0,0,dpr,0,0);
  }
  writeScore();
  const reduced = win.matchMedia('(prefers-reduced-motion: reduce)');
  const calm = () => reduced.matches || doc.body.classList.contains('gentle');
  if (calm()) return;
  const motion = createScoreMotion();
  let nextBeat = 0, elapsed = 0, clock = 0, nextPaint = 0, moving = false;
  function rest() {
    if (!moving) return;
    moving = false;
    counter.style.transform = '';
    counter.style.removeProperty('--score-ink-strength');
    for (const digit of digits) digit.style.removeProperty('--score-ink-strength');
    g?.clearRect(0,0,width,height);
  }
  kit.frame(dt => {
    if (calm()) {
      rest();
      motion.reset();
      return;
    }
    elapsed += dt;
    clock += dt;
    if (elapsed >= loopSeconds) {
      elapsed -= loopSeconds;
      nextBeat = 0;
    }
    let units = 0, taps = 0;
    while (nextBeat < beats.length && beats[nextBeat].at <= elapsed) {
      const beat = beats[nextBeat++];
      units += beat.units;
      taps += beat.taps;
    }
    if (units) {
      total += units * 24;
      writeScore();
    }
    const pose = motion.step(dt,units,taps);
    if (!pose.active) { rest(); return; }
    moving = true;
    paintScoreInk(counter,digits,pose,clock);
    counter.style.transform = `translateY(${pose.lift}px) rotate(${pose.tilt}deg) scale(${pose.scaleX},${pose.scaleY})`;
    if (g && clock >= nextPaint) {
      paintScoreHeat(g,width,height,pose.heat,clock,palette,pose.impact,SCORE_FIRE_HEADROOM);
      nextPaint = clock+1/36;
    }
  });
};

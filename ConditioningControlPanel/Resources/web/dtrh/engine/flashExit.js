/* ============================================================================
 * flashExit.js - how a flash card leaves (owner, 2026-10-12).
 *
 * The port of the app's own flash leave styles (Services/Flash/FlashExit.cs and
 * FlashShatter.cs): pop, TV off, twirl, melt, glitch, shatter. A popped card
 * used to vanish on the spot; now each pop draws one of these, never the same
 * one twice in a row. A card that simply runs out of time takes the soft fade.
 *
 * This is a trance game, so the styles are the calm cousins of the desktop
 * ones: a little slower, no white-out, and opacity only ever goes DOWN (the
 * desktop glitch flickers; this one slices and sinks). Motion level: off is the
 * short fade whatever the pick, reduced drops the swell, the spin and the
 * tumble and runs a quarter shorter.
 *
 * exitPlan() is pure and node tested. runExit() is the DOM half.
 * ==========================================================================*/

/** The styles a pop draws from. */
export const EXIT_POOL = ['pop', 'tvoff', 'twirl', 'melt', 'glitch', 'shatter'];

/** Milliseconds per style at full motion. `fade` is the timeout / motion-off leave. */
export const EXIT_MS = { pop: 280, tvoff: 320, twirl: 480, melt: 640, glitch: 380, shatter: 780, fade: 160 };

const SHARD_COLS = 4, SHARD_ROWS = 3;

/** One style from the pool, never `last` again. */
export function pickExit(last, rnd = Math.random) {
  const pool = EXIT_POOL.includes(last) ? EXIT_POOL.filter((s) => s !== last) : EXIT_POOL;
  return pool[Math.min(pool.length - 1, Math.floor(rnd() * pool.length))];
}

// small seeded noise so one card's glitch / shatter is its own (0..1)
function noise(seed, k) {
  const x = Math.sin(seed * 12.9898 + k * 78.233) * 43758.5453;
  return x - Math.floor(x);
}

/**
 * What a leave looks like. Returns { style, duration, easing, origin, frames } where each
 * frame is { offset, t, opacity, filter?, clip? }: `t` is the transform that FOLLOWS the
 * card's own centring translate. Shatter returns no frames, it returns `shards`.
 */
export function exitPlan(style, level = 'full', seed = 1) {
  if (level === 'off' || !EXIT_MS[style] || style === 'fade') {
    return { style: 'fade', duration: EXIT_MS.fade, easing: 'linear', origin: '50% 50%',
      frames: [{ offset: 0, t: 'scale(1)', opacity: 1 }, { offset: 1, t: 'scale(1)', opacity: 0 }] };
  }
  const reduced = level === 'reduced';
  const duration = Math.round(EXIT_MS[style] * (reduced ? 0.75 : 1));
  const plan = { style, duration, easing: 'ease-in', origin: '50% 50%', frames: [] };
  switch (style) {
    case 'pop':
      plan.frames = reduced
        ? [{ offset: 0, t: 'scale(1)', opacity: 1 }, { offset: 1, t: 'scale(0.6)', opacity: 0 }]
        : [{ offset: 0, t: 'scale(1)', opacity: 1 },
          { offset: 0.3, t: 'scale(1.06)', opacity: 1 },
          { offset: 1, t: 'scale(0.2)', opacity: 0 }];
      break;
    case 'tvoff':
      // squash to a warm line, then pinch the line to a dot
      plan.frames = [
        { offset: 0, t: 'scale(1, 1)', opacity: 1, filter: 'brightness(1)' },
        { offset: 0.55, t: 'scale(1.04, 0.03)', opacity: 1, filter: 'brightness(1.5)' },
        { offset: 1, t: 'scale(0, 0.03)', opacity: 0, filter: 'brightness(1.5)' }];
      break;
    case 'twirl': {
      const turn = reduced ? 0 : (noise(seed, 1) < 0.5 ? -300 : 300);
      plan.frames = [
        { offset: 0, t: 'rotate(0deg) scale(1)', opacity: 1 },
        { offset: 0.7, t: `rotate(${Math.round(turn * 0.55)}deg) scale(0.42)`, opacity: 1 },
        { offset: 1, t: `rotate(${turn}deg) scale(0)`, opacity: 0 }];
      break;
    }
    case 'melt': {
      const stretch = reduced ? 1.3 : 1.7;
      plan.origin = '50% 0%';
      plan.frames = [
        { offset: 0, t: 'translateY(0%) scale(1, 1)', opacity: 1, filter: 'blur(0px)' },
        { offset: 1, t: `translateY(18%) scale(0.85, ${stretch})`, opacity: 0, filter: 'blur(3px)' }];
      break;
    }
    case 'glitch': {
      // a handful of hard sideways jumps on thin slices while it sinks out; no flicker
      const steps = reduced ? 3 : 6;
      plan.easing = 'steps(1, end)';
      for (let i = 0; i <= steps; i++) {
        const p = i / steps;
        const dx = i === 0 || reduced ? 0 : (noise(seed, i) - 0.5) * 16 * p;
        const top = i === 0 ? 0 : Math.round(noise(seed, i + 20) * 30 * p);
        const bottom = i === 0 ? 0 : Math.round(noise(seed, i + 40) * 30 * p);
        plan.frames.push({
          offset: p, t: `translateX(${dx.toFixed(1)}%) scale(1, ${(1 - 0.2 * p).toFixed(3)})`,
          opacity: Number((1 - p * p).toFixed(3)), clip: `inset(${top}% 0% ${bottom}% 0%)`,
        });
      }
      break;
    }
    case 'shatter': {
      // pieces ease apart from the middle and sink, each with its own lean
      plan.easing = 'cubic-bezier(0.3, 0.1, 0.5, 1)';
      plan.cols = SHARD_COLS; plan.rows = SHARD_ROWS;
      plan.shards = [];
      for (let r = 0; r < SHARD_ROWS; r++) {
        for (let c = 0; c < SHARD_COLS; c++) {
          const k = r * SHARD_COLS + c;
          const ox = (c + 0.5) / SHARD_COLS - 0.5, oy = (r + 0.5) / SHARD_ROWS - 0.5;
          plan.shards.push({
            col: c, row: r,
            dx: Number((ox * (0.5 + 0.5 * noise(seed, k))).toFixed(3)),          // fractions of the card width
            dy: Number((oy * 0.3 + 0.25 + 0.35 * noise(seed, k + 50)).toFixed(3)), // fractions of the card height, always down
            rot: reduced ? 0 : Math.round((noise(seed, k + 100) - 0.5) * 70),
            delay: Math.round(noise(seed, k + 150) * 90),
          });
        }
      }
      break;
    }
    default: break;
  }
  return plan;
}

/** Keyframes for element.animate(): the card's centring translate, then the plan's own. */
export function toKeyframes(plan, base = 'translate(-50%, -50%)', startOpacity = 1) {
  return plan.frames.map((f) => {
    const k = { offset: f.offset, transform: `${base} ${f.t}`, opacity: f.opacity * startOpacity };
    if (f.filter) k.filter = f.filter;
    if (f.clip) k.clipPath = f.clip;
    return k;
  });
}

// The picture's own pixel size (a clip, a picture, or neither yet).
function sourceSize(el) {
  return { w: el.videoWidth || el.naturalWidth || 0, h: el.videoHeight || el.naturalHeight || 0 };
}

// Cut the card into pieces of its current frame and let them sink. False = could not cut.
function runShatter(el, plan, done) {
  const src = sourceSize(el);
  const w = el.offsetWidth, h = el.offsetHeight;
  if (!src.w || !src.h || !w || !h || !el.parentNode) return false;
  // the card draws object-fit: cover, so cut from the same crop
  const scale = Math.max(w / src.w, h / src.h);
  const cropW = w / scale, cropH = h / scale;
  const cropX = (src.w - cropW) / 2, cropY = (src.h - cropH) / 2;
  const box = document.createElement('div');
  box.className = 'rh-fx-shards';
  box.style.left = el.style.left; box.style.top = el.style.top;
  box.style.width = w + 'px'; box.style.height = h + 'px';
  const pw = w / plan.cols, ph = h / plan.rows;
  try {
    for (const s of plan.shards) {
      const c = document.createElement('canvas');
      c.width = Math.max(1, Math.round(pw)); c.height = Math.max(1, Math.round(ph));
      c.style.left = (s.col * pw) + 'px'; c.style.top = (s.row * ph) + 'px';
      c.style.width = pw + 'px'; c.style.height = ph + 'px';
      c.getContext('2d').drawImage(el,
        cropX + (s.col / plan.cols) * cropW, cropY + (s.row / plan.rows) * cropH, cropW / plan.cols, cropH / plan.rows,
        0, 0, c.width, c.height);
      box.appendChild(c);
    }
  } catch (e) { return false; }
  el.parentNode.appendChild(box);
  el.style.visibility = 'hidden';
  let left = plan.shards.length;
  const finish = () => { if (box.parentNode) box.remove(); done(); };
  const one = () => { left -= 1; if (left <= 0) finish(); };
  plan.shards.forEach((s, i) => {
    try {
      const a = box.children[i].animate([
        { transform: 'translate(0px, 0px) rotate(0deg)', opacity: 1 },
        { transform: `translate(${(s.dx * w).toFixed(0)}px, ${(s.dy * h).toFixed(0)}px) rotate(${s.rot}deg)`, opacity: 0 },
      ], { duration: plan.duration, delay: s.delay, easing: plan.easing, fill: 'forwards' });
      a.onfinish = one; a.oncancel = one;
    } catch (e) { one(); }
  });
  setTimeout(finish, plan.duration + 400);   // safety net
  return true;
}

/**
 * Play a leave on a flash card, then call `done` (exactly once). The card must be the
 * centred kind (`transform: translate(-50%, -50%)`). Never throws.
 */
export function runExit(el, style, { level = 'full', seed = (Math.random() * 1e6) | 0, done = () => {} } = {}) {
  let called = false;
  const end = () => { if (!called) { called = true; done(); } };
  try {
    el.style.pointerEvents = 'none';
    let plan = exitPlan(style, level, seed);
    if (plan.style === 'shatter') {
      if (runShatter(el, plan, end)) return plan.style;
      plan = exitPlan('pop', level, seed);       // nothing to cut yet: the plain pop
    }
    const startOp = Number(getComputedStyle(el).opacity) || 1;
    el.style.transformOrigin = plan.origin;
    const a = el.animate(toKeyframes(plan, 'translate(-50%, -50%)', startOp),
      { duration: plan.duration, easing: plan.easing, fill: 'forwards' });
    a.onfinish = end; a.oncancel = end;
    setTimeout(end, plan.duration + 300);        // safety net
    return plan.style;
  } catch (e) { end(); return 'none'; }
}

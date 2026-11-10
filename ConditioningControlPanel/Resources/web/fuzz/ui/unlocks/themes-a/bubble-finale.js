/* The dome stays playable under the side fade. The engine owns the credits and the pause clock. */
import { ENDINGS } from '../../../data/content/endings.js';

const ROAM = 30;
const CLOSE = 4.2;
const BLACK_BEAT = 0.32;
const TITLE = 0.3;
const RELEASE = 0.72;

/* Same ear-specific pitches and mixer channel as the Listening route. The ending owns their envelope. */
function bubbleHum(ctx) {
  const input = ctx.audio.input?.({ bus: 'ambient', source: 'binaural', sourceLabel: 'Binaural hum', gain: ctx.gentle ? .65 : 1 });
  if (!input) return null;
  const { ac, node } = input;
  const ears = ac.createChannelMerger(2);
  const envelope = ac.createGain();
  envelope.gain.value = 0;
  ears.connect(envelope).connect(node);
  const tones = [220, 226].map((hz, ear) => {
    const tone = ac.createOscillator();
    tone.type = 'sine';
    tone.frequency.value = hz;
    tone.connect(ears, 0, ear);
    tone.start();
    return tone;
  });
  let dead = false;
  let lastLevel = 0;
  function target(level) {
    if (dead || level === lastLevel) return;
    lastLevel = level;
    envelope.gain.setTargetAtTime(level, ac.currentTime, level === 0 ? .014 : .08);
  }
  return {
    update(seconds) {
      const p = Math.min(1, seconds / ROAM);
      const rise = p*p*(3-2*p);
      const fade = Math.max(0, Math.min(1, (ROAM + CLOSE - .42 - seconds) / (CLOSE - .42)));
      target(Math.round((.024 + .09*rise)*fade*1000)/1000);
    },
    hold() { target(0); },
    stop() {
      if (dead) return;
      dead = true;
      for (const tone of tones) { tone.stop(); tone.disconnect(); }
      ears.disconnect();
      envelope.disconnect();
      input.stop();
    },
  };
}

const CSS = `
  .bi-finale {
    position: absolute; inset: 0; z-index: 20; overflow: hidden;
    pointer-events: none; isolation: isolate;
  }
  .bi-finale::before, .bi-finale::after {
    content: ""; position: absolute; inset-block: 0; width: 77%;
    will-change: transform;
    background: linear-gradient(to right, #000 65%, rgba(0,0,0,.97) 69%, rgba(0,0,0,.72) 78%, rgba(0,0,0,.25) 90%, transparent);
  }
  .bi-finale::before { left: 0; transform: translateX(var(--bi-close, -100%)); }
  .bi-finale::after { right: 0; transform: scaleX(-1) translateX(var(--bi-close, -100%)); }
  .bi-finale[data-black] { background: #000; }
  .bi-finale[data-black]::before, .bi-finale[data-black]::after { display: none; }
  .bi-finale__title {
    position: absolute; inset: 0; display: grid; place-items: center; margin: 0;
    color: #ff8cc9; font: 900 clamp(1px, 12.8vw, 19vh)/1 "Arial Black", "Segoe UI", system-ui, sans-serif;
    letter-spacing: -.055em; white-space: nowrap; text-align: center; text-shadow: none;
  }
  .bi-finale__title[hidden] { display: none; }
`;

/* A glass triad with a softer upper partial, on the same mixer path as the game's effects. */
function glassChime(ctx) {
  const input = ctx.audio.input?.({ bus: 'sfx', gain: ctx.gentle ? 0.55 : 0.8 });
  if (!input) return;
  const { ac, node } = input;
  const start = ac.currentTime;
  for (const [hz, delay, gain] of [[523.25, 0, 0.35], [659.25, 0.045, 0.22], [783.99, 0.085, 0.16], [1567.98, 0.1, 0.045]]) {
    const t = start + delay;
    const tone = ac.createOscillator();
    const envelope = ac.createGain();
    tone.type = 'sine';
    tone.frequency.value = hz;
    envelope.gain.setValueAtTime(0.0001, t);
    envelope.gain.exponentialRampToValueAtTime(gain, t + 0.016);
    envelope.gain.exponentialRampToValueAtTime(0.0001, t + 0.8);
    tone.connect(envelope);
    envelope.connect(node);
    tone.onended = () => { tone.disconnect(); envelope.disconnect(); };
    tone.start(t);
    tone.stop(t + 0.81);
  }
  ctx.cleanup(() => input.stop());
}

export function playBubbleFinale(ctx, stage, { onBlack } = {}) {
  ctx.css(CSS);
  const veil = ctx.el('div', '', null, stage);
  veil.className = 'bi-finale';
  const title = ctx.el('strong', '', ENDINGS.bi_end.trigger, veil);
  title.className = 'bi-finale__title';
  title.hidden = true;
  return new Promise(resolve => {
    const doc = stage.ownerDocument;
    const clock = doc.defaultView.performance;
    const hum = bubbleHum(ctx);
    let activeSeconds = 0;
    let previous = clock.now();
    const resetClock = () => {
      previous = clock.now();
      if (doc.hidden || ctx.paused) hum?.hold();
    };
    doc.addEventListener('visibilitychange', resetClock);
    ctx.on('pause', resetClock);
    ctx.on('resume', resetClock);
    let elapsed = 0;
    let phase = 'roam';
    const finish = () => {
      if (phase === 'done') return;
      phase = 'done';
      hum?.stop();
      doc.removeEventListener('visibilitychange', resetClock);
      removeFrame();
      resolve();
    };
    const removeFrame = ctx.frame(() => {
      const now = clock.now();
      const dt = (now - previous) / 1000;
      previous = now;
      if (doc.hidden || ctx.paused) return;
      activeSeconds += dt;
      if (phase === 'roam' || phase === 'closing') hum?.update(activeSeconds);
      elapsed += dt;
      switch (phase) {
        case 'roam':
          if (elapsed < ROAM) return;
          phase = 'closing';
          elapsed -= ROAM;
          break;
        case 'black':
          if (elapsed < BLACK_BEAT) return;
          title.hidden = false;
          glassChime(ctx);
          phase = 'title';
          elapsed = 0;
          return;
        case 'title':
          if (Math.round(elapsed * 1000000) < TITLE * 1000000) return;
          title.hidden = true;
          phase = 'release';
          elapsed = 0;
          return;
        case 'release':
          if (elapsed >= RELEASE) finish();
          return;
      }
      const p = Math.min(1, elapsed / CLOSE);
      const eased = p * p * (3 - 2 * p);
      veil.style.setProperty('--bi-close', `${((eased - 1) * 100).toFixed(3)}%`);
      if (p === 1) {
        hum?.stop();
        veil.dataset.black = '';
        phase = 'black';
        elapsed = 0;
        onBlack?.();
      }
    });
    ctx.on('suspend', ({ on, reason }) => {
      resetClock();
      if (on && reason === 'panic') finish();
    });
    ctx.cleanup(finish);
  });
}

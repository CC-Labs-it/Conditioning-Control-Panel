import { btnCentre, fill } from './kit.js';
import { ROUTE_COPY } from './copy.js';
import { createRhythm, takeBeat, MIN_BPM, MAX_BPM, weightY, bpmAtWeight, beatPeriod, createPulse, advancePulse, retimePulse } from './metronome-model.js';

const CSS = `
.tc-metronome{position:relative;width:100%;max-width:164px;margin:auto;color:var(--ink);text-align:center;user-select:none;--mt-case:color-mix(in srgb,var(--niche) 28%,#c78769)}
.tc-metronome svg{display:block;width:100%;height:auto;overflow:visible}
.tc-metronome .mt-case{fill:var(--mt-case)}
.tc-metronome .mt-lamp{fill:#f5e9cf;transition:fill 300ms ease}
.tc-metronome[data-beat=left] .mt-lamp-left,.tc-metronome[data-beat=right] .mt-lamp-right{fill:#f5c34e}
.tc-metronome .mt-lever{cursor:pointer;outline:none;touch-action:manipulation}
.tc-metronome .mt-weight{cursor:ns-resize;outline:none;touch-action:none}
.tc-metronome .mt-weight:focus-visible .mt-gold{stroke:var(--niche-text);stroke-width:5}
.tc-metronome[data-adjusting=true] .mt-weight{cursor:grabbing}
.tc-metronome .mt-lever:focus-visible>path:first-of-type{stroke:var(--niche-text);stroke-width:6}
.tc-metronome p{position:absolute;bottom:0;left:0;right:0;margin:0;pointer-events:none;font:700 11px/1.2 var(--font,Arial,sans-serif);color:var(--ink);text-shadow:0 1px 3px var(--page)}
.tc-metronome p:empty{display:none}
`;

/** A physical tempo weight controls the swing and sound together. */
export function mountMetronome(ctx) {
  ctx.css(CSS);
  const dock = ctx.dock({ edge: 'right', title: 'Metronome', width: 164 });
  const root = ctx.el('div', '', null, dock);
  root.title = 'Scroll up to speed up or down to slow down. You can also drag the gold weight. Tap the lever on the beat.';
  dock.dock?.setReady(false);
  root.className = 'tc-metronome';
  root.dataset.dockSurface = '';
  root.dataset.widget = 'metronome';
  root.innerHTML = `<svg viewBox="30 14 180 240" aria-label="Metronome, 60 beats per minute">
    <defs>
      <linearGradient id="mt-chrome"><stop stop-color="#632c69"/><stop offset=".24" stop-color="#fdfaff"/><stop offset=".55" stop-color="#b6b3c4"/><stop offset=".8" stop-color="#faf8ff"/><stop offset="1" stop-color="#6e466f"/></linearGradient>
      <linearGradient id="mt-gold"><stop stop-color="#ad6d19"/><stop offset=".3" stop-color="#fff0b3"/><stop offset=".65" stop-color="#e8b84e"/><stop offset="1" stop-color="#a9711e"/></linearGradient>
      <clipPath id="mt-arm-cut"><rect x="108" y="32" width="24" height="65"/><rect x="108" y="113" width="24" height="70"/></clipPath>
    </defs>
    <image href="assets/glossy/metronome-body.webp" x="30" y="20" width="180" height="223.3"/>
    <text class="mt-bpm" x="120" y="212" text-anchor="middle" fill="#35222f" font-family="Arial,sans-serif" font-size="12" font-weight="900">60 BPM</text>
    <g class="mt-arm" data-dock-interactive>
      <g class="mt-lever" role="button" tabindex="0" aria-label="Tap metronome lever with the beat">
        <rect x="92" y="28" width="56" height="150" fill="transparent" pointer-events="all"/>
        <image href="assets/glossy/metronome-arm.webp" x="110.1" y="44.3" width="19.8" height="136.2" clip-path="url(#mt-arm-cut)" pointer-events="none"/>
        <rect x="116.3" y="96" width="7.4" height="18" fill="url(#mt-chrome)" pointer-events="none"/>
      </g>
      <g class="mt-weight" role="slider" tabindex="0" aria-label="Metronome tempo" aria-orientation="vertical" aria-valuemin="40" aria-valuemax="208">
        <title>Drag up to slow down, down to speed up</title>
        <rect x="98" y="95" width="44" height="40" fill="transparent" pointer-events="all"/>
        <rect class="mt-gold" x="108" y="104" width="24" height="22" rx="5" fill="url(#mt-gold)" stroke="#926023" stroke-width="1.5"/>
        <path d="M112 108H128M112 122H128" stroke="#fff2c0" stroke-width="1.5"/>
      </g>
    </g>
  </svg><p role="status"></p>`;
  const arm = root.querySelector('.mt-arm'), lever = root.querySelector('.mt-lever');
  const weight = root.querySelector('.mt-weight'), label = root.querySelector('.mt-bpm'), svg = root.querySelector('svg');
  const rhythm = createRhythm(), receipt = root.querySelector('p');
  const pulse = createPulse(ctx.store.get('bpm'), ctx.state.clock);
  let angle = null, beat = null, receiptUntil = 0, drag = null;
  const tempoLabel = () => {
    label.textContent = `${pulse.bpm} BPM`;
    root.dataset.bpm = String(pulse.bpm);
    svg.setAttribute('aria-label', `Metronome, ${pulse.bpm} beats per minute`);
    weight.setAttribute('transform', `translate(0 ${weightY(pulse.bpm) - 115})`);
    weight.setAttribute('aria-valuenow', String(pulse.bpm));
    weight.setAttribute('aria-valuetext', `${pulse.bpm} beats per minute. Moving the weight up slows the beat and down speeds it up.`);
  };
  const draw = () => {
    const calm = ctx.gentle || ctx.reduced;
    const next = calm ? 0 : (pulse.side ? 26 : -26) * Math.cos(Math.PI * pulse.phase);
    const rounded = Math.round(next * 100) / 100;
    if (rounded !== angle) { arm.setAttribute('transform', `rotate(${rounded} 120 174)`); angle = rounded; }
    const nextBeat = calm ? 'still' : pulse.side ? 'right' : 'left';
    if (nextBeat !== beat) { root.dataset.beat = nextBeat; beat = nextBeat; }
  };
  const tick = () => ctx.audio.play('assets/audio/sfx/count_tick.mp3', { bus: 'sfx', gain: .16, pan: ctx.has('a_phones') ? (pulse.side ? .6 : -.6) : 0 });
  tempoLabel(); draw(); tick();
  ctx.frame(() => {
    if (ctx.paused || ctx.state.ended) return;
    if (advancePulse(pulse, ctx.state.clock)) tick();
    draw();
    if (receipt.textContent && ctx.state.clock >= receiptUntil) receipt.textContent = '';
  });
  function changeTempo(bpm) {
    retimePulse(pulse, bpm, ctx.state.clock);
    ctx.store.set('bpm', pulse.bpm);
    rhythm.streak = 0; rhythm.lastBeat = null;
    tempoLabel();
  }
  function releaseWeight(event) {
    if (!drag || (event?.pointerId != null && event.pointerId !== drag.id)) return;
    const id = drag.id; drag = null;
    retimePulse(pulse, pulse.bpm, ctx.state.clock); pulse.held = false;
    root.dataset.adjusting = 'false';
    try { if (weight.hasPointerCapture(id)) weight.releasePointerCapture(id); } catch { /* detached pointer */ }
  }
  weight.addEventListener('pointerdown', event => {
    if (ctx.paused || ctx.state.ended || drag || event.button > 0 || event.isPrimary === false) return;
    event.preventDefault(); event.stopPropagation(); dock.dock?.cancelMove();
    // Freeze at the current angle. Use its vertical scale so grabbing never jumps the weight.
    pulse.held = true;
    drag = { id: event.pointerId, y: event.clientY, start: weightY(pulse.bpm), scale: Math.max(.1, Math.abs(arm.getScreenCTM()?.d || 1)) };
    root.dataset.adjusting = 'true';
    weight.focus({ preventScroll: true }); weight.setPointerCapture(event.pointerId);
  });
  weight.addEventListener('pointermove', event => {
    if (!drag || event.pointerId !== drag.id) return;
    event.preventDefault(); event.stopPropagation();
    if (ctx.paused || ctx.state.ended) { releaseWeight(event); return; }
    changeTempo(bpmAtWeight(drag.start + (event.clientY - drag.y) / drag.scale));
  });
  let wheelRemainder = 0;
  root.addEventListener('pointerleave', () => { wheelRemainder = 0; });
  root.addEventListener('wheel', event => {
    if (ctx.paused || ctx.state.ended || drag || event.ctrlKey || event.metaKey || event.shiftKey ||
        !event.deltaY || Math.abs(event.deltaX) > Math.abs(event.deltaY)) return;
    event.preventDefault();
    event.stopPropagation();
    // One standard wheel notch changes two BPM. Smooth trackpads accumulate smaller movements.
    const scale = event.deltaMode === 1 ? 2 / 3 : 1 / 50;
    const delta = event.deltaMode === 2 ? -Math.sign(event.deltaY) * 2 : -event.deltaY * scale;
    if (Math.sign(wheelRemainder) !== Math.sign(delta)) wheelRemainder = 0;
    wheelRemainder += delta;
    const steps = Math.trunc(wheelRemainder);
    wheelRemainder -= steps;
    const next = Math.max(MIN_BPM, Math.min(MAX_BPM, pulse.bpm + steps));
    if (next !== pulse.bpm) changeTempo(next);
  }, { passive: false });
  for (const type of ['pointerup', 'pointercancel', 'lostpointercapture']) weight.addEventListener(type, releaseWeight);
  weight.addEventListener('click', event => { event.preventDefault(); event.stopPropagation(); });
  weight.addEventListener('keydown', event => {
    if (ctx.paused || ctx.state.ended) return;
    const step = event.shiftKey ? 10 : 1;
    const delta = ['ArrowDown', 'ArrowRight'].includes(event.key) ? step : ['ArrowUp', 'ArrowLeft'].includes(event.key) ? -step : 0;
    if (delta || event.key === 'Home' || event.key === 'End') {
      event.preventDefault(); event.stopPropagation();
      changeTempo(event.key === 'Home' ? MIN_BPM : event.key === 'End' ? MAX_BPM : pulse.bpm + delta);
    }
  });
  for (const event of ['pause', 'suspend', 'ending']) ctx.on(event, () => releaseWeight());
  root.ownerDocument.defaultView.addEventListener('blur', releaseWeight);
  ctx.cleanup(() => { releaseWeight(); root.ownerDocument.defaultView.removeEventListener('blur', releaseWeight); });
  function tap({ auto = false, x, y } = {}) {
    if (auto || ctx.paused || ctx.state.ended || pulse.held) return;
    const multiplier = takeBeat(rhythm, ctx.state.clock, pulse.lastTick, beatPeriod(pulse.bpm));
    if (!multiplier) return;
    const b = btnCentre(ctx), r = ctx.rates();
    const got = ctx.pay(Math.max(1, r.click) * multiplier, 'tick', { x: x ?? b.x, y: y ?? b.top });
    receipt.textContent = `+${ctx.fmt(got)}`; receiptUntil = ctx.state.clock + 650;
    if (rhythm.streak >= 2) ctx.float(x ?? b.x, (y ?? b.top) - 22, fill(ROUTE_COPY.onTick, { n: ctx.fmt(got) }), 'color:var(--stamp);font-weight:700;');
  }
  ctx.on('tap', tap);
  lever.addEventListener('click', () => {
    const r = arm.getBoundingClientRect(); tap({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
  });
  lever.addEventListener('keydown', (event) => {
    if (!['Enter', ' '].includes(event.key)) return;
    event.preventDefault();
    if (!event.repeat) { const r = arm.getBoundingClientRect(); tap({ x: r.left + r.width / 2, y: r.top + r.height / 2 }); }
  });
  return { root };
}

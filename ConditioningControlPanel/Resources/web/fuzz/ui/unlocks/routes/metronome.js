import { btnCentre, fill } from './kit.js';
import { ROUTE_COPY } from './copy.js';

const CSS = `
.tc-metronome{width:100%;max-width:250px;margin:auto;color:var(--ink);text-align:center;user-select:none;--mt-case:color-mix(in srgb,var(--niche) 28%,#c78769)}
.tc-metronome svg{display:block;width:100%;height:auto;overflow:visible}
.tc-metronome .mt-case{fill:var(--mt-case)}
.tc-metronome .mt-lamp{fill:#f5e9cf;transition:fill 300ms ease}
.tc-metronome[data-beat=left] .mt-lamp-left,.tc-metronome[data-beat=right] .mt-lamp-right{fill:#f5c34e}
.tc-metronome p{margin:0 0 5px;font:600 12px/1.3 var(--font,Arial,sans-serif);color:var(--ink-soft)}
`;

/** One tick per second, with the original tap window and streak payout. */
export function mountMetronome(ctx) {
  ctx.css(CSS);
  const root = ctx.el('div', '', null, ctx.dock({ edge: 'right', title: 'Metronome' }));
  root.className = 'tc-metronome';
  root.dataset.widget = 'metronome';
  const marks = [120, 100, 80, 60, 40].map((bpm, i) => {
    const y = 64 + i * 21, left = 103 - i * 4;
    return `<path d="M${left} ${y}H113" stroke-width="${bpm === 60 ? 3 : 2}"/>
      <text x="132" y="${y + 3}" stroke="none" fill="#584d40" font-size="9">${bpm}</text>`;
  }).join('');
  root.innerHTML = `<svg viewBox="0 0 240 228" role="img" aria-label="Metronome, 60 beats per minute">
    <g stroke="#352f2b" stroke-width="3.5" stroke-linejoin="round" stroke-linecap="round">
      <ellipse cx="120" cy="218" rx="85" ry="5" fill="#352f2b" stroke="none" opacity=".12"/>
      <path d="M60 202v11h18v-11m84 0v11h18v-11" fill="#6c5249"/>
      <path class="mt-case" d="M91 18h58q8 0 11 10l38 157q2 10-8 10H50q-10 0-8-10L80 28q3-10 11-10Z"/>
      <path d="m91 29-36 151m94-151 36 151" fill="none" stroke="#fff0cc" stroke-width="3" opacity=".55"/>
      <path d="M98 39h44l30 129H68Z" fill="#fff3d7"/>
      <path d="M100 46h40l27 116H73Z" fill="none" stroke="#d9bd96" stroke-width="1.5"/>
      <g fill="none" stroke="#73614b" font-family="Arial,sans-serif" font-weight="700">${marks}</g>
      <path d="m81 127-7-5 7-5" fill="#dc8c56" stroke-width="2"/>
      <path d="M49 184h142v16H49Z" fill="#76554b"/>
      <rect x="39" y="197" width="162" height="13" rx="5" fill="#aa705a"/>
      <path d="M49 201h142" stroke="#efc39b" stroke-width="2"/>
      <rect x="96" y="181" width="48" height="19" rx="5" fill="#fff0c8" stroke-width="2"/>
      <text x="120" y="194" text-anchor="middle" fill="#352f2b" stroke="none" font-family="Arial,sans-serif" font-size="12" font-weight="900">60 BPM</text>
      <circle class="mt-lamp mt-lamp-left" cx="65" cy="189" r="4" stroke-width="2"/>
      <circle class="mt-lamp mt-lamp-right" cx="175" cy="189" r="4" stroke-width="2"/>
    </g>
    <!-- The arm is a sibling above the whole case, with no clipping ancestor. -->
    <g class="mt-arm" stroke="#352f2b" stroke-width="3" stroke-linejoin="round">
      <path d="M117 31q3-5 6 0v143h-6Z" fill="#f5eee0"/>
      <path d="M119 37v125" stroke="#fff" stroke-width="1"/>
      <path d="M108 103h24l4 9-4 15h-24l-4-15Z" fill="#edb847"/>
      <path d="M110 108h20m-19 14h18" fill="none" stroke="#fff0ad" stroke-width="2"/>
      <rect x="116" y="106" width="8" height="17" rx="2" fill="#ad7d32" stroke-width="2"/>
      <circle cx="120" cy="174" r="10" fill="#66544a"/>
      <circle cx="120" cy="174" r="5" fill="#edb847" stroke-width="2"/>
    </g>
  </svg><p>Tap with the beat</p>`;
  const arm = root.querySelector('.mt-arm');
  let side = 0, lastTick = 0, streak = 0, age = 0, angle = null, beat = null;
  const draw = () => {
    const calm = ctx.gentle || ctx.reduced;
    const next = calm ? 0 : (side ? 26 : -26) * Math.cos(Math.PI * Math.min(1, age));
    const rounded = Math.round(next * 100) / 100;
    if (rounded !== angle) { arm.setAttribute('transform', `rotate(${rounded} 120 174)`); angle = rounded; }
    const nextBeat = calm ? 'still' : side ? 'right' : 'left';
    if (nextBeat !== beat) { root.dataset.beat = nextBeat; beat = nextBeat; }
  };
  const swing = () => {
    side = 1 - side;
    lastTick = ctx.state.clock;
    age = 0;
    draw();
    ctx.audio.play('assets/audio/sfx/count_tick.mp3', { bus: 'sfx', gain: .16, pan: ctx.has('a_phones') ? (side ? .6 : -.6) : 0 });
  };
  swing();
  ctx.every(1000, swing);
  ctx.frame(dt => { age += dt; draw(); });
  ctx.on('tap', ({ auto, x, y }) => {
    if (auto) return;
    const gap = Math.min(ctx.state.clock - lastTick, lastTick + 1000 - ctx.state.clock);
    if (Math.abs(gap) > 130) { streak = 0; return; }
    streak += 1;
    const b = btnCentre(ctx), r = ctx.rates();
    const got = ctx.pay(Math.max(1, r.click) * Math.min(4, streak), 'tick', { x: x ?? b.x, y: y ?? b.top });
    if (streak >= 2) ctx.float(b.x, b.top - 22, fill(ROUTE_COPY.onTick, { n: ctx.fmt(got) }), 'color:var(--stamp);font-weight:700;');
  });
  return { root };
}

/* A compact source mixer. New sounds appear as their features begin playing. */
const speaker = muted => `<svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M3 9h4l5-4v14l-5-4H3Z" fill="currentColor"/><path d="${muted ? 'm16 9 6 6m0-6-6 6' : 'M16 8q5 4 0 8M19 5q8 7 0 14'}" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>`;
export function mountAudioMixer(ctx) {
  ctx.css(`
    .audio-mixer{min-width:0;padding:20px 2px 2px;font:13px/1.2 var(--font);color:var(--ink)}
    .mix-row{padding:5px 3px;border-bottom:1px solid var(--line)}
    .mix-row:last-child{border:0}.mix-name{display:block;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-weight:700}
    .mix-controls{display:flex;align-items:center;gap:5px;margin-top:2px}
    .mix-mute{display:grid;place-items:center;flex:none;width:30px;height:30px;padding:0;background:var(--panel);border:1px solid var(--line);border-radius:5px;color:var(--ink)}
    .mix-mute[aria-pressed=true]{color:var(--ink-soft);background:var(--fill)}
    .mix-slider{width:100%;min-width:30px;margin:0;accent-color:var(--accent);cursor:pointer}
    .mix-value{flex:none;width:30px;text-align:right;font-variant-numeric:tabular-nums;font-size:11px}
    .mix-empty{margin:4px;color:var(--ink-soft)}
    @media(pointer:coarse){.mix-mute{width:44px;height:44px}.mix-slider{height:44px}}
  `);
  const root = ctx.el('div', '', null, ctx.dock({ title: 'Audio Mixer', width: 280 }));
  root.className = 'audio-mixer'; root.dataset.widget = 'audio_mixer';
  const empty = ctx.el('p', '', 'Sounds appear here as you unlock them.', root); empty.className = 'mix-empty';
  const mixer = ctx.audio.mixer, rows = new Map();
  if (!mixer) return {};
  const update = () => {
    const sources = mixer.sources(); empty.hidden = sources.length > 0;
    for (const source of sources) {
      let row = rows.get(source.id);
      if (!row) {
        const host = ctx.el('div', '', null, root); host.className = 'mix-row'; host.dataset.audioSource = source.id;
        const name = ctx.el('label', '', source.label, host); name.className = 'mix-name';
        const controls = ctx.el('div', '', null, host); controls.className = 'mix-controls';
        const mute = ctx.el('button', '', null, controls); mute.type = 'button'; mute.className = 'mix-mute';
        const input = ctx.el('input', '', null, controls); input.type = 'range'; input.min = '0'; input.max = '100'; input.step = '1'; input.className = 'mix-slider';
        input.id = `mix-${source.id}`; name.htmlFor = input.id; input.setAttribute('aria-label', `${source.label} volume`);
        const value = ctx.el('output', '', '', controls); value.className = 'mix-value';
        mute.addEventListener('click', () => { if (!ctx.paused) mixer.setMuted(source.id, !mixer.sources().find(s => s.id === source.id)?.muted); });
        input.addEventListener('input', () => { if (!ctx.paused) mixer.setVolume(source.id, Number(input.value) / 100); });
        row = { name, mute, input, value }; rows.set(source.id, row);
      }
      const percent = Math.round(source.volume * 100);
      row.name.textContent = source.label; row.input.value = String(percent); row.value.textContent = `${percent}%`;
      row.mute.setAttribute('aria-label', `${source.muted ? 'Unmute' : 'Mute'} ${source.label}`);
      row.mute.setAttribute('aria-pressed', String(source.muted)); row.mute.innerHTML = speaker(source.muted || percent === 0);
    }
  };
  ctx.cleanup(mixer.subscribe(update)); update();
  return {};
}

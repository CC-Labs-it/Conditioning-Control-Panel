/* The HUD and pause card share the player's master mute preference. */
const speaker = muted => `<svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path d="M3 9h4l5-4v14l-5-4H3Z" fill="currentColor"/><path d="${muted ? 'm16 9 6 6m0-6-6 6' : 'M16 8q5 4 0 8M19 5q8 7 0 14'}" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>`;

export function bindAudioControls(doc, audio, extras, save) {
  const byId = id => doc.getElementById(id);
  const controls = byId('audio-controls'), toggle = byId('audio-toggle'), panel = byId('audio-mixer');
  const masterMute = byId('audio-mute'), pauseMute = byId('mute');
  const volume = byId('game-volume'), volumeValue = byId('game-volume-value');
  const sources = byId('audio-sources'), template = byId('mix-channel');
  const rows = new Map();

  function paintMute() {
    const muted = audio.muted;
    const label = muted ? 'Unmute game audio' : 'Mute game audio';
    masterMute.innerHTML = speaker(muted);
    masterMute.setAttribute('aria-label', label);
    masterMute.setAttribute('aria-pressed', String(muted));
    masterMute.title = label;
    pauseMute.checked = muted;
  }
  function setMute(muted) {
    audio.setMuted(muted);
    extras.muted = muted;
    paintMute();
    save();
  }
  function paintVolume() {
    const percent = Math.round(audio.volume * 100);
    volume.value = String(percent);
    volumeValue.value = percent + '%';
    volume.setAttribute('aria-valuetext', volumeValue.value);
  }
  audio.setMuted(!!extras.muted);
  audio.setVolume(extras.masterVolume);
  paintMute();
  paintVolume();
  masterMute.addEventListener('click', () => setMute(!audio.muted));
  pauseMute.addEventListener('change', () => setMute(pauseMute.checked));
  volume.addEventListener('input', () => {
    audio.setVolume(volume.valueAsNumber / 100);
    extras.masterVolume = audio.volume;
    paintVolume();
    save();
  });

  function makeRow(source) {
    const host = template.content.firstElementChild.cloneNode(true);
    host.dataset.audioSource = source.id;
    const name = host.querySelector('label'), input = host.querySelector('input');
    const mute = host.querySelector('button'), value = host.querySelector('output');
    input.id = 'mix-' + source.id;
    name.htmlFor = input.id;
    value.setAttribute('for', input.id);
    mute.addEventListener('click', () => audio.mixer.setMuted(source.id, mute.getAttribute('aria-pressed') !== 'true'));
    input.addEventListener('input', () => audio.mixer.setVolume(source.id, input.valueAsNumber / 100));
    sources.appendChild(host);
    const row = { name, input, mute, value };
    rows.set(source.id, row);
    return row;
  }
  function paintSources(list) {
    if (panel.hidden) return;
    panel.querySelector('.mix-empty').hidden = list.length > 0;
    for (const source of list) {
      const row = rows.get(source.id) || makeRow(source);
      const percent = Math.round(source.volume * 100);
      row.name.textContent = source.label;
      row.input.value = String(percent);
      row.value.value = percent + '%';
      row.input.setAttribute('aria-label', source.label + ' volume');
      row.input.setAttribute('aria-valuetext', row.value.value);
      row.mute.setAttribute('aria-label', (source.muted ? 'Unmute ' : 'Mute ') + source.label);
      row.mute.setAttribute('aria-pressed', String(source.muted));
      row.mute.innerHTML = speaker(source.muted || percent === 0);
    }
  }
  audio.mixer.subscribe(paintSources);

  function close() {
    if (panel.hidden) return false;
    panel.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
    if (panel.contains(doc.activeElement)) toggle.focus({ preventScroll: true });
    return true;
  }
  toggle.addEventListener('click', () => {
    if (close()) return;
    panel.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
    paintSources(audio.mixer.sources());
  });
  function outside(event) { if (!controls.contains(event.target)) close(); }
  doc.addEventListener('pointerdown', outside, true);
  doc.addEventListener('focusin', outside);
  return { close };
}

import { nichesFor, togglePick, MAX_NICHES } from '../host/scrolllerNiches.js';
import { SOURCES, sourceLabel, openFilePicker } from '../host/localLibrary.js';

/** Plain text only: catalog and file names never become markup. */
export function mediaRecap(view = {}, trackId) {
  const labels = new Map((view.catalog || []).map(row => [row.id, row.label]));
  const niches = view.niches || nichesFor(trackId);
  const pictures = view.source === 'files' ? 'My files' + (view.files ? ' (' + view.files + ')' : ' - choose files')
    : view.source === 'off' ? 'Pictures off' : 'Scrolller: ' + niches.map(id => labels.get(id) || id).join(' + ');
  return { pictures, song: view.songName || 'Track default' };
}

/** A native modal keeps course changes, file picking and controller focus inside the menu. */
export function createCourseMedia({root, mediaFor = () => ({})}) {
  const dialog = document.createElement('dialog');
  dialog.className = 'g-track-picker g-media-picker';
  dialog.setAttribute('aria-labelledby', 'g-media-title');
  root.append(dialog);
  let course = null, opener = null;
  const emit = (kind, value = {}) => root.dispatchEvent(new CustomEvent('glassroad-course-media', {
    bubbles: true, detail: {trackId: course.id, kind, ...value},
  }));
  const close = () => {dialog.close();opener?.focus({preventScroll:true});};
  const button = (parent, label, click, pressed) => {
    const b = document.createElement('button');b.type = 'button';b.textContent = label;b.onclick = click;
    if (pressed !== undefined) b.setAttribute('aria-pressed', String(pressed));
    parent.append(b);return b;
  };
  function paint() {
    if (!course) return;
    const focus = dialog.contains(document.activeElement) ? document.activeElement?.dataset.focus : null;
    const view = mediaFor(course.id) || {}, source = view.source || 'off';
    dialog.innerHTML = '<header><div><small>MAKE IT YOURS</small><h2 id="g-media-title"></h2></div><button type="button" class="g-track-back">Done</button></header><section class="g-media-pictures"><h3>Pictures</h3><div class="g-media-sources"></div><div class="g-media-files"></div></section><section class="g-media-niches"><h3>Scrolller niches</h3><p>Pick up to ' + MAX_NICHES + '</p><div class="g-media-chips"></div></section><section class="g-media-music"><h3>Music</h3><div class="g-media-songs"></div><p class="g-media-song-note">Your own songs play in solo races. Online uses the room song.</p></section>';
    dialog.querySelector('h2').textContent = course.name;
    dialog.querySelector('.g-track-back').onclick = close;
    const rows = dialog.querySelector('.g-media-sources');
    for (const id of SOURCES) {
      const b = button(rows, sourceLabel(id), () => emit('source', {source:id}), source === id);b.dataset.focus = 'source-' + id;
      if (id === 'scrolller' && view.scrolllerAvailable === false) {b.disabled = true;b.title = 'Scrolller is unavailable on this host';}
    }
    const files = dialog.querySelector('.g-media-files');
    if (source === 'files') {
      const note = document.createElement('p');note.textContent = view.files ? view.files + ' files picked. They stay on this device.' : 'Choose pictures or videos. They stay on this device.';files.append(note);
      button(files, view.files ? 'Change files' : 'Choose files', () => openFilePicker(picked => emit('files', {files:picked}))).dataset.focus = 'files';
    }
    const nicheBox = dialog.querySelector('.g-media-chips'), picks = view.niches || nichesFor(course.id);
    for (const row of view.catalog || []) button(nicheBox, row.label, () => {
      const next = togglePick(picks, row.id);if (next) emit('niches', {niches:next});
    }, picks.includes(row.id)).dataset.focus = 'niche-' + row.id;
    if (!(view.catalog || []).length) {
      const note = document.createElement('p');note.textContent = 'Turn on Scrolller to load its niche list.';nicheBox.append(note);
    }
    button(nicheBox, 'Track default', () => emit('niches', {niches:null})).dataset.focus = 'niche-default';
    const songs = dialog.querySelector('.g-media-songs');
    for (const row of [{id:'default',label:'Track default'}, ...(view.songs || []).filter(row => row.id !== 'default')]) {
      button(songs, row.label, () => emit('song', {songId:row.id}), (view.songId || 'default') === row.id).dataset.focus = 'song-' + row.id;
    }
    button(songs, 'Choose music file', () => {
      const input = document.createElement('input');input.type = 'file';input.accept = 'audio/*';
      input.style.display = 'none';document.body.append(input);
      input.onchange = () => {const file = input.files?.[0];if (file) emit('song-file', {file});input.remove();};
      input.addEventListener('cancel', () => input.remove(), {once:true});input.click();
    }).dataset.focus = 'song-file';
    const current = document.createElement('p');current.textContent = 'Selected: ' + mediaRecap(view, course.id).song;songs.append(current);
    if (focus) [...dialog.querySelectorAll('[data-focus]')].find(b => b.dataset.focus === focus)?.focus({preventScroll:true});
  }
  dialog.addEventListener('click', e => {if (e.target === dialog) {const r = dialog.getBoundingClientRect();if (e.clientX < r.left || e.clientX > r.right || e.clientY < r.top || e.clientY > r.bottom) close();}});
  return {
    get open(){return dialog.open;},
    show(value, trigger = document.activeElement){course = value;opener = trigger;paint();if (!dialog.open) dialog.showModal();},
    refresh(){if (dialog.open) paint();},
    nav(dir){if (!dialog.open) return;if (dir === 'back') {close();return;}const rows = [...dialog.querySelectorAll('button')], at = rows.indexOf(document.activeElement);if (dir === 'ok') document.activeElement?.click?.();else {const step = {left:-1,up:-1,right:1,down:1}[dir];if (step) rows[(at + step + rows.length) % rows.length]?.focus();}},
    dispose(){dialog.remove();},
  };
}

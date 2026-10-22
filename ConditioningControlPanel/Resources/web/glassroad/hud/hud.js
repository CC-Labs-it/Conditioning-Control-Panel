/* hud/hud.js - the minimal DOM HUD and the start / pause / finish card.
 *
 * Speed, lap and time, the countdown, three drift tier pips and an empty credit slot
 * (filled with "via Scrolller" once media shows, Phase B). The card reuses index.html's
 * #card (title, note, Start button) and adds a Restart button. Any key but Escape starts
 * from the start card and resumes from the pause card (Escape is the pause key and goes
 * through the input map). Copy is dry and short. Writes touch the DOM only on a change.
 */

export function formatTime(s) {
  if (!(s >= 0)) s = 0;
  const m = Math.floor(s / 60), r = s - m * 60;
  return m + ':' + (r < 10 ? '0' : '') + r.toFixed(2);
}

const SKIP_KEYS = new Set(['Escape', 'Shift', 'Control', 'Alt', 'Meta', 'Tab', 'CapsLock']);

/** @returns {{setCard(kind: string|null, data?: object): void, setReady(ready: boolean, note?: string): void,
 *   onStart(fn: Function): void, onResume(fn: Function): void, onRestart(fn: Function): void, update(v: object): void}} */
export function createHud({ root, card }, feel, bus, { doc = globalThis.document, now = () => performance.now() } = {}) {
  root.classList.add('hud');
  root.innerHTML =
    '<div class="hud-top"><span class="hud-lap"></span><span class="hud-time"></span></div>' +
    '<div class="hud-count" aria-hidden="true"></div>' +
    '<div class="hud-bottom"><span class="hud-pips"><i></i><i></i><i></i></span>' +
    '<span class="hud-speed"><b>0</b> km/h</span></div>' +
    '<div class="hud-credit" data-slot="credit"></div>';
  const $ = (sel) => root.querySelector(sel);
  const el = { lap: $('.hud-lap'), time: $('.hud-time'), count: $('.hud-count'), speed: $('.hud-speed b'), pips: [...root.querySelectorAll('.hud-pips i')] };

  const title = card.querySelector('.card-title');
  const note = card.querySelector('.card-note');
  const go = card.querySelector('#card-go') || card.querySelector('button');
  let again = card.querySelector('#card-restart');
  if (!again) {
    again = doc.createElement('button');
    again.type = 'button'; again.id = 'card-restart'; again.textContent = 'Restart'; again.hidden = true;
    go.after(again);
  }

  const fns = { start: [], resume: [], restart: [] };
  const call = (k) => fns[k].forEach((fn) => { try { fn(); } catch (e) { console.warn('[hud]', k, e); } });
  let kind = 'start', ready = false, waitNote = note?.textContent || '', goUntil = 0, countN = 0;
  const last = {};
  const put = (key, node, text) => { if (last[key] !== text) { last[key] = text; node.textContent = text; } };

  function primary() {
    if (kind === 'start' && ready) call('start');
    else if (kind === 'pause') call('resume');
    else if (kind === 'finish') call('restart');
  }
  go.addEventListener('click', primary);
  again.addEventListener('click', () => call('restart'));
  doc.addEventListener('keydown', (e) => {
    if (card.hidden || e.repeat || SKIP_KEYS.has(e.key)) return;
    if (kind === 'finish' && e.key !== 'Enter') return;   // a held throttle never restarts
    e.preventDefault();
    primary();
  });

  function setCard(k, data = {}) {
    kind = k;
    card.hidden = !k;
    if (!k) return;
    card.dataset.kind = k;
    again.hidden = k !== 'pause';
    if (k === 'start') {
      title.textContent = data.title || 'Threshold';
      note.textContent = ready ? 'Press any key' : waitNote;
      go.textContent = 'Start'; go.disabled = !ready;
    } else if (k === 'pause') {
      title.textContent = 'Paused';
      note.textContent = 'Press any key to carry on';
      go.textContent = 'Resume'; go.disabled = false;
    } else if (k === 'finish') {
      title.textContent = 'Finished';
      note.textContent = formatTime(data.timeS) + (data.best ? '  best ' + formatTime(data.best) : '');
      go.textContent = 'Again'; go.disabled = false;
    }
    if (!go.disabled) go.focus?.({ preventScroll: true });
  }

  function setReady(r, n) {
    ready = !!r;
    if (n) waitNote = n;
    if (kind === 'start') setCard('start');
  }

  const offs = [
    bus.on('countdown', ({ n }) => {
      countN = n;
      if (!feel.get('gentle.on')) { el.count.classList.remove('pulse'); void el.count.offsetWidth; el.count.classList.add('pulse'); }
    }),
    bus.on('go', () => { countN = 0; goUntil = now() + feel.get('juice.goReleaseS') * 1000; }),
    bus.on('drift-tier', ({ tier }) => pips(tier)),
    bus.on('drift-release', () => pips(0)),
    bus.on('drift-cancel', () => pips(0)),
  ];
  const colours = () => feel.get('juice.sparkColours');
  function pips(tier) {
    const c = colours();
    el.pips.forEach((p, i) => { p.classList.toggle('on', i < tier); p.style.setProperty('--pip', c[i] || '#fff'); });
  }

  function update({ lap = 0, laps = 1, timeS = 0, speedPct = 0, countdown = null, paused = false } = {}) {
    put('lap', el.lap, 'Lap ' + Math.min(lap + 1, laps) + '/' + laps);
    put('time', el.time, formatTime(timeS));
    put('speed', el.speed, String(Math.round(Math.max(0, speedPct) * feel.get('speed.topMps') * 3.6)));
    const n = countdown || countN;
    put('count', el.count, n > 0 ? String(n) : now() < goUntil ? 'Go' : '');
    root.classList.toggle('paused', !!paused);
  }

  return {
    setCard, setReady, update,
    onStart: (fn) => fns.start.push(fn), onResume: (fn) => fns.resume.push(fn), onRestart: (fn) => fns.restart.push(fn),
    credit: root.querySelector('.hud-credit'),
    dispose() { offs.forEach((off) => off()); },
  };
}

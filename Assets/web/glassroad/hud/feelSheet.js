/* hud/feelSheet.js - the live feel sheet (?feel=1).
 *
 * One row per feel.json key, grouped by section in folds. Numbers get a number box and a
 * slider, arrays one box per entry, booleans a checkbox, strings a text box. An edit goes
 * through feel.set and applies on the next tick. Edits are kept in this browser
 * (localStorage, every access in try/catch) and laid over feel.json on the next open.
 * Copy JSON puts the whole sheet on the clipboard for the owner to paste back; Reset
 * drops the kept edits. The gentle group is host state, not a knob, so it is left out.
 */

const STORE = 'glassroad.feel.v1';
const SKIP = new Set(['version', 'gentle']);

const load = () => { try { return JSON.parse(localStorage.getItem(STORE) || 'null'); } catch (e) { return null; } };
const save = (obj) => { try { localStorage.setItem(STORE, JSON.stringify(obj)); } catch (e) { /* private window */ } };
const drop = () => { try { localStorage.removeItem(STORE); } catch (e) { /* ignore */ } };

/** Lay kept edits over the feel, leaf by leaf (unknown keys are ignored). */
export function applyKept(feel, kept) {
  if (!kept || typeof kept !== 'object') return 0;
  let n = 0;
  for (const [g, grp] of Object.entries(kept)) {
    if (SKIP.has(g) || !grp || typeof grp !== 'object') continue;
    for (const [k, v] of Object.entries(grp)) if (feel.set(g + '.' + k, v)) n++;
  }
  return n;
}

/** feel.reset() that keeps the gentle group: Gentle is host state, a sheet reset never switches it off. */
export function resetKeepingGentle(feel) {
  const host = feel.toJSON().gentle || {};
  feel.reset();
  for (const [k, v] of Object.entries(host)) feel.set('gentle.' + k, v);
}

/** Slider range for a starting value: 0..3x (or -3x..3x for negatives), 100 steps. */
export function rangeFor(v) {
  const span = Math.max(Math.abs(v) * 3, 1);
  return { min: v < 0 ? -span : 0, max: span, step: Number.isInteger(v) && Math.abs(v) >= 2 ? 1 : span / 100 };
}

export function openFeelSheet(feel, root = globalThis.document?.body) {
  const doc = root.ownerDocument || globalThis.document;
  applyKept(feel, load());
  const panel = doc.createElement('aside');
  panel.className = 'feel-sheet';
  root.appendChild(panel);
  // typing in a box never steers the kart or resumes a card (Escape still pauses)
  panel.addEventListener('keydown', (e) => { if (e.key !== 'Escape') e.stopPropagation(); });
  const keep = () => save(feel.toJSON());

  function input(type, value, onChange) {
    const i = doc.createElement('input');
    i.type = type;
    if (type === 'checkbox') i.checked = !!value; else i.value = value;
    i.addEventListener(type === 'range' ? 'input' : 'change', () => onChange(i));
    return i;
  }

  function row(path, value) {
    const r = doc.createElement('label');
    r.className = 'feel-row';
    const name = doc.createElement('span');
    name.textContent = path.split('.')[1];
    r.appendChild(name);
    const commit = (v) => { feel.set(path, v); keep(); };
    if (typeof value === 'boolean') r.appendChild(input('checkbox', value, (i) => commit(i.checked)));
    else if (Array.isArray(value)) {
      value.forEach((v, idx) => r.appendChild(input(typeof v === 'number' ? 'number' : 'text', v, (i) => {
        const arr = feel.get(path).slice();
        arr[idx] = typeof v === 'number' ? Number(i.value) : i.value;
        commit(arr);
      })));
    } else if (typeof value === 'number') {
      const box = input('number', value, (i) => { slide.value = i.value; commit(Number(i.value)); });
      const slide = input('range', value, (i) => { box.value = i.value; commit(Number(i.value)); });
      const rg = rangeFor(value);
      slide.min = rg.min; slide.max = rg.max; slide.step = rg.step; box.step = 'any';
      r.append(box, slide);
    } else r.appendChild(input('text', value, (i) => commit(i.value)));
    return r;
  }

  function build() {
    panel.textContent = '';
    const bar = doc.createElement('div');
    bar.className = 'feel-bar';
    const btn = (text, fn) => { const b = doc.createElement('button'); b.type = 'button'; b.textContent = text; b.onclick = fn; bar.appendChild(b); return b; };
    const copy = btn('Copy JSON', async () => {
      const text = JSON.stringify(feel.toJSON(), null, 2);
      try { await navigator.clipboard.writeText(text); copy.textContent = 'Copied'; }
      catch (e) { console.log(text); copy.textContent = 'In the console'; }
      setTimeout(() => { copy.textContent = 'Copy JSON'; }, 1500);
    });
    btn('Reset', () => { drop(); resetKeepingGentle(feel); build(); });
    btn('Hide', () => panel.classList.toggle('folded'));
    panel.appendChild(bar);
    for (const [g, grp] of Object.entries(feel.toJSON())) {
      if (SKIP.has(g) || !grp || typeof grp !== 'object') continue;
      const fold = doc.createElement('details');
      const sum = doc.createElement('summary');
      sum.textContent = g;
      fold.appendChild(sum);
      for (const [k, v] of Object.entries(grp)) fold.appendChild(row(g + '.' + k, v));
      panel.appendChild(fold);
    }
  }

  build();
  return { el: panel, close() { panel.remove(); } };
}

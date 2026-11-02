/* ui/intro.js - what stands between a visit and the button (owner's mockup, handoff-1102/tc-intro-mockup.html).
 *   First visit  the whole joke contract: text shrinks as it goes, skimming repeats sections, 8 hidden things pay points.
 *   Later visit  "We updated our terms": a new llama line and the age box, on every load, as the owner asked.
 *   Decline      a calm screen with Read it again. No game, no audio, nothing saved: main.js writes nothing until accepted.
 * openIntro() answers {done, suspend(on), close()}; done resolves with {points} when the player may start. */

import { brandText } from '../data/brand.js';
import { whole } from '../sim/format.js';
import { EGG_LINES, EGG_POINTS, INTRO, JUMP_LINES, LLAMA_WORDS, TERMS, UPDATE_LINES } from '../data/content/terms.js';
import { createLegal, esc, expandHtml } from './legal.js';

/* The text falls from this size to that one, top to bottom. */
const TOP_PX = 15.5, END_PX = 6.5;
/* Skimming: a fast scroll adds sections just above the last line. Capped, so a fast or stubborn scroll always gets there. */
const SKIM_SCREENS = 3.2, SKIM_GAP_MS = 600, SKIM_MAX = 14, SKIM_BATCH = 3, REPEAT_PX = 8.5;
const REPEATABLE = TERMS.filter((t) => /^<b>\d+\. /.test(t) && !/ id=|class=/.test(t));
const ASIDE = '<p class="aside">';

export function termSize(i, n = TERMS.length) {
  return TOP_PX - (TOP_PX - END_PX) * Math.pow(i / (n - 1), 1.05);
}

function termsHtml() {
  return TERMS.map((t, i) => {
    const aside = t.startsWith(ASIDE);
    const inner = aside ? t.slice(ASIDE.length, -4) : t;
    return `<p${aside ? ' class="aside"' : ''} style="font-size:${termSize(i).toFixed(2)}px">${inner}</p>`;
  }).join('');
}

export function updateLine(life) {
  return UPDATE_LINES[Math.floor(life.updates || 0) % UPDATE_LINES.length];
}

export function openIntro({ doc = document, life, skip = false, hosted = false, leave, gentle = () => false, base = 0, showCount = () => {} }) {
  if (skip) return { done: Promise.resolve({ points: 0 }), suspend() {}, close() {} };

  const legal = createLegal({ doc, gentle });
  const $ = (id) => doc.getElementById(id);
  const win = doc.defaultView;
  /** Hidden things pay only the first time the contract is signed; a returning player who reads it again has nothing owed. */
  const pays = !life.contract;
  let resolve, clock = 0, tickTime = null, endSelection = () => {};
  const done = new Promise((r) => { resolve = r; });
  let points = 0;

  const stopClock = () => { clearInterval(clock); clock = 0; };
  const startClock = () => { if (tickTime && !clock) { tickTime(); clock = setInterval(tickTime, 1000); } };
  const cleanScreen = () => { stopClock(); tickTime = null; endSelection(); endSelection = () => {}; };

  const acceptRow = (extra = '') => `<div class="foot"><p class="over">${esc(brandText(INTRO.over))}</p>`
    + `<div class="btns"><button class="plain" id="accept" type="button" disabled>${esc(INTRO.accept)}</button><button class="link" id="decline" type="button">${esc(INTRO.decline)}</button></div>${extra}</div>`;

  function enter() {
    cleanScreen();
    life.contract = true;
    life.age = true;
    legal.close();
    resolve({ points: pays ? points : 0 });
    doc.getElementById('btn')?.focus();
  }

  /* ---- first visit ---- */
  function first() {
    cleanScreen();
    points = 0;
    showCount(base);
    const started = win.performance.now();
    legal.show({
      small: false,
      title: INTRO.firstTitle,
      head: INTRO.firstHead,
      body: `<p class="intro">${expandHtml(INTRO.firstIntro)}</p>`
        + `<div class="termswrap"><div id="terms" tabindex="0" aria-label="${esc(INTRO.termsLabel)}">${termsHtml()}</div>`
        + `<button id="jump" type="button" aria-label="${esc(INTRO.jumpLabel)}"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v15M5 12l7 7 7-7"/></svg></button></div>`
        + `<div class="read"><span>${esc(INTRO.read)}</span><div class="track" role="progressbar" aria-label="${esc(INTRO.read)}" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0"><div id="fill"></div></div><span id="pct">0%</span></div>`
        + `<div class="checks"><label><input type="checkbox" id="age"> ${esc(INTRO.age)}</label>`
        + `<label class="off" id="readLabel"><input type="checkbox" id="read" disabled> ${esc(brandText(INTRO.readBox))}</label></div>`
        + acceptRow('<p class="status" id="status" role="status"></p>'),
    });
    const terms = $('terms');
    const found = new Set();
    let reached = false, spent = 0, skims = 0, jumps = 0, llamas = 3, repeatAt = 0;
    let lastY = 0, lastT = win.performance.now(), lastSkim = 0;

    function egg(key, line) {
      if (found.has(key)) return;
      found.add(key);
      const eggs = $('eggs');
      if (eggs) eggs.textContent = found.size;
      const tail = INTRO.found.replace('{n}', found.size);
      if (!pays) { legal.toast(`${line} ${tail}`, 4200); return; }
      points += EGG_LINES[key][1] || EGG_POINTS;
      showCount(base + points);
      legal.toast(`${line} ${brandText(INTRO.have).replace('{n}', whole(points))} ${tail}`, 4200);
    }

    function repeatSections() {
      const last = terms.lastElementChild;
      for (let i = 0; i < SKIM_BATCH; i += 1) {
        const src = REPEATABLE[repeatAt % REPEATABLE.length];
        repeatAt = (repeatAt + 11) % REPEATABLE.length;   // steps through them out of order
        const m = /^<b>(\d+)\. ([^<]*)<\/b> ?([\s\S]*)$/.exec(src);
        const p = doc.createElement('p');
        p.className = 'again';
        p.style.fontSize = `${REPEAT_PX}px`;
        p.innerHTML = `<b>${m[1]}. ${m[2]} ${esc(INTRO.repeated)}</b> ${m[3]}`;
        terms.insertBefore(p, last);
      }
    }

    function drawStatus() {
      const age = $('age').checked, read = $('read').checked;
      let s;
      if (!reached) s = age ? INTRO.statusAge : INTRO.statusNoAge;
      else if (!read) s = INTRO.statusRead.replace('{secs}', spent).replace('{skims}', skims ? ` and skimmed ${skims} ${skims === 1 ? 'time' : 'times'}` : '');
      else if (!age) s = INTRO.statusNeedAge;
      else s = INTRO.statusDone;
      $('status').textContent = s;
      $('accept').disabled = !(age && read && reached);
    }

    terms.addEventListener('scroll', () => {
      const now = win.performance.now();
      const dy = terms.scrollTop - lastY, dt = Math.max(1, now - lastT);
      lastY = terms.scrollTop;
      lastT = now;
      const atEnd = terms.scrollTop >= terms.scrollHeight - terms.clientHeight - 3;
      if (!reached && dy > 0 && (dy / dt) * 1000 > terms.clientHeight * SKIM_SCREENS && (atEnd || now - lastSkim > SKIM_GAP_MS) && skims < SKIM_MAX) {
        lastSkim = now;
        skims += 1;
        repeatSections();
        if (skims === 1) legal.toast(INTRO.skimOnce);
        if (skims === SKIM_MAX) legal.toast(INTRO.skimDone);
      }
      const span = terms.scrollHeight - terms.clientHeight;
      const pct = span <= 0 ? 100 : Math.min(100, Math.round((terms.scrollTop / span) * 100));
      $('fill').style.width = `${pct}%`;
      $('fill').parentElement.setAttribute('aria-valuenow', pct);
      if (!reached) $('pct').textContent = `${pct}%`;
      if (!reached && terms.scrollTop >= span - 3) {
        reached = true;
        spent = Math.max(1, Math.round((win.performance.now() - started) / 1000));
        $('read').disabled = false;
        $('readLabel').classList.remove('off');
        $('pct').textContent = spent < 90 ? INTRO.allegedly : '100%';
        drawStatus();
      }
    }, { passive: true });

    tickTime = () => {
      const s = $('secs'), c = $('clock');
      if (s) s.textContent = Math.round((win.performance.now() - started) / 1000);
      if (c) c.textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    };
    startClock();
    $('dev').textContent = win.innerWidth < 700 ? 'phone' : 'computer';

    /* The hidden things: a tap, a click, or Enter and Space from the keyboard. */
    const pressEgg = (el) => {
      const key = el.id || el.dataset.egg;
      if (key === 'redact') el.classList.remove('bar');
      if (key === 'flip') el.classList.remove('flip');
      if (EGG_LINES[key]) egg(key, EGG_LINES[key][0]);
    };
    for (const el of terms.querySelectorAll('.egg')) { el.tabIndex = 0; el.setAttribute('role', 'button'); }
    terms.addEventListener('click', (e) => { const el = e.target.closest?.('.egg'); if (el) pressEgg(el); });
    terms.addEventListener('keydown', (e) => {
      if (e.key !== 'Enter' && e.key !== ' ') return;
      const el = e.target.closest?.('.egg');
      if (el) { e.preventDefault(); pressEgg(el); }
    });
    const onSelection = () => {
      const g = $('ghost'), sel = win.getSelection();
      if (g && sel && !sel.isCollapsed && sel.containsNode(g, true)) egg('ghost', EGG_LINES.ghost[0]);
    };
    doc.addEventListener('selectionchange', onSelection);
    endSelection = () => doc.removeEventListener('selectionchange', onSelection);

    $('optout').addEventListener('change', (e) => {
      e.target.checked = false;
      llamas = Math.min(llamas + 1, LLAMA_WORDS.length - 1);
      const word = LLAMA_WORDS[llamas];
      for (const n of terms.querySelectorAll('.lln')) n.textContent = `${word} (${llamas})`;
      const line = EGG_LINES.opt[0].replace('{llamas}', word);
      if (found.has('opt')) legal.toast(line); else egg('opt', line);
    });
    $('jump').addEventListener('click', () => {
      legal.nudge($('jump'));
      jumps += 1;
      if (jumps === JUMP_LINES.length + 1) {
        terms.scrollTop = 0;
        lastY = 0;
        egg('jump', EGG_LINES.jump[0]);
        jumps = 0;
        return;
      }
      legal.toast(JUMP_LINES[jumps - 1]);
    });
    $('age').addEventListener('change', drawStatus);
    $('read').addEventListener('change', drawStatus);
    $('accept').addEventListener('click', enter);
    $('decline').addEventListener('click', declined);
    drawStatus();
    terms.focus({ preventScroll: true });
  }

  /* ---- later visits: the age box stays, the wall of text does not ---- */
  function back() {
    cleanScreen();
    legal.show({
      title: INTRO.backTitle,
      head: INTRO.backHead,
      body: `<p class="intro">${esc(INTRO.backIntro.replace('{line}', updateLine(life)))}</p>`
        + `<div class="checks"><label><input type="checkbox" id="age"> ${esc(INTRO.backAge)}</label></div>${acceptRow()}`,
    });
    $('age').addEventListener('change', () => { $('accept').disabled = !$('age').checked; });
    $('accept').addEventListener('click', () => { life.updates = Math.floor(life.updates || 0) + 1; enter(); });
    $('decline').addEventListener('click', declined);
    $('age').focus({ preventScroll: true });
  }

  /* ---- declined ---- */
  function declined() {
    cleanScreen();
    points = 0;
    showCount(base);
    legal.show({
      title: INTRO.declineTitle,
      head: INTRO.declineHead,
      body: `<p class="intro">${esc(hosted ? INTRO.declineHosted : INTRO.declineTab)}</p>`
        + `<div class="foot"><div class="btns"><button class="plain" id="again" type="button">${esc(INTRO.again)}</button>`
        + `${leave ? `<button class="link" id="leave-intro" type="button">${esc(INTRO.leave)}</button>` : ''}</div></div>`,
    });
    $('again').addEventListener('click', first);
    if (leave) $('leave-intro').addEventListener('click', leave);
    $('again').focus({ preventScroll: true });
  }

  if (life.contract) back(); else first();

  return {
    done,
    /** Wake Up and Escape while the window is up: nothing is running, and they say so. */
    wake() { legal.wakeNote(INTRO.wakeIdle); },
    /** The host's stop: nothing here takes a press and the clock stops until it lifts. */
    suspend(on) { legal.inert = on; if (on) stopClock(); else startClock(); },
    /** The host is closing: done never resolves. */
    close() { cleanScreen(); legal.inert = true; },
  };
}

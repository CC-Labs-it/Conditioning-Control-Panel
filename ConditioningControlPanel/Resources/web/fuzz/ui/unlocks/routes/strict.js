/* Tap: this route pays for doing as you are told, when you are told. Orders, tests, reviews. Failing costs nothing but
 * the bonus, and the button always works. */

import { ORDERS, DENIED_LINES, COUNT_LINES, BEAT_LINES, STILL_TEST, PHRASES, PHRASE_LINES, CORNER_LINES, REVIEWS } from '../../../data/content/index.js';
import { deck, fill, btnCentre, setLine, dropLine, meterRow, bar, ring, burst, claim, release, docOf, tone, onDark } from './kit.js';
import { ROUTE_COPY } from './copy.js';

const RED = '#b3121d', RED_DARK = '#ff8a92', GREEN = '#1a7a3a';
const red = (ctx) => (onDark(ctx) ? RED_DARK : RED);
const STAMP = (ctx, size) => `font:800 ${size}px/1 Arial,Helvetica,sans-serif;color:${red(ctx)};letter-spacing:.5px;`;

function good(ctx, line) {
  setLine(ctx, line, `color:${onDark(ctx) ? '#9dff9d' : GREEN};font-weight:700;`, 1700);
}
function bad(ctx, line) {
  setLine(ctx, line, `color:${red(ctx)};font-weight:700;`, 1700);
}

export default {
  /* Tap Orders: an order appears, you have a second and a bit, and obeying pays five taps and a second of pay. */
  t_orders: {
    mount(ctx) {
      tone(ctx, { '--page': '#ece2dd', '--panel': '#f5eeea', '--panel-short': '#ddd0c9', '--line': '#a08c84', '--fill': '#d3b9ae', '--btn': '#e8dbd5', '--stamp': RED });
      const words = deck(ORDERS.words, ctx), obeyed = deck(ORDERS.obeyed, ctx), missed = deck(ORDERS.missed, ctx), early = deck(ORDERS.early, ctx);
      let open = false, shown = null, earlyAt = 0;
      const css = ctx.css(`#btn.routes-ordered{outline:3px solid ${red(ctx)};outline-offset:3px}`);
      void css;
      const close = () => { open = false; ctx.button.el.classList.remove('routes-ordered'); dropLine(shown); shown = null; };
      const order = () => {
        if (open || !claim(ctx, 'orders', 1400)) return;
        open = true;
        shown = setLine(ctx, words(), STAMP(ctx, 22), 0);
        ctx.button.el.classList.add('routes-ordered');
        ctx.after(1350, () => { if (!open) return; close(); release('orders'); bad(ctx, missed()); });
      };
      ctx.after(900, order);
      ctx.every(8000, order);
      ctx.on('tap', ({ auto, x, y }) => {
        if (auto) return;
        if (!open) {
          if (ctx.state.clock - earlyAt > 45000 && ctx.rng() < 0.03) { earlyAt = ctx.state.clock; bad(ctx, early()); }
          return;
        }
        close();
        release('orders');
        const b = btnCentre(ctx), r = ctx.rates();
        const got = ctx.pay(r.click * 5 + r.fps, 'order', { x: x ?? b.x, y: y ?? b.y });
        ctx.float(b.x, b.top - 8, `+${ctx.fmt(got)}`, `color:${red(ctx)};font:800 22px Arial,Helvetica,sans-serif;`);
        ring(ctx, b.x, b.y, 170, red(ctx), 500);
        good(ctx, obeyed());
        ctx.voice('obey', { gain: 0.5 });
      });
      return { off() { close(); } };
    },
  },

  /* Refused Taps: some taps are refused for no reason, and the next one pays double. Never two in a row. */
  t_denied: {
    mount(ctx) {
      const refused = deck(DENIED_LINES.refused, ctx), next = deck(DENIED_LINES.next, ctx);
      let fate = 'ok', since = 0;
      ctx.mod({ click: (v) => (fate === 'no' ? 0 : fate === 'double' ? v * 2 : v) });
      ctx.on('tap', ({ auto, gain }) => {
        const b = btnCentre(ctx);
        if (fate === 'no') {
          fate = 'double';
          if (auto) return;
          if (!ctx.gentle) ctx.anim(ctx.button.el, [{ transform: 'translateX(-7px)' }, { transform: 'translateX(7px)' }, { transform: 'translateX(-3px)' }, { transform: 'translateX(0)' }], { duration: 260 });
          ctx.float(b.x + b.w / 3, b.y - 14, ROUTE_COPY.refused, `color:${red(ctx)};font:800 34px Arial,Helvetica,sans-serif;`);
          bad(ctx, refused());
          ctx.voice('denied', { gain: 0.4 });
          return;
        }
        if (fate === 'double') {
          fate = 'ok';
          since = 0;
          if (!auto) { good(ctx, next()); ctx.float(b.x, b.top - 8, `+${ctx.fmt(gain)}`, `color:${onDark(ctx) ? '#9dff9d' : GREEN};font-weight:700;font-size:20px;`); }
          return;
        }
        since += 1;
        if (since >= 4 && ctx.rng() < 0.22) fate = 'no';
      });
      return {};
    },
  },

  /* Tap Countdown: twenty taps before it hits zero, once a minute. Losing costs nothing, and says so. */
  t_count: {
    mount(ctx) {
      const NEED = 20, start = deck(COUNT_LINES.start, ctx), won = deck(COUNT_LINES.won, ctx), lost = deck(COUNT_LINES.lost, ctx);
      let live = false, done = 0, line = null, num = null, m = null, stopTick = null, left = 0;
      const clear = () => { live = false; stopTick?.(); dropLine(line); num?.remove(); m?.el.remove(); line = num = m = null; release('count'); };
      const go = () => {
        if (live) return;
        if (!claim(ctx, 'count', 8500)) { ctx.after(3000, go); return; }
        live = true; done = 0; left = 8;
        const b = btnCentre(ctx);
        line = setLine(ctx, fill(start(), { n: NEED }), STAMP(ctx, 18), 0);
        num = ctx.el('div', `position:absolute;left:${b.right + 12}px;top:${b.top - 4}px;${STAMP(ctx, 54)}`, String(left));
        m = bar(ctx, 'floaters', `position:absolute;left:${b.left}px;width:${b.w}px;top:${b.bottom + 4}px;height:7px;`, red(ctx));
        stopTick = ctx.every(1000, () => {
          left -= 1;
          num.textContent = String(left);
          ctx.audio.play('assets/audio/sfx/count_tick.mp3', { bus: 'sfx', gain: 0.5 });
          if (left <= 0) { clear(); bad(ctx, lost()); }
        });
      };
      ctx.after(4000, go);
      ctx.every(60000, go);
      ctx.on('tap', ({ auto }) => {
        if (!live || auto) return;
        done += 1;
        m.set(done / NEED, 120);
        if (done < NEED) return;
        clear();
        const b = btnCentre(ctx), r = ctx.rates();
        ctx.pay(r.click * NEED + r.fps * 10, 'count', { x: b.x, y: b.y });
        burst(ctx, b.x, b.y, red(ctx), 16, 110);
        ctx.audio.play('assets/audio/sfx/streak_milestone.mp3', { bus: 'sfx', gain: 0.6 });
        good(ctx, won());
      });
      return { off() { release('count'); } };
    },
  },

  /* Beat Tapping: a beat runs for a while and keeps speeding up. A tap on the beat pays extra, and the streak grows. */
  t_faster: {
    mount(ctx) {
      const on = deck(BEAT_LINES.on, ctx), off = deck(BEAT_LINES.off, ctx), faster = deck(BEAT_LINES.faster, ctx);
      const row = ctx.el('div', 'display:flex;align-items:center;gap:4px;height:14px;', null, meterRow(ctx));
      const dots = Array.from({ length: 8 }, () => ctx.el('div', 'width:9px;height:9px;border-radius:50%;background:rgba(128,128,128,.4);', null, row));
      let live = false, gap = 850, i = 0, cancel = null, lastBeat = -9999, streak = 0, beats = 0;
      const paint = (n) => dots.forEach((d, j) => { d.style.background = j === n ? red(ctx) : 'rgba(128,128,128,.4)'; });
      const beat = () => {
        if (!live) return;
        paint(i % 8);
        i += 1; beats += 1;
        lastBeat = ctx.state.clock;
        ctx.audio.play('assets/audio/sfx/tally_tick.mp3', { bus: 'sfx', gain: 0.45 });
        const b = btnCentre(ctx);
        ring(ctx, b.x, b.y, 110, red(ctx), 350);
        gap = Math.max(450, gap - 30);
        if (beats % 8 === 0) setLine(ctx, faster(), STAMP(ctx, 16), 900);
        if (beats >= 26) { live = false; paint(-1); release('beat'); return; }
        cancel = ctx.after(gap, beat);
      };
      const start = () => {
        if (live) return;
        if (!claim(ctx, 'beat', 20000)) { ctx.after(3000, start); return; }
        live = true; gap = 850; i = 0; beats = 0; streak = 0;
        beat();
      };
      ctx.after(2500, start);
      ctx.every(45000, start);
      ctx.on('tap', ({ auto, x, y }) => {
        if (!live || auto) return;
        const d = Math.min(ctx.state.clock - lastBeat, Math.abs(lastBeat + gap - ctx.state.clock));
        const b = btnCentre(ctx);
        if (d > Math.min(150, gap * 0.28)) {
          if (streak >= 3) setLine(ctx, off(), STAMP(ctx, 16), 800);
          streak = 0;
          return;
        }
        streak += 1;
        const r = ctx.rates();
        ctx.pay(r.click * Math.min(4, 1 + streak / 4), 'beat', { x: x ?? b.x, y: y ?? b.y });
        if (streak % 4 === 0) setLine(ctx, `${on()} ${streak}`, `color:${onDark(ctx) ? '#9dff9d' : GREEN};font-weight:700;`, 800);
      });
      return { off() { cancel?.(); release('beat'); } };
    },
  },

  /* Hands-Off Test: five seconds of DO NOT. The whole test is not doing it, and passing pays. */
  t_still: {
    mount(ctx) {
      const words = deck(STILL_TEST.words, ctx), passed = deck(STILL_TEST.passed, ctx), failed = deck(STILL_TEST.failed, ctx);
      let live = false, line = null, m = null, cancel = null;
      const clear = () => { live = false; cancel?.(); dropLine(line); m?.el.remove(); line = m = null; ctx.button.el.classList.remove('routes-hands'); release('still'); };
      ctx.css(`#btn.routes-hands{outline:2px dashed ${red(ctx)};outline-offset:3px}`);
      const go = () => {
        if (live) return;
        if (!claim(ctx, 'still', 5200)) { ctx.after(3000, go); return; }
        live = true;
        const b = btnCentre(ctx);
        line = setLine(ctx, words(), STAMP(ctx, 21), 0);
        m = bar(ctx, 'floaters', `position:absolute;left:${b.left}px;width:${b.w}px;top:${b.bottom + 4}px;height:7px;`, red(ctx));
        m.set(1, 5000);
        ctx.button.el.classList.add('routes-hands');
        cancel = ctx.after(5000, () => {
          clear();
          const r = ctx.rates(), c = btnCentre(ctx);
          ctx.pay(r.click * 25 + r.fps * 8, 'still', { x: c.x, y: c.y });
          good(ctx, passed());
          ctx.audio.play('assets/audio/sfx/rank_settle.mp3', { bus: 'sfx', gain: 0.6 });
        });
      };
      ctx.after(1500, go);
      ctx.every(75000, go);
      ctx.on('tap', ({ auto }) => { if (live && !auto) { clear(); bad(ctx, failed()); } });
      return { off() { ctx.button.el.classList.remove('routes-hands'); release('still'); } };
    },
  },

  /* Type the Phrase: on-screen keys or the real keyboard, no text box anywhere. Wrong keys cost nothing. Not now always works. */
  t_type: {
    mount(ctx) {
      const phrases = deck(PHRASES, ctx), right = deck(PHRASE_LINES.right, ctx), wrong = deck(PHRASE_LINES.wrong, ctx);
      const doc = docOf(ctx);
      let handle = null, onKey = null;
      const openCard = () => {
        if (handle && !handle.closed) return;
        const phrase = phrases();
        let at = 0;
        const node = doc.createElement('div');
        const show = doc.createElement('div');
        show.style.cssText = 'font:700 21px/1.3 Arial,Helvetica,sans-serif;background:#fff;border:1px solid #555;padding:8px;text-align:center;white-space:pre-wrap;color:#bbb;min-height:44px;';
        node.appendChild(show);
        const note = doc.createElement('div');
        note.style.cssText = `min-height:20px;margin:6px 0;color:${RED};font-size:15px;`;
        node.appendChild(note);
        const paint = () => {
          show.textContent = '';
          const a = doc.createElement('span'); a.style.color = RED; a.textContent = phrase.slice(0, at);
          show.append(a, phrase.slice(at));
        };
        const press = (ch) => {
          if (ch !== phrase[at]) { note.textContent = wrong(); return; }
          at += 1;
          paint();
          note.textContent = '';
          ctx.audio.play('assets/audio/sfx/ui_click.mp3', { bus: 'sfx', gain: 0.4 });
          if (at < phrase.length) return;
          const r = ctx.rates(), b = btnCentre(ctx);
          ctx.pay(r.click * 30 + r.fps * 15, 'typed', { x: b.x, y: b.y });
          ctx.toast(right());
          handle.close();
        };
        const keys = doc.createElement('div');
        keys.style.cssText = 'display:flex;flex-direction:column;gap:4px;align-items:center;';
        ['qwertyuiop', 'asdfghjkl', 'zxcvbnm'].forEach((row) => {
          const r = doc.createElement('div');
          r.style.cssText = 'display:flex;gap:3px;justify-content:center;width:100%;';
          for (const ch of row) {
            const k = doc.createElement('button');
            k.type = 'button'; k.textContent = ch;
            k.style.cssText = 'min-height:40px;flex:1 1 0;min-width:0;max-width:40px;padding:0;font-size:16px;';
            k.addEventListener('pointerdown', (e) => { e.preventDefault(); press(ch); });
            r.appendChild(k);
          }
          keys.appendChild(r);
        });
        const space = doc.createElement('button');
        space.type = 'button'; space.textContent = ROUTE_COPY.space;
        space.style.cssText = 'min-height:40px;width:60%;padding:0;';
        space.addEventListener('pointerdown', (e) => { e.preventDefault(); press(' '); });
        keys.appendChild(space);
        node.appendChild(keys);
        paint();
        onKey = (e) => {
          if (e.ctrlKey || e.metaKey || e.altKey || e.key.length !== 1) return;
          e.preventDefault();
          press(e.key.toLowerCase());
        };
        doc.addEventListener('keydown', onKey);
        handle = ctx.modal({
          title: PHRASE_LINES.prompt,
          node,
          buttons: [{ label: ROUTE_COPY.notNow }],
          onClose: () => { doc.removeEventListener('keydown', onKey); onKey = null; },
        });
      };
      ctx.after(1500, openCard);
      ctx.every(120000, openCard);
      return { off() { if (onKey) doc.removeEventListener('keydown', onKey); } };
    },
  },

  /* Cornered Button: sent to the edge of its zone for ten seconds, smaller, and still working. Taps out there pay half again. */
  t_corner: {
    mount(ctx) {
      const sent = deck(CORNER_LINES.sent, ctx), back = deck(CORNER_LINES.back, ctx);
      const wrap = ctx.parts.buttonWrap;
      let on = false, out = null;
      ctx.mod({ click: (v) => (on ? v * 1.5 : v) });
      const go = () => {
        if (on) return;
        if (!claim(ctx, 'corner', 10500)) { ctx.after(3000, go); return; }
        on = true;
        const { w } = ctx.bounds(), b = btnCentre(ctx);
        const dx = Math.min(300, (w - b.w * 0.72) / 2 - 8);
        out = ctx.anim(wrap, [{ transform: 'none' }, { transform: `translate(${dx}px,8px) scale(.72) rotate(3deg)` }], { duration: 260, easing: 'ease-out', fill: 'forwards' });
        ctx.emit('routes:corner', { on: true });
        setLine(ctx, sent(), STAMP(ctx, 16), 2600);
        ctx.after(10000, () => {
          on = false;
          out?.reverse();
          ctx.after(300, () => { out?.cancel(); out = null; });
          ctx.emit('routes:corner', { on: false });
          setLine(ctx, back(), STAMP(ctx, 16), 2000);
          release('corner');
        });
      };
      ctx.after(2000, go);
      ctx.every(50000, go);
      return { off() { if (on) ctx.emit('routes:corner', { on: false }); release('corner'); } };
    },
  },

  /* Performance Review: every two minutes a paper arrives with a grade from how much you tapped. A doubles all earnings for thirty seconds. */
  t_grade: {
    mount(ctx) {
      let boost = 0, seen = ctx.state.taps;
      ctx.mod({ fps: (v, s) => (s.clock < boost ? v * 2 : v), click: (v, s) => (s.clock < boost ? v * 2 : v) });
      const tag = ctx.el('div', `font:12px Arial,Helvetica,sans-serif;color:${red(ctx)};`, '', meterRow(ctx));
      const review = () => {
        const taps = ctx.state.taps - seen; const per = taps / Math.max(1, (ctx.state.clock - (ctx.store.get('at') || 0)) / 60000);
        seen = ctx.state.taps;
        ctx.store.set('at', ctx.state.clock);
        const grade = per >= 90 ? 'A' : per >= 50 ? 'B' : per >= 25 ? 'C' : per >= 8 ? 'D' : 'F';
        const lines = REVIEWS[grade], line = lines[Math.floor(ctx.rng() * lines.length)];
        if (!claim(ctx, 'review', 4300, 'low')) { ctx.after(3000, review); return; }
        const { w, h } = ctx.bounds(), pw = Math.min(280, w - 60);
        const p = ctx.el('div', `position:absolute;z-index:3;left:${(w - pw) / 2}px;top:${h - 176 - (ctx.parts.shop.ownerDocument.getElementById('dock-strip')?.offsetHeight || 0)}px;height:168px;width:${pw}px;overflow:hidden;box-sizing:border-box;padding:14px;background:#fbf7ea;color:#222;border:1px solid #999;box-shadow:0 3px 10px rgba(0,0,0,.35);font:15px/1.3 "Times New Roman",Times,serif;`, null, 'over');
        ctx.el('div', 'font-weight:700;font-size:17px;margin-bottom:6px;', REVIEWS.title, p);
        for (let i = 0; i < 3; i++) ctx.el('div', 'height:3px;width:70%;background:#ccc;margin:6px 0;', null, p);
        ctx.el('div', 'margin-top:8px;padding-right:60px;', line, p);
        const stamp = ctx.el('div', `position:absolute;right:14px;top:10px;font:800 64px/1 Arial,Helvetica,sans-serif;color:${RED};transform:rotate(-8deg);opacity:0;`, grade, p);
        ctx.anim(p, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'translateY(100vh) rotate(-2deg)' }, { transform: 'translateY(0) rotate(-2deg)' }], { duration: 450, easing: 'ease-out', fill: 'forwards' });
        ctx.after(800, () => {
          ctx.anim(stamp, ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [{ opacity: 0, transform: 'scale(2.2) rotate(-8deg)' }, { opacity: 1, transform: 'scale(1) rotate(-8deg)' }], { duration: 220, fill: 'forwards' });
          ctx.audio.play('assets/audio/sfx/thud.mp3', { bus: 'sfx', gain: 0.6 });
          if (grade === 'A') {
            boost = ctx.state.clock + 30000;
            tag.textContent = ROUTE_COPY.gradeBoost;
            ctx.after(30000, () => { tag.textContent = ''; });
            const b = btnCentre(ctx);
            burst(ctx, b.x, b.y, RED, 20, 140);
          }
        });
        ctx.after(4200, () => { p.remove(); release('review', 'low'); });
      };
      ctx.after(1000, () => { if (!ctx.store.get('at')) ctx.store.set('at', Math.max(0, ctx.state.clock - 60000)); ctx.toast(ROUTE_COPY.fileOpened); });
      ctx.after(20000, review);
      ctx.every(120000, review);
      return {};
    },
  },
};

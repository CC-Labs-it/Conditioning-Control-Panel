import { community } from '../../meta-count.js';
import { DISCORD_INVITE } from '../../links.js';

const minute = 60_000;
const css = `
/* The dock normally zooms its contents. Let text reflow at its actual width. */
.dk[data-fz-owner="go_pals"]>.dk-b{width:100%!important;zoom:1!important}
.tc-community{container-type:inline-size;box-sizing:border-box;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:5px;font:13px/1.3 var(--font);font-variant-numeric:tabular-nums}
.tc-community-full{padding:10px 12px;display:grid;gap:8px}
.tc-community-full h3{margin:0;padding-bottom:6px;border-bottom:1px solid var(--line);font:700 15px var(--font)}
.tc-community-row{display:grid;grid-template-columns:49px 1fr;align-items:baseline;gap:5px}
.tc-community-row b{font-size:12px}
.tc-community-row small{display:block;color:var(--ink-soft)}
.tc-community-full button{justify-self:start;border:1px solid var(--ink-soft);border-radius:4px;background:var(--panel-short,var(--panel));color:var(--ink);font:600 13px var(--font);padding:5px 12px;min-height:32px;cursor:pointer}
.tc-community-full button:focus-visible{outline:2px solid var(--ink);outline-offset:2px}
.tc-community-inline{display:none;white-space:nowrap;overflow-x:auto;padding:5px;font-size:11px}
@container (max-width:199px){.tc-community-full{display:none}.tc-community-inline{display:block}}
@media (pointer:coarse){.tc-community-full button{min-height:44px}}
`;

function row(ctx, parent, label) {
  const wrap = ctx.el('div', '', null, parent);
  wrap.className = 'tc-community-row';
  ctx.el('b', '', label, wrap);
  const detail = ctx.el('div', '', null, wrap);
  return { count: ctx.el('span', '', '', detail), age: ctx.el('small', '', '', detail) };
}

function ageLabel(sample, now) {
  const age = now - Date.parse(sample?.asOf);
  return age > minute ? Math.floor(age / minute) + ' min ago' : '';
}

export function mountCommunity(ctx) {
  ctx.css(css);
  const body = ctx.dock({ title: 'CCP Community', width: 244, onMin: hidden => { if (!hidden) refresh(); } });
  const doc = body.ownerDocument;
  const card = ctx.el('div', '', null, body);
  card.className = 'tc-community';
  const full = ctx.el('div', '', null, card);
  full.className = 'tc-community-full';
  ctx.el('h3', '', 'CCP Community', full);
  const discordRow = row(ctx, full, 'Discord'), ccpRow = row(ctx, full, 'CCP');
  discordRow.count.title = 'Approximate Discord counts';
  const join = ctx.el('button', '', 'Join Discord', full);
  join.type = 'button';
  join.addEventListener('click', () => ctx.openLink(DISCORD_INVITE));
  const inline = ctx.el('div', '', '', card);
  inline.className = 'tc-community-inline';

  const controller = new AbortController();
  let lastRequest = -Infinity, samples = {}, nextPaint = 0;
  function draw(now) {
    // CDN responses and hidden tabs can outlive the samples they carried.
    const discord = now - Date.parse(samples.discord?.asOf) < 10 * minute ? samples.discord : null;
    const ccp = now - Date.parse(samples.ccp?.asOf) < 3 * minute ? samples.ccp : null;
    const d = discord && ctx.fmt(discord.online), c = ccp && ctx.fmt(ccp.active);
    discordRow.count.textContent = discord ? d + ' online · ' + ctx.fmt(discord.members) + ' members' : 'Unavailable';
    ccpRow.count.textContent = ccp ? c + ' active in the last 3 min' : 'Unavailable';
    const da = ageLabel(discord, now), ca = ageLabel(ccp, now);
    discordRow.age.textContent = da;
    ccpRow.age.textContent = ca;
    inline.textContent = [discord && d + ' on Discord' + (da ? ' (' + da + ')' : ''),
      ccp && c + ' in CCP' + (ca ? ' (' + ca + ')' : '')].filter(Boolean).join(' · ') || 'Unavailable';
    inline.title = inline.textContent;
    nextPaint = Math.min(...[discord, ccp].filter(Boolean).map(s => {
      const start = Date.parse(s.asOf);
      return start + (Math.max(0, Math.floor((now - start) / minute)) + 1) * minute + 1;
    }));
  }
  function refresh() {
    if (controller.signal.aborted || ctx.paused || ctx.state.ended || doc.hidden || body.dock.visible === false) return;
    const now = Date.now();
    if (now >= nextPaint) draw(now);
    if (now - lastRequest < minute) return;
    lastRequest = now;
    community(controller.signal).then(value => {
      if (controller.signal.aborted) return;
      if (value) samples = value;
      draw(Date.now());
    });
  }
  draw(Date.now());
  ctx.on('resume', refresh);
  doc.addEventListener('visibilitychange', refresh);
  ctx.cleanup(() => { controller.abort(); doc.removeEventListener('visibilitychange', refresh); });
  ctx.every(1000, refresh);
  refresh();
  return {};
}

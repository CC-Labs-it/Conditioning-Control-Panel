import { community } from '../../meta-count.js';
import { DISCORD_INVITE } from '../../links.js';
import { crayonStarSvg } from '../basics/gold-fx.js';

const minute = 60_000;
const css = `
/* Only text windows reflow inside the dock's scaled body. */
.dk[data-fz-owner="go_pals"]>.dk-b { width:100%!important; zoom:1!important; }
.tc-community {
  --cc-ink:#372c3f; --cc-cream:#fff8df; --cc-violet:#5145a9;
  container-type:inline-size; position:relative; isolation:isolate; box-sizing:border-box;
  color:var(--cc-ink); background:#f8efd6 url("assets/shop/pencil/rag-paper.webp") repeat center / 210px 168px;
  border:2px solid var(--cc-ink); border-radius:9px 5px 12px 5px / 5px 11px 6px 10px;
  box-shadow:inset 0 0 0 2px #fffaf0,2px 3px 0 #302738;
  font:600 13px/1.18 ShopRound,"Segoe UI",sans-serif; font-variant-numeric:tabular-nums; text-align:left;
}
.tc-community *, .tc-community *::before, .tc-community *::after { box-sizing:border-box; }
.tc-community-full { padding:8px 10px 10px; }
.tc-community-head { position:relative; isolation:isolate; padding:4px 28px 8px 7px; margin:0 -3px 3px; }
.tc-community-head::before {
  content:""; position:absolute; z-index:-1; inset:1px 6px 3px 0; transform:rotate(-1deg);
  background:#eeb049; mask:url("assets/shop/pencil/pigment.webp") center / 100% 100% no-repeat;
}
.tc-community-head h3 {
  padding:0; margin:0; color:#fff9e3; font:600 23px/1.05 ShopRound,"Arial Rounded MT Bold",sans-serif;
  letter-spacing:-.65px; -webkit-text-stroke:2.7px var(--cc-ink); paint-order:stroke fill;
  text-shadow:0 2px 0 var(--cc-ink); transform:rotate(-2deg);
}
.tc-community-star { position:absolute; right:-3px; top:-6px; width:45px; height:45px; transform:rotate(12deg); pointer-events:none; }
.tc-community-row { position:relative; display:grid; grid-template-columns:40px minmax(0,1fr); gap:9px; align-items:center; padding:4px 3px 5px; }
.tc-community-row + .tc-community-row { border-top:1px dashed #b7a4a3; }
.tc-community-art {
  display:grid; place-items:center; width:39px; height:39px; transform:rotate(-8deg); margin:0;
  background:#fffdfa; border:1.6px solid var(--cc-ink); box-shadow:1px 2px 0 #55445b;
  border-radius:11px 8px 9px 6px / 8px 11px 7px 10px; overflow:hidden; pointer-events:none;
}
.tc-community-art img { display:block; width:100%; height:100%; padding:2px; border-radius:inherit; object-fit:contain; }
.tc-community-row[data-source=ccp] .tc-community-art { transform:rotate(6deg); border-radius:50% 46% 49% 44%; }
.tc-community-row b { display:block; font:600 15px/1.1 ShopRound,"Segoe UI",sans-serif; color:var(--cc-violet); }
.tc-community-row[data-source=ccp] b { color:#972e66; }
.tc-community-count { display:block; margin-top:2px; font:650 12px/1.26 "Segoe UI",sans-serif; }
.tc-community-count[data-missing] { color:#655970; }
.tc-community-row small { display:block; color:#695e68; font:500 10px/1.2 "Segoe UI",sans-serif; }
.tc-community-row small:empty { display:none; }
.tc-community-foot { position:relative; display:flex; justify-content:center; margin-top:4px; }
.tc-community-full button {
  position:relative; width:100%; min-height:34px; margin:0; padding:4px 22px; touch-action:manipulation;
  color:white; background:#5846ac; border:2px solid var(--cc-ink); border-radius:9px 6px 12px 7px / 8px 10px 7px 9px;
  box-shadow:0 2px 0 var(--cc-ink); font:600 15px/1 ShopRound,"Segoe UI",sans-serif; cursor:pointer;
}
.tc-community-full button::after {
  content:""; position:absolute; width:8px; height:8px; right:14px; top:50%; border:solid #ffe3a0;
  border-width:2px 2px 0 0; transform:translateY(-50%) rotate(43deg);
}
.tc-community-full button::before {
  content:""; position:absolute; inset:2px; pointer-events:none; border:7px solid transparent;
  border-image:url("assets/shop/pencil/lilac-edge.webp") 20 28 / 6px 10px stretch; opacity:.7;
}
.tc-community-full button:focus-visible { outline:3px solid #b94378; outline-offset:2px; }
.tc-community-full button:active { background:#4a3b92; }
@media (hover:hover) { .tc-community-full button:hover { background:#6552ba; } }
@media (pointer:coarse) { .tc-community-full button { min-height:44px; } }
.tc-community-spark { position:absolute; width:8px; height:12px; left:50%; top:50%; opacity:0; pointer-events:none; background:#f8bc39;
  clip-path:polygon(48% 0,62% 35%,100% 48%,67% 61%,50% 100%,35% 66%,0 52%,34% 37%); }
.tc-community-inline { display:none; white-space:nowrap; overflow-x:auto; padding:5px 6px; font-size:11px; }
@container (max-width:199px) { .tc-community-full { display:none; } .tc-community-inline { display:block; } }
`;

const starArt = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(crayonStarSvg(0));

function artwork(ctx, parent, src) {
  const image = ctx.el('img', '', null, parent);
  image.alt = '';
  image.draggable = false;
  image.src = src;
  return image;
}

function row(ctx, parent, label, icon) {
  const wrap = ctx.el('div', '', null, parent);
  wrap.className = 'tc-community-row';
  wrap.dataset.source = label.toLowerCase();
  const badge = ctx.el('div', '', null, wrap);
  badge.className = 'tc-community-art';
  artwork(ctx, badge, 'assets/img/brand/' + icon);
  const detail = ctx.el('div', '', null, wrap);
  ctx.el('b', '', label, detail);
  const count = ctx.el('span', '', '', detail);
  count.className = 'tc-community-count';
  return { badge, count, age: ctx.el('small', '', '', detail) };
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
  const head = ctx.el('div', '', null, full);
  head.className = 'tc-community-head';
  ctx.el('h3', '', 'CCP Community', head);
  const star = artwork(ctx, head, starArt);
  star.className = 'tc-community-star';
  const discordRow = row(ctx, full, 'Discord', 'discord-mark.png');
  const ccpRow = row(ctx, full, 'CCP', 'ccp-server-icon.png');
  discordRow.count.title = 'Approximate Discord counts';
  const foot = ctx.el('div', '', null, full);
  foot.className = 'tc-community-foot';
  const join = ctx.el('button', '', 'Join Discord', foot);
  join.type = 'button';
  const sparks = [-1, 1].flatMap(side => [-1, 0, 1].map(lane => {
    const el = ctx.el('i', '', null, foot);
    el.className = 'tc-community-spark';
    el.setAttribute('aria-hidden', 'true');
    el.style.left = side < 0 ? '9%' : '91%';
    el.style.background = ['#e975aa', '#ffd153', '#a299ed'][lane + 1];
    return { el, x: side * (lane ? 14 : 19), y: lane * 15 - 3 };
  }));
  const lively = () => !ctx.gentle && !ctx.reduced && !ctx.paused && !doc.hidden && body.dock.visible;
  function stampStar() {
    ctx.anim(star, [
      { transform: 'rotate(12deg) scale(.83)' }, { transform: 'rotate(23deg) scale(1.13)', offset: .46 },
      { transform: 'rotate(12deg) scale(1)' },
    ], { duration: 430, easing: 'ease-out' });
  }
  join.addEventListener('pointerenter', event => {
    if (event.pointerType === 'mouse' && lively()) stampStar();
  });
  join.addEventListener('click', () => {
    if (!ctx.openLink(DISCORD_INVITE) || !lively()) return;
    ctx.anim(join, [{ transform:'translateY(2px) scale(.97)' }, { transform:'scale(1.02)', offset:.55 }, { transform:'none' }], 220);
    for (const { el, x, y } of sparks) ctx.anim(el, [
      { opacity:0, transform:'scale(.4)' }, { opacity:1, offset:.22 },
      { opacity:0, transform:`translate(${x}px,${y}px) rotate(${x * 2}deg) scale(.7)` },
    ], { duration:460, easing:'ease-out' });
  });
  const inline = ctx.el('div', '', '', card);
  inline.className = 'tc-community-inline';

  const controller = new AbortController();
  let lastRequest = -Infinity, samples = {}, nextPaint = 0, firstOpen = true;
  function draw(now) {
    // CDN responses and hidden tabs can outlive the samples they carried.
    const discord = now - Date.parse(samples.discord?.asOf) < 10 * minute ? samples.discord : null;
    const ccp = now - Date.parse(samples.ccp?.asOf) < 3 * minute ? samples.ccp : null;
    const d = discord && ctx.fmt(discord.online), c = ccp && ctx.fmt(ccp.active);
    let changed = false;
    for (const [source, text] of [
      [discordRow, discord ? d + ' online · ' + ctx.fmt(discord.members) + ' members' : null],
      [ccpRow, ccp ? c + ' active in the last 3 min' : null],
    ]) {
      if (text && source.count.textContent !== text && lively()) {
        ctx.anim(source.badge, [{ scale:'1' }, { scale:'1.12', offset:.4 }, { scale:'1' }], 300);
        changed = true;
      }
      source.count.toggleAttribute('data-missing', !text);
      source.count.textContent = text ?? 'Unavailable';
    }
    if (changed) stampStar();
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
    if (firstOpen) {
      firstOpen = false;
      if (lively()) stampStar();
    }
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

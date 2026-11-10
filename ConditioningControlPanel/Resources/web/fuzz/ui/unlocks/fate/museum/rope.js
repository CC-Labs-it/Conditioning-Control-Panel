/* ui/unlocks/fate/museum/rope.js - ft_rope: two small brass posts and a sagging red velvet rope round the main button.
 * Everything ignores the pointer, so taps still reach the button. The rope sways a little on a manual press, at most once
 * per swing (well under 3 Hz); Gentle and reduced motion keep it still. "Someone coughs" stays silent: there is no
 * recorded cough in the tree, and nothing is synthesised. */

const INK = '#2a2031';

const CSS = `
.fz-rope-post { position:absolute; bottom:-15px; width:8px; height:calc(52% + 15px); box-sizing:border-box; pointer-events:none;
  background:linear-gradient(90deg,#8a6420,#f0d37e 40%,#b48630); border:2px solid ${INK}; border-radius:3px; z-index:3; }
.fz-rope-post.l { left:-14px; } .fz-rope-post.r { right:-14px; }
.fz-rope-post i { position:absolute; left:50%; top:-9px; width:14px; height:14px; margin-left:-7px; box-sizing:border-box; border-radius:50%;
  background:radial-gradient(circle at 35% 30%,#fff3c4 0 18%,#e2b851 40%,#9a6d1c); border:2px solid ${INK}; }
.fz-rope-post b { position:absolute; left:50%; bottom:-5px; width:22px; height:7px; margin-left:-11px; box-sizing:border-box; border-radius:50%;
  background:linear-gradient(#e7c461,#9a6d1c); border:2px solid ${INK}; box-shadow:2px 2px 0 rgba(42,32,49,.2); }
.fz-rope { position:absolute; left:-10px; right:-10px; top:52%; height:calc(48% + 13px); overflow:visible; pointer-events:none; z-index:2;
  transform-origin:50% 0; }
`;

export function mountRope(ctx) {
  ctx.css(CSS);
  const wrap = ctx.parts.buttonWrap;
  const doc = wrap.ownerDocument;
  for (const side of ['l', 'r']) {
    const post = ctx.el('div', '', null, wrap);
    post.className = `fz-rope-post ${side}`;
    post.setAttribute('aria-hidden', 'true');
    post.append(doc.createElement('i'), doc.createElement('b'));
  }
  const holder = ctx.el('div', '', null, wrap);
  holder.className = 'fz-rope';
  holder.setAttribute('aria-hidden', 'true');
  const sag = 186 + Math.random() * 10;   // a hand-hung rope, never quite the same twice
  const d = `M1 3 Q50 ${sag.toFixed(1)} 99 3`;
  holder.innerHTML = `<svg viewBox="0 0 100 100" preserveAspectRatio="none" width="100%" height="100%" style="display:block;overflow:visible">
  <path d="${d}" fill="none" stroke="${INK}" stroke-width="10" stroke-linecap="round" vector-effect="non-scaling-stroke"/>
  <path d="${d}" fill="none" stroke="#9c1832" stroke-width="6.5" stroke-linecap="round" vector-effect="non-scaling-stroke"/>
  <path d="${d}" fill="none" stroke="#e05a72" stroke-width="1.6" stroke-linecap="round" stroke-dasharray="7 9" vector-effect="non-scaling-stroke" transform="translate(0 -2.5)" opacity=".85"/>
</svg>`;

  let swinging = null;
  ctx.on('tap', (e) => {
    if (e?.auto || ctx.gentle || ctx.reduced || swinging) return;
    swinging = ctx.anim(holder, [
      { transform: 'scaleY(1) rotate(0deg)' },
      { transform: 'scaleY(1.1) rotate(.8deg)', offset: 0.3 },
      { transform: 'scaleY(.97) rotate(-.5deg)', offset: 0.65 },
      { transform: 'scaleY(1) rotate(0deg)' },
    ], { duration: 950, easing: 'ease-out' });
    const done = () => { swinging = null; };
    swinging.onfinish = done;
    swinging.oncancel = done;
  });
  return {};
}

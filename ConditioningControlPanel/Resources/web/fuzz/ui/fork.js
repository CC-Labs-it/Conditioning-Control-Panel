/* ui/fork.js - the choice card: each option's name, one line, and a live demo from the kit. The demo code is lazy: it
 * loads when the first card opens and only plays while an option is on screen. */

import { FORKS, ITEMS } from '../data/items.js';
import { brandText } from '../data/brand.js';
import { setRich } from './rich-text.js';
import { hueClass } from './palette.js';

let demos = null;
function loadDemos() {
  demos ||= (async () => {
    const { Demo } = await import('./demos/kit.js');
    await Promise.all([import('./demos/basics.js'), import('./demos/routes.js'), import('./demos/themes1.js'), import('./demos/themes2.js'), import('./demos/extras.js'), import('./demos/wave2-spiral.js'), import('./demos/wave2-edges.js'), import('./demos/wave2-meta.js'), import('./demos/score-heat.js'), import('./demos/deep-end.js'), import('./demos/sissy-late.js'), import('./demos/double-wield.js'), import('./demos/warp-tunnel.js')]);
    const {registerTrailDemos} = await import('./demos/trail-upgrades.js');
    registerTrailDemos();
    const { registerEmiUpgradeDemos } = await import('./demos/emi-upgrades.js');
    registerEmiUpgradeDemos();
    const { registerBubbleDemo } = await import('./demos/bambi-dome.js');
    registerBubbleDemo();
    await import('./demos/button-ending.js');
    await import('./demos/fate-themes.js');
    return Demo;
  })();
  return demos;
}

/** The option's demo: its lane's first unlock that has one. */
function demoIdFor(Demo, lane) {
  const row = ITEMS.find((it) => it.lane === lane && Demo.has(it.id));
  return row ? row.id : null;
}

/** `pre`: the lane a Renewal Sky star remembered from last run (Already Picked, Pre-signed): marked and focused, still the player's press. */
export function openFork(doc, cards, forkId, onPick, pre = null) {
  const fork = FORKS[forkId];
  const stops = [];
  const list = doc.createElement('div');
  const entries = Object.entries(fork.opts);
  list.className = 'opts';
  let io = null, held = false;

  const hosts = entries.map(([lane, opt]) => {
    const b = doc.createElement('button');
    b.type = 'button';
    b.className = `opt ${hueClass(lane)}`.trim();
    b.dataset.lane = lane;
    const demo = doc.createElement('div');
    demo.className = 'demo';
    demo.style.cssText = 'aspect-ratio:8/5;background:#d4d0c8;border:1px solid #8a867d;';
    const name = doc.createElement('b');
    name.textContent = brandText(opt.name);
    const what = doc.createElement('small');
    setRich(what, opt.what);
    b.append(demo, name, what);
    if (lane === pre) {
      const tag = doc.createElement('small');
      tag.className = 'opt-pre';
      tag.textContent = 'Picked last run';
      tag.style.cssText = 'display:inline-block;margin-top:4px;padding:1px 6px;border-radius:6px;background:#fff3c4;color:#3b2a10;font-weight:600;';
      b.append(tag);
      b.classList.add('pre');
    }
    b.addEventListener('click', () => { handle.close(); onPick(lane); });
    list.appendChild(b);
    return { lane, demo };
  });

  const handle = cards.open({
    title: fork.name,
    body: fork.ask,
    node: list,
    className: entries.length > 3 ? 'wide fork' : 'fork',
    front: true,
    buttons: [{ label: 'Not yet' }],
    onOpen: start,
    onClose() {
      io?.disconnect();
      for (const h of hosts) if (h.stop) h.stop();
      stops.length = 0;
    },
  });
  /* Wake Up: previews stop with everything else and start again on the way back, unless Gentle came on meanwhile. */
  handle.hold = (on) => {
    held = on;
    if (!on) { start(); return; }
    io?.disconnect();
    io = null;
    for (const h of hosts) if (h.stop) { h.stop(); h.stop = null; }
  };
  function start() {
      /* Previews are not checked against the 3 Hz rule, so Gentle goes without them. */
      if (doc.body.classList.contains('gentle')) { for (const h of hosts) h.demo.hidden = true; return; }
      loadDemos().then((Demo) => {
        if (handle.closed || held) return;
        const startOne = (h) => {
          if (h.stop) return;
          const id = demoIdFor(Demo, h.lane);
          if (id) h.stop = Demo.play(h.demo, id);
        };
        if (typeof IntersectionObserver === 'function') {
          io = new IntersectionObserver((seen) => {
            for (const e of seen) {
              const h = hosts.find((x) => x.demo === e.target);
              if (!h) continue;
              if (e.isIntersecting) startOne(h);
              else if (h.stop) { h.stop(); h.stop = null; }
            }
          });
          hosts.forEach((h) => io.observe(h.demo));
        } else hosts.forEach(startOne);
      }).catch((e) => console.error('demos failed', e));
  }
  return handle;
}

export const _loadDemos = loadDemos;

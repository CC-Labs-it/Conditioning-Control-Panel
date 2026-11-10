import { FACE_HTML } from './button-face-art.js';
import { keyMail, sealedPosts } from '../data/button-lines.js';
import { THEME_TASTES } from './unlocks/theme-skins.js';
import { liveOn } from '../sim/switches.js';
import { whole } from '../sim/format.js';
import { drawFace } from './unlocks/extras/emi-face.js';
import { PET_MOODS, petSettle, petStart, petTap } from './emi-pet.js';
import { inEmiSweet } from '../sim/tap-rate.js';
import { CLOUD_FIRST, cloudGap, nextCloudLabel } from '../data/cloud-lines.js';

const SWEET_HOLD_MS = 450;

export const SKIN_ITEMS = Object.freeze({
  rounded:'round', bubble:'bubble', bigbtn:'big', tinybtn:'tiny',
  si_track:'si-parcel', bi_lips:'bi-lips', dr_hex:'dr-hex', tr_skin:'tr-pendulum', go_worn:'worn', lo_pad:'padlock', ce_bar:'censorbar',
});
export const SKIN_NAMES = Object.freeze({
  round:'Round', bubble:'CCP Bubble', big:'Big', tiny:'Tiny',
  'human-check':'Human Check', 'bi-lips':'Bambi Cloud', 'dr-hex':'Drone Firmware', 'tr-pendulum':'Fortune 8-Ball',
  worn:'Still Watching', padlock:'Keyholder Support', censorbar:'Redacted', 'si-parcel':'Order Tracking', emi:'EMI',
});
export function wardrobeSkins(run, showcase) {
  const active = id => run.owned[id] && liveOn(run.off,id);
  const skins = new Set(Object.entries(SKIN_ITEMS).filter(([id]) => active(id)).map(([,skin]) => skin));
  skins.add('human-check');
  skins.add('emi');
  if (showcase) for (const skin of Object.keys(FACE_HTML)) skins.add(skin);
  const taste = run.store.taste2?.theme;
  if (active('taste2') && Object.hasOwn(THEME_TASTES,taste) && THEME_TASTES[taste].skin) skins.add(THEME_TASTES[taste].skin);
  return [...skins];
}

export const SWING_MS = 3900;
const phase = now => Math.sin(now * 2 * Math.PI / SWING_MS);
export const pendulumCenter = now => Math.abs(phase(now)) < .26;
/* Order Tracking: one courier step per manual tap, then a new order. Kept in the run store so a reload picks up the parcel. */
const PARCEL_STEPS = ['Packed', 'Shipped', 'Out for delivery', 'At your door', 'Please try it on'];
const PARCEL_ITEMS = ['1x frilly uniform', '1x lace gloves', '1x satin bow', '1x pleated skirt', '1x white stockings', '1x pink collar', '1x apron, ruffled'];
function paintParcel(face, bag) {
  const p = bag.parcel ||= { order: 1104, step: 0, item: 0 };
  const card = face.classList.contains('tc-parcel') ? face : face.querySelector('.tc-parcel');
  if (!card) return;
  card.classList.toggle('arrived', p.step === PARCEL_STEPS.length - 1);
  card.querySelector('small').textContent = 'ORDER #' + p.order + ' · TRACKING';
  card.querySelector('b').textContent = PARCEL_STEPS[p.step];
  card.querySelector('.tc-pc-item').textContent = PARCEL_ITEMS[p.item % PARCEL_ITEMS.length] + ' · size: yours';
  card.querySelectorAll('.tc-pc-track i').forEach((dot, i) => { dot.classList.toggle('on', i <= p.step); dot.classList.toggle('now', i === p.step); });
}
function advanceParcel(bag) {
  const p = bag.parcel ||= { order: 1104, step: 0, item: 0 };
  if (p.step < PARCEL_STEPS.length - 1) p.step += 1;
  else { p.step = 0; p.order += 1; p.item += 1; }
}
const fortunes = ['All signs point down.','Outlook: horizontal.','Ask again, slower.','Oh, you will.','Already halfway there.','Deeply likely.','You tell me, sleepyhead.','The suspense suits you.'];
const decks = {padlock:keyMail, censorbar:sealedPosts};

// Store indexes so a wardrobe change or reload continues the current deck.
function drawPair(bag, skin, take = true) {
  const cards = decks[skin];
  let pile = bag[skin];
  const first = !Array.isArray(pile);
  if (first || !pile.length || pile.some(i => !Number.isInteger(i) || i < 0 || i >= cards.length)) {
    pile = cards.map((_,i) => i);
    if (first) pile.shift();
    for (let i = pile.length - 1; i > 0; i--) {
      const j = Math.floor(Math.random() * (i + 1));
      [pile[i],pile[j]] = [pile[j],pile[i]];
    }
    if (first) pile.push(0);
    else if (pile.at(-1) === bag[skin+'Last']) [pile[0],pile[pile.length-1]] = [pile.at(-1),pile[0]];
    bag[skin] = pile;
  }
  if (!take) return cards[pile.at(-1)];
  const index = pile.pop();
  bag[skin+'Last'] = index;
  return cards[index];
}

export function makeFace(doc, skin) {
  if (!Object.hasOwn(FACE_HTML,skin) && skin !== 'bubble') return null;
  const box = doc.createElement('span');
  box.className = 'fz-face';
  if (skin === 'bubble') {
    box.classList.add('fz-bubble');
    box.setAttribute('aria-label','Pop bubble');
  } else box.innerHTML = FACE_HTML[skin];
  if (skin === 'emi') dressEmi(box);
  return box;
}

/* EMI's screen face goes on top of her body picture. Answers the parts the pet ladder repaints. */
function dressEmi(box) {
  const art = box.querySelector('.tc-emi-art');
  if (!art) return null;
  return { art, img: art.querySelector('img'), tag: box.querySelector('.tc-emi-tag'), face: drawFace(art, 'happy') };
}

export function bindButtonFace(button, engine, gentle) {
  let skin = '', box = null, face = null, expires = 0, taps = 0;
  let pending = null, rotor = null, angle = '', fortuneIndex = 0, held = false;
  let emi = null, pet = petStart(), hopAt = -1e9, hearts = 0, sweetUntil = 0;
  let cloud = CLOUD_FIRST, cloudAt = 0, cloudW = 0;
  const bank = () => engine.run.store.buttonReplies ||= {};
  const doc = button.ownerDocument;
  function reset() {
    expires = 0;
    if (!box) return;
    box.innerHTML = FACE_HTML[skin] || '';
    face = box.firstElementChild;
    rotor = skin === 'tr-pendulum' ? face.querySelector('.tc-pend') : null;
    angle = '';
    held = gentle();
    if (rotor && held) face.querySelector('.tc-readout').textContent = 'Tap for a reading';
    if (pending) face.querySelector('b').textContent = pending[0];
    if (skin === 'si-parcel') paintParcel(face, bank());
    if (skin === 'bi-lips') { const b = face.querySelector('b'); b.textContent = cloud; if (cloudW) b.style.minWidth = cloudW + 'px'; }
    emi = skin === 'emi' ? dressEmi(box) : null;
    if (emi) { pet = petStart(); paintEmi(false); }
  }
  /* The mood shows as body pose, screen face and tag. Gentle: a short fade instead of a jump. */
  function paintEmi(fade) {
    const m = PET_MOODS[pet.rung];
    const src = 'assets/img/emi/' + m.pose + '.png';
    if (!emi.img.src.endsWith(src)) emi.img.src = src;
    emi.face.paint(m.face);
    emi.tag.textContent = m.tag;
    emi.art.dataset.mood = m.id;
    if (fade && gentle()) emi.art.animate?.([{ opacity: .55 }, { opacity: 1 }], { duration: 240, easing: 'ease-out' });
  }
  function hopEmi(rung) {
    const real = performance.now();
    if (gentle() || real - hopAt < 120) return;
    hopAt = real;
    const frames = rung >= 4
      ? [{ transform: 'none' }, { transform: 'translateX(-4px) rotate(-6deg) scale(1.06,.92)' }, { transform: 'translateX(4px) rotate(5deg)' }, { transform: 'translateX(-2px) rotate(-2deg)' }, { transform: 'none' }]
      : [{ transform: 'none' }, { transform: 'translateY(2px) scale(1.12,.86)' }, { transform: 'translateY(-9px) scale(.94,1.08)' }, { transform: 'none' }];
    emi.art.animate?.(frames, { duration: rung >= 4 ? 300 : 280, easing: 'ease-out' });
    if (rung > 2 || hearts >= 3) return;
    const heart = doc.createElement('i');
    heart.className = 'tc-emi-heart';
    heart.textContent = '♥';
    heart.setAttribute('aria-hidden', 'true');
    const dx = Math.round(Math.random() * 40 - 20);
    heart.style.left = 'calc(50% + ' + dx + 'px)';
    emi.art.parentElement.append(heart);
    hearts += 1;
    const a = heart.animate?.([{ transform: 'translate(-50%,0) scale(.6)', opacity: 0 }, { transform: 'translate(-50%,-14px) scale(1)', opacity: 1, offset: .25 }, { transform: 'translate(-50%,-40px) scale(.9)', opacity: 0 }], { duration: 700, easing: 'ease-out' });
    const done = () => { heart.remove(); hearts -= 1; };
    if (a) a.onfinish = a.oncancel = done; else done();
  }
  function equip(name) {
    if (skin === name) return;
    box?.remove();
    button.removeAttribute('data-art-face');
    button.removeAttribute('aria-label');
    skin = name; box = makeFace(doc,skin); pending = null; rotor = null; taps = 0; expires = 0; cloud = CLOUD_FIRST; cloudAt = 0;
    if (!box) return;
    button.dataset.artFace = skin;
    if (skin === 'bubble') button.setAttribute('aria-label','Pop bubble');
    if (skin === 'emi') button.setAttribute('aria-label','Pet EMI');
    button.append(box);
    if (Object.hasOwn(decks,skin)) {
      pending = drawPair(bank(),skin,false);
    }
    reset();
  }
  /* Bambi Cloud (owner 2026-11-10): the label turns to another login line or Bambi trigger every 6 to 12 s, with a short
   * fade (a slow one under Gentle). Math.random only. The tap's "Welcome back, Bambi" holds it (expires), then reset()
   * puts the current label back. The bouncing copies re-clone on the text change (bouncing-art.js watchFace). */
  function turnCloud() {
    const now = engine.now;
    if (!cloudAt) {
      /* The face keeps the width of its first line, so a shorter label never shrinks the button. */
      const first = face?.querySelector('b'), w = first?.offsetWidth || 0;
      if (w > 0 && cloud === CLOUD_FIRST) { cloudW = w; first.style.minWidth = w + 'px'; }
      cloudAt = now + cloudGap();
      return;
    }
    if (now < cloudAt) return;
    cloudAt = now + cloudGap();
    const b = face?.querySelector('b'), next = nextCloudLabel(cloud);
    if (!b) return;
    cloud = next;
    const quiet = gentle(), put = () => { if (!expires && face?.contains(b)) b.textContent = cloud; };
    const out = b.animate?.([{ opacity: 1 }, { opacity: 0 }], { duration: quiet ? 700 : 140, easing: 'ease-in', fill: 'forwards' });
    if (!out) { put(); return; }
    out.onfinish = () => { put(); out.cancel(); b.animate([{ opacity: 0 }, { opacity: 1 }], { duration: quiet ? 900 : 220, easing: 'ease-out' }); };
  }
  /* The sweet spot cue holds SWEET_HOLD_MS of game time past the last press in it, so a steady rhythm reads as one glow. */
  function markSweet(on, now) {
    if (on) sweetUntil = now + SWEET_HOLD_MS;
    const show = sweetUntil > now;
    if (show !== emi.art.hasAttribute('data-sweet')) emi.art.toggleAttribute('data-sweet', show);
  }
  function tick() {
    if (!box) return;
    if (emi) {
      if (emi.art.hasAttribute('data-sweet')) markSweet(false, engine.now);
      const next = petSettle(pet, engine.now);
      if (next !== pet) { pet = next; paintEmi(true); }
      /* Idle: a blink now and then, held still under Gentle. */
      if (!pet.rung) { const t = engine.now % 4600; emi.face.paint(!gentle() && t > 4200 && t < 4320 ? '-_-' : '0_0'); }
    }
    if (expires && engine.now >= expires) {
      if (Object.hasOwn(decks,skin)) {
        pending = drawPair(bank(),skin,false);
      }
      reset();
    }
    if (skin === 'bi-lips' && !expires) turnCloud();
    if (rotor) {
      const quiet = gentle();
      if (quiet !== held && !expires) face.querySelector('.tc-readout').textContent = quiet ? 'Tap for a reading' : 'Tap on center';
      held = quiet;
      const pose = quiet ? 'none' : 'rotate(' + (28 * phase(engine.now)).toFixed(2) + 'deg)';
      if (pose !== angle) { rotor.style.transform = pose; angle = pose; }
    }
  }
  engine.subscribe('tap', ({auto,gain,via,mult}) => {
    if (auto || !box || !face) return;
    const now = engine.now;
    if (emi) {
      const sweet = via === 'hand' && inEmiSweet(mult);
      if (via === 'hand') markSweet(sweet, now);
      const before = pet.rung;
      pet = petTap(pet, now, sweet);
      if (pet.rung !== before) paintEmi(true);
      hopEmi(pet.rung);
    } else if (Object.hasOwn(decks,skin)) {
      const pair = drawPair(bank(),skin);
      face.classList.add('done');
      face.querySelector('small').textContent = pair[0];
      face.querySelector('b').textContent = pair[1];
      expires = now + 4200;
    } else if (skin === 'tr-pendulum') {
      const center = pendulumCenter(now);
      if (!gentle() && !center) {
        if (!expires) face.querySelector('.tc-readout').textContent = 'Wait for the notch';
        return;
      }
      face.classList.add('answered');
      face.querySelector('strong').textContent = fortunes[fortuneIndex++ % fortunes.length];
      const paysDouble = !gentle() && engine.run.owned.tr_skin && liveOn(engine.run.off,'tr_skin');
      face.querySelector('.tc-readout').textContent = paysDouble ? 'Perfect · +'+whole(gain) : 'Fortune drawn';
      expires = now + 1700;
    } else if (skin === 'dr-hex') {
      const installed = expires > now && taps % 2 === 1;
      taps = installed ? 2 : 1;
      face.querySelector('b').textContent = installed ? 'INSTALLING USER' : 'DOWNLOADING';
      face.querySelector('small').textContent = installed ? 'user: you   [100%]' : 'firmware.exe   [99%]';
      face.querySelector('.tc-meter').style.transform = 'scaleX('+(installed ? 1 : .99)+')';
      expires = now + (installed ? 2200 : 7000);
    } else if (skin === 'si-parcel') {
      advanceParcel(bank());
      paintParcel(face, bank());
    } else if (skin === 'bi-lips') {
      face.querySelector('b').textContent = 'Welcome back, Bambi'; face.querySelector('i').textContent = '✓'; expires = now + 2000;
    } else if (skin === 'human-check') {
      face.classList.add('done'); face.querySelector('.tc-box').textContent = '✓';
      face.querySelector('b').textContent = 'Human enough.'; expires = now + 2000;
    } else if (skin === 'worn') {
      face.querySelector('b').textContent = 'Ep. '+(++taps)+' · Just one more';
      face.querySelector('.tc-playbtn span').textContent = 'Next episode';
      face.style.setProperty('--tc-pos',(13 + taps * 9 % 70)+'%'); expires = now + 3500;
    }
  });
  return {equip,tick};
}

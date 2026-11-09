/* Shared art across shop, guide, purchase flights and switches. */
import { ICON_CATALOG } from './icon-catalog.js';
import { badgeMarkup, iconMarkup } from './icon-art.js';
import { clips } from '../assets/clipart/clips.js';
import { amethystEnamel } from './dispenser-enamel.js';

export const ICON_SETS = Object.freeze({ plain: 'plain', glossy: 'glossy', emoji: 'plain' });
let current = 'plain';
const runtimeArt = { a_tick: 'metronome-icon-96', toy_box: 'toy-box-icon-96', dispenser: 'dispenser-icon-96' };
const ns = 'http://www.w3.org/2000/svg';
const active = new WeakMap();
let dispenserIconUrl, loadingDispenserIcon;

function paintIcon(svg, id) {
  svg.innerHTML = markupFor(id);
  svg.dataset.finish = current;
  if (current !== 'glossy' || id !== 'dispenser') return;
  const layer = svg.firstElementChild;
  if (dispenserIconUrl) { layer.setAttribute('href',dispenserIconUrl); return; }
  if (!loadingDispenserIcon) {
    const image = svg.ownerDocument.createElement('img');
    image.src = layer.getAttribute('href');
    loadingDispenserIcon = image.decode()
      .then(() => dispenserIconUrl = amethystEnamel(image).toDataURL())
      .catch(() => { loadingDispenserIcon = null; });
  }
  loadingDispenserIcon.then(url => { if (url) layer.setAttribute('href',url); });
}

function markupFor(id) {
  const art = ICON_CATALOG[id];
  if (current === 'glossy') return runtimeArt[id]
    ? `<image href="assets/glossy/${runtimeArt[id]}.webp" width="64" height="64"/>`
    : iconMarkup(art, true);
  const { x, y, w, h } = clips[art.shape];
  // The sheets use 128 pixels for the original 64-unit viewBox. Extra margins
  // allow high hops, swings and their shadows to travel outside the rest pose.
  return `<svg data-clip="${art.shape}" x="${x / 2}" y="${y / 2}" width="${w / 2}" height="${h / 2}" viewBox="0 0 ${w} ${h}" overflow="hidden"><image href="assets/clipart/${art.shape}.webp" width="${w}" height="${h}"/></svg>` + badgeMarkup(art.badge);
}

export function stopIconMotion(doc) {
  const clip = active.get(doc);
  if (!clip) return;
  active.delete(doc);
  clip.animation?.cancel();
  clip.sheet?.remove();
  clip.poster.style.visibility = '';
}

function allowsMotion(svg) {
  const doc = svg.ownerDocument;
  return svg.isConnected && !svg.closest('[inert]') && !doc.body.classList.contains('gentle') &&
    !doc.defaultView.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

export async function playIcon(svg) {
  const viewport = svg?.querySelector('[data-clip]');
  if (!viewport || !allowsMotion(svg)) return;
  const doc = svg.ownerDocument;
  stopIconMotion(doc);
  const clip = { poster: viewport.firstElementChild };
  active.set(doc, clip);
  const shape = viewport.dataset.clip;
  const url = 'assets/clipart/' + shape + '.motion.webp';
  const preload = new doc.defaultView.Image();
  preload.src = url;
  try { await preload.decode(); }
  catch { if (active.get(doc) === clip) stopIconMotion(doc); return; }
  if (active.get(doc) !== clip) return;
  if (!viewport.isConnected || !allowsMotion(svg)) { stopIconMotion(doc); return; }
  const { frames, columns, w, h, ms } = clips[shape];
  const sheet = doc.createElementNS(ns, 'image');
  sheet.setAttribute('width', w * columns);
  sheet.setAttribute('height', h * Math.ceil(frames / columns));
  sheet.setAttribute('href', url);
  clip.sheet = sheet;
  viewport.appendChild(sheet);
  clip.poster.style.visibility = 'hidden';
  const keys = Array.from({ length: frames }, (_, i) => ({
    offset: i / (frames - 1), easing: 'steps(1, end)',
    transform: `translate(${-(i % columns) * w}px, ${-Math.floor(i / columns) * h}px)`,
  }));
  clip.animation = sheet.animate(keys, { duration: ms, fill: 'forwards' });
  clip.animation.onfinish = () => { if (active.get(doc) === clip) stopIconMotion(doc); };
}

function wireMotion(doc) {
  function target(event) {
    const control = event.target.closest?.('button, [role="button"]');
    return control && !control.contains(event.relatedTarget) ? control.querySelector('svg[data-fz-icon]') : null;
  }
  doc.addEventListener('pointerover', event => { if (event.pointerType !== 'touch') playIcon(target(event)); });
  doc.addEventListener('pointerdown', event => { if (event.pointerType === 'touch') playIcon(target(event)); });
  doc.addEventListener('focusin', event => { if (event.target.matches(':focus-visible')) playIcon(target(event)); });
}
const documents = new Set();
export const iconSet = () => current;
export function setIconSet(name) {
  if (!Object.hasOwn(ICON_SETS, name)) return;
  current = ICON_SETS[name];
  for (const doc of documents) {
    stopIconMotion(doc);
    for (const svg of doc.querySelectorAll('svg[data-fz-icon]')) {
      paintIcon(svg,svg.dataset.fzIcon);
    }
  }
}

function styles(doc) {
  if (documents.has(doc)) return;
  documents.add(doc);
  wireMotion(doc);
  const s = doc.createElement('style');
  s.textContent = `
    svg.ico { display:inline-block;flex:none;width:48px;height:48px;overflow:visible;
      --art-a:color-mix(in srgb,var(--hue-a,var(--niche,#ee9bb7)) 55%,#fff0cf);
      --art-b:color-mix(in srgb,var(--hue-b,var(--niche-2,#a7c8dd)) 40%,#a8d8df); }
    .row svg.ico { width:46px;height:46px; }
    #goal .row svg.ico { width:58px;height:58px; }
    .purchase-ghost svg.ico { width:32px;height:32px; }
    .dk-i svg.ico { width:24px;height:24px;vertical-align:middle; }
    @media (max-width:759px) {
      .row svg.ico { width:40px;height:40px; }
      #goal .row svg.ico { width:46px;height:46px; }
    }
  `;
  doc.head.appendChild(s);
}

export function iconFor(doc, item) {
  const id = item.iconId || item.id;
  const art = ICON_CATALOG[id];
  if (!art) throw new Error(`Missing clip art for ${item.id}`);
  styles(doc);
  const svg = doc.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '0 0 64 64');
  svg.setAttribute('width', '48');
  svg.setAttribute('height', '48');
  svg.setAttribute('class', 'ico');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('focusable', 'false');
  svg.dataset.fzIcon = id;
  paintIcon(svg,id);
  return svg;
}

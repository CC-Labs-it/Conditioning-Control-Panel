/* hud/hitCamChrome.js - the HIT CAM inset's DOM chrome (render/hitCam.js drives it every frame it shows).
 *
 * The picture itself is drawn by the renderer into the canvas (render/post.js); this lies exactly over that
 * rect with the same transform (slide, tilt, scale about the centre): an ink frame with a pink offset shadow,
 * a CSS vignette, the exposure flash that decays from contact, the pink glow pulse, the tilted HIT CAM chip with
 * its blinking rec dot (INCOMING while the shot is still flying), the victim's name and the item, and the comic
 * impact word popping in. Styles live in styles.css (the HIT CAM section). Writes touch the DOM only on a change.
 * pointer-events none, aria-hidden: the toast already says the hit in words.
 */

export function createHitCamChrome(doc = globalThis.document, parent = doc && doc.body) {
  if (!doc || !parent) return { render() {}, hide() {}, dispose() {}, el: null };
  const root = doc.createElement('div');
  root.className = 'hitcam';
  root.setAttribute('aria-hidden', 'true');
  root.hidden = true;
  root.innerHTML = '<div class="hitcam-frame"><i class="hitcam-vig"></i><i class="hitcam-flash"></i>' +
    '<b class="hitcam-word"></b><span class="hitcam-chip"><i></i><em></em></span>' +
    '<span class="hitcam-tag"><b></b><small></small></span></div>';
  parent.appendChild(root);
  const $ = (s) => root.querySelector(s);
  const el = { frame: $('.hitcam-frame'), flash: $('.hitcam-flash'), word: $('.hitcam-word'), chip: $('.hitcam-chip'),
    chipText: $('.hitcam-chip em'), name: $('.hitcam-tag b'), item: $('.hitcam-tag small') };
  const last = new Map();
  /** Set a style / text only when it changed. */
  function put(node, key, value, text = false) {
    const id = key + (text ? '#t' : '');
    const k = node === el.frame ? 'f' : node === el.flash ? 'l' : node === el.word ? 'w' : node === el.chip ? 'c' : node === el.chipText ? 'ct' : node === el.name ? 'n' : 'i';
    if (last.get(k + id) === value) return;
    last.set(k + id, value);
    if (text) node.textContent = value;
    else if (key.startsWith('--')) node.style.setProperty(key, value);
    else node.style[key] = value;
  }
  const r2 = (x) => Math.round(x * 100) / 100;
  return {
    el: root,
    /**
     * v = { x, y, w, h (CSS px, the seat), dx (slide px), rotDeg, scale, flash 0..1, glow 0..1,
     *   word { alpha, scale }, wordText, chip 0..1, chipText, name, item, gentle }
     */
    render(v) {
      if (root.hidden) root.hidden = false;
      root.classList.toggle('gentle', !!v.gentle);
      put(el.frame, 'left', Math.round(v.x) + 'px');
      put(el.frame, 'top', Math.round(v.y) + 'px');
      put(el.frame, 'width', Math.round(v.w) + 'px');
      put(el.frame, 'height', Math.round(v.h) + 'px');
      put(el.frame, 'transform', 'translateX(' + r2(v.dx) + 'px) rotate(' + v.rotDeg + 'deg) scale(' + r2(v.scale) + ')');
      put(el.frame, '--glow', String(r2(v.glow)));
      put(el.flash, 'opacity', String(r2(v.flash)));
      put(el.word, 'opacity', String(r2(v.word.alpha)));
      put(el.word, 'transform', 'rotate(-8deg) scale(' + r2(v.word.scale) + ')');
      put(el.word, 'textContent', v.wordText || '', true);
      put(el.chip, 'opacity', String(r2(v.chip)));
      put(el.chip, 'transform', 'rotate(-4deg) scale(' + r2(1 + (1 - v.chip) * 0.8) + ')');
      put(el.chipText, 'textContent', v.chipText || 'HIT CAM', true);
      put(el.name, 'textContent', v.name || '', true);
      put(el.item, 'textContent', v.item || '', true);
    },
    hide() { if (!root.hidden) root.hidden = true; },
    dispose() { root.remove(); last.clear(); },
  };
}

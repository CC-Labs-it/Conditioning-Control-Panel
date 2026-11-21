import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../../vendor/three/three.module.min.js';
import { createExpoMedia } from '../ui/expo/media-panes.js';

class Node extends EventTarget {
  constructor(tag, doc) {
    super(); this.tagName = tag.toUpperCase(); this.ownerDocument = doc; this.children = [];
    this.style = {}; this.dataset = {}; this.attributes = new Map(); this.clientHeight = 600;
    this.paused = true; this.complete = true; this.naturalWidth = 640; this.readyState = 4; this.videoWidth = 640;
  }
  set src(value) { this.attributes.set('src', value); }
  get src() { return this.attributes.get('src') || ''; }
  setAttribute(key, value) { this.attributes.set(key, value); }
  getAttribute(key) { return this.attributes.get(key) ?? null; }
  removeAttribute(key) { this.attributes.delete(key); }
  append(...nodes) { for (const node of nodes) { node.remove(); node.parentNode = this; this.children.push(node); } }
  insertBefore(node) { this.append(node); }
  replaceChildren(...nodes) { for (const child of [...this.children]) child.remove(); this.append(...nodes); }
  remove() { if (this.parentNode) { const list = this.parentNode.children; list.splice(list.indexOf(this), 1); this.parentNode = null; } }
  play() { this.paused = false; return Promise.resolve(); }
  pause() { this.paused = true; }
  load() {}
}
const flush = async () => { for (let i = 0; i < 40; i++) await Promise.resolve(); };
function harness(kind) {
  const doc = { hidden: false, baseURI: 'https://expo.test/fuzz/index.html', nodes: [] };
  doc.createElement = tag => { const node = new Node(tag, doc); doc.nodes.push(node); return node; };
  const row = { kind, url: `https://expo.test/${kind === 'clip' ? 'clip.mp4' : 'motion.gif'}` };
  let selections = 0;
  const ctx = { gentle: false, reduced: false, paused: false, css() {}, on() { return () => {}; },
    pictures: { source: 'own', next: async () => { selections++; return row; },
      acquire: async item => ({ url: item.url, release() {} }) } };
  const camera = new THREE.PerspectiveCamera(68, 1, .08, 220);
  const object = new THREE.Object3D(); object.position.z = -5; object.updateMatrixWorld(true);
  const dome = { inside: false, depth: 0, mediaOccluders: [],
    picturePanes: [{ object, offset: 0, width: 2, height: 2, opacity: 1 }] };
  const media = createExpoMedia(ctx, doc.createElement('div'), camera, dome);
  return { ctx, dome, media, row, doc, get selections() { return selections; } };
}

for (const kind of ['clip', 'gif']) test(`live motion preference stops and resumes an Expo ${kind} without a pause event`, async () => {
  const h = harness(kind);
  try {
    h.media.draw(); await flush();
    assert.equal(h.selections, 0, 'an inactive room never requests personal media');
    h.dome.inside = true; h.dome.depth = 1; h.media.draw(); await flush();
    const player = h.doc.nodes.find(node => node.tagName === (kind === 'clip' ? 'VIDEO' : 'IMG'));
    assert.ok(player, 'the active pane acquired a player');
    const moving = () => kind === 'clip' ? !player.paused : player.src === h.row.url;
    assert.ok(moving());
    for (const preference of ['reduced', 'gentle']) {
      h.ctx[preference] = true; h.media.draw(); await flush();
      assert.equal(moving(), false, `${preference} stops motion immediately`);
      h.ctx[preference] = false; h.media.draw(); await flush();
      assert.equal(moving(), true, `motion resumes when ${preference} is disabled`);
    }
    h.dome.inside = false; h.media.draw(); await flush();
    assert.equal(moving(), false, 'leaving the room releases the player');
    assert.equal(player.src, '');
  } finally { h.media.dispose(); }
});

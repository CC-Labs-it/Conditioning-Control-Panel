/* ============================================================================
 * board/annotate.js - draws the player's arrows and circles on the board.
 *
 * The gesture and the shapes live in board/annotations.js (no three.js there,
 * so it is tested in Node). This file finds the square under the cursor the way
 * drag.js does (a man first, so his head names his own square, then the board
 * plane) and lays flat unlit meshes over the board: drawn last and without a
 * depth test, so an arrow reads across the men it passes, like on lichess.
 * Local only: nothing is sent, saved or put in a replay.
 *
 * Wiring: boot.js builds it after the drag hand and pumps nothing; it redraws
 * only when the shapes change.
 * ==========================================================================*/

import * as THREE from 'three';
import { squareToWorld, worldToSquare } from './scene.js';
import { createAnnotator } from './annotations.js';

export const TUNING = Object.freeze({
  y: 0.03,              // just over the move hints
  color: 0x15781B,      // the lichess green: reads on pink and on cream
  opacity: 0.78,
  previewOpacity: 0.45, // the one still being drawn
  shaft: 0.16,          // arrow width, in squares
  head: 0.42,           // head width, in squares
  headLen: 0.38,        // head length, in squares
  ringInner: 0.40,      // circle, in squares
  ringOuter: 0.48,
});

const T = TUNING;
const NOPE = () => {};  // never pickable: drag.js raycasts the men under these

export function createAnnotate({ view, isBusy = () => false }) {
  const canvas = view.renderer.domElement;
  const ray = new THREE.Raycaster();
  const ndc = new THREE.Vector2();
  const ground = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  const hit = new THREE.Vector3();
  const a = new THREE.Vector3();
  const b = new THREE.Vector3();
  const unit = squareToWorld('b1', 0, new THREE.Vector3()).distanceTo(squareToWorld('a1', 0, a));

  const root = new THREE.Group();
  root.name = 'pbp-annotations';
  root.renderOrder = 20;
  view.boardGroup.add(root);

  const mat = (opacity) => new THREE.MeshBasicMaterial({
    color: T.color, transparent: true, opacity, depthTest: false, depthWrite: false, side: THREE.DoubleSide,
  });
  const solid = mat(T.opacity);
  const faint = mat(T.previewOpacity);
  const ring = new THREE.RingGeometry(T.ringInner * unit, T.ringOuter * unit, 48).rotateX(-Math.PI / 2);

  function squareAt(ev) {
    const r = canvas.getBoundingClientRect();
    if (!r.width || !r.height) return null;
    ndc.set(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
    ray.setFromCamera(ndc, view.camera);
    const hits = view.pieceGroup ? ray.intersectObjects(view.pieceGroup.children, true) : [];
    for (const h of hits) {
      let node = h.object;
      while (node && node.parent !== view.pieceGroup) node = node.parent;
      if (node && node.userData.square) return node.userData.square;
    }
    if (!ray.ray.intersectPlane(ground, hit)) return null;
    return worldToSquare(hit.x, hit.z);   // the board sits at the origin, as drag.js assumes
  }

  /** One flat arrow, tail at the centre of `from`, tip at the centre of `to`. */
  function arrowGeometry(from, to) {
    squareToWorld(from, 0, a);
    squareToWorld(to, 0, b);
    const len = a.distanceTo(b);
    const head = Math.min(T.headLen * unit, len * 0.6);
    const shaft = len - head;
    const w = (T.shaft * unit) / 2;
    const hw = (T.head * unit) / 2;
    // drawn along +x in the x/y plane, then laid down and turned
    const s = new THREE.Shape();
    s.moveTo(0, -w); s.lineTo(shaft, -w); s.lineTo(shaft, -hw); s.lineTo(len, 0);
    s.lineTo(shaft, hw); s.lineTo(shaft, w); s.lineTo(0, w); s.closePath();
    const g = new THREE.ShapeGeometry(s).rotateX(-Math.PI / 2);
    g.rotateY(-Math.atan2(b.z - a.z, b.x - a.x));
    g.translate(a.x, 0, a.z);
    return g;
  }

  function add(shape, material) {
    const own = shape.kind === 'arrow';
    const m = new THREE.Mesh(own ? arrowGeometry(shape.from, shape.to) : ring, material);
    if (!own) { squareToWorld(shape.from, 0, a); m.position.set(a.x, 0, a.z); }
    m.position.y = T.y;
    m.userData.own = own;
    m.renderOrder = 20;
    m.raycast = NOPE;
    root.add(m);
  }

  function draw(shapes, preview) {
    for (const m of [...root.children]) { root.remove(m); if (m.userData.own) m.geometry.dispose(); }
    for (const s of shapes) add(s, solid);
    if (preview && !shapes.some(s => s.from === preview.from && s.to === preview.to)) add(preview, faint);
  }

  const hand = createAnnotator({ target: canvas, squareAt, onChange: draw, blocked: isBusy });

  return {
    ...hand,
    count: () => root.children.length,
    dispose() {
      hand.dispose();
      draw([], null);
      ring.dispose(); solid.dispose(); faint.dispose();
      view.boardGroup.remove(root);
    },
  };
}

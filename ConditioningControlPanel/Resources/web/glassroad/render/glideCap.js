/* Accepted W4 attachment: the cap stays mounted while only its existing spin child opens and turns. */
export const GLIDE_ASSET = 'glide-keep-beanie';
export function createGlideCap(art, model) {
  const asset = art?.theme === 'boudoir' ? 'glide-boudoir-beanie' : GLIDE_ASSET;
  const source = art?.model?.(asset), mount = model.getObjectByName('head_attach');
  const row = art?.row?.(asset), base = art?.row?.('emi-teacup-kart');
  if (!source || !mount || (row?.baseSha256 && base?.sha256 && row.baseSha256 !== base.sha256)) return null;
  const cap = source.scene.clone(true), spin = cap.getObjectByName('spin');
  if (!spin) return null;
  cap.position.set(0, 0, 0); cap.quaternion.identity(); cap.scale.setScalar(1);
  const materials = new Set();
  cap.traverse(o => {
    if (!o.isMesh) return;
    o.frustumCulled = false;
    if (Array.isArray(o.material)) o.material = o.material.map(m => { const c = m.clone(); materials.add(c); return c; });
    else if (o.material) { o.material = o.material.clone(); materials.add(o.material); }
  });
  const rest = spin.quaternion.clone();
  let open = 0, angle = 0;
  spin.scale.setScalar(0); cap.visible = false; mount.add(cap);
  return {
    cap, spin,
    update(active, dt, gentle = false) {
      if (dt <= 0) return;
      open += ((active ? 1 : 0) - open) * (1 - Math.exp(-dt / .12));
      if (!active && open < .001) open = 0;
      cap.visible = active || open > 0;
      spin.scale.setScalar(open);
      angle = (angle + dt * (gentle ? 10 : 24) * open) % (Math.PI * 2);
      spin.quaternion.copy(rest); spin.rotateY(angle);
    },
    reset() { open = 0; angle = 0; cap.visible = false; spin.scale.setScalar(0); spin.quaternion.copy(rest); },
    dispose() { cap.removeFromParent(); for (const m of materials) m.dispose(); },
  };
}

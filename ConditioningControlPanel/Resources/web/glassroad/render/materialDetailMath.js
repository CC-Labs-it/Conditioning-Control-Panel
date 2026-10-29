/* Keep the material slice to two shipped scalar maps. No normals, new passes or UV changes. */
export const MATERIAL_DETAIL_IDS = Object.freeze(['detail-porcelain-roughness', 'detail-gold-wear']);
export function materialDetailId(id, tier) { return tier === 'phone' ? id + '-phone' : id; }

/** Existing source roughness controls highlight width; unknown sources retain the former soft finish. */
export function surfaceFinish(src = {}) {
  const roughness = Number.isFinite(src.roughness) ? Math.max(0.18, Math.min(0.85, src.roughness)) : 0.5;
  const metalness = Math.max(0, Math.min(1, Number(src.metalness) || 0));
  return { roughness, metalness, exponent: 12 + (1 - roughness) * (1 - roughness) * 116,
    gain: 0.24 + (1 - roughness) * 0.3 + metalness * 0.35 };
}

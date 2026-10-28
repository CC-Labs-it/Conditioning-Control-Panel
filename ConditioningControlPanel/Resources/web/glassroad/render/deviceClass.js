/* render/deviceClass.js - the DEVICE behind the quality tier. Pure, no three.js.
 *
 * The tier (render/quality.js pickTier) is the LOOK: art LODs, particle counts, geometry segments. A pinned
 * Quality option replaces it, and a phone pinned to High used to lose every phone protection with it, because
 * each one keyed on the tier name: iOS then killed the GPU process at the end of the warm (owner's screenshot,
 * 2026-10-28, "loading stalled after water (threshold, webgpu, high)"). The MEMORY protections (staged warm,
 * the water probe, mirror resolution, decoder counts, the clips the cover waits for) now key on the device,
 * which no pin can change: a coarse pointer, or a host whose caps say phone.
 */

/** The live device block, set once by createRenderer; readers that have no renderer handle read it here. */
export const DEVICE = { name: 'desktop' };

/** 'phone' when the pointer is coarse or the host platform says phone, else 'desktop'. */
export function deviceClass({ coarse = false, platform = null } = {}) {
  return platform === 'phone' || coarse ? 'phone' : 'desktop';
}

/** The tier a MEMORY budget reads: a phone always budgets as the phone tier, whatever the pinned look. */
export function memoryGuard(device, tier) {
  return device === 'phone' ? 'phone' : tier;
}

export function setDevice(name) {
  DEVICE.name = name === 'phone' ? 'phone' : 'desktop';
  return DEVICE.name;
}

/** memoryGuard against the live device block. */
export function memoryTier(tier) {
  return memoryGuard(DEVICE.name, tier);
}

/** True when a pinned Quality sits above the auto pick (lowest first order; 'auto' or an unknown pin is never above). */
export function pinAboveAuto(pin, auto, order = ['phone', 'mid', 'high']) {
  const p = order.indexOf(pin), a = order.indexOf(auto);
  return p >= 0 && a >= 0 && p > a;
}

/** The options card's Quality hint: the phone warning when a pin on a phone sits above the auto pick, else the stock line. */
export const QUALITY_ABOVE_AUTO = 'Above Auto on a phone: may run out of memory';
export function qualityHint(pin, { device = 'desktop', autoTier = null } = {}, stock = 'Auto follows your PC') {
  return device === 'phone' && pinAboveAuto(pin, autoTier) ? QUALITY_ABOVE_AUTO : stock;
}

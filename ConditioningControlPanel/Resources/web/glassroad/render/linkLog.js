/* Dev only (?logLinks=1): every render pipeline (WebGL2: one linked program) three builds is logged into
 * globalThis.__links with the page phase, the object, its material and the pass that asked for it, so a program
 * that links mid-race (a 50-300 ms Firefox stall: it links one by one, on the main thread) names itself.
 * Read it with JSON.stringify(__links.filter(l => l.phase === 'racing')). Never on without the flag. */
const born = new WeakMap();   // material -> where it was made (dev only, set while the log is on)
const pass = (stack) => {
  const s = String(stack || '');
  for (const [k, re] of [['shadow', /updateShadow|renderShadow|shadow/i], ['cube', /waterReflections/], ['inset', /renderInset|warmInset/],
    ['warm', /warmAll|stagedWarm|compileAsync|garageWarm|warmLights/], ['scene', /render\/post\.js|RenderPipeline/]]) if (re.test(s)) return k;
  return 'other';
};
const label = (o) => { if (!o) return ''; const path = []; for (let p = o; p && path.length < 4; p = p.parent) if (p.name || p === o) path.push(p.name || p.type); return path.join(' < '); };

export function logPipelineLinks(three, { Material = null, phase = () => (globalThis.__glassroad && globalThis.__glassroad.phase) || '' } = {}) {
  const backend = three && three.backend;
  if (!backend || typeof backend.createRenderPipeline !== 'function') return false;
  const log = globalThis.__links = [], seen = new Set();
  const setValues = Material && Material.prototype.setValues;
  if (setValues) Material.prototype.setValues = function(values) {
    if (!born.has(this)) born.set(this, (new Error().stack || '').split('\n').slice(2, 8).map((l) => l.replace(/https?:\/\/[^/]+\//, '')).join(' | '));
    return setValues.call(this, values);
  };
  const create = backend.createRenderPipeline;
  backend.createRenderPipeline = function(renderObject, promises) {
    const t0 = performance.now();
    try { return create.call(this, renderObject, promises); } finally {
      if (renderObject && renderObject.material) queueMicrotask(() => seen.add(renderObject.material.id));
      const ro = renderObject || {}, m = ro.material || {}, stack = new Error().stack || '';
      log.push({ t: Math.round(t0), ms: Math.round((performance.now() - t0) * 10) / 10, phase: phase(), pass: pass(stack), tier: (globalThis.__glassroad && globalThis.__glassroad.tier) || '', shadowType: three.shadowMap && three.shadowMap.type, shadowOn: three.shadowMap && three.shadowMap.enabled,
        object: label(ro.object), objectType: ro.object && ro.object.type, material: (m.type || '') + (m.name ? ' ' + m.name : ''), matId: m.id, born: born.get(m) || '', seenMat: seen.has(m.id), count: ro.object && ro.object.count, geo: ro.geometry && Object.keys(ro.geometry.attributes || {}).join(','),
        target: ro.context && ro.context.renderTarget ? (ro.context.renderTarget.texture && ro.context.renderTarget.texture.name) || 'rt' : 'canvas',
        stack: stack.split('\n').slice(2, 12).map((l) => l.replace(/https?:\/\/[^/]+\//, '').replace(/vendor\/three\/build\/three\.webgpu\.min\.js/, 'T')).join(' | ') });
    }
  };
  return true;
}

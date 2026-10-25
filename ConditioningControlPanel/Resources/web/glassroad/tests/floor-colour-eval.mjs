// Small independent interpreter for arithmetic TSL graphs. Fixed fwidth represents a near-road pixel.
// This executes the real colour graph, rather than copying any floor pattern's equations.
export function evaluateColour(root, footprint = .01) {
  const cache = new Map(), expand = (v, i) => v.length === 1 ? v[0] : v[i];
  const apply = (fn, ...values) => Array.from({ length: Math.max(...values.map(v => v.length)) }, (_, i) => fn(...values.map(v => expand(v, i))));
  const smooth = (a, b, x) => { const t = Math.max(0, Math.min(1, (x - a) / (b - a))); return t * t * (3 - 2 * t); };
  function run(n) {
    if (cache.has(n)) return cache.get(n);
    let v;
    if (n.isInputNode) v = n.value?.toArray ? n.value.toArray() : [n.value];
    else if (n.condNode) v = run(run(n.condNode)[0] ? n.ifNode : n.elseNode);
    else if (n.isSplitNode) { const x = run(n.node); v = [...n.components].map(k => x['xyzw'.indexOf(k)]); }
    else if (n.node) v = run(n.node);
    else if (n.nodes) v = n.nodes.flatMap(run);
    else if (n.isOperatorNode) {
      const operations = { '+': (a,b) => a+b, '-': (a,b) => a-b, '*': (a,b) => a*b, '/': (a,b) => a/b, '%': (a,b) => a-b*Math.floor(a/b), '<': (a,b) => a<b };
      if (!operations[n.op]) throw new Error('Unsupported operator '+n.op);
      v = apply(operations[n.op], run(n.aNode), run(n.bNode));
    } else if (n.isMathNode) {
      const a = run(n.aNode), b = n.bNode && run(n.bNode), c = n.cNode && run(n.cNode);
      const unary = { sin: Math.sin, cos: Math.cos, abs: Math.abs, floor: Math.floor, fract: x => x-Math.floor(x), fwidth: () => footprint };
      const binary = { max: Math.max, min: Math.min, step: (edge,x) => x<edge?0:1, pow: Math.pow };
      if (unary[n.method]) v = apply(unary[n.method], a);
      else if (binary[n.method]) v = apply(binary[n.method], a, b);
      else if (n.method === 'length') v = [Math.hypot(...a)];
      else if (n.method === 'atan') v = b ? apply(Math.atan2, a, b) : apply(Math.atan, a);
      else if (n.method === 'mix') v = apply((x,y,t) => x*(1-t)+y*t, a,b,c);
      else if (n.method === 'clamp') v = apply((x,lo,hi) => Math.min(hi,Math.max(lo,x)), a,b,c);
      else if (n.method === 'smoothstep') v = apply(smooth, a,b,c);
      else throw new Error('Unsupported math '+n.method);
    } else throw new Error('Unsupported node '+Object.keys(n).join(','));
    cache.set(n,v); return v;
  }
  return run(root);
}

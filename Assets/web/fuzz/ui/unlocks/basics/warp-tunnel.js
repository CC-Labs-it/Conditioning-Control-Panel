/* ui/unlocks/basics/warp-tunnel.js - the Warp Tunnel's renderer: one full-screen fragment shader, a real tunnel in depth.
 * Owner 2026-11-08: the old effect (three bordered frames scaling down) read as "just rectangles moving in".
 * The walls are helical ribbons in the theme colours, rings rush past, the far end fades to whatever is behind
 * (the desktop, a spiral) so the player falls toward it. Pulses are bright rings that leave the far end and
 * rush out toward the viewer (one per manual press). Own colours only, no pictures in GL: Scrolller files carry no
 * CORS and can never be a texture on the web (AGENTS.md T&C SCROLLLER CORS). Pure drawing, no game state. */

const VERT = 'attribute vec2 p;void main(){gl_Position=vec4(p,0.,1.);}';
const FRAG = `precision mediump float;
uniform vec2 uRes,uC;uniform float uT,uSpin,uAlpha;uniform vec3 uA,uB,uD;uniform vec4 uRings;
void main(){
  vec2 p=(gl_FragCoord.xy-uC)/uRes.y;
  float r=length(p)+1e-4;
  float a=atan(p.y,p.x)*.15915494;
  float z=.3/r;
  float zz=z+uT;
  float u=a+uSpin+z*.04;
  float fu=fract(u*8.+zz*.22);
  float fz=fract(zz*1.1);
  float cu=smoothstep(.45,.55,fu);
  float cz=smoothstep(.45,.55,fz);
  float chk=abs(cu-cz);
  float groove=min(min(fu,1.-fu)*3.,min(fz,1.-fz)*6.);
  groove=smoothstep(0.,.12,groove);
  vec3 col=mix(uB,uA,chk);
  col*=.45+.55*groove;
  float glow=0.;
  for(int i=0;i<4;i++){float d=z-uRings[i];glow+=exp(-d*d*60.)*step(.01,uRings[i]);}
  col=mix(col,mix(uA,vec3(1.),.6),clamp(glow,0.,1.)*.85);
  float lit=.3+.7*smoothstep(.03,.75,r);
  col*=lit;
  col=mix(uD,col,smoothstep(.03,.35,r));
  float al=uAlpha*smoothstep(.035,.2,r);
  gl_FragColor=vec4(col*al,al);
}`;

/** A WebGL tunnel on `canvas`, or null when the browser gives no WebGL (the caller shows nothing). */
export function createTunnelGL(canvas) {
  const gl = canvas.getContext('webgl', { premultipliedAlpha: true, alpha: true, antialias: false, depth: false, stencil: false, powerPreference: 'low-power' });
  if (!gl) return null;
  let prog = null, loc = null;
  const build = () => {
    const sh = (type, src) => { const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s); return s; };
    prog = gl.createProgram();
    gl.attachShader(prog, sh(gl.VERTEX_SHADER, VERT));
    gl.attachShader(prog, sh(gl.FRAGMENT_SHADER, FRAG));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) { prog = null; return; }
    gl.useProgram(prog);
    const buf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    const at = gl.getAttribLocation(prog, 'p');
    gl.enableVertexAttribArray(at);
    gl.vertexAttribPointer(at, 2, gl.FLOAT, false, 0, 0);
    loc = {};
    for (const n of ['uRes', 'uC', 'uT', 'uSpin', 'uAlpha', 'uA', 'uB', 'uD', 'uRings']) loc[n] = gl.getUniformLocation(prog, n);
  };
  build();
  if (!prog) return null;
  let lost = false;
  canvas.addEventListener('webglcontextlost', (e) => { e.preventDefault(); lost = true; });
  canvas.addEventListener('webglcontextrestored', () => { lost = false; build(); });
  return {
    get ok() { return !lost && !!prog; },
    /** s: { t, spin, alpha, cx, cy (0-1 of the canvas, from top-left), a, b, d ([r,g,b] 0-1), rings (up to 4 depths, 0 = none) } */
    draw(s) {
      if (lost || !prog) return;
      const w = canvas.width, h = canvas.height;
      gl.viewport(0, 0, w, h);
      gl.clearColor(0, 0, 0, 0);
      gl.clear(gl.COLOR_BUFFER_BIT);
      gl.uniform2f(loc.uRes, w, h);
      gl.uniform2f(loc.uC, s.cx * w, (1 - s.cy) * h);
      gl.uniform1f(loc.uT, s.t);
      gl.uniform1f(loc.uSpin, s.spin);
      gl.uniform1f(loc.uAlpha, s.alpha);
      gl.uniform3fv(loc.uA, s.a);
      gl.uniform3fv(loc.uB, s.b);
      gl.uniform3fv(loc.uD, s.d);
      const r = s.rings || [];
      gl.uniform4f(loc.uRings, r[0] || 0, r[1] || 0, r[2] || 0, r[3] || 0);
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    },
    dispose() { gl.getExtension('WEBGL_lose_context')?.loseContext(); },
  };
}

/** A CSS colour (custom properties and color-mix included) as [r,g,b] in 0-1, through the browser's own parser. */
export function cssRgb(doc, value, fallback = [1, 0.4, 0.7]) {
  const probe = doc.createElement('span');
  probe.style.cssText = 'position:absolute;width:0;height:0;visibility:hidden;';
  probe.style.color = value;
  doc.body.appendChild(probe);
  const got = doc.defaultView.getComputedStyle(probe).color;
  probe.remove();
  const nums = (got.match(/-?[\d.]+(e-?\d+)?/g) || []).map(Number);
  if (nums.length < 3) return fallback;
  // rgb(255, 0, 10) is 0-255; color(srgb 1 0 .04) is 0-1
  return /^color\(/.test(got) ? nums.slice(0, 3) : nums.slice(0, 3).map((n) => n / 255);
}

export const mixRgb = (x, y, t) => x.map((v, i) => v + (y[i] - v) * t);

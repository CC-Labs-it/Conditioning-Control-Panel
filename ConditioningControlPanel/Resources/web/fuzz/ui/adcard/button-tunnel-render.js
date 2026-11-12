import { WALL_FRAGMENT, SCREEN_VERTEX, DUST_VERTEX, DUST_FRAGMENT } from './button-tunnel-shaders.js';

const LENS = 1.42;
const HALF_TURN = Math.PI;
const FULL_TURN = HALF_TURN * 2;
const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));
export const tunnelLens = LENS;

function program(gl, vertex, fragment, attributes, uniforms) {
  const link = gl.createProgram();
  for (const [type, source] of [[gl.VERTEX_SHADER, vertex], [gl.FRAGMENT_SHADER, fragment]]) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(shader));
    gl.attachShader(link, shader);
    gl.deleteShader(shader);
  }
  gl.linkProgram(link);
  if (!gl.getProgramParameter(link, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(link));
  const attr = Object.fromEntries(attributes.map(name => [name, gl.getAttribLocation(link, name)]));
  const loc = Object.fromEntries(uniforms.split(' ').map(name => [name, gl.getUniformLocation(link, name)]));
  return { link, attr, loc };
}

function makeBuffer(gl, array) {
  const buffer = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(array), gl.STATIC_DRAW);
  return buffer;
}

function drawGL(canvas, gl) {
  const camera = 'resolution gaze eye lens';
  const wall = program(gl, SCREEN_VERTEX, WALL_FRAGMENT, ['point'], camera+' clock spin entry capture calm');
  const dust = program(gl, DUST_VERTEX, DUST_FRAGMENT, ['point'], camera+' clock motion opacity');
  const quad = makeBuffer(gl, [-1,-1, 3,-1, -1,3]);
  let seed = 17431;
  const rand = () => { seed = (seed*16807)%2147483647; return seed/2147483647; };
  const motes = [];
  for(let i=0;i<430;i++) motes.push(rand()*FULL_TURN, .88+rand()*3.45, -5+rand()*82);
  const stars = makeBuffer(gl, motes);
  function cameraUniforms(p, s) {
    const u=p.loc;
    gl.uniform2f(u.resolution,canvas.width,canvas.height);
    gl.uniform2f(u.gaze,s.yaw,s.pitch);
    gl.uniform3f(u.eye,s.x,s.y,s.z);
    gl.uniform1f(u.lens,LENS);
    gl.uniform1f(u.clock,s.time);
  }
  function attr(index, size, stride=0, offset=0) {
    gl.enableVertexAttribArray(index);
    gl.vertexAttribPointer(index,size,gl.FLOAT,false,stride,offset);
  }
  function paint(s) {
    if(gl.isContextLost()) return;
    gl.viewport(0,0,canvas.width,canvas.height);
    gl.disable(gl.DEPTH_TEST);gl.disable(gl.BLEND);
    gl.useProgram(wall.link);cameraUniforms(wall,s);
    gl.uniform1f(wall.loc.spin,s.spin);gl.uniform1f(wall.loc.entry,s.entry);
    gl.uniform1f(wall.loc.capture,s.capture);gl.uniform1f(wall.loc.calm,s.calm?1:0);
    gl.bindBuffer(gl.ARRAY_BUFFER,quad);attr(wall.attr.point,2);
    gl.drawArrays(gl.TRIANGLES,0,3);gl.disableVertexAttribArray(wall.attr.point);
    if(!s.calm && s.entry>.1){
      gl.useProgram(dust.link);cameraUniforms(dust,s);
      gl.uniform1f(dust.loc.opacity,s.entry*(1-s.capture));gl.uniform1f(dust.loc.motion,1);
      gl.enable(gl.BLEND);gl.blendFuncSeparate(gl.SRC_ALPHA,gl.ONE,gl.ZERO,gl.ONE);
      gl.bindBuffer(gl.ARRAY_BUFFER,stars);attr(dust.attr.point,3);
      gl.drawArrays(gl.POINTS,0,motes.length/3);gl.disableVertexAttribArray(dust.attr.point);
    }
  }
  function dispose(){
    gl.deleteBuffer(quad);gl.deleteBuffer(stars);
    for(const p of [wall,dust]) gl.deleteProgram(p.link);
  }
  return {paint,dispose};
}

// Same walk, lens and cutout positions on devices without WebGL. Rings and
// tracks are projected into the view, so both walking and turning still work.
function draw2D(canvas){
  const ctx=canvas.getContext('2d');
  function paint(s){
    if(!ctx) return;
    const w=canvas.width,h=canvas.height,f=h*.5*LENS;
    const cy=Math.cos(s.yaw),sy=Math.sin(s.yaw),cp=Math.cos(s.pitch),sp=Math.sin(s.pitch);
    function project(x,y,z){
      x-=s.x;y-=s.y;z-=s.z;
      const rx=cy*x-sy*z, rz=sy*x+cy*z;
      const ry=cp*y-sp*rz,d=sp*y+cp*rz;
      if(d<.10) return null;
      return [w*.5+f*rx/d,h*.5-f*ry/d,f/d];
    }
    function line(points,color,width){
      ctx.strokeStyle=color;ctx.lineWidth=width;ctx.beginPath();
      let started=false;
      for(const p of points){
        if(!p){started=false;continue;}
        if(started)ctx.lineTo(p[0],p[1]);else ctx.moveTo(p[0],p[1]);
        started=true;
      }
      ctx.stroke();
    }
    ctx.clearRect(0,0,w,h);ctx.save();
    ctx.globalAlpha=s.entry;
    const portal=project(0,0,78);
    const px=portal?.[0]??w*.5,py=portal?.[1]??h*.5;
    const ambient=ctx.createRadialGradient(px,py,0,px,py,Math.max(w,h)*.8);
    ambient.addColorStop(0,'#c96d9e');ambient.addColorStop(.15,'#452351');ambient.addColorStop(.68,'#211d45');ambient.addColorStop(1,'#120f2a');
    ctx.fillStyle=ambient;ctx.fillRect(0,0,w,h);
    for(let zz=76;zz>Math.max(-6,s.z-7);zz-=3){
      const pts=[];
      for(let j=0;j<=32;j++){
        const a=j*FULL_TURN/32+s.spin*.7;
        pts.push(project(4.6*Math.cos(a),4.6*Math.sin(a),zz));
      }
      line(pts, Math.round(zz/3)%2?'#9877aa':'#4d878e',clamp(f/Math.max(1,zz-s.z)*.025,.5,4));
    }
    for(let j=0;j<10;j++){
      const pts=[];
      for(let zz=Math.max(-6,s.z-5);zz<79;zz+=.85){
        const a=j*FULL_TURN/10-zz*.106-s.spin*FULL_TURN;
        pts.push(project(4.55*Math.cos(a),4.55*Math.sin(a),zz));
      }
      const color=['#ff77be','#82e2e6','#a67bf2','#fed193','#eb92db'][j%5];
      line(pts,color,3);
    }
    for(const x of [-1.44,1.44]){
      const points=[];
      for(let z=s.z-2;z<=78;z+=.5)points.push(project(x,-1.48,z));
      line(points,'#ffa7d8',2.7);
    }
    if(portal){
      const r=clamp(portal[2]*3.1,4,Math.max(w,h)*3);
      const halo=ctx.createRadialGradient(px,py,0,px,py,r);
      halo.addColorStop(0,'#fff4f6');halo.addColorStop(.18,'#ffd6ed');halo.addColorStop(.42,'#ff78bfc7');halo.addColorStop(1,'#fa5da600');
      ctx.fillStyle=halo;ctx.fillRect(px-r,py-r,2*r,2*r);
    }
    ctx.restore();
    if(s.capture>.52){ctx.fillStyle='rgba(255,220,235,'+clamp((s.capture-.52)/.48,0,1)+')';ctx.fillRect(0,0,w,h);}
  }
  return {paint};
}

export function createTunnelRenderer(doc, host, lite){
  let canvas=doc.createElement('canvas'), renderer, finished=false;
  canvas.setAttribute('aria-hidden','true');host.append(canvas);
  function useFallback(){
    const replacement=canvas.cloneNode(false);canvas.replaceWith(replacement);canvas=replacement;
    renderer=draw2D(canvas);
  }
  try{
    const gl=canvas.getContext('webgl',{alpha:true,premultipliedAlpha:false,depth:false,antialias:false,powerPreference:'low-power'});
    if(!gl)useFallback();
    else{
      renderer=drawGL(canvas,gl);
      const release=renderer.dispose;
      renderer.dispose=()=>{release();if(!gl.isContextLost())gl.getExtension('WEBGL_lose_context')?.loseContext();};
      canvas.addEventListener('webglcontextlost',()=>{if(!finished){renderer.dispose();useFallback();}},{once:true});
    }
  }catch(error){console.warn('Button tunnel switched to the canvas renderer.',error);useFallback();}
  return {
    paint(s,width,height){
      const density=Math.min(doc.defaultView.devicePixelRatio||1,lite?1:1.25, (lite?720:1250)/Math.max(1,width,height));
      const w=Math.max(1,Math.round(width*density)),h=Math.max(1,Math.round(height*density));
      if(canvas.width!==w||canvas.height!==h){canvas.width=w;canvas.height=h;}
      renderer.paint(s);
    },
    dispose(){finished=true;renderer.dispose?.();canvas.remove();}
  };
}

import { BUBBLE_OPTICS_GLSL, refractBambiRay } from './bambi-atmosphere.js';

// A single inward-facing projection surface. Both renderers use exactly this
// mapping, including the alternate path for host media that disallows WebGL.
const TAU = 2 * Math.PI;
const DOME_RADIUS = 480;
const CAMERA_LIMIT = 16.8;
const clamp = (x,min,max) => Math.max(min,Math.min(max,x));

function pointOnDome(u,v) {
  const theta = (u-.5)*TAU, phi = v*Math.PI*.5;
  const radius = DOME_RADIUS*Math.cos(phi);
  return [radius*Math.sin(theta), DOME_RADIUS*Math.sin(phi), -radius*Math.cos(theta)];
}

function hemisphere(columns,rows) {
  const vertices=[], uvs=[], indices=[];
  for (let y=0;y<=rows;y++) for (let x=0;x<=columns;x++) {
    const u=x/columns, v=y/rows;
    vertices.push(...pointOnDome(u,v));
    uvs.push(u,1-v);
    if(x===columns || y===rows) continue;
    const a=y*(columns+1)+x, b=a+1, c=a+columns+1;
    indices.push(a,b,c);
    if(y<rows-1) indices.push(b,c+1,c);
  }
  return {vertices:new Float32Array(vertices), uvs:new Float32Array(uvs), indices:new Uint16Array(indices)};
}

const VERTEX = `
attribute vec3 aPosition;
uniform vec3 uEye;
uniform vec4 uBasis;
uniform vec2 uLens;
varying vec3 vPosition;
void main() {
  vec3 p = aPosition - uEye;
  float side = uBasis.y * p.x + uBasis.x * p.z;
  float forward = uBasis.x * p.x - uBasis.y * p.z;
  float up = uBasis.w * p.y - uBasis.z * forward;
  float depth = uBasis.w * forward + uBasis.z * p.y;
  gl_Position = vec4(side * uLens.x, up * uLens.y, depth * 1.0002 - .1, depth);
  vPosition = aPosition;
}`;

const FRAGMENT = `
precision highp float;
uniform sampler2D uProjection;
uniform bool uExternalMask;
uniform vec3 uEye;
uniform float uTime;
varying vec3 vPosition;
${BUBBLE_OPTICS_GLSL}
void main() {
  vec3 ray = bambiRefractedRay(normalize(vPosition-uEye),uEye,uTime);
  float b = dot(uEye,ray);
  float hit = -b + sqrt(max(0.0,b*b + 230400.0-dot(uEye,uEye)));
  vec3 n = normalize(uEye+ray*hit);
  vec2 uv = vec2(fract(.5+atan(n.x,-n.z)/6.28318530718),asin(clamp(n.y,0.0,1.0))/.5/3.14159265359);
  vec3 color = texture2D(uProjection,uv).rgb;
  float alpha=uExternalMask?1.0:smoothstep(0.0,.082,max(0.0,normalize(vPosition).y));
  gl_FragColor = vec4(color*alpha,alpha);
}`;

function shader(gl,type,text) {
  const stage=gl.createShader(type);
  gl.shaderSource(stage,text);
  gl.compileShader(stage);
  if(gl.getShaderParameter(stage,gl.COMPILE_STATUS)) return stage;
  const message=gl.getShaderInfoLog(stage);
  gl.deleteShader(stage);
  throw new Error(message);
}

function webglProjection(canvas) {
  const gl=canvas.getContext('webgl',{alpha:true,antialias:true,depth:false,stencil:false,premultipliedAlpha:true,powerPreference:'low-power'});
  if(!gl) return null;
  const mesh=hemisphere(96,36);
  const vs=shader(gl,gl.VERTEX_SHADER,VERTEX), fs=shader(gl,gl.FRAGMENT_SHADER,FRAGMENT);
  const program=gl.createProgram();
  gl.attachShader(program,vs); gl.attachShader(program,fs); gl.linkProgram(program);
  gl.deleteShader(vs); gl.deleteShader(fs);
  if(!gl.getProgramParameter(program,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(program));
  gl.useProgram(program);
  const positions=gl.createBuffer(), indices=gl.createBuffer(), texture=gl.createTexture();
  gl.bindBuffer(gl.ARRAY_BUFFER,positions); gl.bufferData(gl.ARRAY_BUFFER,mesh.vertices,gl.STATIC_DRAW);
  const attribute=gl.getAttribLocation(program,'aPosition');
  gl.enableVertexAttribArray(attribute); gl.vertexAttribPointer(attribute,3,gl.FLOAT,false,0,0);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,indices); gl.bufferData(gl.ELEMENT_ARRAY_BUFFER,mesh.indices,gl.STATIC_DRAW);
  gl.bindTexture(gl.TEXTURE_2D,texture); gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,true);
  gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
  const at=name=>gl.getUniformLocation(program,name);
  const eye=at('uEye'), basis=at('uBasis'), lens=at('uLens'), time=at('uTime'),externalMask=at('uExternalMask');
  gl.uniform1i(at('uProjection'),0);
  gl.clearColor(0,0,0,0);
  let lastSource=null, lastRevision=-1, tw=0, th=0;
  return {
    draw(source,revision,camera,axes,masked=false) {
      if(gl.isContextLost()) return false;
      if(source!==lastSource||revision!==lastRevision) {
        if(source.width!==tw||source.height!==th||source!==lastSource) {
          gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,gl.RGBA,gl.UNSIGNED_BYTE,source);
          tw=source.width; th=source.height;
        } else gl.texSubImage2D(gl.TEXTURE_2D,0,0,0,gl.RGBA,gl.UNSIGNED_BYTE,source);
        lastSource=source; lastRevision=revision;
      }
      gl.viewport(0,0,canvas.width,canvas.height); gl.clear(gl.COLOR_BUFFER_BIT);
      gl.uniform3f(eye,camera.x,camera.y,camera.z); gl.uniform4fv(basis,axes);
      const scale=1/Math.tan(camera.fovY*.5);
      gl.uniform2f(lens,scale*camera.height/camera.width,scale);
      gl.uniform1f(time,camera.opticalTime);gl.uniform1i(externalMask,masked?1:0);
      gl.drawElements(gl.TRIANGLES,mesh.indices.length,gl.UNSIGNED_SHORT,0);
      return true;
    },
    dispose() {
      gl.deleteBuffer(positions); gl.deleteBuffer(indices); gl.deleteTexture(texture); gl.deleteProgram(program);
      gl.getExtension('WEBGL_lose_context')?.loseContext();
    }
  };
}

// Invert the small thin-film deviation to project a feature from the atlas into
// the camera. The WebGL fragment shader maps in the opposite direction.
function screenPoint(x,y,z,camera,axes,scale,out=[]) {
  const dx=x-camera.x, dy=y-camera.y, dz=z-camera.z, distance=Math.hypot(dx,dy,dz);
  const nx=dx/distance, ny=dy/distance, nz=dz/distance;
  const [rx,ry,rz]=refractBambiRay(nx,ny,nz,camera,true,out);
  const side=axes[1]*rx+axes[0]*rz, forward=axes[0]*rx-axes[1]*rz;
  const up=axes[3]*ry-axes[2]*forward, depth=axes[3]*forward+axes[2]*ry;
  out[0]=camera.width*.5+side*scale/depth;out[1]=camera.height*.5-up*scale/depth;out[2]=depth;
  return out;
}

// A browser may display an approved cross-origin image/video in a canvas but
// prohibit pixel reads and WebGL uploads. Mapping source triangles using only
// drawImage keeps those pixels in the exact same spherical projection.
function rasterProjection(canvas) {
  const ctx=canvas.getContext('2d'), mesh=hemisphere(48,16);
  const points=new Float32Array(mesh.vertices.length), covered=new Uint8Array(mesh.indices.length/3);
  let lastRegions=null;
  const corners=new Float64Array(6), projected=new Float64Array(3);
  // Move each edge out by a device pixel. Expanding from the centroid leaves
  // hairline cracks along acute triangles near the edge of a wide-angle view.
  const corner=(at,x,y,nx,ny,mx,my)=>{
    const amount=1.35/Math.max(.16,1+nx*mx+ny*my);
    corners[at]=x+(nx+mx)*amount;corners[at+1]=y+(ny+my)*amount;
  };
  return (source,camera,axes,regions=null,masked=false) => {
    const w=canvas.width,h=canvas.height,sx=w/camera.width,sy=h/camera.height;
    const scale=camera.height*.5/Math.tan(camera.fovY*.5);
    ctx.setTransform(1,0,0,1,0,0);ctx.clearRect(0,0,w,h);
    for(let i=0;i<points.length;i+=3) {
      const p=screenPoint(mesh.vertices[i],mesh.vertices[i+1],mesh.vertices[i+2],camera,axes,scale,projected);
      points[i]=p[0]*sx; points[i+1]=p[1]*sy; points[i+2]=p[2];
    }
    if(regions&&regions!==lastRegions) {
      lastRegions=regions;
      for(let tri=0;tri<mesh.indices.length;tri+=3) {
        const ia=mesh.indices[tri],ib=mesh.indices[tri+1],ic=mesh.indices[tri+2];

        const x0=mesh.uvs[ia*2],x1=mesh.uvs[ib*2],x2=mesh.uvs[ic*2];
        const y0=mesh.uvs[ia*2+1],y1=mesh.uvs[ib*2+1],y2=mesh.uvs[ic*2+1];
        const left=Math.min(x0,x1,x2),right=Math.max(x0,x1,x2),top=Math.min(y0,y1,y2),bottom=Math.max(y0,y1,y2);
        covered[tri/3]=0;
        for(const r of regions)if(left<r.x+r.w&&right>r.x&&top<r.y+r.h&&bottom>r.y){covered[tri/3]=1;break;}
      }
    }
    const sw=source.width,sh=source.height;
    for(let tri=0;tri<mesh.indices.length;tri+=3) {
      const ia=mesh.indices[tri],ib=mesh.indices[tri+1],ic=mesh.indices[tri+2];
      if(regions&&!covered[tri/3])continue;
      const a=ia*3,b=ib*3,c=ic*3;
      if(points[a+2]<.02||points[b+2]<.02||points[c+2]<.02) continue;
      const x0=points[a],x1=points[b],x2=points[c],y0=points[a+1],y1=points[b+1],y2=points[c+1];
      if(Math.max(x0,x1,x2)<0||Math.min(x0,x1,x2)>w||Math.max(y0,y1,y2)<0||Math.min(y0,y1,y2)>h) continue;
      const u0=mesh.uvs[ia*2]*sw,u1=mesh.uvs[ib*2]*sw,u2=mesh.uvs[ic*2]*sw;
      const v0=mesh.uvs[ia*2+1]*sh,v1=mesh.uvs[ib*2+1]*sh,v2=mesh.uvs[ic*2+1]*sh;
      const det=(u1-u0)*(v2-v0)-(u2-u0)*(v1-v0);
      const ax=((x1-x0)*(v2-v0)-(x2-x0)*(v1-v0))/det, bx=((y1-y0)*(v2-v0)-(y2-y0)*(v1-v0))/det;
      const ay=((x2-x0)*(u1-u0)-(x1-x0)*(u2-u0))/det, by=((y2-y0)*(u1-u0)-(y1-y0)*(u2-u0))/det;
      const sign=Math.sign((x1-x0)*(y2-y0)-(y1-y0)*(x2-x0));
      const l0=Math.max(.001,Math.hypot(x0-x1,y0-y1));
      const l1=Math.max(.001,Math.hypot(x1-x2,y1-y2));
      const l2=Math.max(.001,Math.hypot(x2-x0,y2-y0));
      const nx0=sign*(y1-y0)/l0,ny0=sign*(x0-x1)/l0;
      const nx1=sign*(y2-y1)/l1,ny1=sign*(x1-x2)/l1;
      const nx2=sign*(y0-y2)/l2,ny2=sign*(x2-x0)/l2;
      corner(0,x0,y0,nx2,ny2,nx0,ny0);corner(2,x1,y1,nx0,ny0,nx1,ny1);corner(4,x2,y2,nx1,ny1,nx2,ny2);
      ctx.save();ctx.beginPath();
      ctx.moveTo(corners[0],corners[1]);ctx.lineTo(corners[2],corners[3]);ctx.lineTo(corners[4],corners[5]);ctx.closePath();ctx.clip();
      ctx.setTransform(ax,bx,ay,by,x0-ax*u0-ay*v0,y0-bx*u0-by*v0);
      const left=Math.max(0,Math.min(u0,u1,u2)-5), top=Math.max(0,Math.min(v0,v1,v2)-5);
      const rw=Math.min(sw,Math.max(u0,u1,u2)+5)-left,rh=Math.min(sh,Math.max(v0,v1,v2)+5)-top;
      ctx.drawImage(source,left,top,rw,rh,left,top,rw,rh);
      if(left===0) ctx.drawImage(source,sw-5,top,5,rh,-5,top,5,rh);
      else if(left+rw===sw) ctx.drawImage(source,0,top,5,rh,sw,top,5,rh);
      ctx.restore();
    }
    if(masked)return;
    const horizon=(camera.height*.5+Math.tan(camera.pitch)*scale+camera.y/DOME_RADIUS*scale)*sy;
    const high=(camera.height*.5+Math.tan(Math.max(-1.565,camera.pitch-.082))*scale+camera.y/DOME_RADIUS*scale)*sy;
    const haze=ctx.createLinearGradient(0,Math.min(high,horizon-.01),0,horizon);
    haze.addColorStop(0,'white');haze.addColorStop(1,'transparent');
    ctx.globalCompositeOperation='destination-in';ctx.fillStyle=haze;ctx.fillRect(0,0,w,h);ctx.globalCompositeOperation='source-over';
  };
}

const CONTROLS=new Set(['KeyW','KeyA','KeyS','KeyD','KeyQ','KeyE','Space','KeyC','ArrowLeft','ArrowRight','ArrowUp','ArrowDown']);
function inputLayer(root,host,camera,events) {
  const doc=root.ownerDocument,win=doc.defaultView,opts={signal:events.signal};
  const keys=new Set(),fingers=new Map(),rise=new Map();
  const input={side:0,forward:0};
  const turn=(x,y)=>{camera.yaw+=x;camera.pitch=clamp(camera.pitch+y,-1.5,1.54);};
  let mouse=null, pendingYaw=0, pendingPitch=0;
  function releaseMouse() {mouse=null;pendingYaw=pendingPitch=0;}
  function followMouse(dt) {
    if(!pendingYaw&&!pendingPitch) return;
    const fraction=1-Math.exp(-dt*38);
    turn(pendingYaw*fraction,pendingPitch*fraction);
    pendingYaw*=1-fraction;pendingPitch*=1-fraction;
    if(Math.abs(pendingYaw)<.00001) pendingYaw=0;
    if(Math.abs(pendingPitch)<.00001) pendingPitch=0;
  }
  win.addEventListener('keydown',e=>{
    if(!CONTROLS.has(e.code)||e.ctrlKey||e.altKey||e.metaKey||doc.hidden||!root.isConnected) return;
    if(e.target?.closest?.('input,textarea,select,[contenteditable="true"]')) return;
    if(e.target?.closest?.('[role="dialog"]')&&!root.closest('[role="dialog"]')?.contains(e.target)) return;
    if(e.code==='Space'&&e.target?.closest?.('button')&&e.target.offsetParent) return;
    keys.add(e.code);e.preventDefault();
  },opts);
  win.addEventListener('keyup',e=>keys.delete(e.code),opts);
  const release=()=>{keys.clear();fingers.clear();rise.clear();input.side=input.forward=0;releaseMouse();};
  win.addEventListener('blur',release,opts);doc.addEventListener('visibilitychange',release,opts);
  root.addEventListener('pointerdown',e=>{
    if(e.pointerType==='mouse'||e.target.closest('button')) return;
    const move=e.clientX-root.getBoundingClientRect().left<camera.width*.5;
    fingers.set(e.pointerId,{move,x:e.clientX,y:e.clientY,ox:e.clientX,oy:e.clientY});
    root.setPointerCapture?.(e.pointerId);
  },opts);
  root.addEventListener('pointerenter',e=>{if(e.pointerType==='mouse') mouse={x:e.clientX,y:e.clientY};},opts);
  root.addEventListener('pointerleave',e=>{if(e.pointerType==='mouse') releaseMouse();},opts);
  root.addEventListener('pointermove',e=>{
    if(e.pointerType==='mouse') {
      if(mouse) {
        // One sweep crosses the panorama even in the embedded preview. Keep
        // the pointer free to leave the frame, pause, or use the host's UI.
        pendingYaw+=(e.clientX-mouse.x)*TAU/(camera.width*.92);
        pendingPitch-=(e.clientY-mouse.y)*Math.PI/(camera.height*.93);
      }
      mouse={x:e.clientX,y:e.clientY};
      return;
    }
    const f=fingers.get(e.pointerId);if(!f) return;
    if(f.move) {
      const span=clamp(Math.min(camera.width,camera.height)*.16,40,82);
      input.side=clamp((e.clientX-f.ox)/span,-1,1);input.forward=clamp((f.oy-e.clientY)/span,-1,1);
    } else turn((f.x-e.clientX)*.0038,(e.clientY-f.y)*.0038);
    f.x=e.clientX;f.y=e.clientY;
  },opts);
  const end=e=>{
    if(fingers.get(e.pointerId)?.move) input.side=input.forward=0;
    fingers.delete(e.pointerId);rise.delete(e.pointerId);
  };
  for(const event of ['pointerup','pointercancel','lostpointercapture']) root.addEventListener(event,end,opts);
  let touchControls=null;
  if(win.matchMedia('(any-pointer:coarse)').matches || win.navigator.maxTouchPoints) {
    touchControls=doc.createElement('div');
    touchControls.className='bambi-flight-controls';
    touchControls.style.cssText='position:absolute;z-index:3;bottom:max(46px,8%);right:max(12px,env(safe-area-inset-right));display:grid;gap:6px;pointer-events:auto;touch-action:none;';
    for(const [label,dir] of [['Rise',1],['Dive',-1]]) {
      const button=doc.createElement('button');button.type='button';button.title=label;button.setAttribute('aria-label',label);
      button.style.cssText='width:44px;height:44px;padding:0;display:grid;place-items:center;border:1px solid #ffdeeac4;border-radius:50%;background:#522039ce;color:#fff2f6;box-shadow:0 2px 8px #3d122d55;touch-action:none;';
      const mark=doc.createElement('span');mark.setAttribute('aria-hidden','true');
      mark.style.cssText='width:11px;height:11px;border-top:2px solid currentColor;border-right:2px solid currentColor;transform:rotate('+(dir===1?-45:135)+'deg);';
      button.append(mark);touchControls.append(button);
      button.addEventListener('pointerdown',e=>{rise.set(e.pointerId,dir);button.setPointerCapture?.(e.pointerId);e.preventDefault();},opts);
      for(const event of ['pointerup','pointercancel','lostpointercapture']) button.addEventListener(event,end,opts);
    }
    host.append(touchControls);
  }
  return {keys,input,rise,turn,followMouse,dispose(){release();touchControls?.remove();}};
}

export function createBambiSky(host,{projection,gentle=false,reduced=false}={}) {
  const doc=host.ownerDocument,win=doc.defaultView,events=new win.AbortController();
  const root=doc.createElement('div');root.className='bambi-sky';
  root.style.cssText='position:absolute;inset:0;overflow:hidden;isolation:isolate;touch-action:none;user-select:none;cursor:default;background:linear-gradient(#58334f,#ad6480 59%,#56334f);';
  host.append(root);
  const chart=doc.createElement('div');chart.style.cssText='position:absolute;inset:0;pointer-events:none';root.append(chart);
  const layer=(tag,parent=chart)=>{const el=doc.createElement(tag);el.style.cssText='position:absolute;inset:0;width:100%;height:100%;pointer-events:none;';parent.append(el);return el;};
  const gpuCanvas=layer('canvas'),nativeMediaHost=layer('div',root);
  let safeCanvas=null,raster=null,remoteCanvas=null,remoteRaster=null;
  for(const el of [gpuCanvas,nativeMediaHost]) el.setAttribute('aria-hidden','true');
  let gpu=null;
  try {gpu=webglProjection(gpuCanvas);} catch { /* Shader support varies on embedded and older devices. */ }
  function canvasLayer(remote=false) {
    const canvas=layer('canvas');canvas.setAttribute('aria-hidden','true');
    if(remote){remoteCanvas=canvas;remoteRaster=rasterProjection(canvas);}
    else {safeCanvas=canvas;raster=rasterProjection(canvas);if(remoteCanvas)chart.insertBefore(canvas,remoteCanvas);}
    return canvas;
  }
  function sizeCanvas(canvas,limit,w,h) {
    const pixelRatio=Math.min(win.devicePixelRatio||1,1.3,limit/Math.max(w,h));
    const cw=Math.max(1,Math.round(w*pixelRatio)),ch=Math.max(1,Math.round(h*pixelRatio));
    if(canvas.width!==cw||canvas.height!==ch){canvas.width=cw;canvas.height=ch;}
  }
  const camera={x:4,y:1.5,z:8,travelX:4,travelY:1.5,travelZ:8,yaw:.13,pitch:.19,fovY:1.64,time:0,opticalTime:0,width:1,height:1};
  const controls=inputLayer(root,host,camera,events), axes=new Float32Array(4);
  const velocity=[0,0,0],soft=gentle||reduced;
  let dead=false, blockedAtlas=null, remoteAge=1, drawAge=1, mode='',width=0,height=0;
  const down=code=>Number(controls.keys.has(code));
  function move(dt) {
    const side=down('KeyD')-down('KeyA')+controls.input.side;
    const forward=down('KeyW')-down('KeyS')+controls.input.forward;
    let vertical=Number(controls.keys.has('Space')||controls.keys.has('KeyE'))-Number(controls.keys.has('KeyC')||controls.keys.has('KeyQ'));
    for(const held of controls.rise.values()) vertical+=held;
    const yaw=down('ArrowRight')-down('ArrowLeft'),pitch=down('ArrowUp')-down('ArrowDown');
    if(yaw||pitch) controls.turn(yaw*dt*1.16,pitch*dt*.86);
    controls.followMouse(dt);
    axes[0]=Math.sin(camera.yaw);axes[1]=Math.cos(camera.yaw);
    axes[2]=Math.sin(camera.pitch);axes[3]=Math.cos(camera.pitch);
    const vx=side*axes[1]+forward*axes[0]*axes[3];
    const vy=vertical+forward*axes[2], vz=side*axes[0]-forward*axes[1]*axes[3];
    const speed=(soft?6.3:9.2)/Math.max(1,Math.hypot(vx,vy,vz));
    const ease=1-Math.exp(-dt*5.5);
    [vx,vy,vz].forEach((v,i)=>{velocity[i]+=(v*speed-velocity[i])*ease;});
    const delta=velocity.map(v=>v*dt);
    camera.travelX+=delta[0];camera.travelY+=delta[1];camera.travelZ+=delta[2];
    const length=Math.hypot(camera.x,camera.y,camera.z);
    if(length>CAMERA_LIMIT-1.8) {
      const n=[camera.x/length,camera.y/length,camera.z/length],out=delta[0]*n[0]+delta[1]*n[1]+delta[2]*n[2];
      if(out>0) for(let i=0;i<3;i++) delta[i]-=n[i]*out*clamp((length-CAMERA_LIMIT+1.8)/1.8,0,1);
    }
    camera.x+=delta[0];camera.y+=delta[1];camera.z+=delta[2];
    const bound=CAMERA_LIMIT/Math.max(CAMERA_LIMIT,Math.hypot(camera.x,camera.y,camera.z));
    camera.x*=bound;camera.y*=bound;camera.z*=bound;
  }
  function update(dt=0) {
    if(dead) return;
    dt=clamp(Number(dt)||0,0,.12);camera.time+=dt;camera.opticalTime=soft?0:camera.time;
    move(dt);drawAge+=dt;remoteAge+=dt;
    const w=root.clientWidth,h=root.clientHeight;if(!w||!h) return;
    if(width!==w||height!==h) {
      camera.width=width=w;camera.height=height=h;
      camera.fovY=Math.min(w<h?1.9:1.68,2*Math.atan(2.65*h/w));
      sizeCanvas(gpuCanvas,1720,w,h);
      drawAge=remoteAge=1;
    }
    const source=projection?.canvas;if(!source?.width||!source.height) return;
    let path=gpu&&!projection.displayOnly&&blockedAtlas!==source?'webgl':'canvas';
    if(drawAge<(path==='webgl'?1/60:1/24)&&mode===path) return;
    drawAge=0;
    const remote=projection.overlay;
    if(remote) {
      const scale=h*.5/Math.tan(camera.fovY*.5);
      const horizon=h*.5+Math.tan(camera.pitch)*scale+camera.y/DOME_RADIUS*scale;
      const high=Math.min(h*.5+Math.tan(Math.max(-1.565,camera.pitch-.082))*scale+camera.y/DOME_RADIUS*scale,horizon-.01);
      chart.style.maskImage=`linear-gradient(to bottom,#000 ${high}px,transparent ${horizon}px)`;
    } else chart.style.maskImage='none';
    if(path==='webgl') {
      try {if(!gpu.draw(source,projection.revision,camera,axes,!!remote)) path='canvas';}
      catch {blockedAtlas=source;path='canvas';}
    }
    if(path==='canvas') {
      if(!safeCanvas)canvasLayer();
      sizeCanvas(safeCanvas,1080,w,h);raster(source,camera,axes,null,!!remote);
    }
    if(remote) {
      if(!remoteCanvas)canvasLayer(true);
      sizeCanvas(remoteCanvas,1080,w,h);remoteCanvas.hidden=false;
      if(remoteAge>=1/24){remoteRaster(remote,camera,axes,projection.overlayRegions,true);remoteAge=0;}
    } else {remoteAge=1;if(remoteCanvas)remoteCanvas.hidden=true;}
    if(mode!==path) {
      mode=path;root.dataset.skyRenderer=mode;
      gpuCanvas.style.display=mode==='webgl'?'block':'none';
      if(safeCanvas)safeCanvas.hidden=mode!=='canvas';
    }
  }
  function project(u,v) {
    const p=pointOnDome(u,v),scale=camera.height*.5/Math.tan(camera.fovY*.5);
    const [x,y,depth]=screenPoint(...p,camera,axes,scale);
    return {x,y,depth,visible:v>=0&&v<=1&&depth>.02&&x>=0&&x<=camera.width&&y>=0&&y<=camera.height};
  }
  update(0);
  return {camera,nativeMediaHost,project,update,dispose() {
    if(dead) return;dead=true;events.abort();controls.dispose();gpu?.dispose();root.remove();
    for(const canvas of [safeCanvas,remoteCanvas,gpuCanvas])if(canvas)canvas.width=canvas.height=1;
  }};
}

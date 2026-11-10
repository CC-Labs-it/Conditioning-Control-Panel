/* Inside a displaced soap-film sphere. All rays, lights, curls and reflection directions use world axes. */
export const BUBBLE_OPTICS_GLSL = `
vec3 bambiFilmNormal(vec3 ray,vec3 eye,float t) {
  float b = dot(eye,ray);
  float s = -b+sqrt(max(.001,b*b+324.-dot(eye,eye)));
  vec3 n = normalize(eye+s*ray);
  vec3 wave = vec3(sin(n.y*6.+n.z*2.+t*.075),cos(n.z*5.-n.x*3.-t*.047),sin(n.x*5.+n.y*3.+t*.053));
  return normalize(n+.043*(wave-n*dot(wave,n)));
}
vec3 bambiRefractedRay(vec3 ray, vec3 eye,float t) {
  vec3 n=bambiFilmNormal(ray,eye,t);
  return refract(ray,-n,.961);
}`;

export function refractBambiRay(x,y,z,camera,inverse=false) {
  const {x:ex,y:ey,z:ez,time=0}=camera;
  const b=ex*x+ey*y+ez*z;
  const s=-b+Math.sqrt(Math.max(.001,b*b+324.-ex*ex-ey*ey-ez*ez));
  let nx=(ex+s*x)/18,ny=(ey+s*y)/18,nz=(ez+s*z)/18;
  const vx=Math.sin(ny*6+nz*2+time*.075),vy=Math.cos(nz*5-nx*3-time*.047),vz=Math.sin(nx*5+ny*3+time*.053);
  const d=vx*nx+vy*ny+vz*nz;
  nx+=.043*(vx-nx*d);ny+=.043*(vy-ny*d);nz+=.043*(vz-nz*d);
  const length=Math.hypot(nx,ny,nz);nx/=length;ny/=length;nz/=length;
  const k=inverse?1/.961:.961;
  const a=nx*x+ny*y+nz*z, bend=Math.sqrt(Math.max(.0001,1.-k*k*(1.-a*a)))-k*a;
  return [k*x+bend*nx,k*y+bend*ny,k*z+bend*nz];
}

const VERT = `
attribute vec2 aCorner;
varying vec2 vUV;
void main() { vUV = aCorner * .5 + .5; gl_Position = vec4(aCorner, 0., 1.); }`;

const FRAG = `
precision highp float;
varying vec2 vUV;
uniform sampler2D uNoise;
uniform vec2 uLens;
uniform vec3 uRight, uUp, uFront;
uniform vec3 uEye, uTravel;
uniform float uTime;
${BUBBLE_OPTICS_GLSL}

// RG encodes two adjacent slices of a repeatable three-dimensional value-noise field.
float noise(vec3 p) {
  vec3 i = floor(p), f = fract(p);
  f = f*f*(3.-2.*f);
  vec2 uv = i.xy + vec2(37.,17.)*i.z + f.xy;
  vec2 rg = texture2D(uNoise, (uv+.5)/128.).rg;
  return mix(rg.r, rg.g, f.z);
}
float fbm(vec3 p) {
  return .58*noise(p)+.28*noise(p*2.07+11.)+.14*noise(p*4.23-5.);
}
vec4 fog(vec3 ray, float distanceToDome) {
  // Quadratic spacing resolves the vapor close to the eye as well as the vast banks below it.
  float trans = 1.;
  vec3 sum = vec3(0.);
  float left = .14;
  const int STEPS = 17;
  for (int i = 0; i < STEPS; ++i) {
    float q = float(i+1) / float(STEPS);
    float right = distanceToDome*q*q+.14;
    float t = mix(left,right,.51);
    vec3 p = ray*t + vec3(uTravel.x,uEye.y,uTravel.z);
    vec3 w = p*vec3(.044,.065,.044) + vec3(.48-uTime*.014,2.82,-1.42+uTime*.007);
    float curl = noise(w*.75 + vec3(0.,w.x*.19,1.));
    vec3 warped = w+vec3(curl*1.5,curl*.90,-curl*.68);
    float cloud = fbm(warped);
    float thread = noise(warped*vec3(6.8,11.,6.8) + 3.1*noise(warped*2.9));
    cloud += .093*(thread-.5);
    float altitude = p.y + 8.6 - 13.*(cloud-.5);
    float layer = (1.-smoothstep(-2.2,8.4,altitude)) * smoothstep(.23,.75,cloud);
    float highMist = smoothstep(.47,.79,cloud)*(1.-smoothstep(3.,28.,abs(p.y+1.5)));
    float density = .139*layer + .048*highMist + .0017;
    float a = 1.-exp(-density*(right-left));
    float lit = clamp(.21 + 1.43*(cloud-.38) + .23*smoothstep(-8.,7.,altitude)
                + 1.19*(cloud-noise(w+vec3(-.39,.56,-.21)))
                + .12*dot(ray,normalize(vec3(-.55,.27,-.81))),0.,1.);
    vec3 shade = mix(vec3(.31,.047,.225),vec3(.85,.30,.52),smoothstep(.03,.74,lit));
    shade = mix(shade,vec3(1.,.68,.80),smoothstep(.49,1.,lit));
    float wisp = smoothstep(.49,.77,thread)*smoothstep(.30,.68,cloud)*(1.-smoothstep(3.,15.,abs(altitude-1.)));
    shade = mix(shade,vec3(.99,.77,.84),.42*wisp);
    sum += trans*a*shade;
    trans *= 1.-a;
    left = right;
  }
  return vec4(sum,1.-trans);
}
float softStrip(float value, float center, float width) {
  float u = (value-center)/width;
  return exp(-u*u);
}
float softbox(vec3 r,vec3 axis,vec3 side) {
  vec3 y = normalize(cross(axis,side)), x = cross(y,axis);
  vec2 uv = vec2(dot(r,x),dot(r,y));
  float v = uv.y-.32*uv.x*uv.x-.058*sin(uv.x*5.);
  float ends = (1.-smoothstep(.22,.72,abs(uv.x))) * smoothstep(.4,.83,dot(r,axis));
  return ends*(.61*softStrip(v,0.,.063)+.91*softStrip(v,.035,.011)+.43*softStrip(v,-.101,.016));
}
vec2 eddyTurn(vec2 p,vec2 origin,float amount,float size) {
  vec2 d=p-origin;
  float a=amount*exp(-dot(d,d)*size);
  return origin+vec2(cos(a)*d.x-sin(a)*d.y,sin(a)*d.x+cos(a)*d.y);
}
// A draining lobe of thick soap film, with tiny liquid lenses gathering along its meniscus.
// Spherical coordinates attach every fold and bead to the membrane itself.
vec4 drainage(vec3 normal,float t) {
  vec2 uv=vec2(atan(normal.z,normal.x),asin(clamp(normal.y,-1.,1.)));
  vec2 flow=eddyTurn(uv,vec2(-.06,.34+.025*sin(t)),2.25,11.);
  flow=eddyTurn(flow,vec2(-.32,-.29),-1.12,19.);
  float fold=flow.x+.21*sin(flow.y*3.45+.21*sin(flow.y*6.8+t*.09))+.041*sin(flow.y*8.5+t*.12);
  float region=(1.-smoothstep(.29,.76,abs(fold-.04)))*smoothstep(-.86,-.34,normal.z+.74);
  float ribbons=fold*43.+.92*sin(flow.y*6.1+fold*6.-t*.23);
  float vein=.5+.5*cos(ribbons);
  float pearl=.5+.5*cos(ribbons*.56+.85);
  vec3 color=mix(vec3(.47,.17,.42),vec3(.98,.46,.71),smoothstep(.0,.87,pearl));
  color=mix(color,vec3(.53,.84,.77),.78*smoothstep(.51,.98,vein)*(1.-.60*pearl));
  color=mix(color,vec3(1.,.81,.58),.87*pow(pearl,5.)*smoothstep(.30,.94,vein));
  float bright=softStrip(fold,-.235,.011)+.74*softStrip(fold,-.276,.004);
  float crease=softStrip(fold,-.25,.014)+.25*pow(1.-vein,7.);
  color=mix(color,vec3(.27,.064,.22),min(.86,crease));
  color=mix(color,vec3(1.,.94,.92),min(1.,bright));
  float film=.16+.19*pow(vein,2.)+.16*pearl+.48*bright+.22*crease;
  vec2 cells=uv*vec2(6.7,7.8);
  vec2 tile=floor(cells), local=fract(cells)-.5;
  float seed=fract(sin(dot(tile,vec2(41.63,17.45)))*43758.55);
  local-=vec2(.22*sin(seed*17.),.22*cos(seed*39.));
  float radius=.028+.20*pow(seed,3.);
  vec2 bead=local/max(.012,radius);
  bead.x*=1.13;
  float d=length(bead);
  float lens=(1.-smoothstep(.88,1.15,d))*smoothstep(.23,.70,seed);
  vec3 cap=vec3(bead.x,-bead.y,sqrt(max(0.,1.-dot(bead,bead))));
  float sky=max(0.,dot(cap,normalize(vec3(-.48,.51,.73))));
  float shine=pow(sky,13.)+.43*softStrip(d,.85,.08)*smoothstep(-.1,.8,bead.y);
  vec3 droplet=mix(vec3(.30,.064,.24),vec3(.96,.61,.82),.25+.52*sky);
  droplet=mix(droplet,vec3(1.,.98,.95),min(1.,4.*shine));
  color=mix(color,droplet,lens);
  film=mix(film,.89,lens);
  return vec4(color,region*min(.89,film));
}
void main() {
  vec2 screen = (vUV*2.-1.)*uLens;
  vec3 ray = normalize(uFront + screen.x*uRight + screen.y*uUp);
  float tDom = min(155.,-dot(uEye,ray)+sqrt(max(.0,pow(dot(uEye,ray),2.)+230400.-dot(uEye,uEye))));
  vec4 cloud = fog(ray,tDom);
  // Ray exits the enclosing radius-eighteen shell centered in the sky dome.
  float b = dot(uEye,ray);
  float s = -b + sqrt(max(0.,b*b+324.-dot(uEye,uEye)));
  vec3 hit = uEye + s*ray;
  vec3 normal = normalize(hit);
  vec3 surface = normal*2.7;
  float t = uTime*.075;
  float eddy = fbm(surface+vec3(t*.30,-t*.14,.5));
  vec3 wetNormal = bambiFilmNormal(ray,uEye,uTime);
  float incidence = clamp(dot(ray,wetNormal),0.,1.);
  float fresnel = .045 + .74*pow(1.-incidence,1.8);
  vec3 refl = reflect(ray,wetNormal);
  // World-oriented softboxes and broken bands reflected by the curved surface.
  float angle = atan(refl.z,refl.x);
  float cut = smoothstep(-.66,.22,cos(angle+1.85));
  float bank = refl.y+.12*sin(angle*2.+.8);
  float light = cut*(.42*softStrip(bank,.53,.140)+.91*softStrip(bank,.505,.029)+.33*softStrip(bank,.575,.013));
  float other = smoothstep(-.1,.73,cos(angle-.5))*(.53*softStrip(bank,-.31,.084)+.69*softStrip(bank,-.335,.023));
  float glint = pow(max(0.,dot(refl,normalize(vec3(.38,.26,.88)))),66.);
  float deposit = 470. + 120.*eddy + 74.*normal.y + 31.*sin(3.*normal.y+normal.x*2.+eddy*5.-t);
  vec3 spectral = .5+.5*cos(12.56637*deposit*sqrt(.57+.43*incidence*incidence) / vec3(665.,535.,458.));
  float lamina = .5+.5*sin(deposit*.093+2.3*eddy);
  vec3 pearl = mix(vec3(.67,.20,.48),mix(vec3(.22,.54,.84),vec3(1.,.78,.52),spectral.r*.70+.30*spectral.b),.48+.33*lamina);
  vec3 gleam = mix(vec3(.69,.61,.98),vec3(1.,.88,.86),spectral.r);
  float box = softbox(refl,normalize(vec3(.394,-.915,.093)),normalize(vec3(.88,.4,.44)));
  float trace = softbox(refl,normalize(vec3(-.756,-.415,-.506)),normalize(vec3(.15,.80,-.65)));
  float reflected = clamp(light + other + .68*glint+1.06*box+.53*trace,0.,1.4);
  float filmA = min(.85, .027+.68*fresnel+.045*lamina + reflected*.59);
  vec3 film = mix(pearl,gleam,clamp(.10+reflected*.82,0.,1.));
  film += vec3(.27,.18,.23)*smoothstep(.68,1.38,reflected);
  vec4 soap = drainage(normal,t);
  film = (film*filmA*(1.-soap.a)+soap.rgb*soap.a)/max(.001,filmA*(1.-soap.a)+soap.a);
  filmA = .83*(filmA*(1.-soap.a)+soap.a);
  vec3 medium = mix(cloud.rgb,film,filmA);
  float cover = cloud.a*(1.-filmA)+filmA;
  gl_FragColor = vec4(medium/max(.0001,cover),cover);
}`;

function compile(gl, type, src) {
  const shader = gl.createShader(type);
  gl.shaderSource(shader, src);
  gl.compileShader(shader);
  if (gl.getShaderParameter(shader, gl.COMPILE_STATUS)) return shader;
  const log = gl.getShaderInfoLog(shader);
  gl.deleteShader(shader);
  throw Error(log);
}

function setupGL(gl) {
  const program = gl.createProgram();
  for (const [type, src] of [[gl.VERTEX_SHADER,VERT],[gl.FRAGMENT_SHADER,FRAG]]) {
    const shader = compile(gl,type,src);
    gl.attachShader(program,shader);
    gl.deleteShader(shader);
  }
  gl.linkProgram(program);
  if (!gl.getProgramParameter(program,gl.LINK_STATUS)) throw Error(gl.getProgramInfoLog(program));
  const uniform = {};
  for (const name of ['uNoise','uLens','uFront','uUp','uRight','uEye','uTravel','uTime'])
    uniform[name] = gl.getUniformLocation(program,name);
  const buf = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER,buf);
  gl.bufferData(gl.ARRAY_BUFFER,new Float32Array([-1,-1,3,-1,-1,3]),gl.STATIC_DRAW);
  const a = gl.getAttribLocation(program,'aCorner');
  const noiseTex = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D,noiseTex);
  const raw = new Uint8Array(128*128), bytes = new Uint8Array(128*128*4);
  let seed = 120911;
  for (let i=0;i<raw.length;i++) { seed=(Math.imul(seed,1664525)+1013904223)>>>0; raw[i]=seed>>>24; }
  for (let y=0;y<128;y++) for (let x=0;x<128;x++) {
    const k=(x+128*y)*4;
    bytes[k]=raw[x+128*y]; bytes[k+1]=raw[((x+37)&127)+128*((y+17)&127)];
    bytes[k+3]=255;
  }
  gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,128,128,0,gl.RGBA,gl.UNSIGNED_BYTE,bytes);
  gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);
  gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.LINEAR);
  return { program,uniform,buf,a,noiseTex };
}

// Shorter raymarch for devices without WebGL. The fog and first-person film still follow the same camera.
function lowDetailVolume(canvas,soft) {
  const ctx=canvas.getContext('2d',{alpha:true});
  const lattice=new Float32Array(32768);
  let seed=83743;
  for(let i=0;i<lattice.length;i++) {
    seed=(Math.imul(seed,1664525)+1013904223)>>>0;
    lattice[i]=seed/4294967296;
  }
  const sat=x=>Math.max(0,Math.min(1,x));
  const gauss=(v,width)=>Math.exp(-(v*v)/(width*width));
  function noise(x,y,z) {
    const i=Math.floor(x),j=Math.floor(y),k=Math.floor(z);
    let u=x-i,v=y-j,w=z-k;
    u=u*u*(3-2*u);v=v*v*(3-2*v);w=w*w*(3-2*w);
    const a=i&31,b=(i+1)&31,c=(j&31)<<5,d=((j+1)&31)<<5,e=(k&31)<<10,f=((k+1)&31)<<10;
    const row=(first,second)=>((1-u)*lattice[a|first|second]+u*lattice[b|first|second]);
    return (1-w)*((1-v)*row(c,e)+v*row(d,e))+w*((1-v)*row(c,f)+v*row(d,f));
  }
  let elapsed=1,image;
  function update(dt,cam) {
    if(!ctx||!(cam.width>0&&cam.height>0))return;
    elapsed+=Math.max(0,dt||0);
    const ratio=Math.min(1,180/cam.width,135/cam.height);
    const w=Math.max(1,Math.round(cam.width*ratio)),h=Math.max(1,Math.round(cam.height*ratio));
    if(canvas.width!==w||canvas.height!==h||!image) {
      canvas.width=w;canvas.height=h;image=ctx.createImageData(w,h);
    } else if(dt>0&&elapsed<1/15) return;
    elapsed=0;
    const data=image.data, tan=Math.tan(cam.fovY/2),sy=Math.sin(cam.yaw),cy=Math.cos(cam.yaw),sp=Math.sin(cam.pitch),cp=Math.cos(cam.pitch);
    const eyeX=cam.x,eyeY=cam.y,eyeZ=cam.z,time=soft?0:cam.time||0;
    const startX=cam.travelX??eyeX,startZ=cam.travelZ??eyeZ;
    for(let j=0;j<h;j++) for(let i=0;i<w;i++) {
      const lx=(2*(i+.5)/w-1)*tan*w/h,ly=(1-2*(j+.5)/h)*tan,inv=1/Math.sqrt(1+lx*lx+ly*ly);
      const x=(sy*cp+cy*lx-sy*sp*ly)*inv,y=(sp+cp*ly)*inv,z=(-cy*cp+sy*lx+cy*sp*ly)*inv;
      let tr=1,r=0,g=0,b=0,prev=.14;
      for(let k=1;k<=8;k++) {
        const end=152*k*k/64+.14, depth=(prev+end)*.5, altitude=eyeY+y*depth;
        const px=(startX+x*depth)*.044-time*.014,py=altitude*.072,pz=(startZ+z*depth)*.044+time*.008;
        const n=.67*noise(px,py+2.8,pz)+.33*noise(px*2.06+4,py*2.06,pz*2.06-2);
        const mist=sat((8-altitude-8.6+13*(n-.5))*.12)*sat((n-.24)*2.1);
        const den=.13*mist+.035*sat((n-.42)*3)*sat(1-Math.abs(altitude+2)/25)+.0017;
        const a=1-Math.exp(-den*(end-prev)), illumination=sat(.32+1.34*(n-.36)+.20*sat((altitude+8)/16));
        r+=tr*a*(.33+.66*illumination);g+=tr*a*(.044+.48*illumination);b+=tr*a*(.20+.53*illumination);
        tr*=1-a;prev=end;
      }
      const dot=eyeX*x+eyeY*y+eyeZ*z;
      const dist=-dot+Math.sqrt(Math.max(.001,dot*dot+324-eyeX*eyeX-eyeY*eyeY-eyeZ*eyeZ));
      const nx=(eyeX+dist*x)/18,ny=(eyeY+dist*y)/18,nz=(eyeZ+dist*z)/18;
      const inc=nx*x+ny*y+nz*z,rx=x-2*inc*nx,ry=y-2*inc*ny,rz=z-2*inc*nz;
      const ang=Math.atan2(rz,rx),bank=ry+.12*Math.sin(ang*2+.8);
      const band=(.4*gauss(bank-.53,.12)+.75*gauss(bank-.50,.029))*sat((Math.cos(ang+1.85)+.6)*1.2);
      const box=sat((rx*.39-ry*.915+rz*.09-.44)*2.7)*gauss(.44*rx+.098*ry-.89*rz+.012,.049);
      const lon=Math.atan2(nz,nx),lat=Math.asin(Math.max(-1,Math.min(1,ny)));
      const fx=lon+.06,fy=lat-.34,turn=2.25*Math.exp(-(fx*fx+fy*fy)*11);
      const bandX=-.06+Math.cos(turn)*fx-Math.sin(turn)*fy,bandY=.34+Math.sin(turn)*fx+Math.cos(turn)*fy;
      const fold=bandX+.20*Math.sin(3.5*bandY)+.045*Math.sin(8*bandY+time*.011);
      const zone=sat((.70-Math.abs(fold-.04))/.50);
      const ripple=.5+.5*Math.sin(43*fold+Math.sin(lat*6));
      const rim=gauss(fold+.235,.024);
      let a=Math.min(.83,.061+.38*(1-inc)**1.5+.44*band+.61*box+zone*(.19+.21*ripple+.38*rim));
      const irid=.5+.5*Math.sin(ny*5.4+nx*3.7+1.4*Math.sin(nz*4.6+time*.035));
      const gloss=sat(band+box);
      let fr=.63+.22*irid+.13*gloss+.13*rim,fg=.32+.09*irid+.50*gloss+zone*(.22*ripple+.40*rim),fb=.65+.19*(1-irid)+.16*gloss+.12*rim;
      if(zone>.15){
        const tx=lon*6.7,ty=lat*7.8,ix=Math.floor(tx),iy=Math.floor(ty);
        const hash=Math.sin(ix*41.63+iy*17.45)*43758.55,grain=hash-Math.floor(hash);
        const rad=.028+.20*grain**3, bx=(tx-ix-.5-.22*Math.sin(grain*17))*1.13/rad,by=(ty-iy-.5-.22*Math.cos(grain*39))/rad;
        const len=Math.hypot(bx,by),lens=zone*sat((1.21-len)*4)*sat((grain-.23)*2.1);
        const glint=sat((-.48*bx-.51*by+.73*Math.sqrt(Math.max(0,1-len*len))-.58)*3);
        fr+=(.50+.50*glint-fr)*lens;fg+=(.18+.80*glint-fg)*lens;fb+=(.42+.54*glint-fb)*lens;
        a+=(.90-a)*lens;
      }
      a*=.83;
      const alpha=1-tr*(1-a);
      const idx=(i+w*j)*4;
      data[idx]=255*((1-a)*r+a*fr)/alpha;
      data[idx+1]=255*((1-a)*g+a*fg)/alpha;
      data[idx+2]=255*((1-a)*b+a*fb)/alpha;
      data[idx+3]=255*alpha;
    }
    ctx.putImageData(image,0,0);
  }
  return {update,dispose(){canvas.remove();},available:!!ctx};
}

/** The sky uses the optics above when projecting its moving media. This transparent layer adds the film and mist. */
export function createBambiAtmosphere(host,{gentle=false,reduced=false}={}) {
  const doc=host.ownerDocument;
  const canvas=doc.createElement('canvas');
  canvas.setAttribute('aria-hidden','true');
  canvas.className='bambi-volume';
  canvas.style.cssText='position:absolute;inset:0;width:100%;height:100%;pointer-events:none';
  host.append(canvas);
  const gl=canvas.getContext('webgl',{alpha:true,premultipliedAlpha:false,antialias:false,depth:false,stencil:false});
  if (!gl) return lowDetailVolume(canvas,reduced);
  let software;
  function switchToSoftware() {
    const replacement=canvas.cloneNode(false);
    canvas.replaceWith(replacement);
    software=lowDetailVolume(replacement,reduced);
  }
  let resources;
  try {resources=setupGL(gl);} catch(e) {
    console.warn('The bubble switched to software rendering.',e);
    switchToSoftware();
    gl.getExtension('WEBGL_lose_context')?.loseContext();
    return software;
  }
  const {program,uniform:u,buf,a,noiseTex}=resources;
  let elapsed=0,dead=false,scale=1,slowSeconds=0;
  function update(dt,cam) {
    if(dead) return;
    if(software) {software.update(dt,cam);return;}
    if(gl.isContextLost()) {switchToSoftware();software.update(dt,cam);return;}
    const w=cam.width,h=cam.height;
    if(!(w>0&&h>0)) return;
    const delta=Math.max(0, Math.min(.2,dt||0));
    // Reduce pixel count once frame delivery is slow for several seconds. Avoid oscillating mid-cinematic.
    slowSeconds=Math.max(0,slowSeconds+(delta>1/26?delta:-delta*.55));
    if(slowSeconds>1.65&&scale>.62){scale*=.82;slowSeconds=0;}
    const ratio = Math.min(.76,850/w,650/h)*scale;
    const cw=Math.max(1,Math.round(w*ratio)),ch=Math.max(1,Math.round(h*ratio));
    if(canvas.width!==cw||canvas.height!==ch){canvas.width=cw;canvas.height=ch;}
    elapsed+=reduced?0:delta*(gentle ? .55 : 1.);
    const eye=[cam.x||0,cam.y||0,cam.z||0];
    const world=[cam.travelX??eye[0],cam.travelY??eye[1],cam.travelZ??eye[2]];
    const cp=Math.cos(cam.pitch),sp=Math.sin(cam.pitch),sy=Math.sin(cam.yaw),cy=Math.cos(cam.yaw);
    const tan=Math.tan(cam.fovY/2);
    gl.viewport(0,0,canvas.width,canvas.height);
    gl.useProgram(program);
    gl.disable(gl.BLEND); gl.disable(gl.DEPTH_TEST); gl.disable(gl.CULL_FACE);
    gl.bindBuffer(gl.ARRAY_BUFFER,buf); gl.enableVertexAttribArray(a); gl.vertexAttribPointer(a,2,gl.FLOAT,false,0,0);
    gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,noiseTex);gl.uniform1i(u.uNoise,0);
    gl.uniform2f(u.uLens,tan*w/h,tan);
    gl.uniform3f(u.uFront,sy*cp,sp,-cy*cp);gl.uniform3f(u.uRight,cy,0,sy);gl.uniform3f(u.uUp,-sy*sp,cp,cy*sp);
    gl.uniform3fv(u.uEye,eye);gl.uniform3fv(u.uTravel,world);
    gl.uniform1f(u.uTime,elapsed);
    gl.drawArrays(gl.TRIANGLES,0,3);
    gl.activeTexture(gl.TEXTURE0);
  }
  function dispose() {
    if(dead)return;dead=true;
    software?.dispose();
    gl.deleteTexture(noiseTex);gl.deleteBuffer(buf);gl.deleteProgram(program);
    gl.getExtension('WEBGL_lose_context')?.loseContext();canvas.remove();
  }
  return {update,dispose,available:true};
}

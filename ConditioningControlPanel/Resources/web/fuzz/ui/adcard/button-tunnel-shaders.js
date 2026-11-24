// The walls and floor are intersections in world space. The cutouts use the same
// camera and lens, so looking sideways reveals how far the paper reaches back.
export const SCREEN_VERTEX = `
attribute vec2 point;
void main(){gl_Position=vec4(point,0.,1.);}
`;

const CAMERA = `
uniform vec2 resolution, gaze;
uniform vec3 eye;
uniform float lens;
vec3 toWorld(vec3 p){
  vec2 t=vec2(cos(gaze.y),sin(gaze.y));
  p.yz=vec2(t.x*p.y+t.y*p.z,-t.y*p.y+t.x*p.z);
  t=vec2(cos(gaze.x),sin(gaze.x));
  p.xz=vec2(t.x*p.x+t.y*p.z,-t.y*p.x+t.x*p.z);
  return p;
}
vec3 toEye(vec3 p){
  vec2 t=vec2(cos(gaze.x),sin(gaze.x));
  p.xz=vec2(t.x*p.x-t.y*p.z,t.y*p.x+t.x*p.z);
  t=vec2(cos(gaze.y),sin(gaze.y));
  p.yz=vec2(t.x*p.y-t.y*p.z,t.y*p.y+t.x*p.z);
  return p;
}`;

export const WALL_FRAGMENT = `
precision highp float;
${CAMERA}
uniform float clock, spin, entry, capture, calm;
const float TAU=6.2831853;
const float endZ=78.;
float seam(float x){return abs(fract(x+.5)-.5);}
float noise(vec2 p){return fract(sin(dot(p,vec2(91.73,47.41)))*19731.42);}
float glow(float d,float width){return exp(-abs(d)*width);}

vec3 wall(vec3 pos,float dist){
  float a=atan(pos.y,pos.x)/TAU;
  float z=pos.z;
  float travel=a+z*.018+spin;
  float band=travel*6.;
  float line=seam(band);
  float bay=z*.205;
  float cross=seam(bay);
  float fog=exp(-dist*.015);
  float aa=.004+min(.11,dist*dist*.000025);
  vec3 violet=vec3(.18,.058,.36);
  vec3 wine=vec3(.42,.055,.27);
  vec3 teal=vec3(.035,.31,.34);
  float lane=floor(band+.5);
  float family=.5+.5*cos(lane*2.0944);
  vec3 tint=mix(teal,mix(violet,wine,.5+.5*sin(lane*1.6)),family);
  vec3 light=mix(vec3(.055,.88,1.),vec3(1.,.08,.48),family);
  float enamel=sin(TAU*(band+.055*cos(bay*TAU)))*.5+.5;
  float coil=.5+.5*cos(TAU*band);
  float bevel=smoothstep(.02,.12,cross)*smoothstep(.02,.10,line);
  float sheen=pow(.5+.5*cos(TAU*(travel*2.+z*.009)-.7),7.);
  vec3 col=tint*(.13+.32*bevel+.27*coil)+vec3(.16,.06,.20)*sheen;
  col+=light*(.08+.18*enamel)*pow(enamel,3.);
  col+=light*(.19*glow(line,12.)+.95*glow(line,135./(1.+aa*16.)));
  // Twin filament along each ceramic ribbon. The second picks up the opposite dye.
  float pin=seam(band-.094);
  col+=mix(light,vec3(.94,.63,1.),.38)*(.35*glow(pin,120.));
  col+=vec3(.28,.24,.43)*glow(cross,49.)*smoothstep(.10,.24,line);
  float dash=(1.-smoothstep(.17,.24,seam(band-.34)))*(1.-smoothstep(.008,.025+aa,cross));
  col+=mix(vec3(1.,.72,.27),vec3(.63,.93,1.),family)*dash*1.45;
  // Thick enamel rims have an unlit underside and a soft highlight.
  float rim=seam(z*.0833);
  col*=.78+.22*smoothstep(.008,.045,rim);
  col+=light*.32*glow(rim,67.);
  float rib=seam(band*7.+z*.10);
  col+=.03*glow(rib,24.)*bevel*light*fog;
  float dust=pow(noise(floor(vec2(travel*780.,z*24.))),40.);
  col+=vec3(.74,.56,.61)*dust*.065*fog;
  // Rows of little lamps are recessed into the broad faces between ribbons.
  vec2 lamp=vec2(seam(band-.35)*13.,seam(bay*3.-.5)*4.9);
  float cell=length(lamp);
  vec3 bulb=mix(vec3(1.,.40,.095),vec3(.37,1.,.86),.5+.5*cos(lane*4.));
  col*=.60+.4*smoothstep(.70,1.12,cell);
  col+=bulb*(.24*glow(cell,1.7)+1.7*(1.-smoothstep(.28,.5+aa,cell)));
  return mix(vec3(.10,.025,.16),col,fog);
}

vec3 road(vec3 p,float dist){
  float x=abs(p.x), z=p.z;
  float side=abs(x-1.43);
  float middle=seam(z*.37);
  float paver=smoothstep(.012,.06,middle)*smoothstep(.009,.06,seam((p.x+.18*floor(z*.37))*1.3));
  vec3 col=mix(vec3(.018,.018,.055),vec3(.066,.065,.15),paver);
  col+=vec3(.24,.048,.23)*exp(-x*1.22)*(.45+.55*cos(z*.23+spin));
  col+=vec3(.84,.09,.52)*glow(side,9.)+vec3(1.,.65,.81)*glow(side,87.);
  col+=vec3(.11,.55,.75)*glow(x-1.22,49.);
  float stud=(1.-smoothstep(.025,.065,abs(x-1.07)))*glow(middle,31.);
  col+=vec3(1.,.82,.58)*stud;
  float reflect=glow(x-.70,3.)*seam(z*.23+sin(x*24.)*.11);
  col+=vec3(.24,.08,.23)*reflect;
  // The light catches the edges of each pane of the raised glass walkway.
  float chevron=seam(z*.19+x*.26);
  col+=mix(vec3(.17,.62,.76),vec3(1.,.35,.61),seam(z*.017)*2.)*
    glow(chevron,90.)*(1.-smoothstep(.28,.83,x))*.48;
  col+=vec3(.28,.11,.31)*glow(seam(z*1.4+.22*sin(x*19.)),39.)*glow(x-.97,4.);
  return mix(vec3(.19,.065,.26),col,exp(-dist*.024));
}

void main(){
  vec2 pixel=(2.*gl_FragCoord.xy-resolution)/resolution.y;
  vec3 ray=toWorld(normalize(vec3(pixel,lens)));
  float qa=max(dot(ray.xy,ray.xy),.00001), qb=dot(eye.xy,ray.xy);
  float hit=(-qb+sqrt(max(0.,qb*qb+qa*(21.16-dot(eye.xy,eye.xy)))))/qa;
  float endpoint=(endZ-eye.z)/max(ray.z,.0001);
  bool end=ray.z>.0 && endpoint<hit;
  vec3 p=eye+hit*ray;
  vec3 col=wall(p,hit);
  // A second radial shell stands proud of the enamel. Its rings have physical
  // undersides and collars: as the camera passes them the silhouettes separate.
  float inner=(-qb+sqrt(max(0.,qb*qb+qa*(17.64-dot(eye.xy,eye.xy)))))/qa;
  vec3 arch=eye+ray*inner;
  float bandZ=arch.z/6.2;
  float edge=seam(bandZ);
  float facet=atan(arch.y,arch.x)/TAU;
  float bayId=floor(bandZ+.5);
  float lobe=.5+.5*cos((facet+spin*.32)*TAU*5.+bayId*.72);
  vec3 tube=mix(vec3(.07,.73,.88),vec3(1.,.28,.54),lobe);
  tube=mix(tube,vec3(1.,.62,.15),smoothstep(.55,.96,sin(bayId*2.3))*lobe);
  if(edge<.034){
    float lumen=glow(edge-.022,172.);
    float gap=smoothstep(.008,.046,seam(facet*9.+bayId*.13));
    col=(vec3(.058,.026,.102)+tube*(.14+.11*lobe+1.75*lumen*gap));
    col=mix(vec3(.18,.055,.24),col,exp(-inner*.015));
  }
  float dir=ray.z>=0.?1.:-1.;
  float plane=(dir>0.?ceil(arch.z/6.2-.034)+.034:floor(arch.z/6.2+.034)-.034)*6.2;
  float planeD=(plane-eye.z)/(abs(ray.z)>.001?ray.z:dir*.001);
  float radialFace=length(eye.xy+ray.xy*planeD);
  if(planeD>0.&&planeD<hit&&radialFace>4.2&&radialFace<4.6){
    float inlay=glow(radialFace-4.28,36.);
    col=mix(vec3(.075,.046,.12)+tube*(.19+inlay*.65),vec3(.21,.073,.25),1.-exp(-planeD*.013));
  }
  float floorD=(-1.48-eye.y)/min(ray.y,-.00001);
  vec3 fp=eye+ray*floorD;
  if(ray.y<0. && floorD>0. && floorD<hit && abs(fp.x)<1.53 && fp.z<endZ){
    col=road(fp,floorD);
    if(end && floorD<endpoint) end=false;
  }
  if(end){
    vec3 ep=eye+ray*endpoint;
    float r=length(ep.xy);
    float a=atan(ep.y,ep.x);
    float corona=glow(r-1.55,5.)+glow(r-1.88,17.)*.7;
    float ring=glow(r-2.55,16.)*(.35+.65*smoothstep(-.1,.25,sin(a*9.+spin*3.)));
    col=vec3(.29,.045,.26)+vec3(.96,.20,.53)*(corona+glow(r,1.18)*2.)
       +vec3(.41,.87,.93)*ring*.68;
    col=mix(col,vec3(1.,.85,.91),1.-smoothstep(.22,1.54,r));
    col+=vec3(1.,.84,.68)*glow(r-.62,21.)*.38;
  }
  // A forward scattering halo sits in space. Turn away and it leaves the frame.
  vec3 target=normalize(vec3(0.,0.,endZ)-eye);
  float along=max(0.,dot(target,ray)), angle=length(cross(target,ray));
  float beam=exp(-angle*(7.+min(20.,(endZ-eye.z)*.24)))*along;
  col+=vec3(.70,.17,.42)*beam*(.62+capture)+vec3(.34,.23,.31)*pow(along,100.);
  col=1.-exp(-max(col,0.)*1.55);
  col=pow(col,vec3(.84));
  // The tube materializes underneath the registered board. Its centre
  // develops a little earlier, without an expanding ring wiping the board.
  float radial=length(pixel/vec2(max(1.,resolution.x/resolution.y),max(1.,resolution.y/resolution.x)));
  float aperture=smoothstep(0.,1.,entry*1.16-min(1.,radial)*.15);
  col=mix(col,vec3(1.,.86,.92),smoothstep(.55,1.,capture));
  gl_FragColor=vec4(col,aperture);
}`;

export const DUST_VERTEX = `
attribute vec3 point;
${CAMERA}
uniform float clock, motion;
varying float shine, hue;
void main(){
  vec3 p=point;
  float angle=p.x+motion*.08*clock+p.z*.016;
  p.xy=vec2(cos(angle),sin(angle))*p.y;
  p.y+=sin(clock*.45+point.x)*.09*motion;
  vec3 v=toEye(p-eye);
  gl_Position=vec4(v.x*lens*resolution.y/resolution.x,v.y*lens,v.z*1.0008-.12,v.z);
  shine=.66+.32*cos(clock*(.7+fract(p.z)) + point.z);
  hue=fract(point.z*1.71);
  gl_PointSize=clamp(resolution.y*(.022+.066*hue)/max(.12,v.z),1.25,18.);
}`;

export const DUST_FRAGMENT = `
precision mediump float;
varying float shine,hue;
uniform float opacity;
void main(){
  vec2 p=gl_PointCoord*2.-1.;
  float r=length(p);
  float core=exp(-r*6.);
  float rays=exp(-abs(p.x*p.y)*85.)*max(0.,1.-r);
  vec3 col=mix(vec3(.49,.93,1.),vec3(1.,.69,.86),hue);
  gl_FragColor=vec4(col,(core+rays*.65)*opacity*shine);
}`;

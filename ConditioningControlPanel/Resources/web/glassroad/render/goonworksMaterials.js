/* Opaque enamel and triangle-based ink. All nodes are shared by WebGPU and WebGL2. */
import { MeshBasicNodeMaterial, Color, DoubleSide, BackSide, DataTexture, RGBAFormat, UnsignedByteType, LinearFilter, RepeatWrapping } from 'three/webgpu';
import { vec2, vec3, float, uv, positionGeometry, positionWorld, normalWorld, cameraPosition, normalize, dot, max, min, abs, pow, mix, smoothstep, fract, fwidth, texture, sin } from 'three/tsl';

export const GOONWORKS_PALETTE = Object.freeze({ fuchsia: '#bd006e', hot: '#e80087', wine: '#69003e',
  white: '#fff9ff', cream: '#fff0fa', ink: '#1a061c', shade: '#3c1037', sky: '#f7d9e9', distant: '#cb8aaf' });
const rgb = hex => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };

export function createGoonworksMaterial(hex, { ink = false, paper = false, unlit = false, outline = false } = {}) {
  const material = new MeshBasicNodeMaterial({ side: outline ? BackSide : DoubleSide, toneMapped:false });
  material.name = 'goonworks-' + (outline || ink ? 'ink' : hex);
  if (outline || ink || unlit) { material.colorNode = rgb(ink || outline ? GOONWORKS_PALETTE.ink : hex); return material; }
  const base = rgb(hex), N = normalize(normalWorld), L = normalize(vec3(-.38, .82, .42));
  const V = normalize(cameraPosition.sub(positionWorld)), light = dot(N, L), H = normalize(L.add(V));
  const bands = smoothstep(-.08, .08, light).mul(.17).add(smoothstep(.52, .66, light).mul(.13)).add(.7);
  const highlight = pow(max(dot(N,H),0), paper ? 22 : 36).mul(paper ? .08 : .28);
  const edge = float(1).sub(abs(dot(N,V))), silhouette = smoothstep(.89,.975,edge);
  // Dots stay attached to each source mesh. Pixel filtering suppresses distant moire.
  const p = positionGeometry.xy.mul(24), cell = fract(p).sub(.5), footprint = max(fwidth(p.x),fwidth(p.y));
  const dots = float(1).sub(smoothstep(.12,.22,cell.length())).mul(float(1).sub(smoothstep(.25,.7,footprint)))
    .mul(float(1).sub(smoothstep(-.05,.3,light))).mul(.13);
  const lit = base.mul(bands).mul(float(1).sub(dots)).add(rgb('#fff9ff').mul(highlight));
  material.colorNode = mix(lit,rgb(GOONWORKS_PALETTE.ink),silhouette.mul(.87));
  return material;
}

/** Apply after createRoadMaterial. Return the resource owner for scene disposal. */
export function applyGoonworksRoad(road, art, track) {
  if (track?.id !== 'goonworks') return null;
  const size = Math.max(2,Math.ceil(track.lengthM)), bytes = new Uint8Array(size*4);
  for(let i=0;i<size;i++){bytes[i*4]=Math.round(Math.min(1,track.frameAt((i+.5)*track.lengthM/size).w/120)*255);bytes[i*4+3]=255;}
  const widths = new DataTexture(bytes,size,1,RGBAFormat,UnsignedByteType);
  widths.minFilter=widths.magFilter=LinearFilter;widths.wrapS=RepeatWrapping;widths.generateMipmaps=false;widths.needsUpdate=true;
  const p=uv(), half=texture(widths,vec2(p.y.div(track.lengthM),.5)).r.mul(60), edge=half.sub(abs(p.x));
  const pixel=max(fwidth(edge),.015), ink=float(1).sub(smoothstep(.19,pixel.add(.38),edge));
  const gutter=float(1).sub(smoothstep(1.65,1.9,edge)).mul(float(1).sub(ink));
  const N=normalize(normalWorld), top=smoothstep(.48,.72,N.y);
  const light=max(dot(N,normalize(vec3(-.38,.82,.42))),0).mul(.08).add(.92);
  const paper=rgb(GOONWORKS_PALETTE.white).mul(light);
  const dark=rgb(GOONWORKS_PALETTE.wine), pink=rgb(GOONWORKS_PALETTE.fuchsia);
  let colour=mix(dark,paper,top);
  // One two-tone checker shoulder and a thin inner rule, leaving the racing line open.
  const checker=smoothstep(.43,.57,sin(p.y.mul(Math.PI/3.5)));
  colour=mix(colour,mix(paper,pink,checker),gutter.mul(top));
  const innerRule=float(1).sub(smoothstep(.055,.11,abs(edge.sub(2.08))));
  colour=mix(colour,pink,innerRule.mul(top).mul(.75));
  colour=mix(colour,rgb(GOONWORKS_PALETTE.ink),ink.mul(top));
  const dashAlong=fract(p.y.div(18)),dash=smoothstep(.02,.055,dashAlong).mul(float(1).sub(smoothstep(.29,.32,dashAlong)))
    .mul(float(1).sub(smoothstep(.13,.2,abs(p.x))));
  const arrowS=fract(p.y.div(84)).mul(84),arrowShape=abs(arrowS.add(abs(p.x).mul(.72)).sub(10));
  const chevron=float(1).sub(smoothstep(.24,.38,arrowShape)).mul(float(1).sub(smoothstep(3.1,3.5,abs(p.x))));
  colour=mix(colour,rgb(GOONWORKS_PALETTE.ink),dash.mul(top).mul(.78));
  colour=mix(colour,pink,chevron.mul(top));
  // Preserve the existing song clock as a restrained fuchsia shoulder response.
  if(road.uniforms?.music){const mu=road.uniforms.music; const pulse=sin(p.y.mul(.13).sub(mu.t.mul(1.8))).mul(.5).add(.5).mul(mu.bassW).mul(.06);colour=colour.add(pink.mul(pulse).mul(gutter));}
  road.material.colorNode=colour;road.material.opacityNode=float(1);road.material.opacity=1;
  road.material.transparent=false;road.material.depthWrite=true;road.material.toneMapped=false;
  let disposed=false;const owner={dispose(){if(!disposed){disposed=true;widths.dispose();}}};
  road.goonworksFinish=owner;return owner;
}

/** Oval cutouts are shared by inner and outer skins, with ink and enamel around each opening. */
export function createGoonworksTubeMaterial(outer=false){
  const material=createGoonworksMaterial(outer?GOONWORKS_PALETTE.fuchsia:GOONWORKS_PALETTE.white,{paper:!outer}),p=uv();
  const along=abs(fract(p.y.div(76)).sub(.5)).mul(76),angular=min(abs(sin(p.x.sub(.04))),abs(sin(p.x.add(.04))));
  const oval=along.div(24).pow(2).add(angular.div(.6).pow(2));
  const opening=float(1).sub(smoothstep(.96,1,oval));
  material.opacityNode=float(1).sub(opening);material.alphaTest=.5;material.transparent=false;material.depthWrite=true;
  const rim=float(1).sub(smoothstep(1.03,1.14,oval));
  const enamel=smoothstep(1.13,1.2,oval).mul(float(1).sub(smoothstep(1.32,1.4,oval)));
  const ribDistance=min(fract(p.y.div(38)),float(1).sub(fract(p.y.div(38)))).mul(38);
  const rib=float(1).sub(smoothstep(1.35,1.55,ribDistance)),ribInk=float(1).sub(smoothstep(.06,.16,abs(ribDistance.sub(1.65))));
  let colour=material.colorNode;
  colour=mix(colour,rgb(GOONWORKS_PALETTE.fuchsia),rib);
  colour=mix(colour,rgb(GOONWORKS_PALETTE.white),enamel);
  colour=mix(colour,rgb(GOONWORKS_PALETTE.ink),max(rim,ribInk));
  material.colorNode=colour;return material;
}

export function createGoonworksPlatformMaterial(w,len){
  const material=createGoonworksMaterial(GOONWORKS_PALETTE.white,{paper:true}),p=uv();
  const edge=min(float(w/2).sub(abs(p.x)),float(len/2).sub(abs(p.y)));
  const ink=float(1).sub(smoothstep(.17,.27,edge));
  const shoulder=float(1).sub(smoothstep(1.1,1.3,edge));
  const checker=smoothstep(-.1,.1,sin(p.x.mul(Math.PI/3)).mul(sin(p.y.mul(Math.PI/3))));
  const arrow=abs(p.y.add(float(len/2-5)).sub(abs(p.x).mul(.62)));
  const chevron=float(1).sub(smoothstep(.25,.4,arrow)).mul(float(1).sub(smoothstep(4.8,5.2,abs(p.x))));
  material.colorNode=mix(material.colorNode,rgb(GOONWORKS_PALETTE.fuchsia),max(shoulder.mul(checker),chevron));
  material.colorNode=mix(material.colorNode,rgb(GOONWORKS_PALETTE.ink),ink);
  material.colorNode=mix(rgb(GOONWORKS_PALETTE.ink),material.colorNode,smoothstep(.4,.7,normalize(normalWorld).y));return material;
}

export function applyGoonworksRails(rails) {
  if(!rails?.material)return;
  rails.material.colorNode=rgb(GOONWORKS_PALETTE.ink);
  rails.material.opacityNode=float(1);rails.material.opacity=1;rails.material.transparent=false;
}

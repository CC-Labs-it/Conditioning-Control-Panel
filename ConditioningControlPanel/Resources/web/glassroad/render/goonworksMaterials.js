/* Opaque enamel and triangle-based ink. All nodes are shared by WebGPU and WebGL2. */
import { MeshBasicNodeMaterial, Color, DoubleSide, BackSide, FrontSide, DataTexture, RGBAFormat, UnsignedByteType, LinearFilter, RepeatWrapping } from 'three/webgpu';
import { vec2, vec3, float, uniform, uv, positionGeometry, positionWorld, normalWorld, cameraPosition, normalize, dot, max, min, abs, pow, mix, smoothstep, fract, fwidth, texture, sin } from 'three/tsl';
import {getGoonworksLayout} from '../track/goonworks-layout.js';
import { trackTableWidth } from './spectrum.js';
import { goonworksMusicFloor } from './materials/goonworksMusicFloor.js';
import { reliefNormal, reliefFootprint } from './surfaceRelief.js';
import { trackTableWidth } from './spectrum.js';

export const GOONWORKS_PALETTE = Object.freeze({ fuchsia: '#b60870', hot: '#dc4296', wine: '#661448',
  magenta:'#993e91',rose:'#ca6897',plum:'#513751',pink:'#e78eb8',
  white:'#ede7e6',cream:'#efe4d1',ivory:'#ede4cf',ice:'#dce8eb',lilac:'#e2dcef',blush:'#ebd4df',warmgray:'#b9aaa9',
  teal:'#36BDAE',periwinkle:'#A19CE5',apricot:'#F2B580',gold:'#E2B748',
  cobalt:'#356BD0',violet:'#7652BA',coral:'#F28182',mint:'#73C9AE',lime:'#B4CD63',tangerine:'#ED994F',indigo:'#4C5799',
  ink: '#1a101e', shade: '#473347', sky: '#dfcadc', distant: '#b785a8',distantCool:'#9c8ead',distantWarm:'#bd9a9a' });
export const GOONWORKS_DISTRICT_PALETTES=Object.freeze({
  start:Object.freeze({road:'white',pink:'fuchsia',accent:'gold'}),
  'rabbit-hole':Object.freeze({road:'ice',pink:'magenta',accent:'teal'}),
  'screen-grotto':Object.freeze({road:'ice',pink:'magenta',accent:'cobalt'}),
  'aperture-mill':Object.freeze({road:'ivory',pink:'fuchsia',accent:'gold'}),
  'pop-quiz':Object.freeze({road:'lilac',pink:'rose',accent:'violet'}),
  'ribbon-helix':Object.freeze({road:'blush',pink:'fuchsia',accent:'coral'}),
  leapfrog:Object.freeze({road:'ice',pink:'rose',accent:'mint'}),
  'pinball-launch':Object.freeze({road:'ivory',pink:'hot',accent:'tangerine'}),
  'grand-fold':Object.freeze({road:'lilac',pink:'magenta',accent:'indigo'}),
  finish:Object.freeze({road:'white',pink:'fuchsia',accent:'apricot'})
});
export const GOONWORKS_PAPER_MATERIALS=Object.freeze(['white','cream','ivory','ice','lilac','blush']);
const rgb = hex => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };

// Analytic print texture has no uploads or per-frame work. Each frequency fades independently
// before it becomes smaller than a pixel, including on scaled, moving instances.
function filteredWave(phase) {
  return sin(phase).mul(float(1).sub(smoothstep(.75,2.8,fwidth(phase))));
}
function surfaceGrain(p, paper) {
  const fibre=filteredWave(dot(p,vec3(91.7,137.3,73.1)));
  const tooth=filteredWave(dot(p,vec3(173.3,-59.7,119.9)));
  return fibre.mul(paper?.019:.008).add(tooth.mul(paper?.012:.006)).add(1);
}

export function createGoonworksMaterial(hex, { ink = false, paper = false, unlit = false, outline = false } = {}) {
  const material = new MeshBasicNodeMaterial({ side: outline ? BackSide : DoubleSide, toneMapped:false });
  material.name = 'goonworks-' + (outline || ink ? 'ink' : hex);
  if (outline || ink || unlit) { material.colorNode = rgb(ink || outline ? GOONWORKS_PALETTE.ink : hex); return material; }
  const base = uniform(new Color(hex)), L = normalize(vec3(-.38, .82, .42));
  // Paper has shallow pressed tooth; the enamel is gently rolled rather than perfectly planar.
  const mould = sin(dot(positionGeometry, vec3(3.2, 1.4, 2.1))).mul(paper ? .006 : .028)
    .add(sin(dot(positionGeometry, vec3(19.7, 7.3, 11.1))).mul(paper ? .0018 : .001));
  const N = reliefNormal(normalWorld, mould, reliefFootprint(positionGeometry.xy.mul(3)), { slope: paper ? .12 : .28 });
  const V = normalize(cameraPosition.sub(positionWorld)), light = dot(N, L), H = normalize(L.add(V));
  const bands = smoothstep(-.08, .08, light).mul(.17).add(smoothstep(.52, .66, light).mul(.13)).add(.7);
  const highlight = pow(max(dot(N,H),0), paper ? 22 : 36).mul(paper ? .035 : .28);
  const edge = float(1).sub(abs(dot(N,V))), silhouette = smoothstep(.89,.975,edge);
  // Dots stay attached to each source mesh. Pixel filtering suppresses distant moire.
  const p = positionGeometry.xy.mul(24), cell = fract(p).sub(.5), footprint = max(fwidth(p.x),fwidth(p.y));
  const dots = float(1).sub(smoothstep(.12,.22,cell.length())).mul(float(1).sub(smoothstep(.25,.7,footprint)))
    .mul(float(1).sub(smoothstep(-.05,.3,light))).mul(.13);
  const shaded=paper?mix(rgb(GOONWORKS_PALETTE.warmgray),base,bands.mul(.75).add(.18)):base.mul(bands);
  // Paper keeps its dry fine tooth; painted toys get a broad, quiet enamel roll instead.
  // The source position follows rotating wheels and gears without swimming across their faces.
  const grain=surfaceGrain(positionGeometry,paper);
  const paintRoll=sin(dot(positionGeometry,vec3(3.2,1.4,2.1))).mul(.5).add(.5);
  const finish=paper?float(1):paintRoll.mul(.06).add(.96);
  const lit = shaded.mul(grain).mul(finish).mul(float(1).sub(dots)).add(rgb('#f5eddf').mul(highlight));
  material.colorNode = mix(lit,rgb(GOONWORKS_PALETTE.ink),silhouette.mul(.87));
  return material;
}

/** Apply after createRoadMaterial. Return the resource owner for scene disposal. */
export function applyGoonworksRoad(road, art, track) {
  if (track?.id !== 'goonworks') return null;
  // one texel per metre, capped at 8192 (WebGPU's default maxTextureDimension2D; GOONWORKS is 9665 m since the reshape)
  const size = trackTableWidth(track.lengthM), bytes = new Uint8Array(size*4*4);
  const layout=getGoonworksLayout(track),halls=['mill-hall','quiz-ballroom','pinball-table'].map(id=>layout.byId[id]);
  const districtColourCache=new Map(Object.entries(GOONWORKS_DISTRICT_PALETTES).map(([id,p])=>[id,[p.road,p.pink,p.accent].map(key=>new Color(GOONWORKS_PALETTE[key]).toArray())]));
  for(let i=0;i<size;i++){const s=(i+.5)*track.lengthM/size;bytes[i*4]=Math.round(Math.min(1,track.frameAt(s).w/120)*255);
    bytes[i*4+1]=halls.some(r=>s>=r.s0&&s<=r.s1)?255:0;bytes[i*4+3]=255;
    const section=layout.sections.find(r=>s>=r.s0&&s<r.s1),colours=districtColourCache.get(section?.id)||districtColourCache.get('start');
    for(let row=1;row<4;row++){const offset=(row*size+i)*4;for(let c=0;c<3;c++)bytes[offset+c]=Math.round(colours[row-1][c]*255);bytes[offset+3]=255;}
  }
  const widths = new DataTexture(bytes,size,4,RGBAFormat,UnsignedByteType);
  widths.minFilter=widths.magFilter=LinearFilter;widths.wrapS=RepeatWrapping;widths.generateMipmaps=false;widths.needsUpdate=true;
  const p=uv(),distanceUV=p.y.div(track.lengthM),district=texture(widths,vec2(distanceUV,.125)),half=district.r.mul(60), edge=half.sub(abs(p.x));
  const pixel=max(fwidth(edge),.015), ink=float(1).sub(smoothstep(.19,pixel.add(.38),edge));
  const gutter=float(1).sub(smoothstep(1.65,1.9,edge)).mul(float(1).sub(ink));
  const N=normalize(normalWorld), top=smoothstep(.48,.72,N.y);
  const light=max(dot(N,normalize(vec3(-.38,.82,.42))),0).mul(.09).add(.87);
  const glaze=smoothstep(0,half,abs(p.x)).mul(.045).add(sin(p.y.mul(.018)).mul(.012)).add(.945);
  // A warm printed board under the glossy shoulder paint. Fine fibres disappear at distance.
  const roadGrain=filteredWave(p.x.mul(43).add(p.y.mul(7))).mul(.014)
    .add(filteredWave(p.y.mul(31).sub(p.x.mul(11))).mul(.009)).add(1);
  const paper=texture(widths,vec2(distanceUV,.375)).rgb.mul(light).mul(glaze).mul(roadGrain);
  const dark=rgb(GOONWORKS_PALETTE.wine),pink=texture(widths,vec2(distanceUV,.625)).rgb,accent=texture(widths,vec2(distanceUV,.875)).rgb;
  let colour=mix(dark,paper,top);
  // Broad district enamel bands stay visible from the kart. The central paper lane and
  // fuchsia outer checker remain the common visual language throughout the course.
  const bandStart=2.8,bandEnd=half.mul(.18).add(bandStart);
  const districtBand=smoothstep(float(bandStart).sub(pixel),float(bandStart).add(pixel),edge)
    .mul(float(1).sub(smoothstep(bandEnd.sub(pixel),bandEnd.add(pixel),edge)));
  const bandRoll=smoothstep(0,1,edge.sub(bandStart).div(half.mul(.18))).mul(.10).add(.93);
  const bandGlaze=mix(accent,paper,.14).mul(light).mul(bandRoll);
  colour=mix(colour,bandGlaze,districtBand.mul(top));
  // An inset ivory pinline makes the wide accent read as finished enamel, not a flat fill.
  const bandLip=float(1).sub(smoothstep(.04,pixel.add(.10),abs(edge.sub(bandEnd.sub(.3)))));
  colour=mix(colour,mix(accent,rgb(GOONWORKS_PALETTE.ivory),.58),bandLip.mul(top).mul(.7));
  // Two fine enamel ribbons bring scale to the wide floor without filling the racing line.
  const laneCenter=half.mul(.12).add(3.8).add(sin(p.y.mul(.026)).mul(.32)),laneDistance=abs(abs(p.x).sub(laneCenter));
  const lanePixel=max(fwidth(laneDistance),.025),laneSoft=float(1).sub(smoothstep(.64,lanePixel.add(.88),laneDistance));
  const laneLine=float(1).sub(smoothstep(.12,lanePixel.add(.22),laneDistance));
  const printable=float(1).sub(smoothstep(.6,1.5,max(fwidth(p.x),fwidth(p.y))));
  colour=mix(colour,mix(pink,accent,.3),laneSoft.mul(.16).add(laneLine.mul(.52)).mul(top).mul(printable));
  // Sparse outlined lozenges on the outer floor recall a toy game board in each large hall.
  const bay=abs(fract(p.y.div(34)).sub(.5)).mul(34),tileX=abs(abs(p.x).sub(half.mul(.64)));
  const lozenge=tileX.div(3.2).add(bay.div(6.4)),tilePixel=max(fwidth(lozenge),.025);
  const tile=float(1).sub(smoothstep(.91,tilePixel.add(1),lozenge));
  const tileRule=float(1).sub(smoothstep(.025,tilePixel.add(.055),abs(lozenge.sub(1))));
  colour=mix(colour,accent,tile.mul(.1).add(tileRule.mul(.48)).mul(district.g).mul(top).mul(printable));
  // Large halls carry faint staggered print blocks outside the racing line. The low contrast
  // leaves the coloured district bands, arrows and physical toy warnings in charge.
  const printCell=vec2(p.x.div(12),p.y.div(24).add(fract(p.x.div(24)).mul(.12)));
  const blockEdge=min(abs(fract(printCell.x).sub(.5)),abs(fract(printCell.y).sub(.5)));
  const blockPixel=max(fwidth(printCell.x),fwidth(printCell.y));
  const blockRule=float(1).sub(smoothstep(.006,max(blockPixel,.003).add(.012),blockEdge));
  const outerPrint=smoothstep(half.mul(.3),half.mul(.5),abs(p.x));
  colour=mix(colour,mix(paper,accent,.28),blockRule.mul(outerPrint).mul(district.g).mul(top).mul(printable).mul(.24));
  // One two-tone checker shoulder and a thin inner rule, leaving the racing line open.
  const checkerPhase=p.y.mul(Math.PI/3.5),checker=mix(smoothstep(-.1,.1,sin(checkerPhase)),float(.5),smoothstep(.6,2,fwidth(checkerPhase)));
  colour=mix(colour,mix(paper,pink,checker),gutter.mul(top));
  const innerRule=float(1).sub(smoothstep(.055,pixel.add(.11),abs(edge.sub(2.08))));
  colour=mix(colour,pink,innerRule.mul(top).mul(.75));
  colour=mix(colour,rgb(GOONWORKS_PALETTE.ink),ink.mul(top));
  const dashAlong=fract(p.y.div(18)),dash=smoothstep(.02,.055,dashAlong).mul(float(1).sub(smoothstep(.29,.32,dashAlong)))
    .mul(float(1).sub(smoothstep(.13,max(fwidth(p.x),.025).add(.2),abs(p.x))));
  const arrowS=fract(p.y.div(84)).mul(84),arrowShape=abs(arrowS.add(abs(p.x).mul(.72)).sub(10));
  const chevron=float(1).sub(smoothstep(.24,max(fwidth(arrowShape),.04).add(.38),arrowShape)).mul(float(1).sub(smoothstep(3.1,3.5,abs(p.x))));
  colour=mix(colour,rgb(GOONWORKS_PALETTE.ink),dash.mul(top).mul(.78).mul(printable));
  colour=mix(colour,pink,chevron.mul(top).mul(printable));
  // This opaque finish replaces the shared road graph, so its visible colour must own the music.
  if (road.uniforms?.music && road.uniforms?.reactive) colour = goonworksMusicFloor({ p, base: colour,
    top, edge, half, pink, accent, reactive: road.uniforms.reactive, music: road.uniforms.music });
  road.material.colorNode=colour;road.material.opacityNode=float(1);road.material.opacity=1;
  road.material.transparent=false;road.material.depthWrite=true;road.material.toneMapped=false;
  // The opaque slab already has correctly wound top, bottom and side faces. Rendering its hidden
  // back faces makes the 25 cm slab fight itself at overview distances with a 0.1 m camera near plane.
  road.material.side=FrontSide;
  let disposed=false;const owner={districts:GOONWORKS_DISTRICT_PALETTES,texture:widths,dispose(){if(!disposed){disposed=true;widths.dispose();}}};
  road.goonworksFinish=owner;return owner;
}

/** Oval cutouts are shared by inner and outer skins, with ink and enamel around each opening. */
export function createGoonworksTubeMaterial(outer=false){
  const material=createGoonworksMaterial(outer?GOONWORKS_PALETTE.fuchsia:GOONWORKS_PALETTE.ice,{paper:!outer}),p=uv();
  // Inner normals face the tunnel; outer normals face the sky. Cull the opposite skin at each view.
  material.side=FrontSide;
  const along=abs(fract(p.y.div(76)).sub(.5)).mul(76),angular=min(abs(sin(p.x.sub(.04))),abs(sin(p.x.add(.04))));
  const oval=along.div(24).pow(2).add(angular.div(.6).pow(2));
  const opening=float(1).sub(smoothstep(.96,1,oval));
  material.opacityNode=float(1).sub(opening);material.alphaTest=.5;material.transparent=false;material.depthWrite=true;
  const rim=float(1).sub(smoothstep(1.03,1.14,oval));
  const enamel=smoothstep(1.13,1.2,oval).mul(float(1).sub(smoothstep(1.32,1.4,oval)));
  const ribDistance=min(fract(p.y.div(38)),float(1).sub(fract(p.y.div(38)))).mul(38);
  const ribWidth=outer?1.35:4.8;
  const ribPixel=max(fwidth(ribDistance),.06);
  const rib=float(1).sub(smoothstep(ribWidth,ribPixel.add(ribWidth+.16),ribDistance));
  const ribInk=float(1).sub(smoothstep(.08,ribPixel.add(.16),abs(ribDistance.sub(ribWidth+.3))));
  let colour=material.colorNode;
  const panelTone=sin(p.y.mul(.033)).mul(.5).add(.5);
  colour=mix(colour,rgb(outer?GOONWORKS_PALETTE.magenta:GOONWORKS_PALETTE.lilac),panelTone.mul(outer?.23:.16));
  colour=mix(colour,rgb(outer?GOONWORKS_PALETTE.magenta:GOONWORKS_PALETTE.teal),rib);
  // Glazed mouldings catch a narrow cream edge; a dry print seam splits the broad panels.
  const ribLip=float(1).sub(smoothstep(.06,ribPixel.add(.16),abs(ribDistance.sub(ribWidth-.35))));
  const panelSeam=abs(fract(p.y.div(19)).sub(.5)).mul(19);
  const seamPixel=max(fwidth(panelSeam),.03);
  const seam=float(1).sub(smoothstep(.035,seamPixel.add(.085),panelSeam));
  const tubeGrain=filteredWave(p.y.mul(23).add(p.x.mul(67))).mul(outer?.005:.012).add(1);
  colour=colour.mul(tubeGrain);
  colour=mix(colour,rgb(outer?GOONWORKS_PALETTE.rose:GOONWORKS_PALETTE.warmgray),seam.mul(outer?.16:.1));
  if(outer)colour=mix(colour,rgb(GOONWORKS_PALETTE.cream),ribLip.mul(.18));
  if(outer)colour=mix(colour,rgb(GOONWORKS_PALETTE.ivory),enamel);
  colour=mix(colour,rgb(GOONWORKS_PALETTE.ink),max(rim,ribInk));
  material.colorNode=colour;return material;
}

export function createGoonworksPlatformMaterial(w,len,index=0){
  const material=createGoonworksMaterial(GOONWORKS_PALETTE.ice,{paper:true}),p=uv();
  const edge=min(float(w/2).sub(abs(p.x)),float(len/2).sub(abs(p.y)));
  const ink=float(1).sub(smoothstep(.17,.27,edge));
  const shoulder=float(1).sub(smoothstep(1.1,1.3,edge));
  const edgePixel=max(fwidth(edge),.04);
  const islandBand=smoothstep(1.45,edgePixel.add(1.6),edge).mul(float(1).sub(smoothstep(3.4,edgePixel.add(3.6),edge)));
  const checkerPhase=vec2(p.x.mul(Math.PI/3),p.y.mul(Math.PI/3));
  const checker=mix(smoothstep(-.1,.1,sin(checkerPhase.x).mul(sin(checkerPhase.y))),float(.5),
    smoothstep(.6,2,max(fwidth(checkerPhase.x),fwidth(checkerPhase.y))));
  const arrow=abs(p.y.add(float(len/2-5)).sub(abs(p.x).mul(.62)));
  const chevron=float(1).sub(smoothstep(.25,.4,arrow)).mul(float(1).sub(smoothstep(4.8,5.2,abs(p.x))));
  material.colorNode=mix(material.colorNode,rgb(GOONWORKS_PALETTE.rose),shoulder.mul(checker));
  material.colorNode=mix(material.colorNode,rgb(index%2?GOONWORKS_PALETTE.lime:GOONWORKS_PALETTE.mint),islandBand);
  // Each island is a glazed game token with an inset cream bevel painted onto its top.
  const bandLip=float(1).sub(smoothstep(.06,edgePixel.add(.14),abs(edge.sub(3.12))));
  const glaze=smoothstep(1.3,3.4,edge).mul(.035).add(.975);
  material.colorNode=material.colorNode.mul(glaze);
  material.colorNode=mix(material.colorNode,rgb(GOONWORKS_PALETTE.cream),bandLip.mul(.55));
  material.colorNode=mix(material.colorNode,rgb(GOONWORKS_PALETTE.teal),chevron);
  material.colorNode=mix(material.colorNode,rgb(GOONWORKS_PALETTE.ink),ink);
  material.colorNode=mix(rgb(GOONWORKS_PALETTE.ink),material.colorNode,smoothstep(.4,.7,normalize(normalWorld).y));return material;
}

export function applyGoonworksRails(rails) {
  if(!rails?.material)return;
  rails.material.colorNode=rgb(GOONWORKS_PALETTE.ink);
  rails.material.opacityNode=float(1);rails.material.opacity=1;rails.material.transparent=false;
}

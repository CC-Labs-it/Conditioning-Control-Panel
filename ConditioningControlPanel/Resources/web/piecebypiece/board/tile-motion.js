// Patterns live in each tile's local surface coordinates and share the paused theme clock.
// Opaque physical materials keep board highlights, shadows and piece silhouettes intact.
const FINISH = {
  observatory: [
    { roughness:.47, metalness:.10, clearcoat:.32, clearcoatRoughness:.38, envMapIntensity:.10, specularIntensity:.36 },
    { roughness:.67, metalness:.02, clearcoat:.14, clearcoatRoughness:.55, envMapIntensity:.13, specularIntensity:.28 },
  ],
  conservatory: [
    { roughness:.24, metalness:.02, clearcoat:.92, clearcoatRoughness:.18, envMapIntensity:.24, specularIntensity:.48, ior:1.46 },
    { roughness:.89, metalness:0, clearcoat:.03, clearcoatRoughness:.8, envMapIntensity:.09, specularIntensity:.20 },
  ],
  theatre: [
    { roughness:.92, metalness:0, clearcoat:0, envMapIntensity:.07, specularIntensity:.18, sheen:1, sheenRoughness:.76 },
    { roughness:.30, metalness:.08, clearcoat:.86, clearcoatRoughness:.22, envMapIntensity:.23, specularIntensity:.44, ior:1.48 },
  ],
  arcade: [
    { roughness:.53, metalness:.62, clearcoat:.18, clearcoatRoughness:.36, envMapIntensity:.24, specularIntensity:.45 },
    { roughness:.39, metalness:.12, clearcoat:.56, clearcoatRoughness:.25, envMapIntensity:.22, specularIntensity:.36 },
  ],
};

const COMMON = /* glsl */`
uniform float uTileTime;
uniform float uTileSeed;
uniform float uTileLight;
varying vec2 vTileLocal;
varying float vTileTop;
float tileHash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float tileNoise(vec2 p) {
  vec2 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
  return mix(mix(tileHash(i),tileHash(i+vec2(1,0)),f.x),mix(tileHash(i+vec2(0,1)),tileHash(i+vec2(1)),f.x),f.y);
}
float tileLine(float distance,float width) {
  float aa=max(fwidth(distance),.0015);
  return 1.0-smoothstep(width,width+aa,distance);
}
`;

const DETAILS = {
  observatory: /* glsl */`
    // One irregular fracture and a short offshoot per tile, with independent stone direction.
    // The noise field changes the actual seam path slowly without a repeated stripe pattern.
    float direction=tileHash(vec2(uTileSeed,7.3))*3.14159265;
    float cs=cos(direction),sn=sin(direction);
    vec2 p=mat2(cs,-sn,sn,cs)*(vTileLocal-.5)+.5;
    float t=uTileTime*.19;
    float seed=tileHash(vec2(uTileSeed,2.9));
    float mineral=tileNoise((p+uTileSeed)*5.5+vec2(t*.25,-t*.13));
    float bend=tileNoise(vec2(p.y*3.4+uTileSeed,t*.55))-.5;
    float splinter=tileNoise(vec2(p.y*15.0+uTileSeed,t*.36))-.5;
    float seam=.22+seed*.56+bend*.32+splinter*.075+.035*sin(t+uTileSeed);
    float vein=tileLine(abs(p.x-seam),.0025+mineral*.003);
    float junction=.22+tileHash(vec2(uTileSeed,4.1))*.38;
    float forkDirection=mix(-1.0,1.0,step(.5,seed));
    float branch=seam+forkDirection*(p.y-junction)*(.50+seed*.50);
    float hair=tileLine(abs(p.x-branch),.002);
    hair*=smoothstep(junction,junction+.035,p.y)*(1.0-smoothstep(junction+.22,junction+.43,p.y))*.66;
    float gold=max(vein,hair)*tileFace;
    vec3 inlay=mix(vec3(.72,.43,.12),vec3(.36,.19,.055),uTileLight);
    diffuseColor.rgb=mix(diffuseColor.rgb,inlay,gold*mix(.78,.52,uTileLight));
    diffuseColor.rgb*=1.0+(mineral-.5)*.20*tileFace;
    tileGlow=inlay*gold*mix(.13,.025,uTileLight);
    tileRoughShift=-gold*.24;
  `,
  conservatory: /* glsl */`
    vec2 p=vTileLocal+vec2(uTileSeed*.31,uTileSeed*.17);
    float t=uTileTime*.16;
    vec2 flow=p*5.4+vec2(t*.55,-t*.32);
    flow+=vec2(sin(flow.y+t),cos(flow.x-t))*.34;
    float water=pow(1.0-abs(sin(flow.x+sin(flow.y*1.4))),10.0);
    water*=.40+.60*pow(1.0-abs(sin(flow.y-flow.x*.65)),3.0);
    float wet=water*tileFace*mix(1.0,.22,uTileLight);
    diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.38,.62,.37),wet*.23);
    // Sparse pollen grains drift across the glaze. Hashes fix the density per tile.
    vec2 drift=p*4.0+vec2(t*.62,-t*.27);
    vec2 cell=floor(drift),dotPoint=fract(drift)-.5;
    float seed=tileHash(cell);
    dotPoint-=vec2(sin(seed*31.0+t),cos(seed*23.0-t))*.16;
    float pollen=tileLine(length(dotPoint),.035)*step(.69,seed)*tileFace;
    vec3 honey=vec3(.77,.60,.21);
    diffuseColor.rgb=mix(diffuseColor.rgb,honey,pollen*.54);
    tileGlow=(vec3(.09,.17,.07)*wet+honey*pollen*.15)*(1.0-uTileLight*.65);
    tileRoughShift=-wet*.12;
  `,
  theatre: /* glsl */`
    vec2 p=vTileLocal;
    float t=uTileTime*.14+uTileSeed*.63;
    float nap=.5+.5*sin(p.x*6.0+p.y*3.0+sin(p.y*5.0-t)*.72-t);
    float weave=(.5+.5*sin(p.x*190.0))*(.5+.5*sin(p.y*190.0));
    float weaveAA=1.0-smoothstep(.8,2.4,max(fwidth(p.x*190.0),fwidth(p.y*190.0)));
    float border=tileLine(abs(max(abs(p.x-.5),abs(p.y-.5))-.466),.0025)*tileFace;
    vec3 silk=mix(vec3(.22,.036,.055),vec3(.83,.55,.43),uTileLight);
    float shimmer=pow(nap,5.0)*mix(.16,.10,uTileLight)*tileFace;
    diffuseColor.rgb=mix(diffuseColor.rgb,silk,shimmer);
    diffuseColor.rgb*=1.0+(weave-.25)*weaveAA*.07*tileFace*(1.0-uTileLight);
    diffuseColor.rgb=mix(diffuseColor.rgb,vec3(.53,.28,.08),border*.40);
    // A slow warm satin ribbon crosses each inlay; its curve travels across the surface.
    float ribbon=tileLine(abs(p.x-.5-.35*sin(t+p.y*3.4)),.012);
    ribbon*=pow(sin(clamp(p.y,0.0,1.0)*3.14159265),2.0)*tileFace;
    tileGlow=vec3(.13,.045,.019)*ribbon*mix(.17,.045,uTileLight);
    tileRoughShift=-shimmer*.75;
  `,
  arcade: /* glsl */`
    vec2 p=vTileLocal;
    float t=uTileTime*.18+uTileSeed*.29;
    float margin=.12+.025*sin(t*.55);
    float railX=tileLine(abs(p.x-margin),.004)+tileLine(abs(p.x-(1.0-margin)),.004);
    float railY=tileLine(abs(p.y-margin),.004)+tileLine(abs(p.y-(1.0-margin)),.004);
    float box=max(railX*step(margin,p.y)*step(p.y,1.0-margin),railY*step(margin,p.x)*step(p.x,1.0-margin));
    float carrierX=fract(p.y*.9-t),carrierY=fract(p.x*.9+t*.83);
    float packetX=tileLine(abs(carrierX-.5),.058)*railX;
    float packetY=tileLine(abs(carrierY-.5),.058)*railY;
    float packets=clamp(packetX+packetY,0.0,1.0)*tileFace;
    float trace=clamp(box,0.0,1.0)*tileFace;
    vec3 neon=mix(vec3(.025,.61,.76),vec3(.71,.075,.31),step(.5,fract(uTileSeed*.73)));
    diffuseColor.rgb=mix(diffuseColor.rgb,neon,trace*mix(.20,.12,uTileLight));
    diffuseColor.rgb=mix(diffuseColor.rgb,neon,packets*mix(.68,.40,uTileLight));
    tileGlow=neon*(trace*.045+packets*mix(.34,.13,uTileLight));
    tileRoughShift=-trace*.12;
  `,
};

export function installTileMotion(material, { id, light, seed, clock }) {
  Object.assign(material, { transparent:false, opacity:1, transmission:0, sheen:0 }, FINISH[id][light ? 1 : 0]);
  if (id === 'theatre' && !light) material.sheenColor.set('#b34859');
  const previous = material.onBeforeCompile, key = material.customProgramCacheKey();
  const uniforms = { uTileTime:clock, uTileSeed:{value:seed}, uTileLight:{value:light ? 1 : 0} };
  material.userData.boardTileMotion = { id, uniforms };
  material.onBeforeCompile = function(shader, renderer) {
    previous.call(this, shader, renderer);
    Object.assign(shader.uniforms, uniforms);
    shader.vertexShader = 'varying vec2 vTileLocal;\nvarying float vTileTop;\n' + shader.vertexShader;
    shader.vertexShader = shader.vertexShader.replace('#include <begin_vertex>', '#include <begin_vertex>\nvTileLocal=position.xz+vec2(.5);\nvTileTop=step(.25,normal.y);');
    shader.fragmentShader = COMMON + shader.fragmentShader;
    shader.fragmentShader = shader.fragmentShader.replace('#include <map_fragment>', `#include <map_fragment>
      float tileFace=smoothstep(.6,.9,vTileTop);
      float tileRoughShift=0.0;
      vec3 tileGlow=vec3(0.0);
      ${DETAILS[id]}`);
    shader.fragmentShader = shader.fragmentShader.replace('#include <roughnessmap_fragment>', '#include <roughnessmap_fragment>\nroughnessFactor=clamp(roughnessFactor+tileRoughShift,.12,1.0);');
    shader.fragmentShader = shader.fragmentShader.replace('#include <emissivemap_fragment>', '#include <emissivemap_fragment>\ntotalEmissiveRadiance+=tileGlow;');
  };
  material.customProgramCacheKey = () => key + '|pbp-board-tiles-1|' + id;
  material.needsUpdate = true;
}

import * as THREE from 'three';

const vertexShader = `
varying vec2 st;
void main(){ st=uv; gl_Position=vec4(position.xy,0.,1.); }
`;
const fragmentShader = `
uniform sampler2D colorBuffer;
uniform sampler2D glowBuffer;
uniform sampler2D sceneDepth;
uniform float strength;
uniform float steam;
uniform float steamTime;
uniform float afterglow;
uniform float arrivalTime;
uniform float aspect;
uniform mat4 viewToWorld;
uniform mat4 inverseProjection;
varying vec2 st;
void main() {
  vec2 uv = st;
  if (arrivalTime >= 0. && arrivalTime < 1.25) {
    vec2 delta = st - vec2(.5,.49);
    float r = length(delta * vec2(aspect,1.));
    float decay = 1. - smoothstep(.08,1.25,arrivalTime);
    float edge = smoothstep(0.,.09,min(min(st.x,1.-st.x),min(st.y,1.-st.y)));
    float wave = sin(16.*r - 11.*arrivalTime) * .045 + sin(8.*r - 6.*arrivalTime) * .025;
    uv += delta * wave * decay * edge;
  }
  vec3 colour = texture2D(colorBuffer,uv).rgb + texture2D(glowBuffer,uv).rgb * strength;
  if(steam>.001){
    vec4 endView=inverseProjection*vec4(uv*2.-1.,texture2D(sceneDepth,uv).r*2.-1.,1.);
    vec3 worldEnd=(viewToWorld*vec4(endView.xyz/endView.w,1.)).xyz;
    vec3 eye=viewToWorld[3].xyz;
    vec3 travel=worldEnd-eye;
    float lengthRay=min(length(travel),22.);
    vec3 stepRay=normalize(travel)*lengthRay/float(STEAM_SAMPLES);
    float transmission=1.;
    for(int j=0;j<STEAM_SAMPLES;j++){
      vec3 p=eye+stepRay*(float(j)+.5);
      vec2 local=p.xz-vec2(-22.,-5.);
      float room=1.-smoothstep(8.05,8.65,length(local));
      float ripple=.28*sin(dot(local,vec2(.70,.56))+steamTime*.22)
                  +.14*sin(dot(local,vec2(-.29,1.13))-steamTime*.27+sin(local.x*.6));
      float band=1.-smoothstep(.05,.69,p.y-(.08+steam*.81+ripple));
      float away=smoothstep(.22,1.1,length(p.xz-eye.xz));
      float pocket=.72+.28*sin(local.x*.82+sin(local.y*.77+steamTime*.1));
      float density=room*band*away*pocket*steam*.115;
      transmission*=exp(-density*length(stepRay));
    }
    vec3 vapor=mix(vec3(.21,.36,.34),vec3(.44,.29,.42),.5+.5*sin(steamTime*.065));
    colour=mix(vapor,colour,transmission);
  }
  vec2 around = (st - .5) * 1.55;
  colour *= 1.0 - .08 * smoothstep(.08,1.0,dot(around,around));
  colour=mix(colour,colour*vec3(.96,1.05,1.07)+vec3(.018,.012,.025),afterglow);
  gl_FragColor = vec4(colour,1.);
  #include <tonemapping_fragment>
  #include <colorspace_fragment>
}`;
const blur = `
uniform sampler2D colorBuffer;
uniform vec2 direction;
uniform float threshold;
varying vec2 st;
vec3 sampleLight(vec2 p) {
  vec3 c=texture2D(colorBuffer,p).rgb;
  float lum=dot(c,vec3(.2126,.7152,.0722));
  return c * smoothstep(threshold,threshold+.45,lum);
}
void main() {
  vec3 c=sampleLight(st)*.227027;
  c+=(sampleLight(st+direction*1.384615)+sampleLight(st-direction*1.384615))*.316216;
  c+=(sampleLight(st+direction*3.230769)+sampleLight(st-direction*3.230769))*.070270;
  gl_FragColor=vec4(c,1.);
}`;

/** Render linear HDR once, then blur the brightest pixels at quarter size and apply the display transform. */
export function createPost(renderer, lite) {
  const hdr = renderer.extensions.has('EXT_color_buffer_float');
  const type = hdr ? THREE.HalfFloatType : THREE.UnsignedByteType;
  const target = new THREE.WebGLRenderTarget(1, 1, { type, samples: Math.min(2,renderer.capabilities.maxSamples) });
  target.depthTexture = new THREE.DepthTexture(1, 1, THREE.UnsignedIntType);
  const a = new THREE.WebGLRenderTarget(1, 1, { type, depthBuffer: false });
  const b = a.clone();
  const bright = new THREE.ShaderMaterial({ vertexShader, fragmentShader: blur, depthTest: false, depthWrite: false,
    uniforms: { colorBuffer: { value: null }, direction: { value: new THREE.Vector2() }, threshold: { value: 1 } } });
  const finish = new THREE.ShaderMaterial({ vertexShader, fragmentShader, depthTest: false, depthWrite: false,
    defines: { STEAM_SAMPLES: lite ? 8 : 14 },
    uniforms: { colorBuffer: { value: target.texture }, glowBuffer: { value: b.texture }, strength: { value: .12 },
      sceneDepth:{value:target.depthTexture}, steam:{value:0}, steamTime:{value:0}, afterglow:{value:0}, arrivalTime:{value:-1}, aspect:{value:1},
      viewToWorld:{value:null}, inverseProjection:{value:null} } });
  const tri = new THREE.BufferGeometry();
  tri.setAttribute('position', new THREE.Float32BufferAttribute([-1,-1,0, 3,-1,0, -1,3,0], 3));
  tri.setAttribute('uv', new THREE.Float32BufferAttribute([0,0, 2,0, 0,2], 2));
  const screen = new THREE.Scene(), eye = new THREE.Camera(), surface = new THREE.Mesh(tri, bright);
  surface.frustumCulled = false; screen.add(surface);
  return {
    resize(w, h) {
      target.setSize(w, h); a.setSize(Math.max(1, Math.ceil(w / 4)), Math.max(1, Math.ceil(h / 4)));
      b.setSize(a.width, a.height);
    },
    draw(scene, camera, calm, weather, arrivalTime = -1) {
      target.resolveDepthBuffer = (weather?.density || 0) > .001;
      renderer.setRenderTarget(target); renderer.render(scene, camera);
      surface.material = bright;
      bright.uniforms.colorBuffer.value = target.texture;
      bright.uniforms.threshold.value = hdr ? 1.25 : .85;
      bright.uniforms.direction.value.set(1 / a.width, 0);
      renderer.setRenderTarget(a); renderer.render(screen, eye);
      bright.uniforms.colorBuffer.value = a.texture; bright.uniforms.threshold.value = -1;
      bright.uniforms.direction.value.set(0, 1 / a.height);
      renderer.setRenderTarget(b); renderer.render(screen, eye);
      surface.material = finish; finish.uniforms.strength.value = calm ? .055 : .12;
      finish.uniforms.steam.value = weather?.density || 0;
      finish.uniforms.steamTime.value = weather?.time || 0;
      finish.uniforms.afterglow.value=weather?.afterglow||0;
      finish.uniforms.arrivalTime.value = arrivalTime;
      finish.uniforms.aspect.value = camera.aspect;
      finish.uniforms.viewToWorld.value = camera.matrixWorld;
      finish.uniforms.inverseProjection.value = camera.projectionMatrixInverse;
      renderer.setRenderTarget(null); renderer.render(screen, eye);
    },
    dispose() { target.dispose(); a.dispose(); b.dispose(); bright.dispose(); finish.dispose(); tri.dispose(); },
  };
}

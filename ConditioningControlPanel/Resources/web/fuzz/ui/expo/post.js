import * as THREE from 'three';

const vertexShader = `
varying vec2 st;
void main(){ st=uv; gl_Position=vec4(position.xy,0.,1.); }
`;
const fragmentShader = `
uniform sampler2D colorBuffer;
uniform sampler2D glowBuffer;
uniform float strength;
uniform float domeTime;
uniform float domeWarp;
uniform float afterglow;
uniform float arrivalTime;
uniform float aspect;
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
  if(domeWarp>.001){
    vec2 p=(st-.5)*vec2(aspect,1.);
    float r=length(p),edge=smoothstep(0.,.09,min(min(st.x,1.-st.x),min(st.y,1.-st.y)));
    float envelope=domeWarp*edge*smoothstep(.07,.38,r);
    float swell=.019*sin(11.*r-domeTime*1.08)+.013*sin(5.*r+domeTime*.62);
    vec2 bent=p*swell+vec2(-p.y,p.x)*(.019*sin(6.*r-domeTime*.82));
    uv+=envelope*bent/vec2(aspect,1.);
  }
  vec3 colour = texture2D(colorBuffer,uv).rgb + texture2D(glowBuffer,uv).rgb * strength;
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
export function createPost(renderer) {
  const hdr = renderer.extensions.has('EXT_color_buffer_float');
  const type = hdr ? THREE.HalfFloatType : THREE.UnsignedByteType;
  const target = new THREE.WebGLRenderTarget(1, 1, { type, samples: Math.min(2,renderer.capabilities.maxSamples) });
  target.resolveDepthBuffer=false;
  const a = new THREE.WebGLRenderTarget(1, 1, { type, depthBuffer: false });
  const b = a.clone();
  const bright = new THREE.ShaderMaterial({ vertexShader, fragmentShader: blur, depthTest: false, depthWrite: false,
    uniforms: { colorBuffer: { value: null }, direction: { value: new THREE.Vector2() }, threshold: { value: 1 } } });
  const finish = new THREE.ShaderMaterial({ vertexShader, fragmentShader, depthTest: false, depthWrite: false,
    uniforms: { colorBuffer: { value: target.texture }, glowBuffer: { value: b.texture }, strength: { value: .12 },
      domeTime:{value:0}, domeWarp:{value:0}, afterglow:{value:0}, arrivalTime:{value:-1}, aspect:{value:1} } });
  const tri = new THREE.BufferGeometry();
  tri.setAttribute('position', new THREE.Float32BufferAttribute([-1,-1,0, 3,-1,0, -1,3,0], 3));
  tri.setAttribute('uv', new THREE.Float32BufferAttribute([0,0, 2,0, 0,2], 2));
  const screen = new THREE.Scene(), eye = new THREE.Camera(), surface = new THREE.Mesh(tri, bright);
  surface.frustumCulled = false; screen.add(surface);
  return {
    compileRoom(room, camera, scene) {
      const previous=renderer.getRenderTarget(),face=renderer.getActiveCubeFace(),level=renderer.getActiveMipmapLevel();
      // Three selects its output shader during compileAsync's synchronous setup.
      try {
        renderer.setRenderTarget(target);
        return renderer.compileAsync(room,camera,scene);
      } finally {
        renderer.setRenderTarget(previous,face,level);
      }
    },
    resize(w, h) {
      target.setSize(w, h); a.setSize(Math.max(1, Math.ceil(w / 4)), Math.max(1, Math.ceil(h / 4)));
      b.setSize(a.width, a.height);
    },
    draw(scene, camera, calm, weather, arrivalTime = -1) {
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
      finish.uniforms.domeTime.value = weather?.time || 0;
      finish.uniforms.domeWarp.value = calm ? 0 : weather?.warp || 0;
      finish.uniforms.afterglow.value=weather?.afterglow||0;
      finish.uniforms.arrivalTime.value = arrivalTime;
      finish.uniforms.aspect.value = camera.aspect;
      renderer.setRenderTarget(null); renderer.render(screen, eye);
    },
    dispose() { target.dispose(); a.dispose(); b.dispose(); bright.dispose(); finish.dispose(); tri.dispose(); },
  };
}

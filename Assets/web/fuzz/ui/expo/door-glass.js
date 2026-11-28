// The glass occupies the inside of the authored shop door frame.
export function createDoorGlass(THREE, object) {
  const clock = {value:0};
  const originals=[];
  object?.traverse(mesh=>{
    if (!mesh.isMesh || !mesh.material.map) return;
    const original=mesh.material;
    const glass=new THREE.ShaderMaterial({
      uniforms:{face:{value:original.map},time:clock},
      transparent:true,depthWrite:false,
      vertexShader:`varying vec2 coord;
        void main(){coord=uv;gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}`,
      fragmentShader:`uniform sampler2D face;uniform float time;varying vec2 coord;
        void main(){
          vec2 p=(coord-0.5)*2.0;float r=length(p);
          float turn=0.19*sin(time*0.38-r*4.0)*(1.0-smoothstep(0.35,1.03,r));
          mat2 spin=mat2(cos(turn),-sin(turn),sin(turn),cos(turn));
          vec2 uv=vec2(0.5)+(spin*p)*vec2(0.28,0.49);
          vec3 color=texture2D(face,uv).rgb;
          float edge=(1.0-smoothstep(0.84,1.0,r));
          gl_FragColor=vec4(color*(1.18+0.06*sin(time*0.7-r*5.0)),edge);
          #include <tonemapping_fragment>
          #include <colorspace_fragment>
        }`
    });
    mesh.material=glass; originals.push([mesh,original,glass]);
  });
  return {step(dt,gentle){clock.value+=dt*(gentle?.32:1);},
    dispose(){for(const [mesh,original,glass] of originals){mesh.material=original;glass.dispose();}}};
}


// A small local studio environment gives eyewear readable softbox reflections.
export function createOpticsEnvironment(THREE){
  const width=512,height=256,data=new Uint8Array(width*height*4);
  const smooth=(a,b,x)=>{const t=Math.max(0,Math.min(1,(x-a)/(b-a)));return t*t*(3-2*t);};
  for(let y=0;y<height;y++)for(let x=0;x<width;x++){
    const u=x/width,v=y/height;
    const box=(cx,cy,w,h)=>{const dx=Math.min(Math.abs(u-cx),1-Math.abs(u-cx));return(1-smooth(w*.85,w,dx))*(1-smooth(h*.85,h,Math.abs(v-cy)));};
    const light=Math.max(box(.18,.49,.036,.22),box(.70,.39,.055,.16)*.85,box(.46,.73,.22,.045)*.7);
    const base=.055+.045*Math.max(0,Math.sin(v*Math.PI));const i=(y*width+x)*4;
    data[i]=Math.round(255*Math.min(1,base+light));data[i+1]=Math.round(255*Math.min(1,base*.97+light*.97));data[i+2]=Math.round(255*Math.min(1,base*.94+light*.91));data[i+3]=255;
  }
  const texture=new THREE.DataTexture(data,width,height,THREE.RGBAFormat);texture.mapping=THREE.EquirectangularReflectionMapping;texture.colorSpace=THREE.SRGBColorSpace;texture.needsUpdate=true;texture.name='Fit.LocalStudioReflection';return texture;
}

/** The menu and 3D controls share the same screen-space stage. */
export function garageLayout(width,height) {
  const mobile=width<700,menuWidth=mobile?0:Math.min(378,Math.max(272,width*.29));
  const left=mobile?0:menuWidth,stageWidth=width-left;
  return {mobile,menuWidth,center:(left+stageWidth*.5)/width,
    offsetX:mobile?0:-menuWidth*.5,offsetY:mobile?height*.08:0,
    arrowLeft:(left+stageWidth*.10)/width,arrowRight:(left+stageWidth*.90)/width,arrowY:mobile?.61:.77};
}


/** Fit a turning rider inside the actual free screen rectangle, including depth at every yaw. */
export function customFraming(width,height,bounds,rect) {
  const fov=33*Math.PI/180,tan=Math.tan(fov*.5),tilt=.08;
  const center=bounds.min.map((v,i)=>(v+bounds.max[i])*.5);
  const half=bounds.min.map((v,i)=>(bounds.max[i]-v)*.5);
  const radius=Math.hypot(half[0],half[2]),vertical=half[1]*Math.cos(tilt)+radius*Math.sin(tilt);
  const depth=radius*Math.cos(tilt)+half[1]*Math.sin(tilt);
  const distance=Math.max(vertical/(tan*rect.height/height*.88),radius/(tan*width/height*rect.width/width*.88))+depth;
  return {position:[0,half[1]+Math.sin(tilt)*distance,-Math.cos(tilt)*distance],target:[0,half[1],0],
    rider:[-center[0],-bounds.min[1],-center[2]],offsetX:width*.5-(rect.left+rect.width*.5),offsetY:height*.5-(rect.top+rect.height*.5)};
}

/** A suspended tab can advance one small step, never skip a complete screen transition. */
export function advanceGarageView(value,custom,dt,reduced=false) {
  const target=custom?1:0,step=Math.max(0,Math.min(.05,Number.isFinite(dt)?dt:0))/(reduced?.8:.62);
  const next=value<target?Math.min(target,value+step):Math.max(target,value-step);
  const ease=x=>x*x*(3-2*x),progress=ease(next),busy=next!==target;
  const edge=Math.min(next,1-next),fade=reduced&&busy?1-ease(Math.min(1,edge/.375)):1;
  return {value:next,progress,busy,framing:reduced?(next<.5?0:1):progress,fade};
}

/** The menu and 3D controls share the same screen-space stage. */
export function garageLayout(width,height) {
  const mobile=width<700,menuWidth=mobile?0:Math.min(378,Math.max(272,width*.29));
  const left=mobile?0:menuWidth,stageWidth=width-left;
  return {mobile,menuWidth,center:(left+stageWidth*.5)/width,
    offsetX:mobile?0:-menuWidth*.5,offsetY:mobile?height*.08:0,
    arrowLeft:(left+stageWidth*.10)/width,arrowRight:(left+stageWidth*.90)/width,arrowY:mobile?.61:.77};
}

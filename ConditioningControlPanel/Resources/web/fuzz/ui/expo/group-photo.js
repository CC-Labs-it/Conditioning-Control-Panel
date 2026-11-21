import * as THREE from 'three';

const rows = [
  ['reginald','margaret','bob','lashes','kevin','gary'],
  ['spitz','greg','bartholomew','henry','doctor','nana']
];

export function groupPhotograph({gallery,scene,renderer,doc,draw}) {
  const riser=gallery.getObjectByName('photo_terrace');
  const originals=rows.flat().map(id=>gallery.getObjectByName('statue_'+id));
  if(!riser || originals.some(model=>!model?.visible))return null;
  const out=doc.createElement('canvas');
  out.width=1100;out.height=756;
  const paper=out.getContext('2d');
  const plate=new THREE.PerspectiveCamera(35.5,1080/606,.05,75);
  plate.position.set(0,2.07,-17.95);
  plate.lookAt(0,.50,-25.98);
  const cameraSize=renderer.getSize(new THREE.Vector2());
  const ratio=renderer.getPixelRatio();
  const yAxis=new THREE.Vector3(0,1,0);
  const party=new THREE.Group();
  const visibility=new Map();
  const setVisible=(object,visible)=>{
    if(object){visibility.set(object,object.visible);object.visible=visible;}
  };
  try{
    scene.add(party);
    setVisible(riser,true);
    for(const name of ['panel_reginald','panel_margaret','panel_henry','photo_release','photo_hatch_0','photo_hatch_1']){
      setVisible(gallery.getObjectByName(name),false);
    }
    originals.forEach((original,i)=>{
      const back=i>=6,column=i%6;
      const positionX=(column-2.5)*1.31+(back ? .21 : -.12);
      const positionZ=back?-26.91:-24.98;
      const bounds=new THREE.Box3().setFromObject(original,true);
      const centre=bounds.getCenter(new THREE.Vector3());
      const originalHeading=Math.atan2(centre.z+26.1,-centre.x);
      const cameraHeading=Math.atan2(positionZ-plate.position.z,-positionX);
      const copy=original.clone(true);
      copy.position.set(0,0,0);copy.scale.multiplyScalar(.82);
      copy.quaternion.premultiply(new THREE.Quaternion().setFromAxisAngle(yAxis,cameraHeading-originalHeading+.12));
      party.add(copy);copy.updateMatrixWorld(true);
      bounds.setFromObject(copy,true);bounds.getCenter(centre);
      copy.position.set(positionX-centre.x,(back?-.05:-.68)-bounds.min.y,positionZ-centre.z);
      setVisible(original,false);
    });
    renderer.setRenderTarget(null);
    renderer.setDrawingBufferSize(1080,606,1);
    renderer.render(scene,plate);
    paper.fillStyle='#ede8d9';paper.fillRect(0,0,1100,756);
    paper.fillStyle='#173d33';paper.fillRect(0,0,1100,66);
    paper.fillStyle='#f7efd9';paper.font='30px Georgia';paper.fillText('The Hall of the Twelve',21,43);
    paper.textAlign='right';paper.font='17px Georgia';paper.fillText('Llama Expo 2037',1078,42);paper.textAlign='left';
    paper.drawImage(renderer.domElement,10,76,1080,606);
    paper.fillStyle='#173d33';paper.font='16px Georgia';
    paper.fillText('Back row   Spitz, Greg, Bartholomew, Henry, Doctor, Nana',22,710);
    paper.fillText('Front row   Reginald, Margaret, Bob, Lashes, Kevin, Gary',22,736);
    // The recap stores the photograph inside the run's existing save record.
    // Keep even a detailed gallery view within the poster's encoded size limit.
    for(const quality of [.83,.70,.55,.40]){
      const photograph=out.toDataURL('image/webp',quality);
      if(photograph.length<=240023)return photograph;
    }
    return null;
  }finally{
    party.removeFromParent();
    for(const [object,visible] of visibility)object.visible=visible;
    renderer.setDrawingBufferSize(cameraSize.x,cameraSize.y,ratio);draw();
  }
}


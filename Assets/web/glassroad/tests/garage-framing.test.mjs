import test from 'node:test';
import assert from 'node:assert/strict';
import {PerspectiveCamera,Vector3} from '../vendor/three/build/three.core.js';
import {customFraming,advanceGarageView} from '../render/garageLayout.js';

test('customize framing keeps the complete rotating outfit inside desktop, tablet and phone free space',()=>{
  const bounds={min:[-.45,.08,-.28],max:[.47,1.62,.33]};
  for(const [w,h,rect]of [[1365,1110,{left:32,top:165,width:800,height:900}],[768,600,{left:20,top:130,width:390,height:450}],[390,850,{left:16,top:130,width:358,height:276}],[650,450,{left:16,top:115,width:618,height:90}]]){
    const frame=customFraming(w,h,bounds,rect),camera=new PerspectiveCamera(33,w/h,.03,40);
    camera.position.fromArray(frame.position);camera.lookAt(new Vector3(...frame.target));camera.setViewOffset(w,h,frame.offsetX,frame.offsetY,w,h);camera.updateMatrixWorld();
    const center=new Vector3(0,(bounds.max[1]-bounds.min[1])*.5,0).project(camera);
    assert.ok(Math.abs((center.x+1)*w*.5-(rect.left+rect.width*.5))<.001,'centered in the space beside the controls');
    assert.ok(Math.abs((1-center.y)*h*.5-(rect.top+rect.height*.5))<.001,'centered vertically in available space');
    for(let angle=0;angle<Math.PI*2;angle+=Math.PI/12)for(const x of [bounds.min[0],bounds.max[0]])for(const y of [bounds.min[1],bounds.max[1]])for(const z of [bounds.min[2],bounds.max[2]]){
      const p=new Vector3(x+frame.rider[0],y+frame.rider[1],z+frame.rider[2]).applyAxisAngle(new Vector3(0,1,0),angle).project(camera);
      const px=(p.x+1)*w*.5,py=(1-p.y)*h*.5;
      assert.ok(px>=rect.left&&px<=rect.left+rect.width,`${w}: outfit stays within horizontal bounds`);
      assert.ok(py>=rect.top&&py<=rect.top+rect.height,`${w}: hat and shoes stay within vertical bounds`);
    }
  }
});

test('screen transition advances smoothly, survives suspension and can reverse from its current position',()=>{
  const a=advanceGarageView(0,true,12),b=advanceGarageView(a.value,true,1/60),reverse=advanceGarageView(b.value,false,1/60);
  assert.ok(a.value>0&&a.value<.1,'a resumed tab cannot skip the transition');
  assert.ok(b.progress>a.progress&&b.progress<1);assert.ok(reverse.value<b.value&&reverse.value>0);
  let view=0;for(let i=0;i<60;i++)view=advanceGarageView(view,true,1/60).value;
  assert.equal(view,1);assert.equal(advanceGarageView(view,true,1/60).busy,false);
  for(let i=0;i<60;i++)view=advanceGarageView(view,false,1/60).value;
  assert.equal(view,0);assert.equal(advanceGarageView(view,false,NaN).value,0);
});

test('calm transitions change the camera only while the model is completely faded out',()=>{
  for(const direction of [true,false]){
    let value=direction?0:1,previous=value;
    for(let i=0;i<60;i++){
      const frame=advanceGarageView(value,direction,1/60,true);
      if(frame.framing!==previous)assert.equal(frame.fade,0,'the framing switch is concealed');
      previous=frame.framing;value=frame.value;
    }
    assert.equal(value,direction?1:0);
    assert.equal(advanceGarageView(value,direction,1/60,true).fade,1);
  }
});

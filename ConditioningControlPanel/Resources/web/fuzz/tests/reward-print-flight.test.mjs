import assert from 'node:assert/strict';
import test from 'node:test';
import {dealPrintPoses,flashFlight,PRINT_DELAY,PRINT_LIFE} from '../ui/unlocks/arcade/slot-flight.js';

const origin={x:180,y:250,w:42,h:50};

test('a deal lands on separate angles with a visible margin on desktop and phone',()=>{
  const poses=dealPrintPoses();
  for(const [width,height] of [[320,568],[1440,900]]){
    for(const aspect of [.3,.75,1,1.8,3]){
      const frames=poses.map(p=>flashFlight(origin,width,height,aspect,1500,false,p));
      const centers=frames.map(f=>[f.x+f.w/2,f.y+f.h/2]);
      for(let i=0;i<frames.length;i++){
        const {w,h,angle}=frames[i], [cx,cy]=centers[i];
        const dx=(w*Math.cos(angle)+h*Math.abs(Math.sin(angle)))/2;
        const dy=(h*Math.cos(angle)+w*Math.abs(Math.sin(angle)))/2;
        assert(cx-dx>0 && cy-dy>0 && cx+dx<width && cy+dy<height);
        assert(Math.max(2*dx/width,2*dy/height)>.85);
        if(i)assert(Math.hypot(cx-centers[i-1][0],cy-centers[i-1][1])>10);
      }
    }
  }
});

test('photo arrivals use the game clock, and Gentle holds one pose per print',()=>{
  const poses=dealPrintPoses();
  const sample=(i,age,calm)=>flashFlight(origin,600,450,1.33,age-i*PRINT_DELAY,calm,poses[i]);
  assert(sample(0,150,false).opacity>0);assert.equal(sample(1,150,false).opacity,0);
  assert(sample(1,450,false).opacity>0);assert.equal(sample(2,450,false).opacity,0);
  assert(sample(2,750,false).opacity>0);
  assert.equal(sample(0,PRINT_LIFE,false).opacity,0);assert(sample(2,PRINT_LIFE,false).opacity>0);
  for(const key of ['x','y','w','h','angle'])assert.equal(sample(0,80,true)[key],sample(0,1100,true)[key]);
});

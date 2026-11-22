// Thresholds are measured at the fixed jambs, in world metres. The tangent follows the closed leaves.
export const door=(id,rooms,x,y,z,axis,width,height,kind='civic',leaves=2,swingSide=0)=>({id,rooms,x,y,z,axis,width,height,kind,leaves,swingSide});
// Keep the architectural opening for the fixed jambs and give the moving panels a separate clear span.
const fit=(opening,width,height=opening.height,reveal)=>({...opening,roughWidth:opening.width,roughHeight:opening.height,width,height,reveal});
const boothReveal={width:2.43,height:2.42,depth:.238};
export const doorways=[
  // The hall and atrium have different openings. The fixed reveal meets the larger atrium arch.
  fit(door('hall-entrance',['hall','atrium'],0,0,16,'x',3.16,2.34),2.90,2.34,{width:3.25,height:2.50,depth:.238}),
  fit(door('portrait-rotunda',['hall','twelve'],0,0,-16,'x',3.16,2.32),2.84,2.32,{width:3.49,height:3.33,depth:.32,offset:-.105}),
  fit(door('hoof-exhibit',['hall','hoof'],-12,0,7,'z',2.35,2.32),2.05,2.32,boothReveal),
  fit(door('dome-exhibit',['hall','dome'],-12,0,-5,'z',2.35,2.32),2.05,2.32,{width:2.86,height:2.81,depth:.25,offset:.055}),
  fit(door('habitat-exhibit',['hall','habitat'],12,0,7,'z',2.35,2.32),2.05,2.32,boothReveal),
  fit(door('shop-entrance',['hall','shop'],12,0,-5,'z',2.35,2.32),2.05,2.32,boothReveal),
  fit(door('staff-entrance',['atrium','service'],12,0,32,'z',2.25,2.12,'staff'),1.96,2.00,{width:2.45,height:2.81,depth:.28,offset:-.05}),
  fit(door('staff-upper',['atrium','service'],12,6.8,23,'z',2.32,2.15,'staff',2,1),1.82,2.15,{width:2.45,height:2.76,depth:.28,offset:-.05}),
  fit(door('catwalk-access',['atrium','catwalk'],-12,6.8,21,'z',2.33,2.12,'staff'),1.92,2.12,{width:2.45,height:2.82,depth:.30,offset:.055}),
  {...door('inspection-shutter',['atrium','catwalk'],-12.04,7.74,28,'z',.62,.84,'staff',1),lift:.94},
  fit(door('archive-entrance',['service','secret'],30.42,0,41,'z',2.24,2.15,'staff',2,1),1.78,2.15,{width:2.45,height:3.04,depth:.28,offset:-.06}),
  {...door('chronology-entrance',['atrium','chronology'],7.8,0,41.1,'x',3.10,2.98,'civic',2,-1),reveal:{width:3.34,height:3.18,depth:.21,offset:.025}},
  {...door('aquarium-return',['atrium','aquarium'],-7.8,0,41.1,'x',3.10,2.98,'civic',2,-1),reveal:{width:3.34,height:3.18,depth:.21,offset:.025}},
  fit(door('archive-to-forum',['chronology','forum'],3.4,0,103,'z',6.60,4.62,'civic',2,1),4.70,2.94),
  fit(door('forum-to-commons',['forum','commons'],-66.2,0,98.6,'x',6.60,4.62),4.70,2.94),
  fit(door('aquarium-climate-doors',['commons','aquarium'],-61.8,0,45.5,'z',6.60,4.62),4.25,2.90,{width:6.98,height:4.96,depth:.165,offset:.0375}),
  // The five staff-room leaves open into the rooms and leave the narrow cross-corridor clear.
  door('staff-breakroom',['service'],18,0,30.8,'x',1.10,2.04,'staff',1,1),
  door('staff-store',['service'],23.65,0,33.2,'x',1.10,2.04,'staff',1,-1),
  door('server-cupboard',['service'],25,0,30.8,'x',1.10,2.04,'staff',1,1),
  fit(door('maintenance-stairs',['service'],30.3,0,30.8,'x',2.29,2.04,'staff',2,1),1.88),
  fit(door('archive-passage',['service'],29.2,0,33.2,'x',2.29,2.04,'staff',2,-1),1.88),
  door('reading-room',['secret'],80.4,0,41,'z',1.21,2.04,'civic',1),
  door('return-reading-room',['secret'],86.4,0,51,'z',1.21,2.04,'civic',1)
];

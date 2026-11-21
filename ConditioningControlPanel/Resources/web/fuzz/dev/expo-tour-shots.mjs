// Camera cuts are separated by unrecorded travel. Within a shot, movement uses the real floor and wall controller.
const shots = {
  admission:{admission:true},
  arrival:{zone:'atrium',guideEntrance:true},
  grand:{zone:'atrium',at:[.95,0,34.07],aim:[1.7,4.55,26.5],
    path:Array.from({length:12},(_,i)=>{const a=(i+1)*Math.PI/24;return [-3.1+4.05*Math.cos(a),34.07-4.05*Math.sin(a)];})},
  upper:{zone:'atrium',at:[-3.1,6.8,18.34],aim:[1.80,3.75,26.7],to:[1,18.34]},
  greenhouse:{zone:'atrium',at:[8.98,0,23.95],aimObject:'expo_control_greenhouse',
    action:'Run irrigation pulse',afterAim:[10.59,1.48,23.66]},
  server:{zone:'service',at:[25.02,0,29.59],aim:[25.01,1.04,27.21],to:[25.0,28.63]},
  service:{zone:'service',at:[30.2,0,24.1],aim:[23,2.7,22.8],to:[28.0,24.1]},
  impossible:{zone:'service',at:[24.32,1.14,23],aim:[21.7,2.93,23],
    path:[[23.4,23],[22.42,23],[22.42,20.71],[23.42,20.71],[25.20,20.71]],pathLook:true,expectCrossings:1},
  contract:{zone:'secret',at:[33.8,0,41],aim:[48,1.37,40.82],to:[40.4,41]},
  chair:{zone:'secret',at:[81.44,0,40.3],aim:[84.73,.98,41.62],to:[82.71,40.48]},
  longer:{zone:'secret',at:[81.36,0,41],aim:[65,1.43,41],travel:[-4.05,0]},
  tiny:{zone:'catwalk',at:[-14.12,6.8,28],aim:[-5.2,6.18,28],to:[-12.53,28],visibleRooms:['atrium']},
  crown:{zone:'catwalk',at:[-22.45,11.95,-2.8],aim:[-22,1.05,-5],to:[-22.05,-4.86],visibleRooms:['dome']},
  hall:{zone:'hall',at:[0,0,13.2],aim:[0,2.4,-9],to:[0,3.1]},
  chronology:{zone:'chronology',archiveSlot:'slot_02'},
  forum:{zone:'forum',at:[2.7,0,103],aim:[-13.6,2.2,103],to:[-.7,103]},
  commons:{zone:'commons',at:[-66.2,0,97.9],aim:[-66.2,2.2,81.6],to:[-66.2,94.6]},
  aquarium:{zone:'aquarium',at:[-49.4,0,45.38],aimObject:'aquarium_ray_2',
    afterAim:[-40.1,2.20,43.2],to:[-46.82,45.38]},
  corrected:{zone:'hall',at:[-10.05,3.4,.44],aim:[-11.65,4.98,.88],expectedStation:'pa_photos',
    afterAim:[-11.70,4.90,-2.10],to:[-9.82,-.18]},
  press:{zone:'hall',at:[.04,0,-7.48],aim:[0,1.077,-9],aimObject:'press_main',action:'Press I AGREE'},
  hoof:{zone:'hoof',at:[-18.46,0,8.92],aim:[-18.4,1.75,10.45],waitForCue:'pa_hall2',action:'Try',repeat:[.65,1.38,1.95,2.5,3.05,3.62,6.80],
    to:[-18.66,7.46],afterAim:[-19.05,1.46,9.99]},
  habitat:{zone:'habitat',at:[19.4,0,8.13],aim:[19.40,1.56,9.55],waitForCue:'pa_hall3',action:'Tap the glass'},
  twelve:{zone:'twelve',at:[-1.69,-.68,-30.27],aim:[-1.25,1.31,-33],to:[-1.40,-30.14],
    expectedStation:'log_reginald',aimObject:'statue_reginald',visibleRooms:['hall']},
  photo:{zone:'twelve',at:[.48,-.68,-24.04],aim:[0,-.613,-25.15],action:'Take the group photograph',afterAim:[-.45,1.18,-30.12]},
  dome0:{zone:'dome',at:[-14.86,0,-5],aim:[-22,1.41,-5],to:[-18.0,-5.21]},
  dome3:{zone:'dome',at:[-17.5,0,-6.3],aim:[-23.1,3.85,-5.03],to:[-18.7,-6.61],dwell:108},
  dome5:{zone:'dome',at:[-18.9,0,-4.2],aim:[-24.4,3.3,-4.86],to:[-19.8,-4.28],dwell:210},
  shelf:{zone:'shop',at:[15.61,0,-1.85],aim:[16.20,.98,-3.7],to:[16.72,-1.81],waitForCue:'pa_shop_1'},
  till:{zone:'shop',at:[20.84,0,-9.75],aim:[21.2,1.32,-11.42],action:'Use the till'},
  loop:{zone:'shop',at:[15.08,0,-11.93],aim:[15.03,1.42,-13.74],action:'Open the exit door'},
  sale:{zone:'shop',at:[22.76,0,-9.89],aim:[22.77,1.37,-11.43],action:'Buy Exit'},
  home:{zone:'shop',at:[15.13,0,-11.94],aim:[15.03,1.30,-13.73],action:['Go home','Open the exit door'],home:true}
};
const profiles={
  M2:[['hoof',13],['habitat',8],['twelve',7],['photo',5],['shelf',5],['till',4],['loop',4],['loop',3],['sale',4],['home',7]],
  M4:[['arrival',8],['grand',7],['upper',5],['service',4],['server',5],['impossible',4],['contract',5],['chair',6],['longer',5],['tiny',4],['crown',7]],
  M5:[['admission',9],['arrival',8],['chronology',11],['forum',5],['commons',5],['aquarium',7],['greenhouse',5],
      ['server',4],['contract',4],['chair',4],['crown',7],['hall',7],['corrected',5],['press',6],['hoof',10],
      ['habitat',7],['twelve',7],['photo',5],['dome0',6],['dome3',9],['dome5',12],
      ['shelf',4],['till',6],['loop',4],['loop',4],['sale',6],['home',13]]
};
export function tourShots(profile){
  if(!profiles[profile])throw Error('Choose M2, M4 or M5.');
  return profiles[profile].map(([id,seconds])=>({id,seconds,...shots[id]}));
}

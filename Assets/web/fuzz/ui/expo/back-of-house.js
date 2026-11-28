import {createWoolRoom} from './wool-room.js';
import {createTwelvePieces} from './twelve-pieces.js';
import {createStaffWing} from './staff-wing.js';

export function createBackOfHouse(api,state){
  const staff=createStaffWing(api,state);
  const pieces=[staff,createTwelvePieces(api,state),createWoolRoom(api,state)];
  return {
    lighting:staff.lighting,
    mount(id,group,bind,surface,clean){for(const piece of pieces)piece.mount(id,group,clean);},
    tick(dt,id){for(const piece of pieces)piece.tick(dt,id);},
    enter(id,old){for(const piece of pieces)piece.enter?.(id,old);},
    dispose(){for(const piece of pieces)piece.dispose?.();}
  };
}

import {createStaffWing} from './staff-wing.js';

export function createBackOfHouse(api,state){
  const pieces=[createStaffWing(api,state)];
  return {
    mount(id,group,bind,surface,clean){for(const piece of pieces)piece.mount(id,group,clean);},
    tick(dt,id){for(const piece of pieces)piece.tick(dt,id);},
    enter(id,old){for(const piece of pieces)piece.enter?.(id,old);},
    dispose(){for(const piece of pieces)piece.dispose?.();}
  };
}

// The archive's seventeen screens (owner, 2026-11-22): never more than one in a small room. The gift shop, Applied Hoof
// Sciences, the Habitat and the staff wing have one each, the four long galleries keep their two, the Atrium has one on
// each floor and the Grand Hall one on the floor and one on the mezzanine. The pack's route file may list more bays;
// only these are built, so an older pack plays the same building.
export const ARCHIVE_SCREENS = Object.freeze([
  'slot_01', 'slot_17', 'slot_19',                 // Atrium: ground, first floor, second floor
  'slot_02', 'slot_03', 'slot_04', 'slot_05',      // Chronology, Forum
  'slot_06', 'slot_07', 'slot_08', 'slot_09',      // Commons, Aquarium
  'slot_10',                                       // staff wing
  'slot_11', 'slot_27',                            // Grand Hall: floor, east mezzanine
  'slot_12', 'slot_13', 'slot_14',                 // Applied Hoof Sciences, Habitat, gift shop
]);
const kept = new Set(ARCHIVE_SCREENS);
export const isArchiveScreen = id => kept.has(id);

// A record watched on a screen that has since gone rests on the screen that took over its part of the room.
const HOMES = {
  slot_01: [15, 16, 22], slot_17: [18, 23], slot_19: [20, 21],
  slot_10: [31, 32, 33], slot_11: [24, 25, 26, 29, 30], slot_27: [28],
  slot_12: [34, 35], slot_13: [36, 37], slot_14: [38, 39, 40],
};
const retired = new Map(Object.entries(HOMES).flatMap(([home, gone]) => gone.map(n => ['slot_' + n, home])));
/** The screen a saved screen id names today: itself when it still stands, its heir when it was retired, else undefined. */
export const screenHome = id => kept.has(id) ? id : retired.get(id);

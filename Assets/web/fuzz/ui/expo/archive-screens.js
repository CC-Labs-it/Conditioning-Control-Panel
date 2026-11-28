// The archive's thirteen screens. Owner, 2026-11-22: never a clump of projectors. Owner, 2026-11-23: a room that has
// its own game has no projector at all, so the gift shop, Applied Hoof Sciences, the Habitat and the staff wing have
// none. The four long galleries keep their two, the Atrium has one on each floor and the Grand Hall one on the floor
// and one on the mezzanine. The pack's route file may list more bays; only these are built, so an older pack plays the
// same building.
export const ARCHIVE_SCREENS = Object.freeze([
  'slot_01', 'slot_17', 'slot_19',                 // Atrium: ground, first floor, second floor
  'slot_02', 'slot_03', 'slot_04', 'slot_05',      // Chronology, Forum
  'slot_06', 'slot_07', 'slot_08', 'slot_09',      // Commons, Aquarium
  'slot_11', 'slot_27',                            // Grand Hall: floor, east mezzanine
]);
const kept = new Set(ARCHIVE_SCREENS);
export const isArchiveScreen = id => kept.has(id);

// A record watched on a screen that has since gone rests on the screen that took over its part of the room, or, for a
// room that lost its projector, on the nearest one outside it: the Atrium's for the staff wing, the Grand Hall's for
// the three rooms off the hall.
const HOMES = {
  slot_01: [15, 16, 22, 10, 31, 32, 33], slot_17: [18, 23], slot_19: [20, 21],
  slot_11: [24, 25, 26, 29, 30, 12, 34, 35, 13, 36, 37, 14, 38, 39, 40], slot_27: [28],
};
const retired = new Map(Object.entries(HOMES).flatMap(([home, gone]) => gone.map(n => ['slot_' + n, home])));
/** The screen a saved screen id names today: itself when it still stands, its heir when it was retired, else undefined. */
export const screenHome = id => kept.has(id) ? id : retired.get(id);

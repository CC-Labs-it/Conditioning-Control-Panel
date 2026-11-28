import {doorways} from './doorways.js';

// Keep the authored shell, doorway signs, glazing and lights in the scene on both sides.
// Interior exhibits can stay loaded and interactive without submitting their meshes through a closed door.
function furnishing(node) {
  if (node.isLight) return false;
  if (node.userData.collectionPiece || node.userData.guestSlot) return true;
  if (node.userData.expoProp) return !/sign|notice|record|door|lamp/i.test(node.name);
  const name = node.name || node.children[0]?.name || '';
  return /^(statue_|cover_|helmet_|portrait_|panel_|scaffold_|redacted_|nameplate_|accession_|log_|archive_bay_|expo_(?:station|fixture|screen|lens|press|control|display|replay|review)_|hoof_|shelf_|aquarium_(?:school|ray)_|Swimming_)/.test(name);
}

export function createRoomVisibility(resident, doors, walker) {
  const visible = new Set(), hidden = [];
  const candidates = new WeakMap();
  function prepare() {
    visible.clear();
    const here = walker.zone, feet = walker.feet;
    visible.add(here);
    // These overlooks and cutaways have views into a second storey outside the door opening.
    if (here === 'dome') { visible.add('hall'); visible.add('catwalk'); }
    if (here === 'twelve') visible.add('hall');
    if (here === 'catwalk') { visible.add('dome'); visible.add('atrium'); }
    if (here === 'service' && feet.y > 4.65) visible.add('atrium');
    for (const door of doorways) {
      if (door.rooms.length !== 2 || !door.rooms.includes(here)) continue;
      // Restore both sides before the hinge starts, including the small gaps around closed leaves.
      if (Math.abs(feet.y-door.y) < door.height+.5 && Math.hypot(feet.x-door.x,feet.z-door.z) < 4.8) {
        for (const id of door.rooms) visible.add(id);
      }
    }
    doors.connectVisibleRooms(visible);
    for (const [id, zone] of resident) {
      if (visible.has(id)) continue;
      for (const node of zone.view.children) {
        if (!node.visible) continue;
        let eligible = candidates.get(node);
        if (eligible === undefined) { eligible = furnishing(node); candidates.set(node, eligible); }
        if (eligible) { hidden.push(node); node.visible = false; }
      }
    }
  }
  function restore() {
    for (const node of hidden) node.visible = true;
    hidden.length = 0;
  }
  return {prepare, restore};
}

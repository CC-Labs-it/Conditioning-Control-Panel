/* ============================================================================
 * videoStage.js - the tube's video stage belongs to a descent (ccp-bugs #1470).
 *
 * The spotlight (engine/spawner.js) pins one of the player's clips just ahead
 * of the camera for the 'video spotlight time'. Its countdown only ticks while
 * the conveyor runs, and a run standing down PARKS the conveyor (the hub never
 * plays video behind it). A clip on stage when the run ended therefore never
 * counted down: it froze in front of the camera over the recap and the hub
 * until the next descent ("video stuck on the screen after the fall ended").
 *
 * The rule here: outside a descent in game mode the stage is CLOSED. Closing
 * it retires whatever is on it (the spawner fades it out on its usual path),
 * and a stage asked for before the close (Private Show's async acquire) is
 * refused when it lands, because every close moves the ticket on.
 * The standalone Fall has no runs, so its stage is never closed.
 * Pure, node tested; engine/spawner.js and engine/scene.js wire it in.
 * ==========================================================================*/

/** Is the stage closed? Only the DtRH game closes it, and only between descents. */
export function stageClosedFor(gameMode, runActive) {
  return !!gameMode && !runActive;
}

/** The stage's open/closed state plus a ticket that moves on every close. */
export function createStageGate() {
  let ticket = 0;
  let closed = false;
  return {
    /** Take before an async acquire; hand back to mayOpen when it lands. */
    ticket: () => ticket,
    /** May a stage asked for under `t` go up now? */
    mayOpen: (t) => !closed && t === ticket,
    isClosed: () => closed,
    /** Set the state. Returns true when a live stage must be retired now. */
    setClosed(on, hasLiveStage) {
      on = !!on;
      if (on && !closed) ticket += 1;
      closed = on;
      return closed && !!hasLiveStage;
    },
  };
}

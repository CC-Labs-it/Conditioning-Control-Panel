import { shapeThresholdNodes } from './shapeThreshold.js';
import { shapeKeepNodes } from './shapeKeep.js';
import { shapeMainframeNodes } from './shapeMainframe.js';
import { shapeBoudoirNodes } from './shapeBoudoir.js';
import { shapeGoonworksNodes } from './shapeGoonworks.js';

export const COURSE_SHAPE_REVISION = 1;
const shape = { threshold: shapeThresholdNodes, keep: shapeKeepNodes, mainframe: shapeMainframeNodes,
  boudoir: shapeBoudoirNodes, goonworks: shapeGoonworksNodes };

/** Reauthor physical road before spline, scenery, collision and room cards share it. */
export function shapeCourseNodes(json, nodes) {
  if (!shape[json.id]) return null;
  return { ...shape[json.id](nodes, json), revision: COURSE_SHAPE_REVISION };
}

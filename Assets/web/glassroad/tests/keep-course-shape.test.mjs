import test from 'node:test';
import { shapeKeepNodes } from '../track/shapeKeep.js';
import { checkCourseShape } from './course-shape-check.mjs';
test('Keep has broad bends and a shorter, gentler ascent with stable node anchors', () => checkCourseShape('keep', shapeKeepNodes, 10000));

import test from 'node:test';
import { shapeMainframeNodes } from '../track/shapeMainframe.js';
import { checkCourseShape } from './course-shape-check.mjs';
test('Mainframe has broad bus and gantry bends while retaining both drop routes', () => checkCourseShape('mainframe', shapeMainframeNodes, 12500));

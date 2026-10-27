import test from 'node:test';
import { shapeBoudoirNodes } from '../track/shapeBoudoir.js';
import { checkCourseShape } from './course-shape-check.mjs';
test('Boudoir has broad vanity curves, a single shelf circuit and attached fork mouths', () => checkCourseShape('boudoir', shapeBoudoirNodes, 11000));

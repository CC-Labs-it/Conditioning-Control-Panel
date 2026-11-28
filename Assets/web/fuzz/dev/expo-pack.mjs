// Usage: node dev/expo-pack.mjs --out <external outputs directory> --version <pack version> [--radio <staging directory>] [--compare]
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';

const builder = fileURLToPath(new URL('./expo-pack.py', import.meta.url));
const python = process.env.PYTHON || (process.platform === 'win32' ? 'python' : 'python3');
const result = spawnSync(python, ['-B', builder, ...process.argv.slice(2)], {stdio: 'inherit'});
if (result.error) console.error('Could not start the Python 3 ZIP builder:', result.error.message);
process.exitCode = result.status ?? 1;

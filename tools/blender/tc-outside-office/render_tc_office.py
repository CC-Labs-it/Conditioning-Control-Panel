"""Render the T&C office pull-back and export the per-frame screen quad.

blender --background --factory-startup --python-exit-code 1 --python render_tc_office.py -- <mode> [options]
  modes:  check            verify fit + occlusion, write office-track.json only
          still F [F...]   render the given frames (1-based) at --pct (default 50) into stills/
          contact          frames 1, 38, 75, 113, 150 at --pct (default 50) into contact-frames/
          final            all 150 frames at 100% into final-frames/
  options: --pct N  --samples N
Every mode first checks frame 1 (screen corners on the frame corners within .001) and that no
object sits between the camera and the screen in any frame, then writes office-track.json.
"""
import bpy, json, sys, time
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

OUT = Path(__file__).resolve().parent
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['check']
mode = argv[0]
def opt(name, default):
    return type(default)(argv[argv.index(name) + 1]) if name in argv else default

bpy.ops.wm.open_mainfile(filepath=str(OUT / 'tc-office.blend'))
s = bpy.context.scene; cam = s.camera
screen = bpy.data.objects['SCREEN_MEADOW']
W, H = 1280, 720
mw = screen.matrix_world
v = [mw @ vert.co for vert in screen.data.vertices]          # BL, BR, TR, TL
corners = [v[3], v[2], v[1], v[0]]                           # TL, TR, BR, BL (contract order)
expect = [(0, 1), (1, 1), (1, 0), (0, 0)]                    # camera-view coords (y up)

# Track + checks (render size does not change the normalised projection; keep it at 1280x720 here).
s.render.resolution_x, s.render.resolution_y, s.render.resolution_percentage = W, H, 100
frames = []; worst0 = 0.0; occluded = []
for f in range(s.frame_start, s.frame_end + 1):
    s.frame_set(f)
    quad = []
    for c, e in zip(corners, expect):
        p = world_to_camera_view(s, cam, c)
        if f == s.frame_start: worst0 = max(worst0, abs(p.x - e[0]), abs(p.y - e[1]))
        quad.append([round(p.x * W, 3), round((1 - p.y) * H, 3)])
    frames.append(quad)
    dg = bpy.context.evaluated_depsgraph_get(); origin = cam.matrix_world.translation
    for i in range(13):
        for j in range(8):
            u = .002 + .996 * i / 12; w = .002 + .996 * j / 7
            target = v[0] + (v[1] - v[0]) * u + (v[3] - v[0]) * w
            d = target - origin
            hit, loc, nrm, idx, obj, _ = s.ray_cast(dg, origin, d.normalized(), distance=d.length - 2e-4)
            if hit: occluded.append((f, round(u, 3), round(w, 3), obj.name))
assert worst0 < .001, f'frame 1 screen corners off by {worst0}'
assert not occluded, f'screen occluded: {occluded[:8]} ({len(occluded)} samples)'
assert len(frames) == s.frame_end - s.frame_start + 1 == 150
track = {'version': 1, 'fps': s.render.fps, 'size': [W, H], 'frames': frames}
(OUT / 'office-track.json').write_text(json.dumps(track, separators=(',', ':')))
print(f'CHECK OK frame1 corner error {worst0:.2e}, no occlusion, {len(frames)} quads, last {frames[-1]}')

if mode == 'check':
    sys.exit(0)

backend = 'CPU'; s.cycles.device = 'CPU'
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for kind in ('OPTIX', 'CUDA'):
        try:
            prefs.compute_device_type = kind; prefs.get_devices()
            devs = [d for d in prefs.devices if d.type == kind]
            if devs:
                for d in prefs.devices: d.use = d in devs
                s.cycles.device = 'GPU'; backend = kind; break
        except Exception: pass
except Exception: pass
print('DEVICE', backend)

if mode == 'final':
    todo = list(range(1, 151)); pct = 100; folder = OUT / 'final-frames'
elif mode == 'contact':
    todo = [1, 38, 75, 113, 150]; pct = opt('--pct', 50); folder = OUT / 'contact-frames'
else:
    todo = []
    for a in argv[1:]:
        if not a.isdigit(): break
        todo.append(int(a))
    pct = opt('--pct', 50); folder = OUT / 'stills'
s.render.resolution_percentage = pct
s.cycles.samples = opt('--samples', s.cycles.samples)
folder.mkdir(exist_ok=True)
t0 = time.time()
for f in todo:
    s.frame_set(f); s.render.filepath = str(folder / f'{f - 1:04d}.png')
    bpy.ops.render.render(write_still=True)
    print(f'FRAME {f} done {time.time() - t0:.1f}s')
(OUT / f'{mode}-render.json').write_text(json.dumps({'blender': bpy.app.version_string, 'device': backend, 'samples': s.cycles.samples,
    'percent': pct, 'frames': todo, 'seconds': round(time.time() - t0, 1)}, indent=1))
print(f'RENDER OK {len(todo)} frames in {time.time() - t0:.1f}s on {backend}')

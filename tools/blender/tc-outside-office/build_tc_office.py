"""T&C "Go Outside" office: a fork of tools/blender/breakout-office (build_office.py ->
build_office_motion.py -> build_office_polish.py), folded into one script and reworked.

Same DNA as Breakout's ending (grey partitions, desk, real props, Cycles, AgX), changed for T&C:
after hours, room mostly dark, the monitor (emissive meadow.jpg) is the key light, desk lamp off,
chair pushed back and empty, llama plush, sticky note on the bezel, spiral mug, headphones,
framed GO OUTSIDE poster, wall clock. No window. Camera: 30 fps, 150 frames, frame 1 fits the
16:9 screen exactly, log-distance eased pull-back, 15 frame hold.

Run:  blender --background --factory-startup --python-exit-code 1 --python build_tc_office.py
Writes tc-office.blend (images packed) next to this file.
"""
import bpy, math, random
from pathlib import Path
from mathutils import Vector, Quaternion
import numpy as np

OUT = Path(__file__).resolve().parent
FUZZ = OUT.parents[2] / 'ConditioningControlPanel' / 'Resources' / 'web' / 'fuzz'
MEADOW = FUZZ / 'assets' / 'outside' / 'meadow.jpg'
GEN = OUT / 'gen'
GEN.mkdir(exist_ok=True)
random.seed(17)
rng = np.random.default_rng(17)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for block in (bpy.data.meshes, bpy.data.materials, bpy.data.lights, bpy.data.cameras, bpy.data.curves):
    for item in list(block):
        block.remove(item)

# ----------------------------------------------------------------------------- node helpers
def nodes_of(m):
    nt = m.node_tree
    return nt.nodes, nt.links, next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')

def mix_rgb(nt, fac, a, b):
    """ShaderNodeMix in RGBA mode. fac/a/b are sockets or plain values."""
    m = nt.nodes.new('ShaderNodeMix'); m.data_type = 'RGBA'
    ins = {s.identifier: s for s in m.inputs}
    for key, v in (('Factor_Float', fac), ('A_Color', a), ('B_Color', b)):
        if hasattr(v, 'is_output'): nt.links.new(v, ins[key])
        else: ins[key].default_value = v
    return next(s for s in m.outputs if s.identifier == 'Result_Color')

def math_node(nt, op, a, b=None, clamp=False):
    n = nt.nodes.new('ShaderNodeMath'); n.operation = op; n.use_clamp = clamp
    for i, v in enumerate((a, b)):
        if v is None: continue
        if hasattr(v, 'is_output'): nt.links.new(v, n.inputs[i])
        else: n.inputs[i].default_value = v
    return n.outputs[0]

def noise(nt, scale, detail=2.5, coord='Object', rough=.5):
    t = nt.nodes.new('ShaderNodeTexNoise'); t.inputs['Scale'].default_value = scale
    t.inputs['Detail'].default_value = detail; t.inputs['Roughness'].default_value = rough
    c = nt.nodes.new('ShaderNodeTexCoord'); nt.links.new(c.outputs[coord], t.inputs['Vector'])
    return t.outputs['Fac']

def ramp(nt, fac, lo, hi):
    r = nt.nodes.new('ShaderNodeMapRange'); nt.links.new(fac, r.inputs['Value'])
    r.inputs['From Min'].default_value = lo; r.inputs['From Max'].default_value = hi
    r.inputs['To Min'].default_value = 0; r.inputs['To Max'].default_value = 1
    return r.outputs['Result']

def mat(name, c, rough=.7, metallic=0.0):
    m = bpy.data.materials.new(name); m.diffuse_color = (*c, 1); m.use_nodes = True
    n, l, p = nodes_of(m)
    p.inputs['Base Color'].default_value = (*c, 1); p.inputs['Roughness'].default_value = rough
    p.inputs['Metallic'].default_value = metallic
    return m

def weathered(m, base_lo, base_hi, rough_lo, rough_hi, scale=24, edge=None, dust=0.0, bump=.0004):
    """Breakout's surface_variation, plus worn edges (Bevel normal mask) and dust on up-facing faces."""
    nt = m.node_tree; n, l, p = nodes_of(m)
    f = noise(nt, scale)
    col = mix_rgb(nt, f, (*base_lo, 1), (*base_hi, 1))
    r = nt.nodes.new('ShaderNodeMapRange'); l.new(f, r.inputs['Value'])
    r.inputs['To Min'].default_value = rough_lo; r.inputs['To Max'].default_value = rough_hi
    rough = r.outputs['Result']
    if edge:
        bev = nt.nodes.new('ShaderNodeBevel'); bev.samples = 6; bev.inputs['Radius'].default_value = .004
        geo = nt.nodes.new('ShaderNodeNewGeometry')
        dot = nt.nodes.new('ShaderNodeVectorMath'); dot.operation = 'DOT_PRODUCT'
        l.new(bev.outputs['Normal'], dot.inputs[0]); l.new(geo.outputs['Normal'], dot.inputs[1])
        e = ramp(nt, math_node(nt, 'SUBTRACT', 1.0, dot.outputs['Value']), .004, .05)
        chips = ramp(nt, noise(nt, 140, 6), .42, .62)
        wear = math_node(nt, 'MULTIPLY', e, chips, clamp=True)
        col = mix_rgb(nt, wear, col, (*edge, 1))
        rough = math_node(nt, 'ADD', rough, math_node(nt, 'MULTIPLY', wear, .18))
    if dust:
        geo = nt.nodes.new('ShaderNodeNewGeometry')
        sep = nt.nodes.new('ShaderNodeSeparateXYZ'); l.new(geo.outputs['Normal'], sep.inputs[0])
        up = ramp(nt, sep.outputs['Z'], .55, .95)
        speck = ramp(nt, noise(nt, 900, 1.5), .45, .8)
        patch = ramp(nt, noise(nt, 6, 2), .35, .75)
        d = math_node(nt, 'MULTIPLY', math_node(nt, 'MULTIPLY', up, math_node(nt, 'ADD', math_node(nt, 'MULTIPLY', speck, .6), math_node(nt, 'MULTIPLY', patch, .4))), dust, clamp=True)
        col = mix_rgb(nt, d, col, (.42, .41, .38, 1))
        rough = math_node(nt, 'ADD', rough, math_node(nt, 'MULTIPLY', d, .5))
    l.new(col, p.inputs['Base Color']); l.new(rough, p.inputs['Roughness'])
    if bump:
        b = nt.nodes.new('ShaderNodeBump'); b.inputs['Strength'].default_value = .12; b.inputs['Distance'].default_value = bump
        l.new(noise(nt, 160, 3), b.inputs['Height']); l.new(b.outputs['Normal'], p.inputs['Normal'])
    return m

def image(path, colorspace='sRGB'):
    img = bpy.data.images.load(str(path), check_existing=True)
    img.colorspace_settings.name = colorspace
    return img

def save_png(name, rgba):
    """rgba: HxWx4 float array, row 0 = top, sRGB values. Saved under gen/ and loaded back."""
    h, w = rgba.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(rgba[::-1]).astype(np.float32).ravel())
    path = GEN / f'{name}.png'
    img.filepath_raw = str(path); img.file_format = 'PNG'; img.save()
    bpy.data.images.remove(img)
    return image(path)

def image_mat(name, img, rough=.6, emission=None, sheen=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    n, l, p = nodes_of(m)
    t = n.new('ShaderNodeTexImage'); t.image = img; t.interpolation = 'Cubic'
    l.new(t.outputs['Color'], p.inputs['Base Color']); p.inputs['Roughness'].default_value = rough
    return m

# ----------------------------------------------------------------------------- geometry helpers
def box(name, loc, dim, material, bevel=.008, rot=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc); o = bpy.context.object; o.name = name; o.dimensions = dim
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.data.materials.append(material)
    if rot: o.rotation_euler = rot
    if bevel:
        m = o.modifiers.new('manufactured rounded edges', 'BEVEL'); m.width = bevel; m.segments = 3
        o.modifiers.new('weighted surface normals', 'WEIGHTED_NORMAL')
    return o

def cyl(name, loc, r, depth, material, rot=None, verts=48, bevel=.003):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc)
    o = bpy.context.object; o.name = name; o.data.materials.append(material)
    if rot: o.rotation_euler = rot
    if bevel:
        m = o.modifiers.new('rim bevel', 'BEVEL'); m.width = min(bevel, depth * .45, r * .45); m.segments = 3
        o.modifiers.new('surface normals', 'WEIGHTED_NORMAL')
    return o

def blob(name, loc, scale, material, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, location=loc)
    o = bpy.context.object; o.name = name; o.scale = scale; o.rotation_euler = rot
    o.data.materials.append(material); bpy.ops.object.shade_smooth()
    return o

def cable(name, pts, r=.005, material=None):
    c = bpy.data.curves.new(name, 'CURVE'); c.dimensions = '3D'; c.bevel_depth = r; c.bevel_resolution = 4
    c.use_fill_caps = True
    s = c.splines.new('BEZIER'); s.bezier_points.add(len(pts) - 1)
    for p, co in zip(s.bezier_points, pts):
        p.co = co; p.handle_left_type = 'AUTO'; p.handle_right_type = 'AUTO'
    o = bpy.data.objects.new(name, c); bpy.context.collection.objects.link(o)
    o.data.materials.append(material or trim)
    return o

def text(name, value, loc, size, material, rotation=(math.pi / 2, 0, 0), align='LEFT', extrude=.0001):
    c = bpy.data.curves.new(name, 'FONT'); c.body = value; c.size = size; c.extrude = extrude
    c.align_x = align; c.space_line = 1.05
    o = bpy.data.objects.new(name, c); bpy.context.collection.objects.link(o)
    o.location = loc; o.rotation_euler = rotation; o.data.materials.append(material)
    return o

def sheet(name, center, w, h, material, curl=None, facing='wall', uv=True, res=(16, 20), rot=0.0):
    """A paper sheet as a dense grid so corners and edges can lift off.
    facing='wall': sheet in XZ on a partition, lifts toward -Y.  facing='desk': sheet in XY, lifts +Z.
    curl: list of (u, v, radius, amount) - a lift around a point in sheet space (u,v in -.5..5)."""
    nx, ny = res; verts = []; faces = []; uvs = []
    ca, sa = math.cos(rot), math.sin(rot)
    for j in range(ny + 1):
        for i in range(nx + 1):
            u = i / nx - .5; v = j / ny - .5; lift = 0.0
            for cu, cv, rad, amt in (curl or []):
                d = math.hypot(u - cu, v - cv)
                if d < rad: lift += amt * ((rad - d) / rad) ** 2.2
            lx, ly = u * w, v * h
            lx, ly = lx * ca - ly * sa, lx * sa + ly * ca
            if facing == 'wall': verts.append((center[0] + lx, center[1] - lift, center[2] + ly))
            else: verts.append((center[0] + lx, center[1] + ly, center[2] + lift))
            uvs.append((u + .5, v + .5))
    for j in range(ny):
        for i in range(nx):
            a = j * (nx + 1) + i
            faces.append((a, a + 1, a + nx + 2, a + nx + 1) if facing == 'desk' else (a, a + nx + 1, a + nx + 2, a + 1))
    me = bpy.data.meshes.new(name); me.from_pydata(verts, [], faces)
    if uv:
        layer = me.uv_layers.new()
        for poly in me.polygons:
            for li in poly.loop_indices:
                layer.data[li].uv = uvs[me.loops[li].vertex_index]
    for p in me.polygons: p.use_smooth = True
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o); me.materials.append(material)
    s = o.modifiers.new('paper thickness', 'SOLIDIFY'); s.thickness = .00025; s.offset = 1 if facing == 'wall' else -1
    return o

def aim(o, target):
    o.rotation_euler = (Vector(target) - o.location).to_track_quat('-Z', 'Y').to_euler()

# ----------------------------------------------------------------------------- materials
fabric = mat('grey partition fabric', (.19, .20, .195), .95)
trim = mat('aged charcoal trim', (.075, .08, .078), .6)
desk = mat('grey laminate', (.29, .29, .27), .55)
plastic = mat('old grey PC plastic', (.12, .13, .125), .5)
keys = mat('worn keycaps', (.23, .235, .22), .6)
paper = mat('old paperwork', (.55, .55, .51), .92)
ink = mat('toner', (.05, .052, .05), .7)
metal = mat('filing cabinet', (.17, .18, .175), .5)
chrome = mat('dull steel fittings', (.42, .43, .42), .32, .9)
blackmetal = mat('black lamp enamel', (.03, .032, .034), .35, .2)
weathered(plastic, (.08, .084, .082), (.14, .145, .138), .38, .58, 19, edge=(.21, .215, .205), dust=.35)
weathered(desk, (.25, .255, .24), (.31, .315, .295), .40, .58, 38, edge=(.40, .40, .37), dust=.45)
weathered(metal, (.13, .138, .135), (.19, .20, .191), .42, .62, 26, edge=(.30, .31, .30), dust=.25)
weathered(trim, (.06, .064, .062), (.09, .095, .092), .5, .7, 30, edge=(.17, .17, .165), dust=.3)
weathered(keys, (.20, .205, .19), (.25, .255, .24), .45, .62, 60, dust=.2)
weathered(blackmetal, (.025, .027, .028), (.045, .047, .05), .3, .45, 30, edge=(.16, .16, .16), dust=.4)

# Partition fabric: Poly Haven rough_linen diffuse (CC0), as in Breakout, desaturated through a ramp.
linen = next((p for p in [*(OUT / 'assets' / 'rough-linen').glob('*diffuse*.jpg'),
                          *(OUT.parent / 'breakout-office' / 'assets' / 'rough-linen').glob('*diffuse*.jpg')]
              if p.stat().st_size > 10000), None)
if linen is None:
    raise FileNotFoundError('rough_linen_diffuse_2k.jpg missing (see README: Asset credits)')
n, l, p = nodes_of(fabric); nt = fabric.node_tree
uv = n.new('ShaderNodeTexCoord'); sc = n.new('ShaderNodeVectorMath'); sc.operation = 'SCALE'; sc.inputs[3].default_value = 7
l.new(uv.outputs['UV'], sc.inputs[0])
t = n.new('ShaderNodeTexImage'); t.image = image(linen); l.new(sc.outputs[0], t.inputs['Vector'])
bw = n.new('ShaderNodeRGBToBW'); l.new(t.outputs['Color'], bw.inputs[0])
fab = mix_rgb(nt, bw.outputs[0], (.060, .066, .066, 1), (.23, .24, .238, 1))
fab = mix_rgb(nt, ramp(nt, noise(nt, 3, 2), .3, .9), fab, (.17, .175, .17, 1))   # big soft blotches: years of use
l.new(fab, p.inputs['Base Color']); p.inputs['Sheen Weight'].default_value = .25; p.inputs['Sheen Roughness'].default_value = .6
b = n.new('ShaderNodeBump'); b.inputs['Strength'].default_value = .25; b.inputs['Distance'].default_value = .0008
l.new(bw.outputs[0], b.inputs['Height']); l.new(b.outputs['Normal'], p.inputs['Normal'])

# Carpet tiles: Brick texture as 50 cm tiles with per-tile tone, fine fibre bump.
carpet = mat('office carpet tiles', (.06, .065, .062), 1)
n, l, p = nodes_of(carpet); nt = carpet.node_tree
co = n.new('ShaderNodeTexCoord'); br = n.new('ShaderNodeTexBrick'); l.new(co.outputs['Object'], br.inputs['Vector'])
br.offset = 0; br.squash = 1; br.inputs['Scale'].default_value = 2; br.inputs['Mortar Size'].default_value = .002
br.inputs['Brick Width'].default_value = 1; br.inputs['Row Height'].default_value = 1; br.inputs['Bias'].default_value = 0
br.inputs['Color1'].default_value = (.05, .055, .056, 1); br.inputs['Color2'].default_value = (.072, .076, .074, 1)
br.inputs['Mortar'].default_value = (.03, .032, .032, 1)
l.new(mix_rgb(nt, ramp(nt, noise(nt, 300, 4), .3, .7), br.outputs['Color'], (.09, .092, .09, 1)), p.inputs['Base Color'])
b = n.new('ShaderNodeBump'); b.inputs['Strength'].default_value = .4; b.inputs['Distance'].default_value = .002
l.new(noise(nt, 600, 6), b.inputs['Height']); l.new(b.outputs['Normal'], p.inputs['Normal'])

# ----------------------------------------------------------------------------- generated textures (own art)
def poster_picture(w=720, h=540):
    """A sunny meadow, stock motivational-poster style. Painted procedurally, no photo."""
    y = np.linspace(0, 1, h)[:, None]; x = np.linspace(0, 1, w)[None, :]
    sky_top = np.array([.20, .48, .86]); sky_low = np.array([.72, .87, .98])
    img = sky_top + (sky_low - sky_top) * (y[..., None] ** .8)
    img = np.broadcast_to(img, (h, w, 3)).copy()
    sx, sy = .76, .2                                   # sun with glow
    d = np.sqrt(((x - sx) * w / h) ** 2 + (y - sy) ** 2)
    img += (np.exp(-(d / .2) ** 2) * .55)[..., None] * np.array([1, .9, .55])
    img = np.where((d < .055)[..., None], np.array([1, .97, .82]), img)
    for cx, cy, s in [(.18, .18, .09), (.27, .15, .07), (.36, .2, .06), (.55, .1, .05), (.62, .12, .04)]:   # clouds
        c = np.exp(-(((x - cx) / (s * 1.8)) ** 2 + ((y - cy) / s) ** 2) * 2.2)
        img = img * (1 - c[..., None] * .85) + c[..., None] * np.array([1, 1, 1]) * .85
    def hill(base, amp, freq, phase, col, tex):
        top = base + amp * np.sin(x * freq + phase) + amp * .4 * np.sin(x * freq * 2.7 + phase * 1.7)
        mask = (y >= top).astype(float)
        depth = np.clip((y - top) * 3, 0, 1)
        grain = rng.random((h, w)) * tex
        shade = np.array(col)[None, None, :] * (1 - .25 * depth[..., None]) * (1 - grain[..., None])
        return mask, shade
    for base, amp, freq, ph, col, tex in [(.56, .025, 7, .4, (.45, .66, .42), .05), (.63, .03, 5, 2.1, (.33, .62, .24), .08),
                                          (.72, .035, 4, 4.0, (.30, .58, .18), .14)]:
        m, s = hill(base, amp, freq, ph, col, tex)
        img = img * (1 - m[..., None]) + s * m[..., None]
    for _ in range(420):                               # flowers in the foreground hill
        fx, fy = rng.random(), .76 + rng.random() * .24
        r = int(1 + (fy - .74) * 9)
        cx, cy = int(fx * w), int(fy * h)
        col = [(1, 1, .96), (1, .86, .25), (1, .55, .75), (.75, .7, 1)][rng.integers(4)]
        img[max(cy - r, 0):cy + r, max(cx - r, 0):cx + r] = col
    img = np.clip(img, 0, 1)
    return np.dstack([img, np.ones((h, w))])

def spiral_band(w=1400, h=590):
    """Mug decal: hypno spiral on the camera side of a white mug. Physical aspect: circumference .251 m x .09 m."""
    u = np.linspace(0, 1, w)[None, :]; v = np.linspace(1, 0, h)[:, None]
    X = (u - .75) * .251; Y = (v - .5) * .106                        # metres around the mug, centre faces -Y
    r = np.sqrt(X ** 2 + Y ** 2); a = np.arctan2(Y, X)
    arms = (np.mod(r / .0075 - a / math.tau * 2, 1) < .5)
    disc = r < .034
    base = np.ones((h, w, 3)) * np.array([.93, .92, .9])
    pink = np.array([.86, .27, .55]); dark = np.array([.13, .07, .16])
    spiral = np.where(arms[..., None], pink, dark)
    edge = np.clip((.034 - r) / .0012, 0, 1)[..., None]
    img = np.where(disc[..., None], base * (1 - edge) + spiral * edge, base)
    return np.dstack([img, np.ones((h, w))])

poster_img = save_png('poster-meadow', poster_picture())
spiral_img = save_png('mug-spiral', spiral_band())

# ----------------------------------------------------------------------------- room shell (Breakout layout)
box('floor', (0, -.6, -.05), (7, 7, .10), carpet, 0)
box('back fabric partition', (0, .65, 1.05), (2.55, .075, 2.10), fabric)
box('left fabric partition', (-1.25, -.30, 1.05), (.075, 1.9, 2.10), fabric)
box('right fabric partition', (1.25, -.30, 1.05), (.075, 1.9, 2.10), fabric)
for x in (-1.25, 0, 1.25): box('partition upright', (x, .608, 1.05), (.024, .012, 2.10), trim, .003)
box('partition top', (0, .65, 2.10), (2.58, .10, .035), trim)
for x in (-1.208, 1.208):
    box('side panel top trim', (x, -.30, 2.10), (.03, 1.92, .035), trim, .004)
    box('side panel rail', (x, -.30, .63), (.022, 1.9, .020), trim, .003)
    box('partition end cap', (x * 1.035, -1.25, 1.05), (.085, .03, 2.12), trim, .006)
for x in (-1.25, 1.25): box('partition base kick', (x, -.30, .04), (.08, 1.9, .08), trim, .004)
box('back partition kick', (0, .61, .04), (2.5, .02, .08), trim, .004)
# Neighbouring cubicle tops beyond, catching the far aisle light (no window, just more office).
for x in (-3.8, 3.8):
    box('neighbour partition', (x, .65, 1.05), (2.55, .075, 2.10), fabric)
    box('neighbour partition top', (x, .65, 2.10), (2.58, .10, .035), trim)

# ----------------------------------------------------------------------------- desk
box('desktop', (0, -.03, .75), (2.30, .95, .055), desk, .006)
box('laminate edge band', (0, -.507, .75), (2.30, .012, .047), trim, .002)
for x in (-.98, .98):
    for y in (-.36, .29): box('desk metal leg', (x, y, .36), (.045, .045, .72), metal)
    box('leg foot', (x, -.035, .012), (.06, .74, .024), metal, .004)
box('cable modesty panel', (0, .32, .48), (2.02, .035, .42), metal)
cyl('cable grommet', (.66, .36, .780), .034, .005, trim)
# Drawer pedestal, left.
box('drawer pedestal', (-.89, -.01, .36), (.43, .63, .70), metal)
for z, hgt in ((.20, .17), (.46, .17), (.65, .12)):
    box('drawer front', (-.89, -.335, z), (.40, .018, hgt), desk, .003)
    box('drawer handle', (-.89, -.357, z + .025), (.14, .025, .013), chrome, .003)
    box('drawer lock', (-.80, -.345, .69), (.014, .01, .014), chrome, .002) if z == .65 else None

# ----------------------------------------------------------------------------- monitor (screen stays the front-most surface)
SW, SH, SY, SZ = .616, .3465, .131, 1.22
box('monitor pedestal', (0, .18, .801), (.32, .22, .045), plastic, .012)
box('monitor stand seam', (0, .185, .84), (.085, .080, .012), trim, .003)
box('monitor neck', (0, .23, .94), (.08, .08, .25), plastic, .012)
box('monitor shell', (0, .19, 1.21), (.68, .09, .43), plastic, .018)
box('monitor back hump', (0, .245, 1.20), (.46, .06, .30), plastic, .03)
for x in (-.312, .312): box('inner bezel vertical', (x, .1375, SZ), (.008, .009, SH + .016), trim, .002)
for z in (SZ - SH / 2 - .004, SZ + SH / 2 + .004): box('inner bezel horizontal', (0, .1375, z), (SW + .016, .009, .008), trim, .002)
text('monitor label', 'OFFICE SYSTEMS', (-.06, .1435, 1.0145), .009, keys)
for x in (.21, .235, .26): box('monitor button', (x, .1435, 1.020), (.013, .004, .003), keys, .001)
led_on = bpy.data.materials.new('monitor power LED'); led_on.use_nodes = True
n, l, p = nodes_of(led_on); p.inputs['Emission Color'].default_value = (.25, 1, .55, 1); p.inputs['Emission Strength'].default_value = 6
cyl('monitor power LED', (.288, .1425, 1.020), .0022, .002, led_on, rot=(math.pi / 2, 0, 0), verts=16, bevel=0)
for x in (-.313, .313):
    for z in (1.0265, 1.3935):
        cyl('monitor case screw', (x, .1445, z - .0015 if z < 1.2 else z + .0015), .003, .002, trim, rot=(math.pi / 2, 0, 0), verts=16)

# The screen: emissive meadow.jpg. Camera rays see strength 1 (true colours), every other ray sees
# strength 3.2, so the monitor is the room's key light while the screen still reads as the meadow.
mesh = bpy.data.meshes.new('screen UV surface')
mesh.from_pydata([(-SW / 2, SY, SZ - SH / 2), (SW / 2, SY, SZ - SH / 2), (SW / 2, SY, SZ + SH / 2), (-SW / 2, SY, SZ + SH / 2)], [], [(0, 1, 2, 3)])
mesh.uv_layers.new()
for i, uvc in enumerate([(0, 0), (1, 0), (1, 1), (0, 1)]): mesh.uv_layers.active.data[i].uv = uvc
screen = bpy.data.objects.new('SCREEN_MEADOW', mesh); bpy.context.collection.objects.link(screen)
sm = bpy.data.materials.new('meadow screen'); sm.use_nodes = True; n = sm.node_tree.nodes; n.clear(); l = sm.node_tree.links
out = n.new('ShaderNodeOutputMaterial'); emit = n.new('ShaderNodeEmission')
tex = n.new('ShaderNodeTexImage'); tex.image = image(MEADOW); tex.interpolation = 'Cubic'
lp = n.new('ShaderNodeLightPath')
strength = n.new('ShaderNodeMath'); strength.operation = 'MULTIPLY_ADD'      # cam*(1-3.2)+3.2
l.new(lp.outputs['Is Camera Ray'], strength.inputs[0]); strength.inputs[1].default_value = 1 - 3.2; strength.inputs[2].default_value = 3.2
l.new(tex.outputs['Color'], emit.inputs['Color']); l.new(strength.outputs[0], emit.inputs['Strength']); l.new(emit.outputs[0], out.inputs[0])
mesh.materials.append(sm)
# Monitor power cable runs down behind the neck into the grommet.
cable('monitor power cable', [(0, .27, 1.05), (.03, .29, .86), (.15, .32, .79), (.45, .36, .79), (.66, .36, .79), (.66, .36, .55)], .0045)

# ----------------------------------------------------------------------------- keyboard, mouse
box('keyboard case', (-.10, -.22, .793), (.49, .165, .03), plastic, .006)
box('keyboard rear lip', (-.10, -.147, .806), (.49, .02, .012), plastic, .004)
for row in range(5):
    for k in range(15):
        o = box('keyboard key', (-.322 + k * .0305 + random.uniform(-.0003, .0003), -.276 + row * .027, .8125 + random.uniform(-.0004, .0004)), (.025, .022, .009), keys, .002)
box('spacebar', (-.10, -.29, .8215), (.16, .018, .008), keys, .002)
for r in range(3):
    for k, ch in enumerate(['QWERTYUIOP', 'ASDFGHJKL', 'ZXCVBNM'][r]):
        text('key legend', ch, (-.3045 + k * .0305, -.2235 - r * .027, .8172), .005, paper, (0, 0, 0))
box('mousepad', (.38, -.22, .780), (.24, .24, .005), trim, .004)
blob('wired mouse', (.38, -.23, .797), (.031, .055, .022), plastic)
box('mouse button seam', (.38, -.262, .815), (.0012, .03, .003), trim, 0)
cable('mouse wire', [(.38, -.17, .795), (.43, -.04, .786), (.39, .26, .786), (.62, .34, .786), (.66, .36, .784)], .0022)
cable('keyboard wire', [(-.10, -.13, .80), (-.17, -.02, .783), (-.05, .30, .783), (.55, .37, .783), (.66, .36, .782)], .0022)

# ----------------------------------------------------------------------------- PC tower, standby LED
box('PC tower', (.88, .23, 1.01), (.22, .38, .47), plastic, .013)
for z in (1.12, 1.18):
    box('drive bay', (.88, .036, z), (.18, .008, .025), trim, .002)
    box('drive bay eject', (.94, .031, z - .006), (.016, .004, .005), keys, .001)
for z in range(10): box('tower ventilation', (.88, .038, .84 + z * .011), (.15, .004, .004), trim, .001)
cyl('tower power button', (.83, .037, 1.215), .009, .004, keys, rot=(math.pi / 2, 0, 0), verts=24)
led = bpy.data.materials.new('standby amber LED'); led.use_nodes = True
n, l, p = nodes_of(led); p.inputs['Emission Color'].default_value = (1, .45, .08, 1); p.inputs['Emission Strength'].default_value = 9
cyl('tower standby LED', (.86, .037, 1.24), .0025, .003, led, rot=(math.pi / 2, 0, 0), verts=16, bevel=0)
cable('tower cable bundle', [(.88, .41, .92), (.69, .40, .80), (.66, .36, .79), (.66, .36, .60)], .008)
for i, (x, y) in enumerate(((.92, .43), (.86, .43), (.80, .43))):
    cable('tower rear cable', [(x, y, .9 + i * .03), (x, y + .05, .78), (x - .1, y + .02, .5), (.4, .45, .05), (.1, .5, .015)], .004)
# Power strip on the floor under the desk.
box('power strip', (.15, .45, .02), (.36, .055, .035), mat('off-white strip', (.45, .45, .42), .5), .008)

# ----------------------------------------------------------------------------- paperwork, binders, tray (Breakout props, cleaned up)
for z in (.783, .820):
    box('document tray base', (-.65, .15, z), (.277, .332, .004), trim, .003)
    for x in (-.789, -.511): box('document tray rim', (x, .15, z + .014), (.004, .33, .032), metal, .002)
    box('document tray back', (-.65, .314, z + .014), (.28, .004, .032), metal, .002)
for side in (-1, 1):
    for y in (.0, .30): box('tray post', (-.65 + side * .13, y, .80), (.006, .006, .036), metal, .001)
for j in range(6):
    sheet(f'paper in tray {j}', (-.65 + random.uniform(-.004, .004), .15 + random.uniform(-.003, .003), .826 + j * .0026),
          .25, .31, paper, curl=[(.5, -.5, .4, .004 + j * .0008)], facing='desk', uv=False, res=(10, 12), rot=random.uniform(-.05, .05))
text('invoice title', 'EXPENSE REPORT', (-.750, .255, .8436), .012, ink, (0, 0, 0))
for j in range(10): box('printed rows', (-.65, .21 - j * .018, .8435), (.19, .0018, .0003), ink, 0)
binder_cols = [mat('binder grey', (.10, .105, .10), .55), mat('binder blue grey', (.07, .08, .095), .55), mat('binder charcoal', (.06, .062, .06), .55)]
for i, (x, year) in enumerate(zip((-1.02, -.965, -.91), ('2024', '2025', '2026'))):
    o = box('binder', (x, .38, .98), (.044, .21, .40), binder_cols[i], .004)
    o.rotation_euler.y = (-.035 if i == 2 else 0)
    box('binder label', (x, .27, 1.03), (.026, .002, .09), paper, .001)
    for z in (.795, 1.163): box('binder worn spine edge', (x, .27, z), (.042, .004, .006), keys, .001)
    bpy.ops.mesh.primitive_torus_add(major_radius=.008, minor_radius=.0017, major_segments=24, minor_segments=8, location=(x, .268, .861), rotation=(math.pi / 2, 0, 0))
    bpy.context.object.name = 'binder pull ring'; bpy.context.object.data.materials.append(chrome)
    text('binder archive year', year, (x - .012, .266, 1.05), .007, ink)

# Telephone (Breakout's sloped one), LCD faintly lit.
x0, x1 = .535, .745; y0, y1 = -.295, -.105; z0 = .7775
v = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, .836), (x1, y0, .836), (x1, y1, .867), (x0, y1, .867)]
me = bpy.data.meshes.new('telephone wedge mesh'); me.from_pydata(v, [], [(0, 3, 2, 1), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7), (4, 5, 6, 7)])
o = bpy.data.objects.new('sloped office telephone', me); bpy.context.collection.objects.link(o); me.materials.append(plastic)
m = o.modifiers.new('case radius', 'BEVEL'); m.width = .009; m.segments = 4; o.modifiers.new('normals', 'WEIGHTED_NORMAL')
for r in range(4):
    for c in range(3): box('phone button', (.62 + c * .025, -.25 + r * .023, .840 + (-.25 + r * .023 + .295) * .16), (.018, .015, .005), keys, .002)
box('receiver handle', (.567, -.20, .890), (.03, .122, .020), trim, .009)
for y in (-.28, -.12):
    box('receiver earpiece', (.567, y, .879), (.051, .053, .044), trim, .015)
lcd = bpy.data.materials.new('phone LCD backlight'); lcd.use_nodes = True
n, l, p = nodes_of(lcd); p.inputs['Base Color'].default_value = (.18, .24, .16, 1); p.inputs['Emission Color'].default_value = (.35, .55, .3, 1); p.inputs['Emission Strength'].default_value = .25
box('phone LCD recess', (.676, -.132, .870), (.097, .040, .006), trim, .003)
box('phone LCD glass', (.676, -.132, .874), (.080, .026, .002), lcd, .001)
text('phone LCD text', '21:47  1 MISSED', (.640, -.140, .8756), .0065, ink, (0, 0, 0))
pts = [(.535 + .010 * math.sin(t / 159 * math.tau * 13), -.28 + t / 159 * .18, .7955 + .009 * math.cos(t / 159 * math.tau * 13)) for t in range(160)]
cable('telephone coil', pts, .002)

# Memo with paper clip, pen.
sheet('desk memo', (.13, -.38, .7785), .13, .105, paper, curl=[(.5, .5, .5, .006)], facing='desk', uv=False, res=(10, 8), rot=-.12)
text('memo print', 'CALL BACK\nEXT. 041', (.083, -.392, .7802), .010, ink, (0, 0, -.12))
cable('paper clip', [(.162, -.358, .7805), (.175, -.358, .7805), (.177, -.347, .7805), (.177, -.33, .7805), (.169, -.326, .7805), (.163, -.332, .7805), (.163, -.350, .7805), (.170, -.350, .7805), (.171, -.334, .7805)], .0009, chrome)
pen = cyl('pen', (.29, -.35, .7845), .0045, .15, mat('blue pen barrel', (.05, .07, .16), .35), rot=(math.pi / 2, 0, -.21), verts=16)
cyl('pen cap clip', (.283, -.315, .789), .0012, .03, chrome, rot=(math.pi / 2, 0, -.21), verts=8, bevel=0)

# ----------------------------------------------------------------------------- T&C touches
# Spiral mug (hollow, Breakout profile) with the decal facing the camera, and an old ring stain.
cer = image_mat('spiral mug ceramic', spiral_img, rough=.18)
plain = mat('white glaze', (.93, .92, .9), .18)
MX, MY = -.52, -.22
profile = [(0, .7775), (.033, .7775), (.039, .7845), (.040, .8745), (.0385, .8835), (.034, .8835), (.032, .8755), (.030, .7905), (0, .7905)]
verts = []; faces = []; N = 72
for r, z in profile:
    for j in range(N): verts.append((MX + r * math.cos(j * math.tau / N), MY + r * math.sin(j * math.tau / N), z))
for k in range(len(profile) - 1):
    for j in range(N): a = k * N + j; b2 = k * N + (j + 1) % N; faces.append((a, b2, b2 + N, a + N))
me = bpy.data.meshes.new('spiral mug mesh'); me.from_pydata(verts, [], faces)
me.materials.append(plain); me.materials.append(cer)
uvl = me.uv_layers.new()
for fi, poly in enumerate(me.polygons):
    k, j = divmod(fi, N); poly.use_smooth = True
    poly.material_index = 1 if k in (1, 2) else 0
    for li in poly.loop_indices:
        vi = me.loops[li].vertex_index; kk, jj = divmod(vi, N)
        if jj == 0 and j == N - 1: jj = N
        uvl.data[li].uv = (jj / N, (profile[kk][1] - .7775) / .106)
mug = bpy.data.objects.new('spiral mug', me); bpy.context.collection.objects.link(mug)
mug.modifiers.new('mug smooth', 'SUBSURF').levels = 1
bpy.ops.mesh.primitive_torus_add(major_radius=.030, minor_radius=.0075, major_segments=40, minor_segments=14, location=(MX - .047, MY, .833), rotation=(math.pi / 2, 0, 0))
bpy.context.object.name = 'mug handle'; bpy.context.object.data.materials.append(plain); bpy.ops.object.shade_smooth()
coffee = mat('cold coffee dregs', (.035, .018, .01), .08)
cyl('coffee dregs', (MX, MY, .796), .0302, .002, coffee, verts=48, bevel=0)
stain = mat('old coffee ring', (.16, .145, .12), .9)
cable('old mug ring', [(-.43 + (.041 + .001 * math.sin(i / 89 * math.tau * 7)) * math.cos(i / 89 * math.tau), -.17 + (.041 + .001 * math.sin(i / 89 * math.tau * 7)) * math.sin(i / 89 * math.tau), .7778) for i in range(90)], .0008, stain)

# Llama plush, sitting left of the keyboard, turned toward the empty chair.
fuzz = mat('plush fleece', (.80, .72, .60), .95)
n, l, p = nodes_of(fuzz); nt = fuzz.node_tree
p.inputs['Sheen Weight'].default_value = 1; p.inputs['Sheen Roughness'].default_value = .35
l.new(mix_rgb(nt, ramp(nt, noise(nt, 90, 4), .35, .7), (.66, .58, .47, 1), (.86, .79, .67, 1)), p.inputs['Base Color'])
b = n.new('ShaderNodeBump'); b.inputs['Strength'].default_value = .5; b.inputs['Distance'].default_value = .0015
l.new(noise(nt, 700, 8), b.inputs['Height']); l.new(b.outputs['Normal'], p.inputs['Normal'])
pinkfelt = mat('pink felt scarf', (.75, .22, .42), .9); nodes_of(pinkfelt)[2].inputs['Sheen Weight'].default_value = .8
eyes = mat('plush eye bead', (.01, .01, .012), .12)
LX, LY, LZ = -.355, -.05, .7775
llama = []
llama.append(blob('llama body', (LX, LY, LZ + .072), (.068, .045, .045), fuzz))
for dx in (-.042, .042):
    for dy in (-.024, .024): llama.append(blob('llama leg', (LX + dx, LY + dy, LZ + .022), (.016, .016, .026), fuzz))
llama.append(blob('llama neck', (LX + .052, LY, LZ + .125), (.022, .022, .05), fuzz, rot=(0, .3, 0)))
llama.append(blob('llama head', (LX + .068, LY, LZ + .178), (.03, .026, .027), fuzz))
llama.append(blob('llama snout', (LX + .095, LY, LZ + .17), (.018, .016, .014), fuzz))
for dy in (-.014, .014):
    llama.append(blob('llama ear', (LX + .058, LY + dy, LZ + .214), (.007, .006, .019), fuzz, rot=(dy * 9, -.2, 0)))
    llama.append(blob('llama eye', (LX + .086, LY + dy * 1.35, LZ + .186), (.0045, .0045, .0045), eyes))
llama.append(blob('llama tail', (LX - .07, LY, LZ + .09), (.012, .012, .016), fuzz))
bpy.ops.mesh.primitive_torus_add(major_radius=.022, minor_radius=.007, major_segments=32, minor_segments=12, location=(LX + .056, LY, LZ + .142), rotation=(0, .55, 0))
bpy.context.object.name = 'llama scarf'; bpy.context.object.data.materials.append(pinkfelt); bpy.ops.object.shade_smooth(); llama.append(bpy.context.object)
blob('llama scarf tail', (LX + .072, LY - .018, LZ + .118), (.006, .012, .022), pinkfelt, rot=(.3, .2, 0))
pivot = bpy.data.objects.new('llama plush', None); bpy.context.collection.objects.link(pivot); pivot.location = (LX, LY, LZ)
bpy.context.view_layer.update()
for o in [*llama, bpy.data.objects['llama scarf tail']]:
    o.parent = pivot; o.matrix_parent_inverse = pivot.matrix_world.inverted()
pivot.rotation_euler.z = -1.55

# Headphones left on the desk: band arcs up, cups flat, cable trailing off to the tower.
hp = mat('headphone black plastic', (.025, .026, .028), .4)
cush = mat('headphone pink cushion', (.55, .16, .32), .75); nodes_of(cush)[2].inputs['Sheen Weight'].default_value = .5
HX, HY = .70, -.405
for side in (-1, 1):
    cx = HX + side * .085
    cyl('headphone cup', (cx, HY, .7775 + .021), .044, .042, hp, rot=(0, side * .18, 0), verts=40, bevel=.006)
    bpy.ops.mesh.primitive_torus_add(major_radius=.034, minor_radius=.011, major_segments=40, minor_segments=14, location=(cx - side * .004, HY, .7775 + .045), rotation=(0, side * .18, 0))
    bpy.context.object.name = 'headphone cushion'; bpy.context.object.data.materials.append(cush); bpy.ops.object.shade_smooth()
arc = [(HX + .085 * math.cos(a), HY + .05 * math.sin(a), .80 + .055 * math.sin(a)) for a in np.linspace(0, math.pi, 9)]
cable('headphone band', arc, .008, hp)
cable('headphone band pad', [(x, y, z + .009) for x, y, z in arc[2:7]], .006, cush)
cable('headphone cable', [(HX + .12, HY, .785), (HX + .17, HY + .05, .7805), (.86, -.2, .7805), (.82, -.05, .7805), (.80, .02, .79), (.80, .045, .80)], .0018, hp)

# Desk lamp, switched off, right of the monitor: base, two arms, conical shade, dark bulb.
LB = Vector((.50, .41, .7775))
cyl('lamp base', LB + Vector((0, 0, .012)), .065, .024, blackmetal, verts=48)
cyl('lamp switch', LB + Vector((.04, -.03, .027)), .008, .008, keys, verts=16)
elbow = Vector((.47, .44, 1.17)); head = Vector((.60, .19, 1.30))
for a, bnd in ((LB + Vector((0, 0, .025)), elbow), (elbow, head)):
    mid = (a + bnd) / 2; d = bnd - a
    for off in (-.012, .012):
        o = cyl('lamp arm rod', mid + Vector((off, 0, 0)), .0045, d.length, blackmetal, verts=12, bevel=0)
        o.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
cyl('lamp elbow joint', elbow, .016, .03, blackmetal, rot=(0, math.pi / 2, 0), verts=24)
cable('lamp spring', [elbow + Vector((.02, 0, 0)) + (LB - elbow) * t * .5 + Vector((0, .01 * math.sin(t * 40), .01 * math.cos(t * 40))) for t in np.linspace(.1, .9, 60)], .0012, chrome)
shade_dir = (Vector((.62, -.02, .80)) - head).normalized()
bpy.ops.mesh.primitive_cone_add(vertices=48, radius1=.078, radius2=.024, depth=.13, location=head + shade_dir * .05, end_fill_type='NOTHING')
o = bpy.context.object; o.name = 'lamp shade'; o.data.materials.append(blackmetal)
o.rotation_euler = (-shade_dir).to_track_quat('Z', 'Y').to_euler()
o.modifiers.new('shade wall', 'SOLIDIFY').thickness = .0015; bpy.ops.object.shade_smooth()
bulb = mat('switched off bulb', (.75, .74, .7), .1); nodes_of(bulb)[2].inputs['Transmission Weight'].default_value = .6
blob('lamp bulb', head + shade_dir * .085, (.026, .026, .026), bulb)
cable('lamp cord', [LB + Vector((-.05, .02, .005)), Vector((.45, .5, .78)), Vector((.40, .52, .5)), Vector((.25, .48, .03))], .0022)

# Sticky note on the bezel, top right, overhanging the case edge, bottom edge curling away.
note_paper = mat('sticky note pink', (.95, .55, .7), .85)
sheet('sticky note', (.368, .1438, 1.355), .076, .076, note_paper, curl=[(0, -.5, .7, .012), (.5, -.5, .35, .006)], facing='wall', uv=False, res=(12, 12), rot=.09)
text('sticky note scrawl', 'terms\naccepted', (.344, .1426, 1.370), .0105, mat('felt pen', (.12, .05, .1), .6), (math.pi / 2, 0, .0), extrude=0)
bpy.data.objects['sticky note scrawl'].rotation_euler.y = -.09

# Framed motivational poster on the back partition, left: GO OUTSIDE.
PX, PZ, PW, PH = -.60, 1.48, .40, .52
FY = .6125
for name, loc, dim in [('poster frame top', (PX, FY - .011, PZ + PH / 2 - .007), (PW, .022, .014)),
                       ('poster frame bottom', (PX, FY - .011, PZ - PH / 2 + .007), (PW, .022, .014)),
                       ('poster frame left', (PX - PW / 2 + .007, FY - .011, PZ), (.014, .022, PH)),
                       ('poster frame right', (PX + PW / 2 - .007, FY - .011, PZ), (.014, .022, PH))]:
    box(name, loc, dim, mat('poster frame aluminium', (.08, .08, .085), .3, .7), .002)
box('poster black mat', (PX, FY - .003, PZ), (PW - .02, .002, PH - .02), mat('poster black border', (.008, .008, .009), .6), 0)
pic = image_mat('poster meadow print', poster_img, rough=.35)
pm = bpy.data.meshes.new('poster picture'); IW, IH = PW - .07, (PW - .07) * .75; IZ = PZ + .07
pm.from_pydata([(PX - IW / 2, FY - .0045, IZ - IH / 2), (PX + IW / 2, FY - .0045, IZ - IH / 2), (PX + IW / 2, FY - .0045, IZ + IH / 2), (PX - IW / 2, FY - .0045, IZ + IH / 2)], [], [(0, 1, 2, 3)])
pm.uv_layers.new()
for i, uvc in enumerate([(0, 0), (1, 0), (1, 1), (0, 1)]): pm.uv_layers.active.data[i].uv = uvc
po = bpy.data.objects.new('poster picture', pm); bpy.context.collection.objects.link(po); pm.materials.append(pic)
box('poster picture keyline', (PX, FY - .0042, IZ), (IW + .006, .0005, IH + .006), mat('poster keyline', (.6, .6, .58), .5), 0)
glass = bpy.data.materials.new('poster glass'); glass.use_nodes = True
n, l, p = nodes_of(glass); p.inputs['Base Color'].default_value = (1, 1, 1, 1); p.inputs['Roughness'].default_value = .04
p.inputs['Transmission Weight'].default_value = 1; p.inputs['IOR'].default_value = 1.5
cream = mat('poster lettering', (.86, .84, .78), .5)
text('poster title', 'GO OUTSIDE', (PX, FY - .0055, PZ - .135), .045, cream, align='CENTER')
text('poster subtitle', 'It is still there. Probably.', (PX, FY - .0055, PZ - .175), .0135, cream, align='CENTER')

# Remaining pinned notice (right), with a lifted corner and small print.
sheet('pinned schedule', (.56, .6115, 1.50), .25, .32, paper, curl=[(.5, -.5, .45, .02), (-.5, -.5, .3, .006)], facing='wall', uv=False)
text('notice heading', 'LATE SHIFT ROTA', (.45, .6092, 1.61), .013, ink)
for j, line in enumerate(['DEPARTMENT 04', 'LIGHTS OUT 20:00', '-----------------------', 'MON  LOCK UP', 'TUE  LOCK UP', 'WED  LOCK UP', 'THU  LOCK UP', 'FRI  LOCK UP', 'PLEASE KEEP ON FILE']):
    text('notice small print', line, (.454, .6094 - (.004 if j > 6 else 0), 1.565 - j * .023), .0085, ink)
cyl('pin', (.56, .604, 1.647), .005, .008, mat('red pin head', (.5, .04, .05), .3), rot=(math.pi / 2, 0, 0), verts=16)

# Wall clock, 21:47.
clockx, clockz = .80, 1.80
for name, r, depth, y, material in [('clock case', .105, .024, .598, trim), ('clock face', .091, .004, .584, paper)]:
    cyl(name, (clockx, y, clockz), r, depth, material, rot=(math.pi / 2, 0, 0), verts=64)
for i in range(60):
    a = i * math.tau / 60; big = i % 5 == 0
    o = box('clock tick', (clockx + math.sin(a) * (.081 if big else .084), .5815, clockz + math.cos(a) * (.081 if big else .084)), (.0022 if big else .0008, .001, .011 if big else .004), ink, 0)
    o.rotation_euler.y = a
for name, a, length, width, y in [('minute', 47 / 60 * math.tau, .072, .0025, .5805), ('hour', (9 + 47 / 60) / 12 * math.tau, .048, .0042, .5795)]:
    o = box('clock ' + name, (clockx + math.sin(a) * length * .38, y, clockz + math.cos(a) * length * .38), (width, .0012, length), ink, .0005)
    o.rotation_euler.y = a
cyl('clock hub', (clockx, .578, clockz), .005, .003, ink, rot=(math.pi / 2, 0, 0), verts=24)

# Empty office chair, pushed back and turned away from the desk.
seatfab = mat('chair seat fabric', (.05, .055, .06), .9)
weathered(seatfab, (.075, .08, .09), (.11, .115, .125), .85, .95, 40)
chair = []
CX, CY, CR = -.74, -.86, -2.45
for i in range(5):
    a = i * math.tau / 5
    o = box('chair base leg', (CX + math.cos(a) * .14, CY + math.sin(a) * .14, .07), (.28, .04, .03), plastic, .01); o.rotation_euler.z = a; chair.append(o)
    w = cyl('chair caster', (CX + math.cos(a) * .29, CY + math.sin(a) * .29, .028), .025, .022, hp, rot=(math.pi / 2, 0, a + math.pi / 2), verts=24); chair.append(w)
chair.append(cyl('chair gas lift', (CX, CY, .26), .022, .34, chrome, verts=32))
chair.append(cyl('chair gas shroud', (CX, CY, .16), .03, .14, hp, verts=32))
chair.append(box('chair seat plate', (CX, CY, .445), (.22, .22, .03), hp, .006))
chair.append(box('chair seat cushion', (CX, CY - .01, .49), (.48, .46, .075), seatfab, .03)); chair[-1].modifiers.new('cushion round', 'SUBSURF').levels = 2
chair.append(box('chair back spine', (CX, CY - .26, .62), (.06, .03, .30), hp, .01))
back = box('chair backrest', (CX, CY - .28, .86), (.44, .065, .46), seatfab, .03); back.rotation_euler.x = -.14; chair.append(back)
back.modifiers.new('cushion round', 'SUBSURF').levels = 2
chair.append(box('chair back shell', (CX, CY - .315, .86), (.40, .02, .40), hp, .02)); chair[-1].rotation_euler.x = -.14
for side in (-1, 1):
    chair.append(box('chair arm post', (CX + side * .23, CY - .03, .57), (.025, .04, .18), hp, .006))
    chair.append(box('chair arm pad', (CX + side * .23, CY - .01, .665), (.06, .24, .03), hp, .012))
cp = bpy.data.objects.new('office chair', None); bpy.context.collection.objects.link(cp); cp.location = (CX, CY, 0)
bpy.context.view_layer.update()
for o in chair: o.parent = cp; o.matrix_parent_inverse = cp.matrix_world.inverted()
cp.rotation_euler.z = CR

# Waste bin under the desk, right.
bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=.13, depth=.32, location=(.55, -.05, .16), end_fill_type='NGON')
o = bpy.context.object; o.name = 'waste bin'; o.data.materials.append(metal)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT'); bpy.ops.object.mode_set(mode='OBJECT')
top = max(o.data.polygons, key=lambda pp: pp.center.z); top.select = True
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.delete(type='FACE'); bpy.ops.object.mode_set(mode='OBJECT')
o.modifiers.new('bin wall', 'SOLIDIFY').thickness = .003
sheet('crumpled sheet', (.55, -.05, .29), .12, .1, paper, curl=[(0, 0, .7, .04), (.3, -.3, .3, -.02)], facing='desk', uv=False, res=(8, 8), rot=.6)

# ----------------------------------------------------------------------------- light: after hours
def area(name, loc, power, color, size, target, shape='DISK', size_y=None, camera=False):
    d = bpy.data.lights.new(name, 'AREA'); d.energy = power; d.color = color; d.shape = shape; d.size = size
    if size_y: d.size_y = size_y
    o = bpy.data.objects.new(name, d); bpy.context.collection.objects.link(o); o.location = loc; aim(o, target)
    o.visible_camera = camera
    return o
area('night corridor spill', (-1.7, -3.6, 2.5), 34, (.55, .64, .85), 2.4, (0, 0, .9))
area('far aisle fluorescent', (3.2, 1.6, 3.0), 30, (.92, .9, .82), 1.2, (1.25, -.3, 2.1), shape='RECTANGLE', size_y=.3)
area('back office glow', (0, 2.6, 3.0), 30, (.6, .68, .8), 3, (0, .65, 2.1))   # rims the partition tops from beyond
world = bpy.data.worlds.new('dark office'); world.use_nodes = True
world.node_tree.nodes['Background'].inputs[0].default_value = (.006, .008, .011, 1)
world.node_tree.nodes['Background'].inputs[1].default_value = 1
bpy.context.scene.world = world

# ----------------------------------------------------------------------------- camera: 30 fps, 150 frames
FPS, END, HOLD = 30, 150, 15
LENS = 39.0
d0 = SW * LENS / 36                       # distance at which the 16:9 screen fills a 36 mm horizontal sensor
C0 = Vector((0, SY, SZ))                  # screen centre
T1 = Vector((-.07, .10, 1.06))            # final look target
P1 = Vector((.20, -2.95, 1.55))           # final camera position
LENS1 = 34.0                              # a touch wider at the end
u0 = Vector((0, -1, 0)); u1 = (P1 - T1).normalized(); d1 = (P1 - T1).length
cdata = bpy.data.cameras.new('CAMERA_PULLBACK'); cdata.lens = LENS; cdata.sensor_fit = 'HORIZONTAL'; cdata.sensor_width = 36
cdata.clip_start = .01; cdata.clip_end = 30
cam = bpy.data.objects.new('CAMERA_PULLBACK', cdata); bpy.context.collection.objects.link(cam); cam.rotation_mode = 'QUATERNION'

def ease(t):
    """Slow start, long glide, very soft landing (quintic-in, eased-out with a long tail)."""
    t = max(0.0, min(1.0, t))
    return t ** 3 * (t * (6 * t - 15) + 10)

prev = None
MOVE_END = END - HOLD                     # frame 135: arrival, then hold to 150
for f in range(1, END + 1):
    t = (f - 1) / (MOVE_END - 1)
    q = ease(t)
    dist = d0 * (d1 / d0) ** q           # log-distance: an even perceived zoom
    qa = ease(max(0, (t - .08) / .92))    # the arc toward the off-axis view starts a beat later
    u = (u0 * (1 - qa) + u1 * qa).normalized()
    tgt = C0.lerp(T1, qa)
    cam.location = tgt + u * dist
    rot = (tgt - cam.location).to_track_quat('-Z', 'Y')
    if prev is not None and rot.dot(prev) < 0: rot.negate()
    cam.rotation_quaternion = rot; prev = rot.copy()
    cdata.lens = LENS + (LENS1 - LENS) * qa; cdata.keyframe_insert(data_path='lens', frame=f)
    cam.keyframe_insert(data_path='location', frame=f); cam.keyframe_insert(data_path='rotation_quaternion', frame=f)

scene = bpy.context.scene
scene.camera = cam; scene.render.fps = FPS; scene.frame_start = 1; scene.frame_end = END
scene.render.engine = 'CYCLES'; scene.cycles.samples = 128; scene.cycles.adaptive_threshold = .02
scene.cycles.use_denoising = True; scene.cycles.denoiser = 'OPENIMAGEDENOISE'
scene.cycles.max_bounces = 8; scene.cycles.glossy_bounces = 3; scene.cycles.transmission_bounces = 4
scene.cycles.sample_clamp_indirect = 6
scene.render.resolution_x = 1280; scene.render.resolution_y = 720; scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'; scene.view_settings.exposure = 0.2
for look in ('AgX - Medium High Contrast', 'Medium High Contrast'):
    try: scene.view_settings.look = look; break
    except TypeError: pass
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
for img in bpy.data.images:
    if img.source == 'FILE' and not img.packed_file: img.pack()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'tc-office.blend'))
print('BUILD OK', OUT / 'tc-office.blend')

"""Fresh-import the game package, check pickable faces and photograph each family."""
import argparse
import json
import sys
from pathlib import Path
import bpy
from mathutils import Vector

p=argparse.ArgumentParser()
p.add_argument("glb",type=Path)
p.add_argument("evidence",type=Path)
a=p.parse_args(sys.argv[sys.argv.index("--")+1:])
a.evidence.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(a.glb))
scene=bpy.context.scene
roots={ob.name:ob for ob in scene.objects if not ob.parent}
assert len(roots)==25,sorted(roots)
assert all(ob.location.length<1e-6 for ob in roots.values())
assert len([ob for ob in scene.objects if ob.name.startswith("hoof_tile_")])==9
assert len([ob for ob in scene.objects if ob.name.startswith("hoof_cap_")])==4
assert len([name for name in roots if name.startswith("shop_item_")])==13
for name in ("hoof_submit","receipt_sheet","moved_cap","moved_price"):assert name in scene.objects,name
for image in bpy.data.images:
    if image.type=="IMAGE":assert image.size[0]>0 and image.packed_file,image.name

def corners(root):
    return [ob.matrix_world @ Vector(c) for ob in root.children_recursive if ob.type=="MESH" for c in ob.bound_box]

def limits(pts):
    return [[round(f(v[i] for v in pts),4) for i in range(3)] for f in (min,max)]

def tris(ob):
    return sum(len(face.vertices)-2 for face in ob.data.polygons) if ob.type=="MESH" else 0

report={root.name:{"boundsBlender":limits(corners(root)),"triangles":sum(tris(child) for child in root.children_recursive)} for root in roots.values()}
(a.evidence/"inspection.json").write_text(json.dumps(report,indent=2))

scene.render.engine="CYCLES"
scene.cycles.samples=5
scene.cycles.use_denoising=True
scene.cycles.max_bounces=4
scene.render.threads_mode="FIXED"
scene.render.threads=6
scene.render.resolution_x=1000
scene.render.resolution_y=650
scene.render.resolution_percentage=100
scene.view_settings.view_transform="AgX"
scene.world=bpy.data.worlds.new("Gallery daylight")
scene.world.use_nodes=True
scene.world.node_tree.nodes.get("Background").inputs["Color"].default_value=(.65,.73,.77,1)
scene.world.node_tree.nodes.get("Background").inputs["Strength"].default_value=.65
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.003))
floor=bpy.context.object
floor.name="Inspection bench"
m=bpy.data.materials.new("Mineral bench")
m.diffuse_color=(.28,.33,.31,1)
floor.data.materials.append(m)
camera=bpy.data.cameras.new("Collection camera")
camera.type="ORTHO"
cam=bpy.data.objects.new("Collection camera",camera)
scene.collection.objects.link(cam)
scene.camera=cam

def lamp(name,loc,energy,color,size):
    data=bpy.data.lights.new(name,"AREA")
    data.energy=energy;data.color=color;data.shape="DISK";data.size=size
    obj=bpy.data.objects.new(name,data)
    scene.collection.objects.link(obj)
    obj.location=loc;obj.rotation_euler=(-Vector(loc)).to_track_quat("-Z","Y").to_euler()
lamp("Window",(-3.4,-4.7,6.4),610,(1,.94,.83),5)
lamp("Silver fill",(3.5,-1.0,3.7),340,(.79,.89,1),4)
lamp("Crown",(1,3.5,5.7),650,(1,.93,.77),4)

def photograph(name, placements, direction=(.24,-1,.46), pad=1.14):
    for root in roots.values():
        for child in root.children_recursive:child.hide_render=True
    pts=[]
    for item,x,y,z,scale in placements:
        root=roots[item]
        root.location=(x,y,z)
        root.scale=(scale,scale,scale)
        for child in root.children_recursive:child.hide_render=False
        bpy.context.view_layer.update()
        pts.extend(corners(root))
    forward=-Vector(direction).normalized()
    q=forward.to_track_quat("-Z","Y")
    right=q@Vector((1,0,0));up=q@Vector((0,1,0))
    xs=[v.dot(right) for v in pts];ys=[v.dot(up) for v in pts];ds=[v.dot(forward) for v in pts]
    focus=(right*(min(xs)+max(xs))/2+up*(min(ys)+max(ys))/2+forward*(min(ds)+max(ds))/2)
    cam.location=focus-forward*7
    cam.rotation_euler=q.to_euler()
    camera.ortho_scale=max(max(xs)-min(xs),(max(ys)-min(ys))*scene.render.resolution_x/scene.render.resolution_y)*pad
    scene.render.filepath=str(a.evidence/(name+".jpg"))
    scene.render.image_settings.file_format="JPEG"
    scene.render.image_settings.quality=91
    bpy.ops.render.render(write_still=True)

photograph("accession",[("rope_post",-1.12,.10,0,1),("rope_span",-.12,.10,0,1),("rope_notice",.88,.10,0,1),("sealed_glass",-.50,-.84,0,1),("moved_button",.43,-.81,0,1)])
photograph("lab",[("hoof_panel",-.68,0,0,1),("hoof_pictures",.68,0,0,1)],(.11,-1,.20))
photograph("counter",[("renewal_door",1.35,.35,0,1),("shop_bay",-.92,.10,0,1),("shop_item_stayed",-1.91,.09,1.23,1.35),("shop_item_queued",-1.39,.09,1.23,1.25),("shop_item_blinked",-.80,.09,1.23,1.1),("shop_item_spat",-.2,.08,1.23,1.13),("shop_printer",-.5,-.02,.69,1),("shop_empty",-1.77,-.01,.69,1)])
photograph("settlement",[("shop_printer",0,0,0,1),("shop_yes",.38,-.01,0,1),("shop_empty",-.52,.05,0,1)],(.24,-1,.45))
items=[name for name in roots if name.startswith("shop_item_")]
items.sort()
for idx in range(0,len(items),4):
    photograph("stock-"+str(1+idx//4),[(item,(i-1.5)*.40,0,0,1) for i,item in enumerate(items[idx:idx+4])],(.13,-1,.33))

"""Fit the keeper chair's collision to the two surfaces in the original Blender room."""
import hashlib
import json
from pathlib import Path
import struct
import sys

zones = Path(sys.argv[1])
original = zones / "habitat-f7dc893cfcac2ed0-collision.glb"
data = original.read_bytes()
size = struct.unpack_from("<I", data, 12)[0]
gltf = json.loads(data[20:20+size])
chair_index = next(i for i, node in enumerate(gltf["nodes"]) if node["name"] == "Chair")
chair = gltf["nodes"][chair_index]
mesh = gltf["meshes"][chair["mesh"]]
accessor = gltf["accessors"][mesh["primitives"][0]["attributes"]["POSITION"]]
lo, hi = accessor["min"], accessor["max"]

def fit(name, target_lo, target_hi):
    scale = [(b-a)/(y-x) for a,b,x,y in zip(target_lo,target_hi,lo,hi)]
    return {**chair, "name": name, "scale": scale,
            "translation": [a-x*s for a,x,s in zip(target_lo,lo,scale)]}

back_index = len(gltf["nodes"])
gltf["nodes"].append(fit("Habitat chair back", [19.405,.58,11.3525], [19.915,.97,11.4175]))
gltf["nodes"][chair_index] = fit("Habitat chair seat", [19.415,.01,11.355], [19.905,.51,11.845])
parents = [node["children"] for node in gltf["nodes"] if "children" in node]
parents += [scene["nodes"] for scene in gltf["scenes"]]
next(indices for indices in parents if chair_index in indices).append(back_index)
document = json.dumps(gltf, separators=(",",":")).encode()
document += b" " * (-len(document)%4)
tail = data[20+size:]
result = struct.pack("<4sII", b"glTF", 2, 20+len(document)+len(tail))
result += struct.pack("<I4s",len(document),b"JSON") + document + tail
dest = zones / ("habitat-chair-" + hashlib.sha256(result).hexdigest()[:16] + "-collision.glb")
dest.write_bytes(result)
print(dest.name)

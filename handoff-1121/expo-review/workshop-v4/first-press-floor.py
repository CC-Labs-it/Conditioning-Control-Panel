"""Restore unobstructed terrazzo illumination under the moving First Press and its relocated sign."""
import hashlib
import io
import json
from pathlib import Path

import numpy as np
from PIL import Image


zones = Path(__file__).resolve().parents[3] / "ConditioningControlPanel/Resources/web/fuzz/assets/expo/zones"
index = zones / "zones.json"
text = index.read_text(encoding="utf-8")
plan = json.loads(text)
hall = next(zone for zone in plan["zones"] if zone["id"] == "hall")
if hall["assetStem"] != "hall-ded2f60ac02e9836":
    raise ValueError("Measure the central floor UVs for the new Hall bake before repairing it.")

# Normalized UV1 corners of the Grand Hall's central terrazzo slab, measured on the
# shipped top triangles. Blender's +Y is the game's -Z. Both lightmaps use this atlas.
def floor_xy(u, v):
    x = -2.219794 + (u - .20401312) * 4.439588 / (.23546197 - .20401312)
    z = -16.000488 + (v - .74709697) * 32.000976 / (.97222858 - .74709697)
    return x, z


def clean_floor(image):
    original = np.asarray(image.convert("RGB"))
    h, w, _ = original.shape
    # Only this part of the floor UV island contains the two baked footprints.
    xa, xb = int(.2041*w), int(.2354*w)+1
    ya, yb = int(.7800*h), int(.815*h)+1
    yy, xx = np.mgrid[ya:yb, xa:xb]
    x, z = floor_xy((xx+.5)/w, (yy+.5)/h)
    raw = original[ya:yb,xa:xb].astype(float)
    dz = z+9
    radius = np.hypot(x,dz)
    sign = np.maximum(np.abs(x-1.82)-.245, np.abs(z+8.67)-.195)
    stop = np.hypot(x+1.55,z+8.96)
    # Fit the local diffuse lighting gradient to unobstructed floor around the lift.
    # Omit the separate stop pedestal, rope feet and the opposite side of the backdrop.
    sample = (radius>1.51)&(radius<2.08)&(sign>.19)&(stop>.47)&(abs(x)<2.12)&(dz> -1.8)
    for sx,sz in ((-1.86,-8),(1.86,-8),(-2.19,-9.4),(2.19,-9.4),(-1.38,-9.93)):
        sample &= np.hypot(x-sx,z-sz)>.32
    field = np.stack([np.ones_like(x),x,dz,x*x,x*dz,dz*dz],axis=2)
    for _ in range(3):
        coef = np.linalg.lstsq(field[sample],raw[sample],rcond=None)[0]
        prediction = field@coef
        sample &= np.linalg.norm(raw-prediction,axis=2)<30
    weight = np.maximum(np.clip((1.48-radius)/.19,0,1),np.clip((.15-sign)/.11,0,1))
    weight *= np.clip((stop-.27)/.12,0,1)
    weight *= abs(x)<2.17
    weight = weight*weight*(3-2*weight)
    cleaned = original.copy()
    cleaned[ya:yb,xa:xb] = np.rint(raw+(prediction-raw)*weight[:,:,None]).clip(0,255).astype(np.uint8)
    return Image.fromarray(cleaned)


for key, suffix in (("lightFile","-light.webp"),("mobileLightFile","-light-mobile.webp")):
    source = zones / (hall["assetStem"]+suffix)
    with Image.open(source) as image:
        result = clean_floor(image)
    buffer = io.BytesIO()
    # Match the original room-transfer budget at each resolution.
    result.save(buffer, "WEBP", quality=94 if key=="lightFile" else 89, method=6)
    data = buffer.getvalue()
    name = "hall-press-floor-"+hashlib.sha256(data).hexdigest()[:16]+suffix
    target = zones/name
    if target.exists():
        assert target.read_bytes()==data
    else:
        target.write_bytes(data)
    if key in hall:
        text=text.replace(json.dumps(hall[key]),json.dumps(name),1)
    else:
        anchor='"assetStem": '+json.dumps(hall["assetStem"])+','
        assert text.count(anchor)==1
        text=text.replace(anchor,anchor+'\n      '+json.dumps(key)+': '+json.dumps(name)+',',1)
    print(name,len(data))
index.write_text(text,encoding="utf-8")

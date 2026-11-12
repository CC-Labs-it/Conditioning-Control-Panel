"""Bake the shared mascot poster and hover sheet from EMI's shipped sprites and neutral face."""
import json
import sys
from pathlib import Path

from PIL import Image

assets = Path(__file__).resolve().parents[1] / "assets"
output = Path(sys.argv[1])
source = assets / "img" / "emi"
face = Image.open(source / "face-neutral.png").convert("RGBA")
size = 128
# Keep the existing atlas layout compatible with saved and already-loaded shop views.
width, height, columns, frames = 142, 140, 8, 54
sheet = Image.new("RGBA", (columns * width, ((frames + columns - 1) // columns) * height))
poses = ["body-idle", "body-sway1", "body-sway2", "body-sway3", "body-sway4", "body-idle"]
posters = []
for name in poses:
    body = Image.open(source / (name + ".png")).convert("RGBA")
    glass = face.resize((round(body.width * .4168), round(body.height * .3763)), Image.Resampling.NEAREST)
    body.alpha_composite(glass, (round(body.width * .3446), round(body.height * .2946)))
    cell = Image.new("RGBA", (width, height))
    cell.alpha_composite(body.resize((size, size), Image.Resampling.LANCZOS), (13, 11))
    posters.append(cell)

for index in range(frames):
    sheet.paste(posters[index * len(poses) // frames], (index % columns * width, index // columns * height))
output.mkdir(parents=True, exist_ok=True)
posters[0].save(output / "mascot.webp", lossless=True, method=4)
sheet.save(output / "mascot.motion.webp", lossless=True, method=4)
print(json.dumps(dict(ms=1800, frames=frames, columns=columns, w=width, h=height, x=-13, y=-11)))

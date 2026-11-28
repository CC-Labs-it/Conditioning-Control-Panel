"""dev/llama-art.py - converts the generated named-llama pictures (handoff-1121/llamas/out/, main checkout) into the
game's webp assets. Re-runnable: run it again whenever the source set is regenerated.

    python dev/llama-art.py [<source dir>] [<roster.json>]

Writes, relative to the fuzz root:
    assets/llamas/<id>.webp            portraits, 384 x 384
    assets/fate/llama/panel.webp       the panel scene, kept at its own size
    assets/fate/llama/chair-chew.webp  640 x 794 (kept aspect)
    assets/fate/llama/chair-spit.webp  640 x 794
    assets/stickers/e_ll_end.webp      Expo 2037 souvenir seal, 512 x 512, transparent outside
    assets/adcard/chair.webp           Sir Reginald, round, 160 x 160, transparent outside
and prints the bytes of each and the total. Needs Pillow.
"""
import json, os, sys
from PIL import Image, ImageDraw
from expo_sticker import souvenir

FUZZ = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
MAIN = 'C:/Projects/Conditioning-Control-Panel---CSharp-WPF/handoff-1121/llamas'
SRC = sys.argv[1] if len(sys.argv) > 1 else f'{MAIN}/out'
ROSTER = sys.argv[2] if len(sys.argv) > 2 else f'{MAIN}/roster.json'
Q = 74   # the wild set is busy (grass, rock, sky): 82 doubled the weight for no visible gain at game size
out = []


def save(im, rel):
    p = os.path.join(FUZZ, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    im.save(p, 'WEBP', quality=Q, method=6)
    out.append((rel, os.path.getsize(p)))


def fit(im, w, h):
    """Cover-crop to w x h, centred."""
    s = max(w / im.width, h / im.height)
    r = im.resize((max(w, round(im.width * s)), max(h, round(im.height * s))), Image.LANCZOS)
    x, y = (r.width - w) // 2, (r.height - h) // 2
    return r.crop((x, y, x + w, y + h))


def round_crop(src, size, border, box, ss=4):
    """A square crop (box in source pixels), round; a white die-cut border when border > 0."""
    face = src.crop(box)
    S, b = size * ss, border * ss
    canvas = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    disc = Image.new('L', (S, S), 0)
    ImageDraw.Draw(disc).ellipse((ss, ss, S - ss - 1, S - ss - 1), fill=255)
    if b:
        canvas.paste(Image.new('RGBA', (S, S), (255, 255, 255, 255)), (0, 0), disc)
        inner = Image.new('L', (S, S), 0)
        ImageDraw.Draw(inner).ellipse((b, b, S - b - 1, S - b - 1), fill=255)
        layer = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        layer.paste(face.resize((S - 2 * b, S - 2 * b), Image.LANCZOS), (b, b))
        canvas.paste(layer, (0, 0), inner)
    else:
        canvas.paste(face.resize((S, S), Image.LANCZOS).convert('RGBA'), (0, 0), disc)
    return canvas.resize((size, size), Image.LANCZOS)


roster = json.load(open(ROSTER, encoding='utf-8'))
for r in roster:
    save(fit(Image.open(f"{SRC}/{r['id']}.png").convert('RGB'), 384, 384), f"assets/llamas/{r['id']}.webp")
save(Image.open(f'{SRC}/panel.png').convert('RGB'), 'assets/fate/llama/panel.webp')
for n in ('chair-chew', 'chair-spit'):
    save(fit(Image.open(f'{SRC}/{n}.png').convert('RGB'), 640, 794), f'assets/fate/llama/{n}.webp')
chair = Image.open(f'{SRC}/reginald.png').convert('RGB')
# Square boxes around Sir Reginald's head in the 1024 portrait (the wild set: head top left, monocle); re-pick them if
# the portrait is regenerated with a different pose.
CHAIR_BOX = (300, 70, 760, 530)
save(souvenir(chair), 'assets/stickers/e_ll_end.webp')
save(round_crop(chair, 160, 0, CHAIR_BOX), 'assets/adcard/chair.webp')

total = 0
for rel, n in out:
    print(f'{n:8d}  {rel}')
    total += n
print(f'{total:8d}  total')

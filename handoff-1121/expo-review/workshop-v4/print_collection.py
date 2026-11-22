"""Prepare the printed faces for the Expo shop and teaching collection."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageOps


def build(workshop, output):
    size = 2048
    atlas = Image.new("RGB", (size, size), "#ece7cf")
    rects = {}
    x = y = row_height = 0

    def tile(name, im):
        nonlocal x, y, row_height
        w, h = im.size
        if x + w + 4 > size:
            x, y, row_height = 0, y + row_height + 4, 0
        if y + h + 4 > size:
            raise ValueError("Print sheet full")
        x0, y0 = x + 2, y + 2
        atlas.paste(im, (x0, y0))
        atlas.paste(im.crop((0, 0, 1, h)).resize((2, h)), (x, y0))
        atlas.paste(im.crop((w-1, 0, w, h)).resize((2, h)), (x0+w, y0))
        atlas.paste(im.crop((0, 0, w, 1)).resize((w, 2)), (x0, y))
        atlas.paste(im.crop((0, h-1, w, h)).resize((w, 2)), (x0, y0+h))
        rects[name] = [(x0+.5)/size, 1-(y0+h-.5)/size, (x0+w-.5)/size, 1-(y0+.5)/size]
        x += w+4
        row_height = max(row_height, h)

    fonts = Path("C:/Windows/Fonts")
    def face(text, family, points):
        return ImageFont.truetype(str(fonts/family), points)

    def label(name, title, sub="", scheme="paper", shape=(400, 134)):
        bg, fg, line = {
            "paper": ("#f7ebce", "#183d37", "#ac763e"), "green": ("#133d37", "#f8eed7", "#cfa866"),
            "red": ("#9c392e", "#fff4df", "#e6b87d"), "blue": ("#143f52", "#f4ecda", "#aaca82")
        }[scheme]
        w, h = shape
        im = Image.new("RGB", shape, bg)
        d = ImageDraw.Draw(im)
        d.rounded_rectangle((5,5,w-6,h-6), radius=8, outline=line, width=2)
        font_size = int(h*(.43 if sub else .58))
        f = face(title, "georgiab.ttf", font_size)
        while d.textlength(title, font=f) > w-32:
            font_size -= 1
            f = face(title, "georgiab.ttf", font_size)
        d.text((w/2,h*(.41 if sub else .49)), title, font=f, anchor="mm", fill=fg)
        if sub:
            sub_size=min(25, int(h*.19))
            f=face(sub,"arialbd.ttf",sub_size)
            while d.textlength(sub,font=f)>w-25:
                sub_size-=1
                f=face(sub,"arialbd.ttf",sub_size)
            d.text((w/2,h*.76),sub,font=f,anchor="mm",fill=fg)
        tile(name, im)

    label("notice", "Please leave the rope here.", "A NOTICE ABOUT THE PREVIOUS NOTICE", "green", (530,180))
    label("agree", "I AGREE", "", "red")
    label("door", "I AGREE", "RENEWAL OFFICE", "green", (610,218))
    label("stamp", "Seal intact", "COLLECTION OFFICE", "paper", (310,112))
    label("ticket", "ADMIT ONE", "EXPO 2037", "green", (330,145))
    label("price", "1 Yes", "INCLUDING TAX", "paper", (188,108))
    label("lab", "Contact plate", "APPLIED HOOF SCIENCES", "blue", (380,94))
    label("mosaic", "Picture test", "APPLIED HOOF SCIENCES", "blue", (380,94))
    label("submit", "Submit", "", "green", (180,90))
    label("blank", "Out of stock.", "PERHAPS MORE VISITING WOULD HELP.", "paper", (400,115))
    label("yes", "Yes", "GIFT SHOP", "green", (220,100))
    label("heater", "Greg", "DESK HEATER", "blue", (230,110))
    label("parcel", "Reading balcony", "TEA SERVICE", "paper", (330,140))
    label("minutes", "Minutes", "UNANIMOUS", "green", (250,180))
    label("feed", "Yes", "FEED IS INCLUDED", "paper", (215,120))
    label("rosette", "Rematch", "GENEVA", "green", (210,150))
    label("box", "Habitat", "KEEPER OFFICE", "blue", (220,120))
    label("folio", "The Update", "READER'S COPY", "green", (250,155))
    label("print", "Settlement", "VISITOR COPY", "green", (315,96))
    for idx, name in enumerate(("nana", "bob", "margaret", "bartholomew", "greg", "doctor", "lashes", "gary", "reginald")):
        im = Image.open(workshop/"artwork"/("portrait_"+name+".webp")).convert("RGB")
        tile("tile"+str(idx),ImageOps.fit(im, (184,184), method=Image.Resampling.LANCZOS, centering=(.5,.36)))
    receipt = Image.new("RGB",(200,432),"#fff8e3")
    d=ImageDraw.Draw(receipt)
    d.text((100,18),"EXPO 2037",font=face("","georgiab.ttf",22),anchor="mt",fill="#173e36")
    d.text((100,55),"VISITOR SERVICES",font=face("","arialbd.ttf",13),anchor="mt",fill="#173e36")
    for i, line in enumerate(("One souvenir","Bag","Rounding","Instructions kept","Deviations","Credit applied")):
        yy=108+i*32
        d.text((10,yy),line,font=face("","arial.ttf",14),fill="#343f39")
        d.line((143, yy+11, 190, yy+11), fill="#8e907f")
    d.line((10,310,190,310),fill="#365345",width=2)
    d.text((100,330),"AGREED",font=face("","georgiab.ttf",26),anchor="mt",fill="#245849")
    for i in range(48):
        if (23*i)%31 > 12: d.rectangle((22+i*3,384,23+i*3,412),fill="#2c443a")
    tile("receipt",receipt)
    output.mkdir(parents=True,exist_ok=True)
    atlas.save(output/"collection-print.jpg", quality=92, subsampling=0, optimize=True)
    (output/"print-uv.json").write_text(json.dumps(rects),encoding="utf-8")
    # Small tiled surface samples are from the same workshop maps as the room shells.
    for name in ("brass","lagoon","oak","collection_felt","action_coral","alloy","cloth","marble"):
        Image.open(workshop/"materials"/name/"base.webp").convert("RGB").resize((192,192),Image.Resampling.LANCZOS).save(output/(name+".jpg"),quality=87,optimize=True)


if __name__=="__main__":
    p=argparse.ArgumentParser()
    p.add_argument("workshop",type=Path)
    p.add_argument("output",type=Path)
    a=p.parse_args()
    build(a.workshop,a.output)

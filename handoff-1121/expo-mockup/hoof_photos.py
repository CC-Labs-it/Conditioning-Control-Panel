"""Four photographic contact sheets for the Applied Hoof Sciences picture test."""
import argparse
from pathlib import Path
import sys


PIPELINE=Path("C:/Projects/Conditioning-Control-Panel---CSharp-WPF/handoff-1121/expo-mockup")
SHEETS=[
    ("field",[
        ("llama-chestnut","A long-necked chestnut llama with unmistakably long banana-shaped ears standing on all four legs in a green paddock, profile."),
        ("alpaca-cream","A soft cream huacaya alpaca in thick fleece, short straight spear-shaped ears and a compact fluffy head, standing on all four legs on a farm."),
        ("sheep","A white sheep with black face and legs standing on grass at a dry stone wall."),
        ("bicycle","A complete teal city bicycle leaning against an old red brick wall.")]),
    ("road",[
        ("llama-piebald","A piebald llama with a tall long neck, distinct long curved ears and two-tone brown and white coat, walking on all four legs in a vineyard."),
        ("camel","A sandy one-humped dromedary standing in profile on pale desert sand, with its single hump clearly outlined."),
        ("traffic-light","A complete three-lens traffic light mounted on a short pole at a city intersection, glowing green."),
        ("goat","A brown domestic goat with swept-back horns and a little beard standing on a stone step.")]),
    ("yard",[
        ("llama-black","A velvet black llama with silver-edged banana ears and a long upright neck, standing on four legs in snow, in profile."),
        ("watering-can","A well-used galvanized green watering can with an obvious long spout and round rose on a greenhouse bench."),
        ("pear","A single ripe red-and-green pear, curved stalk intact, sunlit on an orchard crate."),
        ("chair","One empty woven wicker armchair with curved arms in a sunlit tiled conservatory.")]),
    ("garden",[
        ("llama-gray","A dignified gray llama with a white face and long curved banana ears, resting naturally in kush position on grass, all four legs folded beneath its body."),
        ("alpaca-brown","A cinnamon-colored huacaya alpaca standing on all four legs with short straight pointed ears, compact neck, dense woolly round head and blunt muzzle."),
        ("postbox","One red cylindrical British-style pillar postbox with a clearly visible black mail slot at a street corner, no legible writing."),
        ("umbrella","A bright yellow open umbrella with its curved wooden handle visible, resting on a wet cobbled path.")])
]


def prompt(subjects):
    setup=("Four separate museum identification photographs in a precise two-by-two contact sheet. "
        "The four frames must meet on the exact horizontal and vertical midpoint. No borders or gaps. "
        "Each photograph has its own backdrop and one clear central subject, small margin, filling its frame. "
        "Keep every identifying feature inside the middle three quarters of each frame horizontally. "
        "Naturalistic late-2020s press photographs, true-to-life proportions and textures, soft daylight, "
        "subtly faded archive colour, fine film grain. Clear silhouettes readable at thumbnail size. "
        "No graphics, writing, labels, caption, watermark, logo, flags, people, or other animals and objects competing with the subject. "
        "Llamas and alpacas must have natural quadruped anatomy, and can only stand or walk on four legs or lie as they do in life. "
        "Never seated upright, never using their legs like hands. The llamas remain calm, dignified, entirely likable. ")
    return setup+" ".join(pos+": "+subject for pos,(_,subject) in zip(("TOP LEFT","TOP RIGHT","BOTTOM LEFT","BOTTOM RIGHT"),subjects))


if __name__=="__main__":
    cli=argparse.ArgumentParser()
    cli.add_argument("--dry",action="store_true")
    cli.add_argument("--limit",type=int,default=4)
    cli.add_argument("--out",type=Path,required=True)
    args=cli.parse_args()
    sheets=SHEETS[:args.limit]
    if args.dry:
        for name,subjects in sheets:
            print(name,":",", ".join(key for key,_ in subjects))
            print(prompt(subjects))
        raise SystemExit(0)
    args.out.mkdir(exist_ok=True,parents=True)
    sys.dont_write_bytecode=True
    sys.path.insert(0,str(PIPELINE))
    import gen_slides
    gen_slides.OUT=args.out
    from PIL import Image
    for name,subjects in sheets:
        source=args.out/(name+".jpg")
        if not source.is_file():
            gen_slides.gen(name,prompt(subjects),[])
        if not source.is_file():
            raise SystemExit("The generator did not return "+name)
        with Image.open(source) as sheet:
            w,h=sheet.size
            for i,(key,_) in enumerate(subjects):
                x=(i%2+.5)*w/2
                y=(i//2+.5)*h/2
                half=min(w,h)*.245
                tile=sheet.crop((int(x-half),int(y-half),int(x+half),int(y+half))).resize((320,320),Image.Resampling.LANCZOS)
                tile.save(args.out/(key+".webp"),quality=86,method=6)

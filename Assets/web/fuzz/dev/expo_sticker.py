"""Print the Expo's souvenir seal around Sir Reginald's approved portrait."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageOps

def souvenir(portrait):
    scale=2
    face=portrait.crop((200,40,880,720)) if portrait.width>=1024 else portrait
    im=Image.new('RGBA',(1024,1024),(0,0,0,0))
    d=ImageDraw.Draw(im)
    d.ellipse((12,12,1012,1012),fill='#fffdf3')
    d.ellipse((34,34,990,990),fill='#173f35',outline='#bc9b60',width=9)
    d.ellipse((68,68,956,956),outline='#cfbb81',width=4)
    photo=ImageOps.fit(face.convert('RGB'),(682,682),method=Image.Resampling.LANCZOS)
    circle=Image.new('L',photo.size,0)
    ImageDraw.Draw(circle).ellipse((0,0,681,681),fill=255)
    d.ellipse((160,161,864,865),fill='#cbad71')
    im.paste(photo,(171,172),circle)
    for x in (108,916):d.ellipse((x-12,500,x+12,524),fill='#cfbb81')
    def font(size,fallback):
        for file in fallback:
            try:return ImageFont.truetype(file,size)
            except OSError:continue
        return ImageFont.load_default(size=size)
    serif=font(94,['georgiab.ttf','DejaVuSerif-Bold.ttf'])
    sans=font(69,['bahnschrift.ttf','DejaVuSans-Bold.ttf'])
    d.rounded_rectangle((234,63,790,178),42,fill='#173f35')
    d.text((512,120),'LLAMA EXPO',font=sans,anchor='mm',fill='#f6ebce')
    d.rounded_rectangle((311,821,713,939),37,fill='#173f35')
    d.text((512,874),'2037',font=serif,anchor='mm',fill='#f6ebce')
    return im.resize((im.width//scale,im.height//scale),Image.Resampling.LANCZOS)

if __name__=='__main__':
    import argparse
    cli=argparse.ArgumentParser()
    cli.add_argument('portrait',type=Path);cli.add_argument('output',type=Path)
    args=cli.parse_args()
    with Image.open(args.portrait) as src:souvenir(src).save(args.output,quality=88,method=6)

"""Prepare the staff, gallery and wool archive print sheet."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageOps
from print_collection import build

p=argparse.ArgumentParser()
p.add_argument("workshop",type=Path)
p.add_argument("output",type=Path)
args=p.parse_args()
build(args.workshop,args.output)
sheet=Image.new("RGB",(2048,2048),"#e6dac0")
rects={}
cells=[
    ("heater","HEATING SERVICES","Do not unplug.","Sign one of three."),
    ("tea","STAFF CANTEEN","Reading balcony","Keep upright while delivering."),
    ("spare","EXHIBITION STORES","Spare visitor","Packed flat."),
    ("hardhat","MAINTENANCE","B.","Property of Bob."),
    ("secret","INSPECTION TAG","Nice try.","B."),
    ("agenda","FORUM","Agenda","Please stand here."),
    ("rota","STAFF CANTEEN","Tomorrow","Attendance has been arranged."),
    ("read","CONTRACT ARCHIVE","The reader","Pull the loose end to amend."),
    ("knit","NANA SPITWELL","Pattern in use","Leave the last row open."),
    ("trolley","READING BALCONY","Tea service","Place the tray here."),
    ("clause","THE UPDATE","Original clauses","A record of your agreements."),
    ("plate","FACILITIES","Spit plate","Llama-side access."),
    ("bob","HEALTH AND SAFETY","Notice","Please read the previous sign."),
    ("stapler","STATIONERY","Greg","Return to the staff desk."),
    ("scaffold","BUILDING SERVICES","Staff access","Hard hats must be worn."),
    ("keeper","HABITAT","Keeper entrance","The door stays open."),
    ("clock","HABITAT","I AGREE","Visitor desk."),
    ("docket","VISITOR SERVICES","Finger","Temporary appointment."),
]
def tile(name,im,i,w,h,across):
    x=i%across*w;y=i//across*h
    sheet.paste(im,(x,y))
    rects[name]=[(x+.5)/2048,1-(y+h-.5)/2048,(x+w-.5)/2048,1-(y+.5)/2048]
for idx,(name,header,title,sub) in enumerate(cells):
    im=Image.new("RGB",(510,190),"#123a38" if idx%3 else "#f1e5cb")
    fg="#f8ecd2" if idx%3 else "#16362e"
    dr=ImageDraw.Draw(im);dr.rounded_rectangle((6,6,503,183),10,outline="#c4a061",width=3)
    for txt,yy,face,size in ((header,34,"arialbd.ttf",22),(title,91,"georgiab.ttf",49),(sub,151,"arial.ttf",22)):
        f=ImageFont.truetype("C:/Windows/Fonts/"+face,size)
        while dr.textlength(txt,font=f)>470:size-=1;f=ImageFont.truetype("C:/Windows/Fonts/"+face,size)
        dr.text((255,yy),txt,font=f,fill=fg,anchor="mm")
    tile(name,im,idx,510,190,4)
for idx,key in enumerate(("flight","loch","summit","woods","moon")):
    im=Image.open(args.workshop/"artwork"/("slide_"+key+".webp")).convert("RGB")
    im=ImageOps.fit(im,(376,282),method=Image.Resampling.LANCZOS)
    x=idx*384+3;y=992
    sheet.paste(im,(x,y));rects[key]=[(x+.5)/2048,1-(y+281.5)/2048,(x+375.5)/2048,1-(y+.5)/2048]
sheet.save(args.output/"collection-print.jpg",quality=91,subsampling=0,optimize=True)
(args.output/"print-uv.json").write_text(json.dumps(rects))

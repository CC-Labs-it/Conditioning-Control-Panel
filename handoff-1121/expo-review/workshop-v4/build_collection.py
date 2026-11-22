"""Build the editable collection and game GLB. Coordinates here use Blender Z up."""
import argparse
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector

pi, sin, cos = math.pi, math.sin, math.cos
opts = argparse.ArgumentParser()
opts.add_argument("textures", type=Path)
opts.add_argument("blend", type=Path)
opts.add_argument("glb", type=Path)
args = opts.parse_args(sys.argv[sys.argv.index("--")+1:])
bpy.ops.wm.read_factory_settings(use_empty=True)
atlas = json.loads((args.textures/"print-uv.json").read_text())
materials = {}
roots = {}
current = None

def mat(name, rgb, rough=.49, metal=0., texture=None, alpha=1):
    m=bpy.data.materials.new(name)
    m.diffuse_color=(*rgb, alpha)
    m.use_nodes=True
    p=m.node_tree.nodes.get("Principled BSDF")
    for key,val in (("Base Color",(*rgb,1)), ("Roughness",rough),("Metallic",metal),("Alpha",alpha)):
        p.inputs[key].default_value=val
    if texture:
        i=m.node_tree.nodes.new("ShaderNodeTexImage")
        i.image=bpy.data.images.load(str(args.textures/(texture+".jpg")),check_existing=True)
        m.node_tree.links.new(i.outputs["Color"],p.inputs["Base Color"])
    if alpha<1:
        m.surface_render_method="BLENDED"
        m.use_transparency_overlap=False
    materials[name]=m

mat("Brass",(.58,.35,.12),.31,.63,"brass")
mat("Enamel",(.06,.22,.19),.35,.22,"lagoon")
mat("Honey oak",(.36,.18,.06),.49,.03,"oak")
mat("Green felt",(.12,.28,.19),.81,0,"collection_felt")
mat("Persimmon",(.79,.18,.09),.32,.13,"action_coral")
mat("Silver",(.55,.61,.60),.29,.65,"alloy")
mat("Linen",(.74,.61,.37),.86,0,"cloth")
mat("Stone",(.65,.67,.61),.44,.06,"marble")
mat("Ink",(.016,.036,.036),.43,.13)
mat("Cream",(.82,.76,.60),.57)
mat("Mint",(.48,.68,.46),.42,.08)
mat("Cord",(.30,.046,.043),.73)
mat("Cordon braid",(.38,.085,.060),.73)
mat("Madder",(.50,.065,.10),.60)
mat("Vellum",(.95,.87,.67),.81)
mat("Glass",(.56,.79,.77),.13,.09,alpha=.24)
mat("Print",(.83,.77,.61),.64,texture="collection-print")

def group(name):
    global current
    current=bpy.data.objects.new(name,None)
    bpy.context.scene.collection.objects.link(current)
    current.empty_display_size=.035
    roots[name]=current
    return current

class Part:
    def __init__(self,name=None):
        self.name=name or current.name+"_body"
        self.vertices=[]
        self.faces=[]
        self.parent=current
    def vertex(self,p):
        self.vertices.append(tuple(p))
        return len(self.vertices)-1
    def face(self,inds,m,uv=None,smooth=False):
        if uv is None:
            p=[Vector(self.vertices[i]) for i in inds]
            n=(p[1]-p[0]).cross(p[2]-p[0])
            axis=max(range(3),key=lambda i: abs(n[i]))
            a,b=((1,2),(0,2),(0,1))[axis]
            uv=[(v[a]*2.8,v[b]*2.8) for v in p]
        self.faces.append((tuple(inds),m,uv,smooth))
    def poly(self,points,m,uv=None,smooth=False):
        self.face([self.vertex(p) for p in points],m,uv,smooth)
    def finish(self):
        m=bpy.data.meshes.new(self.name)
        m.from_pydata(self.vertices,[],[f[0] for f in self.faces])
        m.update()
        slots=list(dict.fromkeys(f[1] for f in self.faces))
        for key in slots:m.materials.append(materials[key])
        uv=m.uv_layers.new()
        for face,(_,key,coords,smooth) in zip(m.polygons,self.faces):
            face.material_index=slots.index(key)
            face.use_smooth=smooth
            for i,point in zip(face.loop_indices,coords):uv.data[i].uv=point
        ob=bpy.data.objects.new(self.name,m)
        bpy.context.scene.collection.objects.link(ob)
        ob.parent=self.parent
        return ob

def rings(part, loops, material, caps=True, smooth=False):
    ids=[[part.vertex(v) for v in loop] for loop in loops]
    if caps:part.face(list(reversed(ids[0])),material)
    for a,b in zip(ids,ids[1:]):
        for j in range(len(a)):
            k=(j+1)%len(a)
            part.face((a[j],a[k],b[k],b[j]),material,smooth=smooth)
    if caps:part.face(ids[-1],material)

def box(p,m,center,size,r=.012,front=False):
    x,y,z=center
    w,d,h=size
    if front:d,h=h,d
    r=min(r,w*.47,d*.47)
    bevel=min(r*.45,h*.21,.012)
    def xyz(u,v,a):return (x+u,y-a,z+v) if front else (x+u,y+v,z+a)
    loops=[]
    for zz,inset in ((-h/2,bevel),(-h/2+bevel,0),(h/2-bevel,0),(h/2,bevel)):
        loop=[]
        for corner,(sx,sy,start) in enumerate(((1,1,0),(-1,1,90),(-1,-1,180),(1,-1,270))):
            for k in range(4):
                a=math.radians(start+k*30)
                loop.append(xyz(sx*(w*.5-r)+max(.0001,r-inset)*cos(a),sy*(d*.5-r)+max(.0001,r-inset)*sin(a),zz))
        loops.append(loop)
    rings(p,loops,m)

def cushion(p,m,center,size):
    # Soft tailoring gives a pinched seam and a domed padded face on the heater.
    x,y,z=center
    w,d,h=size
    loops=[]
    def signed(t,power):return math.copysign(abs(t)**power,t)
    for i in range(1,16):
        b=-pi/2+i*pi/16
        ring=[]
        for j in range(48):
            t=j*2*pi/48
            edge=(1-.021*math.exp(-((b+.25)/.15)**2))
            ring.append((x+w/2*signed(cos(b),.34)*signed(cos(t),.33)*edge,
                         y+d/2*signed(sin(b),.38),
                         z+h/2*signed(cos(b),.34)*signed(sin(t),.33)*edge))
        loops.append(list(reversed(ring)))
    rings(p,loops,m,caps=False,smooth=True)
    for j in range(48):
        k=(j+1)%48
        p.poly([(x,y-d/2,z),loops[0][k],loops[0][j]],m,smooth=True)
        p.poly([loops[-1][j],loops[-1][k],(x,y+d/2,z)],m,smooth=True)

def lathe(p,m,center,profile,n=32):
    x,y,z=center
    loops=[]
    for radius,h in profile:
        if radius==0:loops.append([p.vertex((x,y,z+h))])
        else:loops.append([p.vertex((x+radius*cos(i*2*pi/n),y+radius*sin(i*2*pi/n),z+h)) for i in range(n)])
    for a,b in zip(loops,loops[1:]):
        for j in range(n):
            k=(j+1)%n
            if len(a)==1:inds=(a[0],b[k],b[j])
            elif len(b)==1:inds=(a[j],a[k],b[0])
            else:inds=(a[j],a[k],b[k],b[j])
            p.face(inds,m,smooth=True)

def tube(p,m,points,r,n=8):
    v=[Vector(q) for q in points]
    loops=[]
    # Use a fixed side axis along this short curve to avoid sudden ring twists.
    tangent=(v[-1]-v[0]).normalized()
    ref=Vector((0,1,0)) if abs(tangent.y)<.85 else Vector((1,0,0))
    for j,q in enumerate(v):
        t=(v[min(j+1,len(v)-1)]-v[max(j-1,0)]).normalized()
        u=t.cross(ref).normalized()
        if u.length<.01:u=t.cross(Vector((0,0,1))).normalized()
        vv=t.cross(u)
        loops.append([q+r*(u*cos(i*2*pi/n)+vv*sin(i*2*pi/n)) for i in range(n)])
    rings(p,loops,m,smooth=True)

def oval(p,m,center,radii,n=24,rows=10):
    x,y,z=center
    rx,ry,rz=radii
    loops=[]
    for j in range(1,rows):
        a=-pi/2+j*pi/rows
        loops.append([(x+rx*cos(a)*cos(i*2*pi/n),y+ry*cos(a)*sin(i*2*pi/n),z+rz*sin(a)) for i in range(n)])
    rings(p,loops,m,caps=False,smooth=True)
    for j in range(n):
        k=(j+1)%n
        p.poly([(x,y,z-rz),loops[0][k],loops[0][j]],m,smooth=True)
        p.poly([loops[-1][j],loops[-1][k],(x,y,z+rz)],m,smooth=True)

def print_face(p,name,center,width,height,top=False,back=False):
    x,y,z=center
    u0,v0,u1,v1=atlas[name]
    if top:corners=[(x-width/2,y-height/2,z),(x+width/2,y-height/2,z),(x+width/2,y+height/2,z),(x-width/2,y+height/2,z)]
    else:
        s=-1 if back else 1
        corners=[(x-s*width/2,y,z-height/2),(x+s*width/2,y,z-height/2),(x+s*width/2,y,z+height/2),(x-s*width/2,y,z+height/2)]
    p.poly(corners,"Print",[(u0,v0),(u1,v0),(u1,v1),(u0,v1)])

def stanchion(p,x=0,y=0,s=1):
    def turn(m,prof):
        lathe(p,m,(x,y,0),[(r*s,h*s) for r,h in prof],20)
    turn("Brass",[(0,0),(.157,0),(.168,.014),(.168,.028),(.153,.044),(.114,.059),(.090,.071),(.052,.096),(.045,.115)])
    turn("Enamel",[(.045,.103),(.046,.124),(.038,.143),(.030,.17),(.026,.595),(.033,.64)])
    turn("Brass",[(.047,.098),(.051,.111),(.049,.124),(.038,.13)])
    turn("Brass",[(.033,.60),(.036,.609),(.036,.632),(.046,.65),(.048,.674),(.044,.695),(.043,.740),(.033,.761)])
    turn("Honey oak",[(.032,.735),(.049,.749),(.061,.773),(.063,.795),(.054,.818),(.033,.836),(0,.843)])
    for sign in (-1,1):
        tube(p,"Brass",[(x+sign*a*s,y,b*s) for a,b in ((.037,.705),(.059,.718),(.070,.708),(.070,.695),(.056,.688),(.039,.699))],.005*s)

def press_build():
    group("rope_post")
    p=Part();stanchion(p);p.finish()
    group("rope_span")
    p=Part()
    points=[(-.935+i*1.87/32,0,.72-.15*sin(pi*i/32)) for i in range(33)]
    tube(p,"Cord",points,.025,12)
    for phase in (0,pi):
        trace=[(-.935+i*1.87/80,.021*cos(i*pi*.19+phase),.72-.15*sin(pi*i/80)+.021*sin(i*pi*.19+phase)) for i in range(81)]
        tube(p,"Cordon braid",trace,.0045,4)
    for s in (-1,1):
        tube(p,"Brass",[(s*.933,0,.72),(s*.964,0,.726),(s*.975,0,.715),(s*.973,0,.70),(s*.958,0,.697)],.008)
    p.finish()
    group("rope_notice")
    p=Part()
    stanchion(p)
    tube(p,"Brass",[(0,0,.75),(0,0,1.11)],.019)
    box(p,"Brass",(0,0,1.12),(.71,.038,.258),.027,True)
    print_face(p,"notice",(0,-.020,1.12),.68,.230)
    print_face(p,"notice",(0,.020,1.12),.68,.230,back=True)
    p.finish()
    group("moved_button")
    p=Part()
    lathe(p,"Ink",(0,0,0),[(0,0),(.245,0),(.258,.014),(.258,.033),(.242,.046)],48)
    lathe(p,"Enamel",(0,0,0),[(.242,.034),(.248,.052),(.234,.075),(.201,.087),(.200,.117)],48)
    lathe(p,"Brass",(0,0,0),[(.205,.081),(.216,.093),(.215,.11),(.199,.12),(.173,.121)],48)
    p.finish()
    cap=Part("moved_cap")
    lathe(cap,"Persimmon",(0,0,0),[(0,.109),(.163,.109),(.179,.12),(.178,.142),(.168,.159),(.147,.164),(0,.164)],48)
    print_face(cap,"agree",(0,0,.165),.23,.077,top=True)
    cap.finish()
    p=Part("moved_price")
    tube(p,"Linen",[(.17,-.09,.12),(.21,-.12,.085),(.248,-.144,.06)],.0035)
    box(p,"Vellum",(.277,-.160,.04),(.133,.013,.072),.007,True)
    print_face(p,"price",(.277,-.167,.04),.126,.065)
    p.finish()
    group("sealed_glass")
    p=Part()
    lathe(p,"Brass",(0,0,0),[(.505,0),(.539,.008),(.543,.024),(.535,.034),(.506,.034)],48)
    lathe(p,"Glass",(0,0,0),[(.520,.027),(.520,.12),(.515,.22),(.491,.35),(.441,.45),(.354,.54),(.236,.606),(.118,.641),(0,.651)],64)
    lathe(p,"Brass",(0,0,0),[(.056,.640),(.060,.650),(.052,.67),(.026,.678),(0,.678)],36)
    for x in (-.18,.18):
        tube(p,"Brass",[(x,-.47,.028),(x,-.489,.072),(x,-.499,.135)],.009)
    box(p,"Brass",(0,-.503,.132),(.438,.022,.123),.021,True)
    print_face(p,"stamp",(0,-.515,.132),.42,.104)
    p.finish()

def lab_build(pictures=False):
    group("hoof_pictures" if pictures else "hoof_panel")
    p=Part()
    for x in (-.445,.445):
        box(p,"Ink",(x,.015,.017),(.215,.365,.034),.021)
        box(p,"Brass",(x,.025,.115),(.086,.085,.180),.018)
    box(p,"Enamel",(0,.006,.396),(1.22,.136,.71),.063,True)
    box(p,"Brass",(0,-.065,.396),(1.19,.018,.672),.044,True)
    box(p,"Ink",(0,-.078,.396),(1.159,.019,.646),.036,True)
    if pictures:print_face(p,"mosaic",(.358,-.090,.623),.331,.083)
    else:print_face(p,"lab",(0,-.089,.663),.55,.082)
    for x in (-.551,.551):
        for z in (.118,.68):
            oval(p,"Silver",(x,-.091,z),(.012,.006,.012),12,6)
            tube(p,"Ink",[(x-.005,-.097,z+.002),(x+.005,-.097,z-.002)],.0012,4)
    p.finish()
    if pictures:
        for i in range(9):
            x=(i%3-1)*.20-.11
            z=.37+(1-i//3)*.19
            p=Part("hoof_tile_"+str(i))
            box(p,"Brass",(x,-.099,z),(.186,.023,.185),.010,True)
            box(p,"Cream",(x,-.112,z),(.177,.013,.177),.006,True)
            print_face(p,"tile"+str(i),(x,-.120,z),.164,.164)
            p.finish()
        p=Part("hoof_submit")
        box(p,"Brass",(.39,-.095,.37),(.234,.020,.254),.03,True)
        box(p,"Enamel",(.39,-.109,.37),(.215,.022,.23),.028,True)
        print_face(p,"submit",(.39,-.122,.37),.190,.095)
        p.finish()
    else:
        for i in range(4):
            x=(i%2-.5)*.30
            z=.38+(.5-i//2)*.24
            p=Part("hoof_cap_"+str(i))
            box(p,"Brass",(x,-.095,z),(.259,.016,.201),.021,True)
            box(p,"Cream",(x,-.108,z),(.240,.024,.182),.020,True)
            box(p,"Enamel",(x,-.121,z),(.115,.005,.101),.011,True)
            box(p,"Mint",(x,-.125,z),(.091,.004,.077),.008,True)
            p.finish()

def bay_build():
    group("shop_bay")
    p=Part()
    for x in (-1.43,1.43):
        for y in (-.327,.327):
            lathe(p,"Brass",(x,y,0),[(0,0),(.056,0),(.06,.013),(.046,.049),(.037,.063),(.026,.085),(.026,1.579),(.034,1.591),(.038,1.616),(.026,1.633),(0,1.635)],16)
    for level in (.19,.69,1.23):
        box(p,"Enamel",(0,0,level-.058),(3.00,.765,.077),.032)
        box(p,"Honey oak",(0,0,level-.017),(3.02,.78,.034),.026)
        for s in (-1,1):
            box(p,"Brass",(0,s*.380,level-.050),(2.99,.018,.027),.007)
        for x in (-1.4,0,1.4):
            box(p,"Brass",(x,0,level-.101),(.049,.70,.053),.009)
    for y in (-.327,.327):
        tube(p,"Brass",[(-1.43,y,1.571),(1.43,y,1.571)],.019,12)
    p.finish()
    group("shop_empty")
    p=Part()
    box(p,"Enamel",(0,0,.02),(.42,.205,.040),.022)
    box(p,"Brass",(0,0,.143),(.43,.035,.211),.018,True)
    print_face(p,"blank",(0,-.019,.143),.408,.118)
    print_face(p,"blank",(0,.019,.143),.408,.118,back=True)
    p.finish()
    group("shop_yes")
    p=Part()
    box(p,"Brass",(0,.015,.010),(.202,.070,.020),.008)
    box(p,"Brass",(0,0,.066),(.21,.020,.111),.008,True)
    print_face(p,"yes",(0,-.011,.066),.196,.089)
    print_face(p,"yes",(0,.011,.066),.196,.089,back=True)
    p.finish()

def printer_build():
    group("shop_printer")
    p=Part()
    box(p,"Ink",(0,0,.027),(.465,.38,.054),.037)
    box(p,"Enamel",(0,0,.142),(.47,.38,.237),.048)
    box(p,"Brass",(0,-.015,.243),(.451,.376,.029),.040)
    box(p,"Cream",(0,.014,.257),(.426,.347,.045),.034)
    box(p,"Ink",(0,-.087,.280),(.26,.032,.010),.006)
    for x in (-.152,.152):
        tube(p,"Brass",[(x,.10,.242),(x,.10,.33),(x,.066,.35)],.012)
    tube(p,"Silver",[(-.163,.066,.35),(.163,.066,.35)],.019,12)
    tube(p,"Vellum",[(-.119,.066,.35),(.119,.066,.35)],.060,32)
    print_face(p,"print",(0,-.192,.170),.354,.108)
    for i in range(4):
        box(p,"Ink",(-.165+i*.021,-.193,.077),(.010,.005,.042),.002,True)
    oval(p,"Mint",(.161,-.194,.07),(.016,.005,.01),16,6)
    p.finish()
    sheet=Part("receipt_sheet")
    path=[(-.087,.281),(-.105,.310),(-.142,.333),(-.180,.333),(-.205,.320),(-.230,.287),(-.248,.231),(-.256,.148),(-.262,.062)]
    u0,v0,u1,v1=atlas["receipt"]
    for i,((ya,za),(yb,zb)) in enumerate(zip(path,path[1:])):
        vstart=v1+(v0-v1)*i/(len(path)-1);vend=v1+(v0-v1)*(i+1)/(len(path)-1)
        sheet.poly([(-.101,yb,zb),(.101,yb,zb),(.101,ya,za),(-.101,ya,za)],"Print",[(u0,vend),(u1,vend),(u1,vstart),(u0,vstart)])
    sheet.finish()

def door_build():
    group("renewal_door")
    p=Part()
    box(p,"Ink",(0,0,1.05),(1.40,.104,2.10),.084,True)
    box(p,"Brass",(0,-.059,1.05),(1.366,.042,2.06),.080,True)
    box(p,"Ink",(0,-.084,1.05),(1.302,.026,1.982),.072,True)
    box(p,"Enamel",(0,-.105,1.05),(1.246,.061,1.920),.086,True)
    box(p,"Brass",(0,-.138,1.31),(.99,.014,.380),.034,True)
    print_face(p,"door",(0,-.146,1.31),.954,.344)
    for x in (-.595,.595):
        for h in (.18,1.92):
            oval(p,"Brass",(x,-.134,h),(.015,.009,.015),16,6)
    # Pressing plate has raised burnished finger-grips on its lower third.
    for i in range(5):
        box(p,"Brass",(0,-.139,.46+i*.046),(.53-abs(2-i)*.045,.012,.011),.005,True)
    p.finish()

def frame(p,art):
    box(p,"Honey oak",(0,.013,.163),(.34,.036,.310),.022,True)
    box(p,"Brass",(0,-.008,.163),(.315,.015,.286),.016,True)
    box(p,"Cream",(0,-.018,.163),(.296,.013,.265),.010,True)
    print_face(p,art,(0,-.026,.163),.231,.225)
    for x in (-.102,.102):
        tube(p,"Brass",[(x,.021,.221),(x,.115,.024),(x,.111,.010)],.011)
    tube(p,"Brass",[(-.12,.108,.009),(.12,.108,.009)],.008)

def book(p,title):
    box(p,"Enamel",(0,.02,.141),(.29,.057,.281),.016,True)
    box(p,"Vellum",(0,-.014,.141),(.277,.043,.265),.011,True)
    box(p,"Enamel",(0,-.039,.141),(.293,.012,.281),.014,True)
    box(p,"Brass",(-.124,-.048,.141),(.019,.008,.259),.003,True)
    print_face(p,title,(.015,-.047,.155),.209,.149)
    tube(p,"Madder",[(.071,-.02,.272),(.071,-.025,.276),(.083,-.046,.251)],.005)

def souvenir(kind):
    group("shop_item_"+kind)
    p=Part()
    if kind=="corrected":frame(p,"tile2")
    elif kind=="attended":
        book(p,"minutes")
        for y in (.071,.090,.109):box(p,"Vellum",(.018,y,.111),(.218,.015,.222),.008,True)
    elif kind=="read":
        book(p,"folio")
        # The handmade slipcase has a knitted cord across its spine.
        tube(p,"Madder",[(-.142,-.01,.260),(-.160,.004,.243),(-.158,.012,.093),(-.174,.015,.071)],.009)
    elif kind=="pressed":
        lathe(p,"Ink",(0,0,0),[(0,0),(.143,0),(.152,.012),(.149,.024),(.132,.03)],36)
        lathe(p,"Brass",(0,0,0),[(.132,.021),(.147,.033),(.143,.053),(.120,.062)],36)
        lathe(p,"Persimmon",(0,0,0),[(.120,.052),(.127,.066),(.125,.078),(.110,.087),(0,.087)],36)
        print_face(p,"agree",(0,-.012,.088),.158,.058,top=True)
        tube(p,"Linen",[(.11,-.018,.046),(.136,-.055,.052),(.17,-.076,.026)],.003)
        box(p,"Cream",(.164,-.087,.018),(.098,.067,.014),.005)
        print_face(p,"price",(.164,-.087,.027),.088,.053,top=True)
    elif kind=="queued":
        stanchion(p,-.092,0,.35)
        stanchion(p,.092,0,.35)
        tube(p,"Cord",[(-.084+i*.168/12,0,.246-.058*sin(pi*i/12)) for i in range(13)],.009)
    elif kind=="spat":
        # A pleated ribbon badge sits in a small brass display rest.
        box(p,"Honey oak",(0,.016,.020),(.24,.15,.040),.019)
        tube(p,"Brass",[(0,.023,.026),(0,.034,.172)],.015)
        for s in (-1,1):
            p.poly([(s*.025,-.016,.189),(s*.087,-.016,.168),(s*.126,-.016,.038),(s*.082,-.016,.061),(s*.057,-.016,.030)],"Madder")
        for i in range(20):
            a=i*2*pi/20;b=(i+1)*2*pi/20;c=(a+b)/2
            def petal(rad,ang,y):return (rad*cos(ang),y,.221+rad*sin(ang))
            p.poly([petal(.064,a,-.032),petal(.137,a,-.030),petal(.145,c,-.053),petal(.131,b,-.029),petal(.065,b,-.033)],"Madder" if i%2 else "Cord")
        oval(p,"Brass",(0,-.052,.221),(.092,.018,.092),32)
        oval(p,"Enamel",(0,-.073,.221),(.080,.009,.079))
        print_face(p,"rosette",(0,-.083,.221),.118,.072)
    elif kind=="unplugged":
        # Quilted desk-heater plush, with contrasting piping and a cloth handle.
        cushion(p,"Green felt",(0,0,.124),(.32,.185,.236))
        cushion(p,"Linen",(0,-.084,.126),(.285,.043,.201))
        for i in range(5):
            x=-.100+i*.050
            box(p,"Green felt",(x,-.108,.116),(.027,.015,.124),.012,True)
        for x in (-.091,.091):oval(p,"Ink",(x,-.032,.025),(.047,.054,.025),16,8)
        tube(p,"Linen",[(-.051,0,.233),(-.049,0,.275),(-.029,0,.288),(.035,0,.288),(.054,0,.271),(.054,0,.233)],.012)
        print_face(p,"heater",(0,-.111,.203),.16,.043)
        for i in range(16):
            x=-.113+i*.015
            tube(p,"Cream",[(x,-.105,.039),(x+.006,-.105,.041)],.0018,4)
    elif kind=="stayed":
        lathe(p,"Honey oak",(0,0,0),[(0,0),(.132,0),(.14,.013),(.138,.028),(.122,.036),(.124,.057),(.116,.067),(0,.067)],40)
        lathe(p,"Brass",(0,0,0),[(.132,.029),(.138,.032),(.136,.043),(.125,.046)],40)
        lathe(p,"Glass",(0,0,0),[(.102,.063),(.137,.105),(.150,.157),(.146,.214),(.126,.257),(.089,.296),(.043,.316),(0,.321)],48)
        lathe(p,"Mint",(0,0,0),[(.105,.064),(.106,.079),(.096,.090),(.087,.114),(.065,.139),(.034,.153),(0,.159)],32)
        tube(p,"Brass",[(0,-.050,.077),(0,-.050,.179)],.0055)
        box(p,"Enamel",(0,-.050,.178),(.098,.014,.045),.006,True)
        for i in range(16):
            a=i*2.399
            oval(p,"Vellum",((.055+.032*(i%3))*cos(a),(.055+.032*(i%3))*sin(a),.108+.018*(i%7)),(.004,.004,.005),8,4)
    elif kind=="blinked":
        frame(p,"tile6")
        # A tiny signature plate marks this edition as Lashes' own.
        box(p,"Brass",(0,-.031,.040),(.17,.007,.021),.003,True)
        tube(p,"Ink",[(-.04,-.036,.042),(-.028,-.036,.048),(-.02,-.036,.037),(.006,-.036,.042),(.026,-.036,.040)],.0016,4)
    elif kind=="fed":
        lathe(p,"Enamel",(0,0,0),[(0,.006),(.105,.006),(.117,.015),(.116,.035),(.111,.044),(.111,.166),(.119,.17),(.120,.181),(.111,.188),(0,.188)],36)
        for h in (.018,.163):lathe(p,"Brass",(0,0,0),[(.111,h),(.120,h+.003),(.119,h+.012),(.112,h+.017)],36)
        box(p,"Ink",(0,0,.189),(.128,.022,.004),.007)
        print_face(p,"feed",(0,-.114,.104),.150,.073)
        for i in range(3):
            lathe(p,"Brass",(.112,-.061,.008*i),[(0,0),(.036,0),(.039,.003),(.038,.008),(0,.009)],24)
    elif kind=="hoofed":
        # A matched pair of cast split-hoof paperweights, with leather cuffs.
        for s in (-1,1):
            x=s*.081
            for sign in (-1,1):
                oval(p,"Ink",(x+sign*.025,-.030,.051),(.038,.100,.050),20,8)
                oval(p,"Honey oak",(x+sign*.020,-.011,.093),(.039,.071,.068),20,8)
            oval(p,"Linen",(x,.023,.122),(.060,.073,.086),20,8)
            lathe(p,"Brass",(x,.03,.16),[(.055,0),(.058,.003),(.055,.014),(.050,.016)],20)
    elif kind=="kept":
        # Keeper's miniature desk and swivel chair, with enamel drawer fronts.
        box(p,"Honey oak",(0,.032,.162),(.347,.169,.022),.012)
        for x in (-.144,.144):
            for y in (-.024,.089):tube(p,"Brass",[(x,y,.009),(x,y,.153)],.009)
        box(p,"Enamel",(.093,.032,.126),(.119,.135,.052),.008)
        tube(p,"Brass",[(.065,-.039,.128),(.118,-.039,.128)],.003)
        lathe(p,"Brass",(-.036,-.109,0),[(0,0),(.047,0),(.050,.007),(.012,.016),(.008,.071),(.021,.078)],20)
        box(p,"Green felt",(-.036,-.109,.087),(.110,.089,.026),.019)
        tube(p,"Brass",[(-.036,-.137,.081),(-.036,-.158,.164)],.007)
        box(p,"Green felt",(-.036,-.158,.152),(.110,.022,.070),.019,True)
        box(p,"Brass",(-.060,.074,.202),(.105,.012,.075),.006,True)
        print_face(p,"box",(-.06,.067,.202),.093,.053)
    elif kind=="delivered":
        for x in (-.115,.115):
            for y in (-.059,.059):
                oval(p,"Ink",(x,y,.022),(.021,.012,.022),12,8)
                tube(p,"Brass",[(x,y,.029),(x,y,.152)],.008)
        for z in (.050,.150):box(p,"Honey oak",(0,0,z),(.285,.151,.014),.009)
        tube(p,"Brass",[(.119,-.058,.15),(.136,-.058,.199),(.136,.058,.199),(.119,.058,.15)],.006)
        lathe(p,"Cream",(-.039,0,.156),[(0,0),(.027,0),(.036,.013),(.036,.040),(.028,.046),(.023,.051),(.015,.065),(0,.067)],18)
        tube(p,"Cream",[(-.064,0,.175),(-.084,0,.188),(-.093,0,.199)],.006)
        for x in (.012,.067):lathe(p,"Cream",(x,-.025,.158),[(0,0),(.016,0),(.019,.025),(.018,.029),(.015,.026),(.013,.008)],16)
        print_face(p,"parcel",(0,-.077,.108),.138,.043)
        box(p,"Brass",(0,-.072,.108),(.150,.008,.053),.005,True)
    else:raise ValueError(kind)
    p.finish()
    if kind in ("spat","stayed"):
        # Editable encounter data is printed by the game on this planar enamel insert.
        y,z=(-.080,.020) if kind=="spat" else (-.139,.030)
        plaque=Part("range_record" if kind=="spat" else "dome_record")
        half=.078
        plaque.poly([(-half,y,z-.014),(half,y,z-.014),(half,y,z+.014),(-half,y,z+.014)],
                    "Ink",[(0,0),(1,0),(1,1),(0,1)])
        plaque.finish()

def save(spacing=2.10):
    # Native source opens as a browsable tray. Game templates export at local zero.
    for i,root in enumerate(roots.values()):
        root.location=((i%5)*spacing,(i//5)*-spacing,0)
    for img in bpy.data.images:
        if img.filepath and not img.packed_file:img.pack()
    bpy.context.preferences.filepaths.save_version=0
    args.blend.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(args.blend),compress=True)
    for root in roots.values():root.location=(0,0,0)
    bpy.context.view_layer.update()
    bpy.ops.export_scene.gltf(filepath=str(args.glb),export_yup=True,export_cameras=False,export_lights=False,
                              export_texcoords=True,export_normals=True,export_materials="EXPORT",export_animations=False)

if __name__=="__main__":
    press_build()
    lab_build()
    lab_build(True)
    bay_build()
    printer_build()
    door_build()
    for name in ("corrected","attended","spat","queued","pressed","hoofed","fed","kept","blinked","stayed","delivered","unplugged","read"):
        souvenir(name)
    save()

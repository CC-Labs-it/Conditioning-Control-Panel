"""Back-of-house joinery, maintenance fittings, gallery hardware and archive textiles."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from build_collection import group,Part,box,lathe,tube,cushion,oval,print_face,save,sin,cos,pi

def face(p,name,c,w,h):
    x,y,z=c
    p.poly([(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2)],name,[(0,0),(1,0),(1,1),(0,1)])

def insert(name,c,w,h):
    part=Part(name)
    face(part,"Cream",c,w,h)
    part.finish()

def placard(name,ink,w=.48,h=.21,variable=False):
    group("annex_"+name)
    p=Part()
    box(p,"Honey oak",(0,0,h/2),(w,.043,h),.016,True)
    box(p,"Brass",(0,-.023,h/2),(w-.020,.012,h-.015),.012,True)
    if not variable:print_face(p,ink,(0,-.031,h/2),w-.037,h-.029)
    p.finish()
    if variable:insert(name+"_face",(0,-.032,h/2),w-.035,h-.030)

def heater():
    group("annex_heater")
    p=Part()
    # Finned radiator with a guard, a cloth-covered cable and two porcelain feet.
    for x in (-.235,.235):
        box(p,"Ink",(x,0,.031),(.107,.328,.062),.025)
        tube(p,"Brass",[(x,0,.05),(x,0,.122)],.030)
    box(p,"Silver",(0,.019,.282),(.55,.283,.369),.067,True)
    for s in (-1,1):
        box(p,"Enamel",(s*.289,0,.291),(.055,.344,.420),.021)
    for i in range(9):
        box(p,"Silver",(-.207+i*.051,-.101,.291),(.028,.185,.315),.009)
    for x in (-.230,.230):tube(p,"Brass",[(x,-.193,.112),(x,-.193,.461)],.008)
    for i in range(7):tube(p,"Brass",[(-.236,-.193,.119+i*.055),(.235,-.193,.119+i*.055)],.0055)
    box(p,"Ink",(0,-.157,.455),(.255,.024,.066),.011,True)
    print_face(p,"heater",(0,-.171,.455),.239,.060)
    tube(p,"Ink",[(.292,.093,.169),(.400,.160,.160),(.461,.188,.068),(.535,.214,.023),(.692,.216,.017)],.011)
    p.finish()
    group("annex_socket");p=Part()
    box(p,"Cream",(0,0,.125),(.168,.077,.25),.021,True)
    box(p,"Ink",(0,-.041,.126),(.108,.017,.132),.025,True)
    for s in (-1,1):box(p,"Brass",(s*.024,-.053,.132),(.009,.008,.031),.003,True)
    p.finish()
    p=Part("heater_plug")
    box(p,"Enamel",(0,-.088,.133),(.090,.086,.088),.023,True)
    tube(p,"Ink",[(0,-.121,.135),(.045,-.151,.115),(.066,-.165,.060),(.056,-.180,.021)],.009)
    p.finish()

def hardhat():
    group("annex_hat");p=Part()
    oval(p,"Brass",(0,-.035,.022),(.158,.20,.017),36,8)
    lathe(p,"Persimmon",(0,0,0),[(.141,.022),(.143,.064),(.136,.119),(.118,.162),(.095,.190),(.063,.207),(0,.213)],40)
    for s in (-1,0,1):
        x=s*.050
        tube(p,"Brass",[(x,-.123,.091),(x,-.112,.139),(x,-.077,.183),(x,-.02,.205),(x,.036,.202),(x,.103,.149)],.006)
    print_face(p,"hardhat",(0,-.143,.088),.072,.034)
    p.finish()

def parcel():
    group("annex_crate");p=Part()
    box(p,"Honey oak",(0,0,.238),(.84,.50,.476),.032)
    for x in (-.32,.32):
        box(p,"Brass",(x,0,.241),(.051,.509,.480),.013)
    for z in (.046,.434):
        box(p,"Honey oak",(0,-.254,z),(.87,.025,.052),.009)
    print_face(p,"spare",(0,-.253,.237),.448,.170)
    p.finish()
    group("annex_tea");p=Part()
    box(p,"Honey oak",(0,0,.030),(.52,.318,.048),.030)
    for s in (-1,1):
        tube(p,"Brass",[(s*.22,-.102,.044),(s*.257,-.102,.089),(s*.257,.101,.089),(s*.22,.101,.044)],.010)
    lathe(p,"Cream",(-.101,.015,.054),[(0,0),(.047,0),(.069,.013),(.079,.062),(.072,.107),(.051,.125),(.046,.135),(.023,.143),(.018,.158),(0,.161)],32)
    tube(p,"Cream",[(-.162,.015,.09),(-.19,.015,.13),(-.205,.015,.152)],.013)
    tube(p,"Brass",[(-.048,.015,.091),(-.019,.015,.104),(-.010,.015,.135),(-.038,.015,.157)],.009)
    for x,y in ((.057,.051),(.124,-.073)):
        lathe(p,"Cream",(x,y,.054),[(0,0),(.026,0),(.032,.014),(.038,.056),(.033,.059),(.029,.054),(.024,.010),(0,.008)],24)
        tube(p,"Brass",[(x+.027,y,.069),(x+.049,y,.079),(x+.046,y,.096),(x+.032,y,.102)],.004)
    box(p,"Cream",(0,-.137,.067),(.19,.015,.068),.007,True)
    print_face(p,"tea",(0,-.146,.067),.18,.061);p.finish()

def filing():
    group("annex_cabinet");p=Part()
    w=2.58;h=1.88;d=.308
    box(p,"Enamel",(0,.115,.911),(w,.069,h-.14),.024,True)
    for s in (-1,1):box(p,"Honey oak",(s*1.263,0,.94),(.065,d,h),.009)
    for i in range(5):
        z=.172+i*.409
        box(p,"Honey oak",(0,0,z),(w,.305,.046),.016)
        box(p,"Brass",(0,-.154,z-.006),(w-.038,.015,.017),.003)
    for i in range(1,6):box(p,"Honey oak",(-1.26+i*.42,0,.991),(.028,d-.02,1.64),.006)
    box(p,"Honey oak",(0,0,.087),(w+.042,d+.046,.131),.019)
    box(p,"Brass",(0,.004,1.852),(w+.078,d+.071,.049),.015)
    box(p,"Enamel",(0,.008,1.900),(w+.043,d+.053,.055),.014)
    p.finish()
    for row in range(4):
        for col in range(6):
            x=-1.05+col*.42;z=.256+row*.409
            plate=Part("cabinet_number_"+str(row*6+col))
            box(plate,"Brass",(x,-.148,z-.073),(.123,.017,.044),.007,True)
            # Card slots in each label rail accept stamped or blank ledgers.
            face(plate,"Cream",(x,-.158,z-.072),.100,.025)
            plate.finish()
    group("annex_swatch");p=Part()
    for i in range(3):
        cushion(p,("Madder","Linen","Green felt")[i],(0,.015,.030+i*.037),(.263,.174,.058))
    tube(p,"Brass",[(-.135,-.060,.052),(-.095,-.095,.055),(-.069,-.097,.097)],.004)
    p.finish()
    group("annex_scarf");p=Part()
    for x in (-.42,.42):
        for y in (-.206,.206):tube(p,"Honey oak",[(x*1.04,y*1.06,0),(x,y,.655)],.024)
    box(p,"Honey oak",(0,0,.662),(.97,.53,.067),.033)
    box(p,"Green felt",(0,0,.700),(.89,.45,.022),.021)
    # Long scarf over the rail, with its unfinished last row and loose working yarn.
    for j in range(12):
        x=-.34+j*.018
        tube(p,"Madder" if j%4 else "Linen",[(x,.16,.717),(x,-.18,.717),(x,-.24,.697),(x,-.268,.652),(x,-.268,.25-j%3*.012)],.010,6)
    for s in (-1,1):
        tube(p,"Honey oak",[(s*.068,.121,.721),(-s*.153,.265,.774)],.0065)
        oval(p,"Brass",(-s*.15,.266,.774),(.013,.013,.013),12,6)
    oval(p,"Madder",(.283,.034,.822),(.098,.092,.105),24,12)
    for j in range(5):
        zz=.77+.020*j
        tube(p,"Linen",[(.283+.091*cos(k*pi/16),.034+.086*sin(k*pi/16),zz+.009*cos(k*pi/8)) for k in range(33)],.002,5)
    tube(p,"Madder",[(.31,-.03,.79),(.32,-.10,.731),(.28,-.163,.723),(.14,-.258,.698),(.13,-.279,.591),(.174,-.286,.471)],.007,8)
    p.finish()
    insert("scarf_pattern",(-.242,-.281,.457),.195,.359)
    group("annex_thread");p=Part()
    # One metre of skein, repeatable end to end along the skirting.
    knots=[(-.5+i/24,.011*sin(i*pi/6),.024+.005*(1-cos(i*pi/6))) for i in range(25)]
    tube(p,"Madder",knots,.009,8)
    tube(p,"Linen",[(x,y+.007*cos(i*pi/3),z+.007*sin(i*pi/3)) for i,(x,y,z) in enumerate(knots)],.0032,5)
    p.finish()

def accessories():
    group("annex_stapler");p=Part()
    box(p,"Ink",(0,0,.013),(.15,.052,.025),.010)
    box(p,"Silver",(0,0,.034),(.133,.035,.017),.007)
    box(p,"Madder",(0,.001,.054),(.153,.045,.033),.013)
    tube(p,"Brass",[(-.058,-.026,.035),(-.058,.026,.035)],.008)
    p.finish()
    group("annex_book");p=Part()
    box(p,"Honey oak",(0,0,.040),(.54,.29,.08),.019)
    for side in (-1,1):
        box(p,"Vellum",(side*.112,0,.111),(.224,.24,.056),.014)
    box(p,"Brass",(0,0,.143),(.013,.236,.009),.004)
    p.finish()
    group("annex_sign");p=Part()
    box(p,"Brass",(0,0,.021),(.322,.207,.042),.018)
    tube(p,"Enamel",[(0,0,.037),(0,0,.910)],.023,14)
    box(p,"Brass",(0,0,.973),(.65,.051,.31),.024,True);p.finish()
    insert("sign_face",(0,-.027,.973),.607,.27)
    group("annex_plinth");p=Part()
    lathe(p,"Brass",(0,0,0),[(0,0),(.189,0),(.196,.017),(.170,.035),(.094,.052),(.071,.070),(0,.070)],28);p.finish()
    group("annex_hook");p=Part()
    box(p,"Honey oak",(0,.01,.11),(.29,.046,.218),.018,True)
    tube(p,"Brass",[(0,-.018,.153),(0,-.071,.137),(0,-.085,.161),(0,-.084,.187)],.013)
    box(p,"Brass",(0,-.019,.065),(.218,.015,.055),.008,True)
    p.finish()
    insert("hook_caption",(0,-.028,.067),.19,.037)
    for art in ("flight","loch","summit","woods","moon"):
        group("annex_photo_"+art);p=Part()
        box(p,"Honey oak",(0,0,.246),(.642,.043,.489),.018,True)
        box(p,"Brass",(0,-.025,.246),(.617,.014,.464),.012,True)
        box(p,"Cream",(0,-.036,.246),(.597,.014,.445),.008,True)
        print_face(p,art,(0,-.044,.246),.514,.386)
        tube(p,"Linen",[(-.190,.027,.393),(0,.044,.495),(.190,.027,.393)],.004)
        p.finish()
    group("annex_agenda");p=Part()
    lathe(p,"Brass",(0,0,0),[(0,0),(.535,0),(.552,.008),(.55,.017),(.52,.023),(0,.023)],48)
    lathe(p,"Enamel",(0,0,0),[(0,.022),(.505,.022),(.5,.032),(0,.032)],48)
    print_face(p,"agenda",(0,0,.033),.67,.25,top=True);p.finish()
    group("annex_spit_plate");p=Part()
    lathe(p,"Brass",(0,0,0),[(0,0),(.221,0),(.231,.012),(.23,.030),(.204,.044),(0,.045)],36)
    lathe(p,"Enamel",(0,0,0),[(0,.042),(.190,.042),(.192,.049),(.18,.058),(0,.058)],36)
    print_face(p,"plate",(0,0,.060),.272,.099,top=True);p.finish()
    group("annex_shadow");p=Part()
    # Ears and the head are a silhouette seen through the frosted band.
    oval(p,"Ink",(0,0,.14),(.115,.019,.144),24,10)
    for s in (-1,1):oval(p,"Ink",(s*.063,0,.312),(.027,.017,.112),20,8)
    p.finish()

def keeper_range():
    group("annex_keeper_door");p=Part()
    # Dark return and a permanently swung leaf give the staff transfer physical depth.
    box(p,"Ink",(0,.012,1.06),(1.10,.065,2.12),.020,True)
    for x in (-.594,.594):box(p,"Honey oak",(x,0,1.085),(.085,.152,2.17),.019)
    box(p,"Honey oak",(0,0,2.180),(1.275,.152,.135),.022)
    box(p,"Brass",(0,-.089,.014),(1.16,.25,.028),.008)
    # The edge of the opened leaf rests against the service wall.
    box(p,"Enamel",(-.704,-.072,1.04),(.168,.17,2.08),.023,True)
    for h in (.27,1.74):tube(p,"Brass",[(-.584,-.091,h),(-.584,-.091,h+.132)],.019,14)
    print_face(p,"keeper",(0,-.080,2.174),.315,.099)
    for i in range(3):
        box(p,"Brass",(-.39+i*.39,-.026,1.135),(.040,.008,.008),.002,True)
    p.finish()
    group("annex_seat");p=Part()
    cushion(p,"Green felt",(0,0,.018),(.426,.387,.037))
    p.finish()
    group("annex_desk_cap");p=Part()
    lathe(p,"Brass",(0,0,0),[(0,0),(.105,0),(.108,.012),(.098,.027),(.081,.031)],28)
    lathe(p,"Persimmon",(0,0,0),[(.074,.024),(.083,.031),(.083,.047),(.075,.051),(0,.052)],32)
    print_face(p,"clock",(0,0,.053),.109,.041,top=True);p.finish()
    group("annex_token");p=Part()
    lathe(p,"Brass",(0,0,0),[(0,0),(.031,0),(.033,.003),(.033,.008),(.030,.011),(0,.011)],32)
    print_face(p,"docket",(0,0,.0115),.042,.016,top=True);p.finish()
    group("annex_range_flag");p=Part()
    lathe(p,"Brass",(0,0,0),[(0,0),(.084,0),(.095,.012),(.079,.027),(.027,.035),(.013,.05),(.012,.804),(.020,.810),(.018,.826),(0,.832)],22)
    p.poly([(.012,0,.765),(.29,-.018,.735),(.23,-.035,.607),(.012,0,.641)],"Madder")
    p.poly([(.011,.002,.638),(.23,-.032,.607),(.29,-.016,.735),(.011,.002,.765)],"Madder")
    box(p,"Brass",(0,-.031,.455),(.191,.025,.091),.009,True)
    p.finish();insert("flag_face",(0,-.045,.455),.171,.074)
    group("annex_judge");p=Part()
    box(p,"Honey oak",(0,0,.055),(.965,.586,.110),.031)
    for s in (-1,1):tube(p,"Brass",[(s*.338,0,.096),(s*.338,0,.918)],.035,16)
    box(p,"Enamel",(0,0,1.00),(.942,.073,.411),.037,True)
    box(p,"Brass",(0,-.043,1.00),(.910,.020,.381),.029,True)
    p.finish();insert("judge_face",(0,-.056,1.00),.865,.341)

heater();hardhat();parcel();filing();accessories();keeper_range()
for name,ink in (("notice","heater"),("rota","rota"),("docket","trolley"),("thread_label","knit"),("staff","keeper"),("safety","scaffold"),("dust_note","secret"),("permit","docket")):
    placard(name,ink,.60,.238,variable=name=="rota")
placard("record","clause",.63,.72,True)
save(3.0)

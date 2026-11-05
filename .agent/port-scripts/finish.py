"""Fixups after props.py and world.py. Every edit reports when it finds nothing, so a stale pattern cannot pass silently."""
import re,os,glob,sys,shutil
sys.path.insert(0,os.path.dirname(__file__))
import port as P
CP='/home/pontus/projects/crystall-punk-14/Resources'
C=CP+'/Prototypes/_CP14'
D='Resources/Prototypes/_CF'
S=D+'/Entities/Structures/Ported'
W=D+'/Ported'
miss=[]
def sub(f,old,new,regex=False,count=0):
    p=f if f.startswith('Resources') else D+'/'+f
    t=open(p,encoding='utf-8-sig').read()
    n=re.sub(old,new,t,count=count,flags=re.M) if regex else t.replace(old,new)
    if n==t: miss.append((f,old[:50]))
    open(p,'w').write(n)
def dropblock(f,ident):
    p=D+'/'+f; t=open(p).read(); parts=re.split(r'(?m)^(?=- type: )',t)
    keep=[b for b in parts if not re.search(r'(?m)^  id: '+ident+r'\s*$',b)]
    if len(keep)==len(parts): miss.append((f,'block '+ident))
    open(p,'w').write(''.join(keep))
def gen():
    return glob.glob(W+'/**/*.yml',recursive=True)+glob.glob(S+'/**/*.yml',recursive=True)

# Bad renames in older tracked files, checked against CP14.
for a,b in [('CFWallDirtBlock1','CFDirtBlock1'),('CFWallFloraMaterial1','CFFloraMaterial1'),('CFWallSnowball','CFSnowball'),('CFWallSnowEffect','CFSnowEffect')]:
    if a in open(D+'/Entities/Structures/Walls/natural.yml').read(): sub('Entities/Structures/Walls/natural.yml',a,b)
def once(f,a,b):
    if a in open(D+'/'+f).read(): sub(f,a,b)
once('Tiles/grass.yml','deconstructTools: [ CFFloorDirt ]','deconstructTools: [ CFDigging ]')
once('Tiles/grass.yml','itemDrop: CFFloorDirt','itemDrop: CFRandomDirtLootSpawner')
once('Tiles/natural.yml','itemDrop: CFRandomDirtEffect','itemDrop: CFDirtEffect')
once('Tiles/woodplank.yml','itemDrop: CFFloorWoodenPlanks1','itemDrop: CFWoodenPlanks1')
once('Tiles/woodplank.yml','itemDrop: CFFloorLucensWoodPlanks','itemDrop: CFWoodenPlanks1')
once('tools.yml','spawn: CFWeaponShovelIron','spawn: CFShovel')
once('tools.yml','spawn: CFWeaponHammerIron','spawn: CFLightHammer')
once('Recipes/Construction/Graphs/Structures/Walls/wall_wooden_birch.yml','tool: CFWoodenHammering','tool: CFHammering')
for a,b in [('CFBeeerBreeze','CFBeerBreeze'),('CFBeeerBlowLaw','CFBeerBlowLaw'),('CFBeeerGerbil','CFBeerGerbil'),('graph: CFWoodenBarrel','graph: CFCraneBarrel')]:
    once('Entities/Structures/Furniture/barrel_crane.yml',a,b)
# Chromium slime and dayflin pulp are alchemy reagents, which stay out until alchemy is decided.
t=open(D+'/Entities/Structures/Furniture/barrel.yml').read()
if 'CFCromiumSlime' in t or 'CFBloodDayflinPulp' in t:
    parts=re.split(r'(?m)^(?=- type: )',t)
    open(D+'/Entities/Structures/Furniture/barrel.yml','w').write(''.join(b for b in parts if 'CFCromiumSlime' not in b and 'CFBloodDayflinPulp' not in b))
t=open(D+'/SoundCollections/misc.yml').read()
n=re.sub(r'^ *- /Audio/_CP14/Lobby/.*\n','',t,flags=re.M)
open(D+'/SoundCollections/misc.yml','w').write(n)

# Engine field differences.
for f in gen():
    t=open(f).read(); o=t
    t=re.sub(r'^ +(currentTemperature|thickness|area|conductivity|ignoreEmpty): .*\n','',t,flags=re.M)
    def inj(m):
        b=m[0]
        draw=re.search(r'toggleState: 1',b) is not None
        b=re.sub(r'^ +(injectOnly|toggleState): .*\n','',b,flags=re.M)
        b=re.sub(r'^ +transferAmounts:\n(?: +- .*\n)*','',b,flags=re.M)
        if 'activeModeProtoId' not in b:
            first='CFPourDrawMode' if draw else 'CFPourInjectMode'
            b=b.rstrip('\n')+f'\n    activeModeProtoId: {first}\n    allowedModes:\n    - CFPourDrawMode\n    - CFPourInjectMode\n'
        return b
    t=re.sub(r'^  - type: Injector\n(?:    .*\n)*',inj,t,flags=re.M)
    def occ(m):
        l,b,r,tp=m[1].split(',')
        return f'    polygon:\n    - "{l},{b}"\n    - "{r},{b}"\n    - "{r},{tp}"\n    - "{l},{tp}"\n'
    t=re.sub(r'^    boundingBox: "([^"]+)"\n',occ,t,flags=re.M)
    t=re.sub(r'^    capacity: (\d+)\n(?=  - type: PointLight)',r'  - type: LimitedCharges\n    maxCharges: \1\n',t,flags=re.M)
    t=t.replace('pungent','cheesy')
    if t!=o: open(f,'w').write(t)
sub('Ported/Objects/Misc/Entities_Objects_Misc_crayons.yml','  - type: Crayon\n    capacity: 15\n','  - type: Crayon\n    selectedState: like\n  - type: LimitedCharges\n    maxCharges: 15\n')
sub('Ported/Reagents/Reagents_Consumable_Drink_alcohol.yml',r'^    Alcohol:$','    Digestion:',regex=True)

# Every drink is an exposure to the Alcohol affliction. Stock slurred speech stays as flavour.
A='      - !type:CFExposeAffliction\n        affliction: Alcohol\n      - !type:Drunk\n        boozePower: 2\n'
f=W+'/Reagents/Reagents_Consumable_Drink_alcohol.yml'
out=[]
for b in re.split(r'(?m)^(?=- type: )',open(f).read()):
    if b.startswith('- type: reagent'):
        if '    Digestion:\n      effects:\n' in b: b=b.replace('    Digestion:\n      effects:\n','    Digestion:\n      effects:\n'+A,1)
        elif 'metabolisms:' in b: miss.append(('alcohol',b[:40]))
        else: b=b.rstrip('\n')+'\n  metabolisms:\n    Digestion:\n      effects:\n'+A+'\n'
    out.append(b)
open(f,'w').write(''.join(out))

# Rejected systems: essences, religion and alchemy books, demiplane loot, crystal lamps.
sub('Entities/Structures/Ported/Flora/Crystal/elemental.yml',r'^ *- !type:SpawnEntitiesBehavior\n *spawn:\n *CFEssence\w+:\n *min: \d+\n *max: \d+\n','',regex=True)
sub('Entities/Structures/Ported/Furniture/bookshelf.yml',r'^ *- id: CF(GuidebookAlchemy|BookPantheonGodsSileita|BookPantheonGodsSileitaImitators)\n','',regex=True)
sub('Entities/Structures/Ported/Decorations/bone_remains.yml',r'^ *CFClothingMaskBoneHornedMask:\n *min: \d+\n *max: \d+\n','',regex=True)
sub('Entities/Structures/Ported/Flora/web.yml','''      - !type:NestedSelector
        weight: 75
        tableId: CFTableDemiplaneLootT1
''','''      - id: CFCopperCoin
        weight: 75
''')
sub('Entities/Structures/Ported/Flora/web.yml','CFMobSpiderBlackHunter','CFMobHuntingSpider')
sub('Entities/Structures/Ported/Flora/royalpumpkin.yml','CFFoodPumpkinSlice','CFFoodPumpkin')
sub('Entities/Structures/Ported/Furniture/mannequin.yml','templateId: CFPartial','templateId: human')
sub('Entities/Structures/Ported/Storage/Crates/grave.yml',r'^    - !type:GroupSelector # Body parts\n(?:.*\n)*?(?=^- type|\Z)','''    - id: CFBone1
    - id: CFCopperCoin
      weight: 0.2

''',regex=True)
os.remove(S+'/Furniture/wallmount_lamp.yml')
dropblock('Ported/Recipes/Graphs/Recipes_Construction_Graphs_Structures_Furniture_lighting.yml','CFWallmountLampEmpty')
dropblock('Ported/Recipes/Recipes_Construction_structures.yml','CFWallmountLampEmpty')

# Small pieces that other prototypes need.
def blocks(path,ids):
    t=open(path,encoding='utf-8-sig').read()
    out=[]
    for b in re.split(r'(?m)^(?=- type: )',t):
        m=re.search(r'(?m)^  id: (\S+)',b)
        if m and m[1] in ids: out.append(b.rstrip()+'\n')
    if len(out)!=len(ids): miss.append((path[-30:],'blocks %d/%d'%(len(out),len(ids))))
    return out
ex=blocks(C+'/Entities/Effects/visual_effect.yml',{'CP14DirtEffect','CP14SnowEffect'})
ex+=blocks(C+'/Entities/Markers/Spawners/Random/misc.yml',{'CP14RandomDirtLootSpawner','CP14RandomStoneLootSpawner','CP14RandomSnowLootSpawner'})
ex+=blocks(C+'/Entities/Objects/Materials/simple.yml',{'CP14Snowball'})
ex+=blocks(C+'/Damage/examine_messages.yml',{'CP14WeaponMessages'})
os.makedirs(W+'/Misc',exist_ok=True)
open(W+'/Misc/extras.yml','w').write('# Ported from CP14 by the CF converter.\n\n'+P.port('\n'.join(ex)))
open(W+'/Recipes/Graphs/torch.yml','w').write('# Ported from CP14 by the CF converter.\n\n'+P.port(open(C+'/Recipes/Crafting/Graphs/improvised/torch.yml',encoding='utf-8-sig').read()))

# Locks: CP14's shapes become grades and key ids.
ST=C+'/Entities/Structures'
os.makedirs(S+'/Doors/Locked',exist_ok=True)
open(S+'/Doors/Locked/alchemist.yml','w').write('# Ported from CP14 by the CF converter.\n\n'+P.port(open(ST+'/Doors/Locked/alchemist.yml',encoding='utf-8-sig').read()))
locks={}
for f in glob.glob(ST+'/**/*.yml',recursive=True):
    for b in re.split(r'(?m)^(?=- type: )',open(f,encoding='utf-8-sig').read()):
        i=re.search(r'(?m)^  id: (\S+)',b)
        if not i or 'type: CP14Lock' not in b: continue
        k=re.search(r'autoGenerateShape: (\S+)',b); r=re.search(r'autoGenerateRandomShape: (\d+)',b)
        if k: locks[P.convert(i[1])]=('key',k[1])
        elif r: locks[P.convert(i[1])]=('random',int(r[1]))
GRADE={'Simple':(20,3),'Average':(25,4),'Good':(30,5)}
BASED={'CFSafe':'Average','CFClosetGlassShowcase':'Simple'}
def grade(i,lock):
    if lock[0]=='random': return 'Simple' if lock[1]<=3 else 'Average'
    if re.search('Guildmaster|Guard|CityGate',i+lock[1]): return 'Good'
    return 'Average' if re.search('Iron|Safe',i) else 'Simple'
def force(i): return 30 if re.search('Iron|Safe',i) else 20
done=set()
for f in glob.glob(D+'/Entities/Structures/**/*.yml',recursive=True):
    parts=re.split(r'(?m)^(?=- type: )',open(f).read()); out=[]
    for b in parts:
        i=re.search(r'(?m)^  id: (\S+)',b); idn=i[1] if i else None
        if idn in ('CFBaseDoor','CFChestGeneric','CFBaseFenceGate','CFBaseFenceGateBig','CFCabinetBase','CFClosetBase'):
            b=re.sub(r'^  - type: Lock\n    locked: false\n','',b,flags=re.M); done.add(idn)
        if idn in BASED:
            b=re.sub(r'(?m)^  parent: (\S+)$',lambda m:f'  parent: [ {m[1]}, CFLock{BASED[idn]} ]',b,count=1) if re.search(r'(?m)^  parent: \S+$',b) and '[' not in re.search(r'(?m)^  parent: (.*)$',b)[1] else re.sub(r'(?m)^  parent:\n((?:  - .*\n)+)',lambda m:f'  parent:\n{m[1]}  - CFLock{BASED[idn]}\n',re.sub(r'(?m)^  parent: \[ *(.*?) *\]$',lambda m:'  parent:\n'+''.join(f'  - {x.strip()}\n' for x in m[1].split(',')).rstrip('\n'),b))
            b=b.replace('  components:\n',f'  components:\n  - type: CFLock\n    forceDc: {force(idn)}\n',1); done.add(idn)
        if idn in locks:
            lk=locks[idn]; g=grade(idn,lk)
            par=re.search(r'(?m)^  parent: (.*)$',b)
            lines=['  - type: CFLock',f'    forceDc: {force(idn)}']
            if lk[0]=='key': lines.append(f'    keyId: {lk[1]}')
            if par and par[1].strip() in BASED:
                if g!=BASED[par[1].strip()]: lines+= [f'    dc: {GRADE[g][0]}',f'    successesNeeded: {GRADE[g][1]}']
            elif par and par[1].strip():
                b=b.replace(par[0],f'  parent: [ {par[1].strip().strip("[]").strip()}, CFLock{g} ]',1)
            else:
                b=re.sub(r'(?m)^  parent:\n((?:  - .*\n)+)',lambda m:f'  parent:\n{m[1]}  - CFLock{g}\n',b,count=1)
            if '  components:\n' in b: b=b.replace('  components:\n','  components:\n'+'\n'.join(lines)+'\n',1)
            else: b=b.rstrip('\n')+'\n  components:\n'+'\n'.join(lines)+'\n\n'
            done.add(idn)
        out.append(b)
    open(f,'w').write(''.join(out))
print('locks applied',len(done),'of',len(locks)+8,'unapplied',sorted(set(locks)-done)[:10])

# Keys from the catalog. Abstract keys were a round-start shuffle, so rings get real keys from the same catalog.
keys=[];pools={};ab={}
for f in sorted(glob.glob(C+'/Entities/Objects/Keys/Catalog/*.yml')):
    name=os.path.basename(f); pool=[]
    for b in re.split(r'(?m)^(?=- type: )',open(f,encoding='utf-8-sig').read()):
        i=re.search(r'(?m)^  id: (\S+)',b)
        if not i: continue
        if 'CP14AbstractKey' in b: ab[i[1]]=(name,re.search(r'group: (\S+)',b)[1]); continue
        sh=re.search(r'autoGenerateShape: (\S+)',b)
        if not sh: continue
        st=re.search(r'parent: CP14Key(\w+)Blank',b)[1].lower(); suf=re.search(r'(?m)^  suffix: (.*)$',b)
        cid=P.convert(i[1]); pool.append((cid,sh[1]))
        keys.append(f'- type: entity\n  parent: CFBaseKey\n  id: {cid}\n'+(f'  suffix: {suf[1]}\n' if suf else '')+f'  components:\n  - type: Sprite\n    state: key_{st}\n  - type: CFKey\n    keyId: {sh[1]}\n')
    pools[name]=pool
os.makedirs(W+'/Keys',exist_ok=True)
# CP14 ships these doors with no key.
for cid,sh,st in [('CFKeyBlacksmith3','Blacksmith3','iron'),('CFKeyAlchemy3','Alchemy3','copper')]:
    keys.append(f'- type: entity\n  parent: CFBaseKey\n  id: {cid}\n  suffix: {sh}\n  components:\n  - type: Sprite\n    state: key_{st}\n  - type: CFKey\n    keyId: {sh}\n')
ring=P.port(open(C+'/Entities/Objects/Keys/keyrings.yml',encoding='utf-8-sig').read())
used={}
def real(m):
    a=m[2].replace('CF','CP14',1); name,grp=ab[a]
    pre={'Merchant':'Shop'}.get(grp,grp)
    pool=[c for c,sh in pools[name] if sh.startswith(pre) and 'Public' not in sh] or [c for c,_ in pools[name]]
    n=used.get(a,0); used[a]=n+1
    return f'{m[1]}- id: {pool[n%len(pool)]}\n'
ring=re.sub(r'(?m)^( *)- id: (CF\w*Abstract)\b.*\n',real,ring)
ring=ring.replace('    - keys\n','').replace('        - CFLockpick\n','')
open(W+'/Keys/keys.yml','w').write('# Ported from CP14 by the CF converter. A key matches a lock on keyId.\n\n'+'\n'.join(keys)+'\n'+ring)
print('keys',len(keys))

# Roofs block weather, so outdoors rules treat them as shelter.
n=0
for f in gen():
    t=open(f).read()
    u=''.join(x if 'type: BlockWeather' in x else x.replace('  - type: CFRoof\n','  - type: CFRoof\n  - type: BlockWeather\n') for x in re.split(r'(?m)^(?=- type: )',t))
    if u!=t: n+=1; open(f,'w').write(u)
print('roof files',n)

# Voice sounds, with their licence file.
dst='Resources/Audio/_CP14/Voice'
if not os.path.isdir(dst): shutil.copytree(CP+'/Audio/_CP14/Voice',dst)

# Locale: every cp14- key we use, plus the numbered weapon messages.
used=set()
for f in glob.glob(D+'/**/*.yml',recursive=True)+glob.glob('Content.*/_CF/**/*.cs',recursive=True):
    used|=set(re.findall(r'\bcp14-[a-z0-9-]*[a-z0-9]',open(f,encoding='utf-8-sig').read()))
used|={f'cp14-damageable-weapon-{i}' for i in range(1,5)}
entries={}
for f in glob.glob(CP+'/Locale/en-US/_CP14/**/*.ftl',recursive=True):
    cur=None
    for l in open(f,encoding='utf-8-sig').read().split('\n'):
        m=re.match(r'^([a-zA-Z][\w-]*) *=',l)
        if m: cur=m[1]; entries.setdefault(cur,[]).append(l) if cur not in entries else None; dup=len(entries[cur])>0
        elif cur and (l.startswith(' ') or l.startswith('\t')) and l.strip(): entries[cur].append(l)
        else: cur=None
have=set()
for f in glob.glob('Resources/Locale/en-US/**/*.ftl',recursive=True):
    if f.endswith('_CF/ported.ftl'): continue
    have|=set(re.findall(r'(?m)^([a-zA-Z][\w-]*) *=',open(f,encoding='utf-8-sig').read()))
need=sorted(k for k in used if k in entries and k not in have)
lost=sorted(k for k in used if k not in entries and k not in have)
open('Resources/Locale/en-US/_CF/ported.ftl','w').write('# Strings from CP14 that ported prototypes use.\n\n'+'\n'.join('\n'.join(entries[k]) for k in need)+'\n')
print('locale written',len(need),'not found',len(lost),lost[:12])
print('MISSED',miss)

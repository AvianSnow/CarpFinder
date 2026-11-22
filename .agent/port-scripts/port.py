"""CP14 -> CF prototype converter. Applies the rules in .agent/ss14rules.md."""
import re,sys,os,glob
# crystall-punk-14 sits beside this repo on every machine. Set CP14_ROOT if it does not.
CP14=os.environ.get('CP14_ROOT') or os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','..','..','crystall-punk-14')).replace(chr(92),'/')
# Windows glob returns backslashes, and the scripts match paths with forward slashes.
_glob=glob.glob
glob.glob=lambda *a,**k:[p.replace(chr(92),'/') for p in _glob(*a,**k)]
DROP=set('''CP14MeleeSelfDamage CP14SharpeningStone CP14Sharpened CP14ModularCraftStartPoint CP14ModularCraftPart CP14SpellStorage CP14MagicEnergyContainer
CP14MagicEnergyDraw CP14MagicManacostModify CP14MagicUnsafeDamage CP14MagicUnsafeSleep CP14DoorInteractionPopup CP14WaveShader CP14BonusDistanceMeleeDamage
CP14MeleeWeaponStaminaCost CP14WeaponChemical CP14Mana CP14MagicEnergyCrystalSlot CP14MagicEnergyExaminable CP14MagicEnergyPhotosynthesis CP14SkillPointConsumable
CP14Skill CP14Fortification CP14ClothingSkillBonus CP14Religion CP14ReligionAltar StaticPrice CP14Lock GuideHelp'''.split())
FIXES=[
 (r'\bCP14(?=[A-Za-z0-9])','CF'),
 (r'\bCFloor','CFFloor'),
]
def blocks(text):
    """Split a file at top-level '- type:' and keep comments with the next block."""
    return re.split(r'\n(?=- type: )',text)
def drop_component(b,name):
    return re.sub(r'^(\s*)- type: '+name+r'\s*(#.*)?\n((?:\1  .*\n|\s*\n)*)','',b,flags=re.M)
def fix_damageable(t):
    def r(m):
        ind=m[1]; body=m[2]
        c=re.search(r'^\s*damageContainer: *(\S+).*\n',body,re.M)
        if not c: return m[0]
        rest=body.replace(c[0],'')
        out=f'{ind}- type: Damageable\n{rest}{ind}- type: Injurable\n{ind}  damageContainer: {c[1]}\n'
        return out
    return re.sub(r'^(\s*)- type: Damageable *\n((?:\1  .*\n)+)',r,t,flags=re.M)
def fix_boxes(t):
    def r(m):
        v=[float(x) for x in m[2].split(',')]
        l,rr=min(v[0],v[2]),max(v[0],v[2]); b,tp=min(v[1],v[3]),max(v[1],v[3])
        return f'{m[1]}"{l},{b},{rr},{tp}"'
    return re.sub(r'(bounds: *)"?(-?[0-9.]+ *, *-?[0-9.]+ *, *-?[0-9.]+ *, *-?[0-9.]+)"?',r,t)
def fix_smooth(t):
    # Engine IconSmooth: required key, states list. CP14: key, base, mode, additionalKeys in any order.
    out=[];lines=t.split('\n');i=0
    while i<len(lines):
        m=re.match(r'^( *)- type: IconSmooth$',lines[i])
        if not m: out.append(lines[i]);i+=1;continue
        ind=m[1];i+=1;f={};extra=[];cur=None
        while i<len(lines):
            l=lines[i]
            k=re.match('^'+ind+r'  (\w+):\s*(.*)$',l)
            it=re.match('^'+ind+r'(?:  )?  ?- (\S+)$',l)
            if k: cur=k[1];f[cur]=k[2];i+=1
            elif cur=='additionalKeys' and it and not l.startswith(ind+'- '): extra.append(it[1]);i+=1
            elif cur=='additionalKeys' and it and l.startswith(ind+'- ') and not l.startswith(ind+'- type:'): extra.append(it[1]);i+=1
            else: break
        ren=lambda x:x.replace('CP14','CF')
        out.append(ind+'- type: IconSmooth')
        if 'key' in f: out.append(f'{ind}  key: {ren(f["key"])}')
        if f.get('mode')=='NoSprite': out.append(ind+'  states: []')
        elif 'base' in f:
            mask=[ren(f['key'])]+[ren(x) for x in extra]
            out+= [ind+'  states:',ind+'  - !type:CornerSpriteSmoothing',f'{ind}    mask: [ {", ".join(mask)} ]',f'{ind}    base: {f["base"]}']
    return '\n'.join(out)
def fix_fields(t):
    t=fix_boxes(t)
    t=re.sub(r'^\s*(breakOnMove|cPAnimationLength|cPAnimationOffset|crystallPunkAllowed|followSkinColor|waggingId|staticId): .*\n','',t,flags=re.M)
    t=t.replace('CFWeaponArcThrust','WeaponArcThrust').replace('CFWeaponArcSlash','WeaponArcSlash')
    t=fix_smooth(t)
    return t
def convert(text):
    for a,b in FIXES: text=re.sub(a,b,text)
    return text
def port(text,drop_extra=()):
    for n in DROP|set(drop_extra):
        text=drop_component(text,n)
        text=drop_component(text,n.replace('CP14','CF'))
    text=convert(text); text=fix_damageable(text); return fix_fields(text)

import glob,os
REJECT=re.compile(r'Vampire|Demiplane|Mana\b|ManaOp|Magic(Energy|Crystal|Lantern|Vision)|Religion|Altar|God|Lurker|Sponsor|Trade(Contract)?Platform|Economy|EnergyCrystal|CrystalLamp|Thaumaturgy|Alchemy|Skill|BloodMoon|Mannequin\w*Vampire',re.I)
def known_components():
    names=set()
    for root in ['Content.Shared','Content.Server','Content.Client','RobustToolbox/Robust.Shared','RobustToolbox/Robust.Client','RobustToolbox/Robust.Server']:
        for f in glob.glob(root+'/**/*.cs',recursive=True):
            if True:
                for m in re.findall(r'class (\w+)Component\b',open(f,encoding='utf-8',errors='ignore').read()):
                    names.add(m)
    return names
def existing_ids():
    ids=set()
    for f in glob.glob('Resources/Prototypes/_CF/**/*.yml',recursive=True):
        for b in blocks(open(f,encoding='utf-8',errors='ignore').read()):
            if not re.match(r'\s*- type: entity',b): continue
            m=re.search(r'^\s*id: *(\S+)',b,re.M)
            if m: ids.add(m[1])
    return ids
def strip_unknown(block,known):
    for n in set(re.findall(r'^\s*- type: (CF\w+)',block,re.M)):
        if n not in known: block=drop_component(block,n)
    return block
def port_file(src,dst,known,ids,kinds=('entity','constructionGraph','construction')):
    text=open(src,encoding='utf-8-sig').read()
    out=[];kept=0;dropped=0
    for b in blocks(text):
        m=re.search(r'^\s*(?:- )?id: *(\S+)',b,re.M)
        raw=m[1] if m else ''
        if not b.lstrip().startswith(('- type','#')) or not m:
            continue
        if REJECT.search(raw) or REJECT.search(re.search(r'parent:.*',b)[0] if re.search(r'parent:.*',b) else ''):
            dropped+=1; continue
        nid=convert(raw)
        if nid in ids: continue
        ids.add(nid)
        out.append(strip_unknown(port(b),known)); kept+=1
    if out:
        os.makedirs(os.path.dirname(dst),exist_ok=True)
        open(dst,'w',encoding='utf-8').write('# Ported from CP14 by the CF converter.\n\n'+'\n'.join(x.rstrip()+'\n' for x in out))
    return kept,dropped

def all_entities(root):
    ids={}
    for f in glob.glob(root+'/**/*.yml',recursive=True):
        for b in blocks(open(f,encoding='utf-8',errors='ignore').read()):
            if not re.match(r'\s*- type: entity',b): continue
            m=re.search(r'^\s*id: *(\S+)',b,re.M)
            if m: ids[m[1]]=b
    return ids
def parents_of(b):
    m=re.search(r'^  parent: *(\[.*\]|\S+) *$',b,re.M)
    if m:
        return [x.strip() for x in m[1].strip('[]').split(',') if x.strip()]
    m=re.search(r'^  parent: *\n((?:  - .*\n)+)',b,re.M)
    return [x.strip()[2:].strip() for x in m[1].splitlines()] if m else []
def set_parents(b,ps):
    new=('  parent: ['+', '.join(ps)+' ]\n') if ps else ''
    b2=re.sub(r'^  parent: *(\[.*\]|\S+) *\n','@@P@@',b,count=1,flags=re.M)
    if '@@P@@' not in b2: b2=re.sub(r'^  parent: *\n((?:  - .*\n)+)','@@P@@',b,count=1,flags=re.M)
    return b2.replace('@@P@@',new)
def close_parents(ported_dir,deps_file,known):
    """Bring in parents from CP14, and drop parents that cannot exist."""
    cp=all_entities(CP14+'/Resources/Prototypes/_CP14')
    for _ in range(12):
        have=set(all_entities('Resources/Prototypes'))
        added=[];missing=set()
        for f in glob.glob(ported_dir+'/**/*.yml',recursive=True):
            for b in blocks(open(f,encoding='utf-8-sig').read()):
                for p in parents_of(b):
                    if p not in have: missing.add(p)
        if not missing: return []
        back={convert(k):v for k,v in cp.items()}
        unresolved=[]
        for p in sorted(missing):
            if p in back and not REJECT.search(p):
                added.append(strip_unknown(port(back[p]),known)); have.add(p)
            else: unresolved.append(p)
        if added:
            with open(deps_file,'a',encoding='utf-8') as o: o.write('\n'+'\n'.join(x.rstrip()+'\n' for x in added))
        if unresolved:
            for f in glob.glob(ported_dir+'/**/*.yml',recursive=True):
                t=open(f,encoding='utf-8-sig').read(); out=[]
                for b in blocks(t):
                    ps=parents_of(b)
                    if any(x in unresolved for x in ps):
                        b=set_parents(b,[x for x in ps if x not in unresolved])
                    out.append(b)
                open(f,'w',encoding='utf-8').write('\n'.join(out))
        if not added: return unresolved
    return []

MAP_COMP={'Drink':'Edible\n    edible: Drink','Food':'Edible'}
def clean_dir(d,known):
    gone={}
    for f in glob.glob(d+'/**/*.yml',recursive=True):
        t=open(f,encoding='utf-8-sig').read(); u=t
        for n in set(re.findall(r'^[ ]+- type: (\w+)\s*(?:#.*)?$',t,re.M)):
            if n in ('entity','constructionGraph','construction','entityTable','ContainerContainer'): continue
            if n in MAP_COMP:
                u=re.sub(r'^(\s*)- type: '+n+r'\b',lambda m:m[1]+'- type: '+MAP_COMP[n].replace('\n    ','\n'+m[1]+'  '),u,flags=re.M); continue
            if n not in known:
                u=drop_component(u,n); gone[n]=gone.get(n,0)+1
        u=re.sub(r'^  components: *\n(?=(?:[ ]*\n)*(?:- |#|\Z))','',u,flags=re.M)
        if u!=t: open(f,'w',encoding='utf-8').write(u)
    return gone

def existing_keys():
    keys=set()
    for f in glob.glob('Resources/Prototypes/_CF/**/*.yml',recursive=True):
        for b in blocks(open(f,encoding='utf-8',errors='ignore').read()):
            k=re.match(r'\s*- type: *(\w+)',b); m=re.search(r'^\s*id: *(\S+)',b,re.M)
            if k and m: keys.add((k[1],m[1]))
    return keys
def port_file2(src,dst,known,keys,extra=None):
    out=[];kept=dropped=0
    for b in blocks(open(src,encoding='utf-8-sig').read()):
        k=re.match(r'\s*(?:#.*\n\s*)*- type: *(\w+)',b); m=re.search(r'^\s*id: *(\S+)',b,re.M)
        if not k or not m: continue
        par=re.search(r'parent:.*',b)
        if REJECT.search(m[1]) or (par and REJECT.search(par[0])): dropped+=1; continue
        key=(k[1],convert(m[1]))
        if key in keys: continue
        keys.add(key)
        b=strip_unknown(port(b),known)
        if extra: b=extra(b)
        out.append(b); kept+=1
    if out:
        os.makedirs(os.path.dirname(dst),exist_ok=True)
        open(dst,'w',encoding='utf-8').write('# Ported from CP14 by the CF converter.\n\n'+'\n'.join(x.rstrip()+'\n' for x in out))
    return kept,dropped
GROUPS={'WoodenPlanks':'CFWoodenPlanks','Stone':'CFStone','Cloth':'CFCloth'}
def fix_graph(b):
    b=re.sub(r'stackGroup: *(\w+)',lambda m:'material: '+GROUPS.get(m[1],m[1] if m[1].startswith('CF') else 'CF'+m[1]),b)
    return b

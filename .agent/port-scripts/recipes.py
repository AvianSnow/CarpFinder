"""CP14 workbench recipes that resolve here -> craftingRecipe plus a CFFormula item. Prints every skip with its reason."""
import re,glob,collections,sys,os
sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); import port as P
C=P.CP14+'/Resources/Prototypes/_CP14'
D='Resources/Prototypes/_CF'
OUT=D+'/Crafting/recipes_ported.yml'; FOUT=D+'/Entities/Objects/Formulas/formulas_ported.yml'
for f in (OUT,FOUT):
    if os.path.exists(f): os.remove(f)
ents={};stacks=set();mats=set()
for f in glob.glob(D+'/**/*.yml',recursive=True)+glob.glob('Resources/Prototypes/Entities/**/*.yml',recursive=True):
    for b in re.split(r'(?m)^(?=- type: )',open(f,encoding='utf-8-sig').read()):
        i=re.search(r'(?m)^  id: (\S+)',b)
        if not i: continue
        if b.startswith('- type: entity'): ents[i[1]]=b
        if b.startswith('- type: stack'): stacks.add(i[1])
        if b.startswith('- type: material'): mats.add(i[1])
have=open(D+'/Crafting/recipes.yml').read()
ids=set(re.findall(r'(?m)^  id: (\S+)',have)); results=set(re.findall(r'(?m)^  result: (\S+)',have))
MAP={'CP14WeaponShieldWooden':'CFWoodenShield','CP14WeaponTowerShieldWooden':'CFTowerShield','CP14BaseCrossbow':'CFHeavyCrossbow','CP14BaseLightCrossbow':'CFCrossbow','CP14CrossboltIron':'CFBolt','CP14BowCombat':'CFShortbow'}
GROUPS={'WoodenPlanks':'CFWoodenPlanks','Stone':'CFStone','Cloth':'CFCloth'}
PRICE={0:50,1:100,2:200,3:300,4:500,5:800,6:1300,7:1800,8:2500,9:3000,10:4000}
def cv(x): return re.sub(r'^CP14','CF',x)
def name(i):
    seen=set()
    while i and i in ents and i not in seen:
        seen.add(i); b=ents[i]
        n=re.search(r'(?m)^  name: "?(.*?)"?\s*$',b)
        if n: return n[1]
        p=re.search(r'(?m)^  parent: *\[? *(\w+)',b); i=p[1] if p else None
    return None
def level(i):
    b=ents.get(i,''); m=re.search(r'type: CFItem\n    level: (\d+)',b)
    return int(m[1]) if m else 0
rec=[];form=[];skip=collections.Counter();why=collections.defaultdict(list)
def no(reason,r): skip[reason]+=1; why[reason].append(r)
for f in sorted(glob.glob(C+'/Recipes/Workbench/**/*.yml',recursive=True)):
    for b in re.split(r'(?m)^(?=- type: )',open(f,encoding='utf-8-sig').read()):
        if not b.startswith('- type: CP14Recipe\n'): continue
        tag=re.search(r'(?m)^  tag: (\S+)',b)[1]; res=re.search(r'(?m)^  result: (\S+)',b)[1]
        rid=re.search(r'(?m)^  id: (\S+)',b)[1].replace('CP14','')
        short=res.replace('CP14','')
        if 'Vampire' in tag: no('vampire',short); continue
        if 'Cooking' in tag: no('cooking has no station',short); continue
        out=MAP.get(res,cv(res))
        if out in results or rid in ids: no('already have',short); continue
        if out not in ents: no('result not ported',short); continue
        reqs=[];bad=None
        for m in re.finditer(r'(?m)^  - !type:(\w+)\n((?:    .*\n)*)',b):
            kind,body=m[1],m[2]
            cnt=re.search(r'count: (\d+)',body); cnt=int(cnt[1]) if cnt else 1
            if kind=='StackResource':
                s=cv(re.search(r'stack: (\S+)',body)[1])
                if s not in stacks: bad='stack '+s
                reqs.append(f'  - !type:CFStackRequirement\n    stack: {s}\n    count: {cnt}\n')
            elif kind=='StackGroupResource':
                s=GROUPS.get(re.search(r'group: (\S+)',body)[1])
                if s not in stacks: bad='group '+str(s)
                reqs.append(f'  - !type:CFStackRequirement\n    stack: {s}\n    count: {cnt}\n')
            elif kind=='ProtoIdResource':
                s=cv(re.search(r'protoId: (\S+)',body)[1])
                if s not in ents: bad='item '+s
                reqs.append(f'  - !type:CFItemRequirement\n    protoId: {s}\n    count: {cnt}\n')
            elif kind=='MaterialResource':
                s=cv(re.search(r'material: (\S+)',body)[1])
                if s not in mats: bad='material '+s
                reqs.append(f'  - !type:CFMaterialRequirement\n    material: {s}\n    count: {cnt}\n')
            else: bad=kind
        if bad: no('needs '+bad,short); continue
        ct=re.search(r'(?m)^  craftTime: (\S+)',b); rc=re.search(r'(?m)^  resultCount: (\d+)',b)
        ids.add(rid); results.add(out)
        rec.append(f'- type: craftingRecipe\n  id: {rid}\n  tag: {tag.replace("CP14","CF")}\n  craftTime: {ct[1] if ct else 1}\n  requirements:\n'+''.join(reqs)+f'  result: {out}\n'+(f'  resultCount: {rc[1]}\n' if rc else ''))
        lv=level(out); nm=name(out) or re.sub(r'(?<!^)(?=[A-Z])',' ',rid).lower()
        price='' if lv==0 else f'  - type: CFItemPrice\n    copper: {PRICE.get(lv,4000)}\n'
        form.append(f'- type: entity\n  parent: CFBaseFormula\n  id: CFFormula{rid}\n  name: formula ({nm})\n  components:\n  - type: CFFormula\n    recipes:\n    - {rid}\n'+price)
open(OUT,'w').write('# CP14 workbench recipes. Materials and craft times are CP14\'s. Stack groups use the plain material.\n\n'+'\n'.join(rec))
open(FOUT,'w').write('# One formula per recipe in Crafting/recipes_ported.yml.\n\n'+'\n'.join(form))
print('recipes',len(rec))
for k,v in skip.most_common(): print(k,v,sorted(set(why[k]))[:8])

"""CP14's produce.yml -> Entities/Objects/Farming/produce.yml: whole vegetables, their slices, and the tags cooking reads.
Prices, Bulk and CFSeed are ours and are kept here, because CP14 has none."""
import re,sys,os
sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); import port as P
SRC=P.CP14+'/Resources/Prototypes/_CP14/Entities/Objects/Consumable/Food/produce.yml'
OUT='Resources/Prototypes/_CF/Entities/Objects/Farming/produce.yml'
# Copper for a whole one. A slice is priced by the cleanup from what it is cut from.
PRICE={'CFFoodCabbage':3,'CFFoodPumpkin':5,'CFFoodPotato':2,'CFFoodCucumber':2,'CFFoodTomatoes':2,'CFFoodPepper':2,'CFFoodOnion':2,'CFFoodApple':2}
# Planted whole.
SEED={'CFFoodPotato':'CFPlantPotato','CFFoodOnion':'CFPlantOnion'}
# Stock's Fruit tag is what a brewing barrel makes wine from.
EXTRA_TAGS={'CFFruits':'Fruit'}
known=P.known_components()
def slice_fix(b):
    def r(m):
        body=m[2]
        c=re.search(r'count: *(\d+)',body); s=re.search(r'slice: *(\S+)',body); t=re.search(r'sliceTime: *(\S+)',body)
        i=m[1]
        out=f'{i}- type: ToolRefinable\n{i}  qualityNeeded: Slicing\n{i}  verbText: refined-slice-verb-name\n'
        if t: out+=f'{i}  refineTime: {t[1]}\n'
        return out+f'{i}  refineResult:\n{i}  - id: {s[1]}\n{i}    amount: {c[1] if c else 1}\n'
    return re.sub(r'(?m)^( *)- type: SliceableFood *\n((?:\1  .*\n)*)',r,b)
def solution_fix(b):
    # FoodInjectableBase already names the solution "food".
    def r(m):
        body=re.sub(r'(?m)^      ','    ',m[2])
        return '  - type: Solution\n    solution:\n'+body
    return re.sub(r'(?m)^  - type: SolutionContainerManager *\n    solutions: *\n      food: *\n((?:        .*\n)*)',lambda m:r((None,None,m[1])),b)
out=[]
for b in P.blocks(open(SRC,encoding='utf-8-sig').read()):
    m=re.search(r'(?m)^  id: *(\S+)',b)
    if not m: continue
    b=slice_fix(b.rstrip()+'\n')
    b=P.drop_component(b,'FoodSequenceElement')
    b=P.strip_unknown(P.port(b),known)
    b=solution_fix(b)
    # This engine's Temperature has no currentTemperature, and InternalTemperature none of CP14's fields.
    b=re.sub(r'(?m)^    currentTemperature: .*\n','',b)
    b=P.drop_component(b,'InternalTemperature')
    i=P.convert(m[1]) if hasattr(P,'convert') else m[1].replace('CP14','CF')
    for a,t in EXTRA_TAGS.items():
        b=re.sub(r'(?m)^(\s*)- '+a+r' *\n',lambda k:k[0]+f'{k[1]}- {t}\n',b)
    add=''
    if i in PRICE: add+=f'  - type: CFItemPrice\n    copper: {PRICE[i]}\n'
    if i in SEED and 'type: CFSeed' not in b: add+=f'  - type: CFSeed\n    plantProto: {SEED[i]}\n'
    out.append(b.rstrip()+'\n'+add)
open(OUT,'w').write('# Vegetables and their slices, from CP14 through .agent/port-scripts/produce.py. Put fixes in the script.\n\n'+'\n'.join(out))
print('produce',len(out))

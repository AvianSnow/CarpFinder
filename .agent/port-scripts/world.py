import sys,glob,os,shutil,re
sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); import port as P
D='Resources/Prototypes/_CF/Ported'
shutil.rmtree(D,ignore_errors=True)
known=P.known_components(); keys=P.existing_keys()
B=P.CP14+'/Resources/Prototypes/_CP14/'
def food_fix(b):
    b=P.drop_component(b,'SliceableFood')
    b=P.drop_component(b,'FoodSequenceElement')
    return b
def prune_metabolisms(b):
    # Drop stages whose effects were all removed, then metabolisms itself if it is empty.
    L=b.split('\n'); out=[]; i=0
    while i<len(L):
        if L[i].rstrip()!='  metabolisms:': out.append(L[i]); i+=1; continue
        i+=1; kept=[]
        while i<len(L) and (L[i].startswith('    ')):
            st=[L[i]]; i+=1
            while i<len(L) and L[i].startswith('     '): st.append(L[i]); i+=1
            if any('- !type:' in x for x in st): kept+=st
        if kept: out.append('  metabolisms:'); out+=kept
    return '\n'.join(out)
def reagent_fix(b):
    b=re.sub(r'^(\s*)Food:\n',r'\1Digestion:\n',b,flags=re.M); b=re.sub(r'^(\s*)Drink:\n',r'\1Digestion:\n',b,flags=re.M)
    b=re.sub(r'^( *)- !type:SatiateHunger',r'\1- !type:Satiate\n\1  satiationType: Hunger',b,flags=re.M)
    b=re.sub(r'^( *)- !type:SatiateThirst',r'\1- !type:Satiate\n\1  satiationType: Thirst',b,flags=re.M)
    b=b.replace('!type:ChemVomit','!type:Vomit')
    # Effects: only hunger, thirst and vomiting stay. CP14's healing, mana and stamina effects are removed.
    lines=b.split('\n'); out=[]; skip=None
    for l in lines:
        ind=len(l)-len(l.lstrip())
        if skip is not None:
            if l.strip()=='' or ind>skip: continue
            skip=None
        m=re.match(r'\s*- !type:(\w+)',l)
        if m and m[1] not in ('Satiate','Vomit'): skip=ind; continue
        if l.strip()=='conditions:': skip=ind; continue
        out.append(l)
    b='\n'.join(out)
    return prune_metabolisms(b)
tot=[0,0]
def run(pattern,dst,skip=(),extra=None):
    for f in sorted(glob.glob(B+pattern,recursive=True)):
        if any(s in f for s in skip): continue
        k,x=P.port_file2(f,D+'/'+dst+'/'+f[len(B):].replace('/','_'),known,keys,extra); tot[0]+=k; tot[1]+=x
run('Stacks/*.yml','Stacks',('coins','stack_groups','medical'))
run('Materials/*.yml','Materials')
run('Flavors/*.yml','Flavors')
run('Reagents/Consumable/**/*.yml','Reagents',extra=reagent_fix)
run('Reagents/blood.yml','Reagents',extra=reagent_fix)
run('Reagents/precurser.yml','Reagents',extra=lambda b: re.sub(r'^  metabolisms:\n(?:    .*\n|\s*\n)*','',reagent_fix(b),flags=re.M))
run('Entities/Objects/Bureaucracy/**/*.yml','Objects/Bureaucracy')
run('Entities/Objects/Carpet/*.yml','Objects/Carpet')
run('Entities/Objects/Fun/**/*.yml','Objects/Fun')
run('Entities/Objects/Misc/*.yml','Objects/Misc',('amulet','monster_parts','halloween'))
run('Entities/Objects/Tools/*.yml','Objects/Tools',('flashlight','sharpening','flint'))
run('Entities/Objects/Consumable/Drinks/*.yml','Objects/Drinks',extra=food_fix)
run('Entities/Objects/Consumable/Food/**/*.yml','Objects/Food',('produce.yml',),extra=food_fix)
run('Recipes/Construction/*.yml','Recipes',('vampire',),extra=P.fix_graph)
run('Recipes/Construction/Graphs/**/*.yml','Recipes/Graphs',('Vampire',),extra=P.fix_graph)
run('Decal/**/*.yml','Decals')
run('Palettes/*.yml','Palettes')
print('kept,dropped',tot)
print('unresolved',P.close_parents(D,D+'/_deps.yml',known))
print('dropped',P.clean_dir(D,known))

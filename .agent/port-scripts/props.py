import sys,glob,os,shutil
sys.path.insert(0,os.path.dirname(os.path.abspath(__file__))); import port as P
D='Resources/Prototypes/_CF/Entities/Structures/Ported'
shutil.rmtree(D,ignore_errors=True)
known=P.known_components(); ids=P.existing_ids()
print('known',len(known),'Sprite' in known,'PointLight' in known,'Icon' in known)
B=P.CP14+'/Resources/Prototypes/_CP14/'
tot=[0,0]
for d in ['Furniture','Storage','Decorations','Fence','Roof','Windows','Doors','Flora','Walls']:
    for f in sorted(glob.glob(B+'Entities/Structures/'+d+'/**/*.yml',recursive=True)):
        if '/Gatherable/' in f: continue
        k,x=P.port_file(f,D+'/'+f.split('Entities/Structures/')[1],known,ids); tot[0]+=k; tot[1]+=x
for n in ['well.yml','lighthouse.yml']:
    k,x=P.port_file(B+'Entities/Structures/'+n,D+'/'+n,known,ids); tot[0]+=k; tot[1]+=x
print('kept,dropped',tot)
print('unresolved',P.close_parents(D,D+'/_deps.yml',known))
print('dropped comps',P.clean_dir(D,known))

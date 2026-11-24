import sys,json,urllib.request,re
def fetch(name):
    q={"size":6,"query":{"bool":{"must":[{"match_phrase":{"name":name}},{"term":{"category":"creature"}}]}},"_source":["name","source","level","text","hp","ac","fortitude_save","reflex_save","will_save","perception","sense","immunity","weakness_raw","resistance_raw","speed_raw","strength","dexterity","constitution","intelligence","wisdom","charisma","trait","size","skill_mod","language"]}
    r=urllib.request.Request('https://elasticsearch.aonprd.com/aon/_search',json.dumps(q).encode(),{'Content-Type':'application/json','User-Agent':'Mozilla/5.0'})
    hits=[h['_source'] for h in json.load(urllib.request.urlopen(r,timeout=60))['hits']['hits']]
    hits=[h for h in hits if h['name'].lower()==name.lower()]
    pref=[h for h in hits if any('Monster Core' in s or 'NPC Core' in s for s in h.get('source',[]))]
    return (pref or hits or [None])[0]
for n in sys.argv[1:]:
    h=fetch(n)
    if not h: print('##',n,'NOT FOUND'); continue
    t=h['text']; i=t.find('Perception')
    print('##',h['name'],'L',h.get('level'),h.get('source'),h.get('size'),h.get('trait'))
    print('  AC',h.get('ac'),'HP',h.get('hp'),'F',h.get('fortitude_save'),'R',h.get('reflex_save'),'W',h.get('will_save'),'Perc',h.get('perception'),'|',h.get('sense'))
    print('  abil',[h.get(k) for k in ('strength','dexterity','constitution','intelligence','wisdom','charisma')],'skills',h.get('skill_mod'),'speed',h.get('speed_raw'),'lang',h.get('language'))
    print('  imm',h.get('immunity'),'weak',h.get('weakness_raw'),'res',h.get('resistance_raw'))
    j=t.find('Speed',i)
    print('  ',t[j:j+int(1100)].replace('\n',' '))

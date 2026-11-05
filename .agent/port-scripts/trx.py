import sys,re,collections,xml.etree.ElementTree as E
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
for u in E.parse(sys.argv[1]).getroot().findall('.//t:UnitTestResult',ns):
    print(u.get('outcome'),u.get('testName'))
    if u.get('outcome')!='Passed':
        m=u.find('.//t:Message',ns); t=m.text if m is not None else ''
        if u.get('testName')=='SpawnAndDirtyAllEntities':
            e=re.findall(r'\[(?:ERRO|FATL)\] (.*?)(?: Exception:|$)',t,re.M)
            c=collections.Counter(re.sub(r"State '[^']*'","State X",x)[:140] for x in e)
            for k,v in c.most_common(12): print('   ',v,k)
            if not e: print('   ',t[:600])
        else: print('   ',t[:700])

"""How much of the character options really work. Run from the repo root: python3 .agent/port-scripts/feature_scan.py

A feat works if CFCharacterSpawnSystem names it, or if it only needs data the game already applies (skills, lores,
Speed, a natural attack). Needs PyYAML. Update the numbers in roadmap.md from what this prints.
"""
import glob
import re
from collections import Counter, defaultdict

import yaml

P = 'Resources/Prototypes/_CF'


class Loader(yaml.SafeLoader):
    pass


Loader.add_multi_constructor('!', lambda l, s, n: l.construct_mapping(n, deep=True) if isinstance(n, yaml.MappingNode) else None)

docs = defaultdict(list)
for path in glob.glob(P + '/Character/**/*.yml', recursive=True):
    with open(path, encoding='utf-8-sig') as f:
        for entry in yaml.load(f, Loader) or []:
            if isinstance(entry, dict):
                docs[entry.get('type')].append(entry)

# A feat's description is a key, so the words are in the .ftl.
strings = {}
for path in glob.glob('Resources/Locale/en-US/_CF/*.ftl'):
    with open(path, encoding='utf-8-sig') as f:
        for line in f:
            if ' = ' in line:
                key, value = line.rstrip('\n').split(' = ', 1)
                strings[key] = value

with open('Content.Server/_CF/Character/CFCharacterSpawnSystem.cs', encoding='utf-8') as f:
    coded = set(re.findall(r'case "(CF\w+)"', f.read()))

totals = Counter()
owners = defaultdict(lambda: [0, 0])
for feat in docs['cfFeat']:
    kind = 'ancestry' if feat.get('ancestry') else 'general' if feat.get('category') else 'class'
    has_data = any(feat.get(k) for k in ('trainedSkills', 'lores', 'speedBonus', 'naturalDie'))
    text = strings.get(feat.get('description'), '')
    placeholder = 'implemented later' in text or 'TBA' in text
    state = 'works' if feat['id'] in coded or (has_data and not placeholder) else 'partly' if has_data else 'nothing'
    totals[kind, state] += 1
    owner = owners[kind, str(feat.get('class') or feat.get('ancestry'))]
    owner[1] += 1
    owner[0] += state == 'works'

print('Feats:', dict(sorted(totals.items())))
for kind in ('ancestry', 'class'):  # General feats have no owner to list by.
    print(' ', kind + ':', ' '.join(f'{k[1]}={v[0]}/{v[1]}' for k, v in sorted(owners.items()) if k[0] == kind))

features = [x for c in docs['cfClass'] for x in c.get('features') or []]
later = [x for x in features if 'implemented later' in str(x.get('description', ''))]
print('Class features:', len(features), 'placeholders:', len(later))

fields = ('darkvision', 'hitPoints', 'upgradeVision', 'size', 'speed', 'grants')
print('Heritages:', len(docs['cfHeritage']), 'with an effect:', sum(any(k in h for k in fields) for h in docs['cfHeritage']))

stubs = []
for path in glob.glob(P + '/Spells/[0-9]/*.yml'):
    with open(path, encoding='utf-8-sig') as f:
        effects = re.findall(r'!type:(CF\w+)', f.read())
    if all(e in ('CFPopupEffect', 'CFSoundEffect', 'CFSpawnEffect') for e in effects):
        stubs.append(path.replace('\\', '/').split('/')[-1][2:-4])
print('Spells:', len(docs['cfSpell']), 'with no real effect:', sorted(stubs))

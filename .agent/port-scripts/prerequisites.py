"""Turns feat prerequisites the game can check into data. Run from the repo root, after general_feats.py:

    python3 .agent/port-scripts/prerequisites.py

For each cfFeat it reads the `prerequisites` text and writes, where it can:
  requiresFeats     another feat by name (any one of the ids with that name)
  requiresAbility   "Strength +2" and the like
  requiresHeritage  "dragonscaled kobold heritage" and the like
Skill ranks are already requiresSkill, and subclass picks requiresChoice. Anything else stays text only.
It rewrites the three fields each run, so it is safe to run again.
"""
import glob
import re

FEATS = 'Resources/Prototypes/_CF/Character/Feats'
HERITAGES = 'Resources/Prototypes/_CF/Character/Ancestry/heritages.yml'
LOCALE = 'Resources/Locale/en-US/_CF'
ABILITIES = ('Strength', 'Dexterity', 'Constitution', 'Intelligence', 'Wisdom', 'Charisma')


def strings():
    found = {}
    for path in glob.glob(LOCALE + '/*.ftl'):
        with open(path, encoding='utf-8-sig') as f:
            for line in f:
                if ' = ' in line:
                    key, value = line.rstrip('\n').split(' = ', 1)
                    found[key] = value.strip()
    return found


def ids_by_name(paths, kind, text):
    found = {}
    for path in paths:
        with open(path, encoding='utf-8-sig') as f:
            content = f.read().replace('\r\n', '\n')
        for match in re.finditer(r'- type: ' + kind + r'\n  id: (\S+)\n  name: (\S+)', content):
            name = text.get(match[2], '').lower()
            if name:
                found.setdefault(name, []).append(match[1])
    return found


def parse(prerequisite, feats, heritages):
    """The fields one line of prerequisite text gives, as (field, value) pairs."""
    result = []
    for part in prerequisite.split(';'):
        part = part.strip().strip('"')
        low = part.lower()
        ability = re.fullmatch(r'(\w+) \+(\d)', part)
        if ability and ability[1] in ABILITIES:
            result.append(('ability', (ability[1], int(ability[2]))))
            continue

        options = [option.strip() for option in re.split(r'\s+or\s+', low)]
        if all(option in feats for option in options):
            result.append(('feats', [fid for option in options for fid in feats[option]]))
            continue

        if low.endswith(' heritage'):
            names = [option.removesuffix(' heritage').strip() for option in re.split(r'\s+or\s+', low)]
            if all(name in heritages for name in names):
                result.append(('heritage', [hid for name in names for hid in heritages[name]]))
    return result


def main():
    text = strings()
    feat_paths = glob.glob(FEATS + '/**/*.yml', recursive=True)
    feats = ids_by_name(feat_paths, 'cfFeat', text)
    heritages = ids_by_name([HERITAGES], 'cfHeritage', text)
    counts = {'feats': 0, 'ability': 0, 'heritage': 0}

    for path in feat_paths:
        with open(path, 'rb') as f:
            raw = f.read().decode('utf-8')
        bom = raw.startswith('﻿')
        crlf = '\r\n' in raw
        content = raw.lstrip('﻿').replace('\r\n', '\n')

        # Drop what an earlier run wrote.
        content = re.sub(r'  requires(Feats|Heritage):\n(  +- .*\n)+', '', content)
        content = re.sub(r'  requiresAbility:\n(    \w+: \d+\n)+', '', content)

        def annotate(match):
            block = match[0]
            lines = re.findall(r'^ *- (.*)$', match[2], re.M)
            fields = {'feats': [], 'ability': {}, 'heritage': []}
            for line in lines:
                for kind, value in parse(line, feats, heritages):
                    if kind == 'ability':
                        fields['ability'][value[0]] = value[1]
                    else:
                        fields[kind] += [v for v in value if v not in fields[kind]]
            extra = ''
            if fields['feats']:
                counts['feats'] += 1
                extra += '  requiresFeats:\n' + ''.join(f'    - {v}\n' for v in fields['feats'])
            if fields['ability']:
                counts['ability'] += 1
                extra += '  requiresAbility:\n' + ''.join(f'    {k}: {v}\n' for k, v in fields['ability'].items())
            if fields['heritage']:
                counts['heritage'] += 1
                extra += '  requiresHeritage:\n' + ''.join(f'    - {v}\n' for v in fields['heritage'])
            return block + extra

        content = re.sub(r'(  prerequisites:\n)((?:  +- .*\n)+)', annotate, content)
        if crlf:
            content = content.replace('\n', '\r\n')
        with open(path, 'wb') as f:
            f.write((('﻿' if bom else '') + content).encode('utf-8'))

    print('feats with each kind:', counts)


if __name__ == '__main__':
    main()

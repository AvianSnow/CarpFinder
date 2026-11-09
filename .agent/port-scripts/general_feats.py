"""Imports general and skill feats through level 4 from Archives of Nethys. Run from the repo root.

It rewrites Resources/Prototypes/_CF/Character/Feats/General/feats.yml and Resources/Locale/en-US/_CF/feats-general.ftl,
so put fixes here and not in those files.
Only Remaster core books, and no archetype feats. Summaries are the site's one line summaries of ORC licensed rules.
"""
import json
import re
import sys
import urllib.request

SOURCES = ('Player Core', 'Player Core 2', 'GM Core')
OUT = 'Resources/Prototypes/_CF/Character/Feats/General/feats.yml'
LOCALE = 'Resources/Locale/en-US/_CF/feats-general.ftl'
RANKS = {'trained': 'Trained', 'expert': 'Expert', 'master': 'Master', 'legendary': 'Legendary'}
SKILLS = ('Acrobatics', 'Arcana', 'Athletics', 'Crafting', 'Deception', 'Diplomacy', 'Intimidation', 'Lore', 'Medicine',
          'Nature', 'Occultism', 'Performance', 'Religion', 'Society', 'Stealth', 'Survival', 'Thievery')
ACTIONS = {'Single Action': 'Action', 'Two Actions': 'Action', 'Three Actions': 'Action', 'Reaction': 'Reaction', 'Free Action': 'Free'}


def fetch():
    query = {
        'size': 2000,
        '_source': ['name', 'level', 'trait', 'primary_source', 'prerequisite', 'summary', 'actions'],
        'query': {'bool': {'filter': [{'term': {'type': 'Feat'}}, {'term': {'trait': 'General'}}, {'range': {'level': {'lte': 4}}}]}},
    }
    request = urllib.request.Request(
        'https://elasticsearch.aonprd.com/aon/_search', json.dumps(query).encode(),
        {'Content-Type': 'application/json', 'User-Agent': 'CarpFinder feat import'})
    with urllib.request.urlopen(request, timeout=60) as response:
        return [hit['_source'] for hit in json.load(response)['hits']['hits']]


def feat_id(name):
    return 'CF' + re.sub(r'[^A-Za-z0-9]', '', name.title())


def feat_key(name):
    return 'cf-feat-' + re.sub(r'(?<=[a-z0-9])(?=[A-Z])', '-', feat_id(name)[2:]).lower()


def ftl_value(text):
    """Fluent reads braces as code, and a leading . [ or * as syntax."""
    text = text.replace('{', '\0').replace('}', '{"}"}').replace('\0', '{"{"}')
    if not text:
        return '{""}'
    return '{"' + text[0] + '"}' + text[1:] if text[0] in '.[*' else text


def quote(text):
    return json.dumps(text, ensure_ascii=False)


def clean(text):
    return re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', '', text or '')).strip()


def required_rank(prerequisite):
    """A single skill rank the game can check. Anything more complicated stays text, shown to the player."""
    found = []
    for rank, skill in re.findall(r'\b(trained|expert|master|legendary) in ([A-Z][a-z]+)', prerequisite or '', re.I):
        if skill.title() in SKILLS:
            found.append((skill.title(), RANKS[rank.lower()]))
    return found[0] if len(found) == 1 and ' or ' not in prerequisite and ' one of ' not in prerequisite else None


def main():
    feats = {}
    for feat in fetch():
        traits = feat.get('trait') or []
        if feat.get('primary_source') not in SOURCES or 'Archetype' in traits:
            continue
        feats[feat['name']] = feat

    lines = ['# General and skill feats through level 4. Made by .agent/port-scripts/general_feats.py: edit that, not this.', '']
    locale = []
    for name in sorted(feats):
        feat = feats[name]
        traits = [t for t in feat['trait'] if t not in ('General', 'Skill')]
        prerequisite = clean(feat.get('prerequisite'))
        key = feat_key(name)
        lines += ['- type: cfFeat', f'  id: {feat_id(name)}', f'  name: {key}', f'  description: {key}-desc']
        locale += [f'{key} = {ftl_value(name)}', f'{key}-desc = {ftl_value(clean(feat.get("summary")))}']
        lines.append(f'  category: {"Skill" if "Skill" in feat["trait"] else "General"}')
        lines.append(f'  level: {feat["level"]}')
        if traits:
            lines.append('  traits:')
            lines += [f'  - {quote(t)}' for t in traits]
        if prerequisite:
            lines += ['  prerequisites:', f'  - {quote(prerequisite)}']
        rank = required_rank(prerequisite)
        if rank:
            lines += [f'  requiresSkill: {rank[0]}', f'  requiresRank: {rank[1]}']
        if feat.get('actions') in ACTIONS:
            lines.append(f'  action: {ACTIONS[feat["actions"]]}')
        lines += ['  pendingAction: true', '']

    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines).rstrip('\n') + '\n')

    with open(LOCALE, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(sorted(locale)) + '\n')

    print(len(feats), 'feats', sum('Skill' in f['trait'] for f in feats.values()), 'skill')
    if len(sys.argv) > 1:
        print('\n'.join(sorted(feats)))


if __name__ == '__main__':
    main()

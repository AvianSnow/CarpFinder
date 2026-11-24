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
# Feats that do something, and the YAML that says what. Everything else is written as pendingAction.
WORKING = {
    'Battle Medicine': ['  grants:', '  - !type:CFGrantOption', '    option: BattleMedicine'],
    # Checked by id in CFWorkbenchSystem.BlockedReason: they gate brewing, and add nothing to the feat.
    'Alchemical Crafting': [],
    'Magical Crafting': [],
    'Multilingual': ['  choices:', '  - kind: Language', '    count: 2'],
    'Arcane Sense': ['  grants:', '  - !type:CFGrantSpell', '    spell: DetectMagic'],
    'Armor Proficiency': ['  grants:', '  - !type:CFGrantArmorProficiency'],
    'Canny Acumen': ['  choices:', '  - kind: SaveOrPerception'],
    'Diehard': ['  grants:', '  - !type:CFGrantOption', '    option: Diehard'],
    'Feather Step': ['  grants:', '  - !type:CFGrantIgnoreTerrain', '    featherStep: true'],
    'Fleet': ['  speedBonus: 5'],
    'Hefty Hauler': ['  grants:', '  - !type:CFGrantOption', '    option: HeftyHauler'],
    'Shield Block': ['  grants:', '  - !type:CFGrantOption', '    option: ShieldBlock'],
    'Skill Training': ['  choices:', '  - kind: Skill'],
    'Toughness': ['  grants:', '  - !type:CFGrantHitPoints', '    perLevel: 1', '  - !type:CFGrantOption', '    option: Toughness'],
    'Weapon Proficiency': ['  grants:', '  - !type:CFGrantProficiency', '    proficiencies: [ MartialWeapons ]', '    rank: Trained'],
    # Trip, the jumps and the right-click skill actions, 24 November 2026.
    'Ancestral Paragon': ['  choices:', '  - kind: Feat', '    featSlot: AncestryFeat', '    featLevel: 1'],
    'Bon Mot': ['  grants:', '  - !type:CFGrantAction', '    action: CFActBonMot'],
    'Dirty Trick': ['  grants:', '  - !type:CFGrantAction', '    action: CFActDirtyTrick'],
    'Fascinating Performance': ['  grants:', '  - !type:CFGrantAction', '    action: CFActFascinatingPerformance'],
    'Cat Fall': ['  grants:', '  - !type:CFGrantSafeFall', '    feetLess: 10'],
    'Continual Recovery': ['  grants:', '  - !type:CFGrantOption', '    option: ContinualRecovery'],
    'Natural Medicine': ['  grants:', '  - !type:CFGrantOption', '    option: NaturalMedicine'],
    'Robust Recovery': ['  grants:', '  - !type:CFGrantOption', '    option: RobustRecovery'],
    'Pickpocket': ['  grants:', '  - !type:CFGrantOption', '    option: Pickpocket'],
    'Forager': ['  grants:', '  - !type:CFGrantOption', '    option: Forager'],
    'Quick Jump': ['  grants:', '  - !type:CFGrantOption', '    option: QuickJump'],
    'Powerful Leap': ['  grants:', '  - !type:CFGrantOption', '    option: PowerfulLeap'],
    'Quick Repair': ['  grants:', '  - !type:CFGrantOption', '    option: QuickRepair'],
    'Acrobatic Performer': ['  grants:', '  - !type:CFGrantOption', '    option: AcrobaticPerformer'],
    'Intimidating Glare': ['  grants:', '  - !type:CFGrantOption', '    option: IntimidatingGlare'],
    'Virtuosic Performer': ['  grants:', '  - !type:CFGrantBonus', '    stat: SkillChecks', '    type: Circumstance', '    value: 1', '    skill: Performance'],
    'Pet': ['  grants:', '  - !type:CFGrantPet', '    kind: Familiar'],
}


# What a working feat does in this game, in place of the site's one line summary.
NOTES = {
    'Acrobatic Performer': 'Perform (right-click yourself) uses Acrobatics when you are better at it than at Performance.',
    'Ancestral Paragon': 'You gain a 1st-level ancestry feat.',
    'Bon Mot': '1 action, within 30 feet: Diplomacy against Will DC. On a success the target takes -2 to Perception and Will saves for a minute, -3 on a critical success.',
    'Cat Fall': 'A fall counts as 10 feet shorter for you, which is one level.',
    'Continual Recovery': 'A creature you Treat Wounds on can be treated again after a sixth of the usual wait.',
    'Dirty Trick': '1 action, in reach: Thievery against Reflex DC. On a success the target is clumsy 1 for 6 seconds, a minute on a critical success.',
    'Fascinating Performance': '1 action, within 30 feet: Performance against Will DC. On a critical success the target is fascinated for 6 seconds.',
    'Forager': 'Subsist (right-click yourself, Forage) never does worse than a success, and you find twice as much food.',
    'Intimidating Glare': 'You Demoralize with a look. You do not take -4 against a creature that does not understand the language you speak.',
    'Natural Medicine': 'Treat Wounds and Battle Medicine use Nature when you are better at it than at Medicine.',
    'Pet': 'You have a pet. Choose it on the Pet tab. It follows you and does not fight.',
    'Pickpocket': 'Steal (right-click a creature) takes from a pocket without the -5 penalty.',
    'Powerful Leap': 'Your Leap and your Long Jump carry 5 feet farther.',
    'Quick Jump': 'High Jump and Long Jump cost 1 action instead of 2.',
    'Quick Repair': 'Repair (right-click a damaged shield) takes 3 seconds instead of 12.',
    'Robust Recovery': 'When you succeed at Treat Poison or Treat Disease, the patient gets +4 to the next save instead of +2.',
    'Virtuosic Performer': '+1 circumstance bonus to Performance checks.',
}


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
        locale += [f'{key} = {ftl_value(name)}', f'{key}-desc = {ftl_value(NOTES.get(name) or clean(feat.get("summary")))}']
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
        lines += WORKING.get(name, ['  pendingAction: true']) + ['']

    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines).rstrip('\n') + '\n')

    with open(LOCALE, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(sorted(locale)) + '\n')

    print(len(feats), 'feats', sum('Skill' in f['trait'] for f in feats.values()), 'skill')
    if len(sys.argv) > 1:
        print('\n'.join(sorted(feats)))


if __name__ == '__main__':
    main()

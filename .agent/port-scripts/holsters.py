"""Lets every CF weapon be holstered on the belt, back or neck, and every shield on the back or neck.

Run from the repo root after adding a weapon or shield:  python3 .agent/port-scripts/holsters.py

It rewrites the Clothing block of each weapon and shield that sets its own Sprite or Clothing, so a child of one
inherits it. A slot whose state is missing from the sprite borrows another carry state (a sword on the belt uses the
back sheath it has for the neck), and a sprite with no carry state at all is simply not drawn while worn.
CFEncumbranceSystem halves the Bulk of a weapon or shield worn in these slots.
"""
import glob
import json
import os
import re

import yaml

WEAPON_SLOTS = ["belt", "back", "neck"]
SHIELD_SLOTS = ["back", "neck"]

# The state the client looks for in each slot, and what to borrow, in order, when it is missing.
NATIVE = {"belt": "equipped-BELT", "back": "equipped-BACKPACK", "neck": "equipped-NECK"}
FALLBACK = {
    "belt": ["equipped-BELT", "equipped-NECK", "equipped-BACKPACK"],
    "back": ["equipped-BACKPACK", "equipped-NECK", "equipped-BELT"],
    "neck": ["equipped-NECK", "equipped-BACKPACK", "equipped-BELT"],
}

ROOT = "Resources/Prototypes/_CF"


class Loader(yaml.SafeLoader):
    pass


Loader.add_multi_constructor("!", lambda loader, suffix, node: None)


def load_entities():
    entities = {}
    for path in glob.glob(f"{ROOT}/**/*.yml", recursive=True):
        try:
            docs = yaml.load(open(path, encoding="utf-8-sig"), Loader=Loader)
        except yaml.YAMLError:
            continue
        if not isinstance(docs, list):
            continue
        for doc in docs:
            if isinstance(doc, dict) and doc.get("type") == "entity" and "id" in doc:
                doc["_file"] = path
                entities[doc["id"]] = doc
    return entities


def parents(entity):
    value = entity.get("parent") or []
    return [value] if isinstance(value, str) else value


def ancestors(entities, entity_id):
    out = []
    for parent in parents(entities.get(entity_id, {})):
        out.append(parent)
        if parent in entities:
            out += ancestors(entities, parent)
    return out


def own(entity, kind):
    for comp in entity.get("components") or []:
        if isinstance(comp, dict) and comp.get("type") == kind:
            return comp
    return None


def resolve(entities, entity_id, kind, field):
    entity = entities.get(entity_id)
    if not entity:
        return None
    comp = own(entity, kind)
    if comp and comp.get(field):
        return comp[field]
    for parent in parents(entity):
        found = resolve(entities, parent, kind, field)
        if found:
            return found
    return None


def has(entities, entity_id, kind):
    entity = entities.get(entity_id)
    if not entity:
        return False
    if own(entity, kind) is not None:
        return True
    return any(has(entities, parent, kind) for parent in parents(entity))


def states(rsi):
    meta = f"Resources/Textures/{rsi}/meta.json"
    if not os.path.exists(meta):
        return set()
    return {s["name"] for s in json.load(open(meta, encoding="utf-8-sig"))["states"]}


def block(indent, rsi, slots):
    pad = " " * indent
    lines = [f"{pad}- type: Clothing", f"{pad}  quickEquip: false"]
    if rsi:
        lines.append(f"{pad}  sprite: {rsi}")
    lines.append(f"{pad}  slots:")
    lines += [f"{pad}  - {slot}" for slot in slots]

    available = states(rsi) if rsi else set()
    borrowed = []
    for slot in slots:
        if NATIVE[slot] in available:
            continue
        state = next((s for s in FALLBACK[slot] if s in available), None)
        if state:
            borrowed.append((slot, state))

    if borrowed:
        lines.append(f"{pad}  clothingVisuals:")
        for slot, state in borrowed:
            lines.append(f"{pad}    {slot}:")
            lines.append(f"{pad}    - sprite: {rsi}")
            lines.append(f"{pad}      state: {state}")
    return lines


def targets(entities):
    found = {}
    for entity_id, entity in entities.items():
        if entity.get("abstract"):
            continue
        if has(entities, entity_id, "MobState") or has(entities, entity_id, "CFCombatant"):
            continue
        if not has(entities, entity_id, "CFWeapon"):
            continue
        if own(entity, "Sprite") is None and own(entity, "Clothing") is None:
            continue
        shield = has(entities, entity_id, "CFShield") or "CFBaseShield" in ancestors(entities, entity_id)
        found[entity_id] = SHIELD_SLOTS if shield else WEAPON_SLOTS
    return found


def rewrite(path, wanted, entities):
    lines = open(path, encoding="utf-8-sig").read().split("\n")
    out = []
    i = 0
    changed = 0
    while i < len(lines):
        line = lines[i]
        if not line.startswith("- type: entity"):
            out.append(line)
            i += 1
            continue

        end = i + 1
        while end < len(lines) and not re.match(r"^- ", lines[end]):
            end += 1
        entity = lines[i:end]
        ids = [re.match(r"^  id: *(\S+)", l) for l in entity]
        entity_id = next((m.group(1) for m in ids if m), None)
        if entity_id not in wanted:
            out += entity
            i = end
            continue

        comps = next((k for k, l in enumerate(entity) if re.match(r"^  components: *$", l)), None)
        item = next((k for k in range(comps + 1, len(entity)) if re.match(r"^ *- type:", entity[k])), None)
        indent = len(entity[item]) - len(entity[item].lstrip())
        rsi = resolve(entities, entity_id, "Clothing", "sprite") or resolve(entities, entity_id, "Sprite", "sprite")
        new = block(indent, rsi, wanted[entity_id])

        start = next((k for k, l in enumerate(entity) if re.match(rf"^ {{{indent}}}- type: Clothing *$", l)), None)
        if start is not None:
            stop = start + 1
            while stop < len(entity) and (
                    not entity[stop].strip()
                    or len(entity[stop]) - len(entity[stop].lstrip()) > indent
                    or (len(entity[stop]) - len(entity[stop].lstrip()) == indent and not entity[stop].lstrip().startswith("- "))):
                stop += 1
            # Keep the blank lines that separated this block from the next entity.
            while stop > start + 1 and not entity[stop - 1].strip():
                stop -= 1
            entity = entity[:start] + new + entity[stop:]
        else:
            last = len(entity)
            while last > 0 and not entity[last - 1].strip():
                last -= 1
            entity = entity[:last] + new + entity[last:]

        out += entity
        changed += 1
        i = end

    open(path, "w", encoding="utf-8").write("\n".join(out))
    return changed


def main():
    entities = load_entities()
    wanted = targets(entities)
    files = sorted({entities[entity_id]["_file"] for entity_id in wanted})
    total = 0
    for path in files:
        mine = {k: v for k, v in wanted.items() if entities[k]["_file"] == path}
        total += rewrite(path, mine, entities)
    print(f"{total} weapons and shields in {len(files)} files")
    for entity_id in sorted(wanted):
        print(f"  {entity_id}: {', '.join(wanted[entity_id])}")


if __name__ == "__main__":
    main()

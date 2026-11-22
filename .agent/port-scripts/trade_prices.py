"""Prices for raw materials and trade goods, in copper (10 cp = 1 sp, 100 cp = 1 gp).

The prices are from Kris's Compendium of Trade Goods by kristkos, in its A5e port by Syrious
(https://github.com/Syrious/kctg-a5e), used with Kris's permission. Kris (Syrious) must be credited, and none of the
module's art may be used. The whole list, with descriptions, is .agent/kctg-trade-goods.txt: look there for a price
before making one up. A 5e coin is taken as the same coin here, so 5 sp is 50 copper.

Each entry is our item, the price of one, and the KCTG good it is priced as. For a stack, give the entity that holds
one: every entity in the same file that has it as parent and its own Stack count gets that many times the price,
because a stack prototype's price is for the whole stack it spawns (`CFCoins.Worth`).

RAW gets `fullResale: true`, which marks raw materials as treasure the dungeon cleanup leaves alone. GOODS does not.

finish.py runs this after food_prices.py, because world.py rewrites the ported files. Run it alone from the repo root
after editing a table. It is safe to run twice.

Left without a price on purpose, because KCTG has no such good or the match would mislead:
  mithril (ore, bar, scrap), scrap metal, scraps of hide, web (KCTG's spider silk is a luxury, ours is a cobweb),
  dyes (only red, 25 gp, and verdigris, 5 gp, are listed, and ours are plain crafting inputs), dirt,
  flora material, compost, wheat, sage, quartz and the elemental shards, floor tiles, carpet, wallpaper, the gold
  goblet (75 gp for something maps scatter about taverns), the crossbow bolt (KCTG's Bolt is a nut and bolt), books
  and paper (KCTG prices its documents at 0), lute, rope, torch, mop, scissors, plates, mugs and bottles.
"""
import re

D = 'Resources/Prototypes/_CF'
M = D + '/Entities/Objects/Materials/'
P = D + '/Ported/Objects/'

RAW = {
    M + 'ores.yml': {
        'CFOreCopper1': (20, 'Copper Ore'),
        'CFOreIron1': (60, 'Iron Ore'),
        'CFOreGold1': (3000, 'Gold Ore'),
        'CFOreCoal1': (5, 'Charcoal'),
    },
    M + 'bars.yml': {
        'CFCopperBar1': (50, 'Copper Ingot'),
        'CFIronBar1': (100, 'Iron Ingot'),
        'CFBrassBar1': (75, 'Pure-Metal Brass Ingot'),
        'CFGoldBar1': (5000, 'Gold Ingot'),
    },
    M + 'hide.yml': {
        'CFThinLeather1': (50, 'Cowhide'),
        'CFLeather1': (100, 'Cured Hide'),
        'CFRuggedLeather1': (250, 'Grain Leather'),
    },
    P + 'Misc/Entities_Objects_Misc_fur.yml': {
        'CFCheapFur': (150, 'Ruined Pelt'),
        'CFFur': (150, 'Fox Hide'),
        'CFGoodFur': (200, 'Saddle Soft Cured Pelt'),
    },
    M + 'misc.yml': {
        'CFAsh1': (1, 'Soot'),
        'CFGlassSheet1': (50, 'Glass'),
        'CFBone1': (1, 'Chicken Bones'),
    },
    M + 'buckle.yml': {
        'CFIronBuckle': (2, 'Buckle'),
    },
    # Cloth unravels into two strings, so it must not be worth less than they are.
    M + 'simple.yml': {
        'CFString': (15, 'Waxed Thread'),
        'CFCloth1': (50, 'Gingham'),
    },
    D + '/Entities/Objects/Farming/fibers.yml': {
        'CFCotton': (50, 'Cotton'),
    },
    M + 'rocks.yml': {
        'CFStoneBlock1': (10, 'Cobblestones'),
        'CFMarbleBlock1': (100, 'Marble'),
    },
    # Plain wood is oak, as its floor tiles are. Spruce is priced as fir, the nearest conifer listed.
    M + 'wood.yml': {
        'CFWoodLog': (20, 'Oak Log'),
        'CFWoodenPlanks1': (25, 'Oak Plank'),
        'CFBirchWoodLog': (17, 'Birch Log'),
        'CFBirchWoodenPlanks1': (20, 'Birch Plank'),
        'CFSpruceWoodLog': (20, 'Fir Log'),
        'CFSpruceWoodenPlanks1': (20, 'Fir Plank'),
        # Lucens inherits from plain wood, and KCTG has nothing like it, so it is priced as oak until it has a
        # price of its own. It is listed so that its bigger stacks are multiplied.
        'CFLucensWoodLog': (20, 'Oak Log'),
        'CFLucensWoodenPlanks1': (25, 'Oak Plank'),
    },
}

GOODS = {
    P + 'Tools/Entities_Objects_Tools_bucket.yml': {
        'CFBucket': (5, 'Bucket'),
    },
    P + 'Tools/Entities_Objects_Tools_fork.yml': {
        'CFWeaponFork': (10, 'Pitchfork'),
    },
    D + '/Entities/Objects/Weapons/_base.yml': {
        'CFShovel': (200, 'Shovel'),
    },
    P + 'Fun/Entities_Objects_Fun_instruments.yml': {
        'CFFluteInstrument': (100, 'Flute'),
        'CFLyraInstrument': (200, 'Lyre'),
    },
    P + 'Fun/Entities_Objects_Fun_dice.yml': {
        'CFd20Dice': (10, 'Bone Dice'),
        'CFd6Dice': (10, 'Bone Dice'),
    },
    P + 'Misc/Entities_Objects_Misc_bell.yml': {
        'CFBell': (200, 'Hand Bell'),
    },
    P + 'Misc/Entities_Objects_Misc_candles.yml': {
        'CFCandle': (1, 'Fat Candles'),
    },
    P + 'Food/Entities_Objects_Consumable_Food_Meals_bowl.yml': {
        'CFBowlWooden': (2, 'Wooden Bowl'),
    },
}

PRICE = re.compile(r'(?m)^( *)- type: CFItemPrice\n(?:\1  .*\n)+')


def coins(copper):
    gp, rest = divmod(copper, 100)
    sp, cp = divmod(rest, 10)
    return ' '.join(f'{n} {unit}' for n, unit in ((gp, 'gp'), (sp, 'sp'), (cp, 'cp')) if n) or '0 cp'


def apply_table(table, full_resale):
    missed = []
    count = 0
    for path, prices in table.items():
        raw = open(path, 'rb').read()
        text = raw.decode('utf-8-sig')
        newline = '\r\n' if '\r\n' in text else '\n'
        text = text.replace('\r\n', '\n')
        blocks = re.split(r'(?m)^(?=- type: )', text)
        found = set()
        for i, block in enumerate(blocks):
            m = re.search(r'(?m)^  id: (\S+)\s*$', block)
            if not m or not block.startswith('- type: entity'):
                continue
            if m[1] in prices:
                copper, source = prices[m[1]]
                found.add(m[1])
                note = f'KCTG: {source}'
            else:
                # A bigger stack of a priced item: its parent's price times its own count.
                parent = re.search(r'(?m)^  parent: (\S+)\s*$', block)
                size = re.search(r'(?m)^ +- type: Stack\n +count: (\d+)\s*$', block)
                if not parent or parent[1] not in prices or not size:
                    continue
                unit, source = prices[parent[1]]
                copper = unit * int(size[1])
                note = f'KCTG: {source}, {size[1]} at {coins(unit)}'
            head = re.search(r'(?m)^  components:\n( +)- type: ', block)
            if not head:
                missed.append((path, m[1], 'no components'))
                continue
            pad = head[1]
            entry = f'{pad}- type: CFItemPrice\n{pad}  copper: {copper} # {note}\n'
            if full_resale:
                entry += f'{pad}  fullResale: true\n'
            if PRICE.search(block):
                block = PRICE.sub(lambda _: entry, block, count=1)
            else:
                block = block.replace('  components:\n', '  components:\n' + entry, 1)
            blocks[i] = block
            count += 1
        missed += [(path, i, 'not found') for i in prices if i not in found]
        out = ''.join(blocks).replace('\n', newline)
        open(path, 'wb').write((b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + out.encode('utf-8'))
    return count, missed


def apply():
    raw, missed = apply_table(RAW, True)
    goods, more = apply_table(GOODS, False)
    missed += more
    print('trade prices', raw + goods, 'missed', missed)
    return missed


if __name__ == '__main__':
    apply()

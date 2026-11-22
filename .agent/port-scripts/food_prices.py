"""Prices for food, in copper (10 cp = 1 sp, 100 cp = 1 gp). finish.py runs this last, because world.py rewrites the
ported food files and would lose prices added by hand. Run it alone from the repo root after editing the table.

Tiers, from the server's price list:
  poor   3-5 cp   raw produce and meat, dough, scraps, slices
  meal   1-3 sp   cooked meat, bread, pies, cheese
  fine   1-3 gp   whole fine cheeses, cured ham, exotic meat, the stuffed pumpkin
Within a tier the number is a judgement call, so not every item costs the same.
"""
import re

D = 'Resources/Prototypes/_CF'
FOOD = D + '/Ported/Objects/Food/Entities_Objects_Consumable_Food_'

PRICES = {
    FOOD + 'Meals_pies_pieces.yml': {
        'CFFoodPiePieceTrash': 3,
        'CFFoodPiePieceMeat': 15,
        'CFFoodPiePiecePumpkin': 12,
        'CFFoodPiePieceVeg': 12,
    },
    FOOD + 'cheese.yml': {
        'CFFoodCheeseWheel': 30,
        'CFFoodCheesePart': 12,
        'CFFoodCheeseSlice': 4,
        'CFFoodBlueCheeseWheel': 160,
        'CFFoodBlueCheesePart': 25,
        'CFFoodBlueCheeseSlice': 5,
        'CFFoodAgedCheeseWheel': 220,
        'CFFoodAgedCheesePart': 30,
        'CFFoodAgedCheeseSlice': 5,
    },
    FOOD + 'dough.yml': {
        'CFFoodDoughLarge': 4,
        'CFFoodBread': 10,
        'CFFoodBreadSlice': 3,
        'CFFoodDoughMedium': 3,
        'CFFoodBreadBun': 5,
        'CFFoodBreadBunBottom': 3,
        'CFFoodBreadBunTop': 3,
        'CFFoodDoughMediumFlat': 3,
    },
    FOOD + 'egg.yml': {
        'CFFoodEgg': 3,
        'CFFoodEggCooked': 10,
    },
    FOOD + 'meat.yml': {
        'CFFoodMeatLamb': 5,
        'CFFoodMeatLambCooked': 20,
        'CFFoodMeatLambSlice': 3,
        'CFFoodMeatLambCookedSlice': 10,
        'CFFoodMeatLambCutlet': 4,
        'CFFoodMeatLambCutletCooked': 18,
        'CFFoodMeatFrogLeg': 4,
        'CFFoodMeatFrogLegCooked': 14,
        'CFFoodMeatHuman': 3,
        'CFFoodMeatHumanCooked': 10,
        'CFFoodMeatDino': 5,
        'CFFoodMeatDinoCooked': 28,
        'CFFoodMeatHydra': 5,
        'CFFoodMeatHydraCooked': 180,
        'CFFoodMeatRabbit': 4,
        'CFFoodMeatRabbitCooked': 16,
        'CFFoodMeatPig': 5,
        'CFFoodMeatPigCooked': 18,
        'CFFoodMeatPigLeg': 5,
        'CFFoodMeatPigLegCooked': 120,
        'CFFoodMeatBoar': 5,
        'CFFoodMeatBoarCooked': 22,
        'CFFoodMeatPigSlice': 3,
        'CFFoodMeatPigCookedSlice': 10,
        'CFFoodMeatMonster': 3,
        'CFFoodMeatMonsterCooked': 12,
        'CFFoodMeatMonsterLeg': 4,
        'CFFoodMeatMonsterLegCooked': 15,
        'CFFoodMeatMonsterSlice': 3,
        'CFFoodMeatMonsterCookedSlice': 10,
        'CFFoodMeatFlemHead': 3,
        'CFFoodMeatFlemHeadCooked': 11,
        'CFFoodMeatFlemTorso': 4,
        'CFFoodMeatFlemTorsoCooked': 14,
        'CFFoodMeatFlemTorsoCut': 3,
        'CFFoodMeatFlemTorsoCookedCut': 18,
        'CFFoodMeatFlemLeg': 4,
        'CFFoodMeatFlemLegCooked': 15,
    },
    FOOD + 'meat_dish.yml': {
        'CFFoodStuffedPumpkin': 250,
    },
    FOOD + 'simple_dish.yml': {
        'CFFoodPotatoHot': 12,
        'CFFoodChromiumJelly': 20,
        'CFFoodSnailShell': 3,
        'CFFoodSnailShellCooked': 15,
    },
    D + '/Entities/Objects/Farming/produce.yml': {
        'CFFoodCabbage': 3,
        'CFFoodPumpkin': 5,
        'CFFoodPotato': 3,
        'CFFoodCucumber': 4,
        'CFFoodTomatoes': 4,
        'CFFoodPepper': 5,
        'CFFoodOnion': 3,
    },
}


def apply():
    missed = []
    for path, prices in PRICES.items():
        raw = open(path, 'rb').read()
        text = raw.decode('utf-8-sig')
        newline = '\r\n' if '\r\n' in text else '\n'
        text = text.replace('\r\n', '\n')
        blocks = re.split(r'(?m)^(?=- type: )', text)
        found = set()
        for i, block in enumerate(blocks):
            m = re.search(r'(?m)^  id: (\S+)\s*$', block)
            if not m or m[1] not in prices:
                continue
            found.add(m[1])
            entry = f'  - type: CFItemPrice\n    copper: {prices[m[1]]}\n'
            if re.search(r'(?m)^  - type: CFItemPrice\n', block):
                block = re.sub(r'(?m)^  - type: CFItemPrice\n    copper: \d+\n', entry, block)
            elif re.search(r'(?m)^  components:\n', block):
                block = re.sub(r'(?m)^  components:\n', '  components:\n' + entry, block, count=1)
            else:
                missed.append((path, m[1], 'no components'))
                continue
            blocks[i] = block
        missed += [(path, i, 'not found') for i in prices if i not in found]
        out = ''.join(blocks).replace('\n', newline)
        open(path, 'wb').write((b'\xef\xbb\xbf' if raw.startswith(b'\xef\xbb\xbf') else b'') + out.encode('utf-8'))
    print('food prices', sum(len(p) for p in PRICES.values()), 'missed', missed)
    return missed


if __name__ == '__main__':
    apply()

# Monsters

A monster is one entity prototype. Copy `Resources/Prototypes/_CF/Entities/Mobs/CFKoboldWarrior.yml` and change the numbers. Spawn it with `spawn CFKoboldWarrior` (use your new id).

The Kobold Warrior is the worked copy of the printed stat block: creature −1, AC 16, 7 HP, Speed 25 feet, spear +3 for 1d6+1 piercing, sling +5 for 1d4 bludgeoning. It uses the kobold texture from `MobBaseKobold` (`Mobs/Animals/Ancestors/kobold.rsi`, state `kobold`).

You do not need the website once you have the numbers. Write the printed AC, HP, level, and ability modifiers straight into the entity. Weapons are separate entities the creature holds.

## Creature

| Printed line | Field |
|---|---|
| Creature level | `CFCombatant.level` (negative is allowed) |
| AC | `CFCombatant.armorClass` — the final number, armor included |
| HP | `CFCombatant.maxHp` and `currentHp` |
| Str, Dex, Con, Int, Wis, Cha | `CFAbilityScore.abilityModifiers` — the modifiers, not scores out of 18 |
| Skills it is trained in | `CFCombatant.trainedSkills` — names such as `Acrobatics`, `Stealth`, `Crafting` |
| Sneak Attack | `CFCombatOptions.sneakAttack: true` |
| Speed | `MovementSpeedModifier`. 25 feet is walk `2.5` and sprint `4.5`. Other speeds multiply those by `feet / 25`. |
| Darkvision | `NightVision`, same block as the kobold |
| Who it hunts | `CFFaction.faction: Monster` and `NpcFactionMember` `SimpleHostile` |
| How it moves and attacks | `InputMover`, `MobMover`, `HTN` task `SimpleHostileCompound`, `CombatMode` |

`Monster` is in `Resources/Prototypes/_CF/Factions/factions.yml`. Members leave each other alone and hunt Alpha, Beta, Charlie, and Delta. A new hostile group is another `cfFaction` with `opposition` listing the factions it fights.

The health bar needs a Critical threshold under the Dead threshold. Set Dead equal to HP and Critical to HP − 1, as the kobold does (Dead 7, Critical 6). At 0 HP the creature falls unconscious and uses the same dying rules as a player.

## Strikes

The hostile AI Strikes with the melee weapon in the active hand. The kobold's gear is `CFKoboldWarriorGear`, which puts `CFSpear` in that hand. The same spear numbers are also on the body, so an empty hand still uses the spear.

A creature with no class is treated as trained. The attack bonus the game rolls is:

`ability + level + 2 + itemBonus`

- Melee ability is Strength. Set `finesse: true` on `CFMeleeWeapon` to use whichever of Strength and Dexterity is higher.
- Ranged ability is Dexterity (`CFRangedWeapon`).
- Melee damage adds Strength. A sling sets `propulsive: true`: half of a positive Strength, or the full penalty when Strength is negative. Other ranged weapons add no Strength.
- `itemBonus` is how you match a printed attack that is not already `ability + level + 2`.

`itemBonus = printed attack − ability − level − 2`

The kobold's spear is Strength +1 at level −1, printed +3: `3 − 1 − (−1) − 2 = 1`. The sling is Dexterity +3, printed +5: `5 − 3 − (−1) − 2 = 1`. Both weapons set `itemBonus: 1`.

Weapon files:

- Melee: copy `Resources/Prototypes/_CF/Entities/Objects/Weapons/CFSpear.yml`. Set `damageDie`, `damageDice`, `damageType`, and `itemBonus`.
- Ranged: copy `Resources/Prototypes/_CF/Entities/Objects/Weapons/CFSling.yml`. Set `damageDie`, `damageType`, `rangeIncrementFeet`, `propulsive`, and `itemBonus`. It still shoots through the bow (wield it and nock an arrow) because there is no sling sprite yet. The hostile AI does not fire it. The creature's melee Strike is the one it uses when it hunts.

Give the creature the weapon with a `startingGear` `inhand` list and a `Loadout` that names that gear. The first item is placed in the active hand. Put the melee weapon first.

## What the printed block does not drive

- Saves and Perception on a creature with no class use the commoner numbers, not the printed Fortitude, Reflex, Will, or Perception.
- A trained skill check is `ability + level + 2`. The kobold's Acrobatics is Dexterity +3 + level −1 + 2 = +4. The printed skill is +5.
- Sneak Attack adds d6s, one die at this level (`max(1, (level + 3) / 4)`). The printed rider is 1d4.
- Scamper, and the thrown spear as its own Strike, are not separate actions. The spear in hand is the melee Strike.

Write those in a comment at the top of the new file when the book has them, so the next creature can see what is still missing.

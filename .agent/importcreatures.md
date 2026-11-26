# Importing Pathfinder 2e creatures

How to turn a printed stat block into a CF monster. The worked example is the Skeleton Guard: `Resources/Prototypes/_CF/Entities/Mobs/Creatures/Undead/skeleton-guard.yml`, tested by `CFSkeletonguardTest`.

Every CF monster thinks with a GOAP brain, which its base gives it. Stock SS14 mobs keep stock HTN. `CFMonsterTest` fails on a monster with no brain or no creature type.

## Steps

1. Check the id is not already under `Creatures/`. Get the stat block from https://2e.aonprd.com/ and put its source and page in a comment above the entity. `www.aonprd.com` is Pathfinder 1e, so find the 2e creature and do not use its numbers: the 1e Giant Spider is the 2e Hunting Spider.
2. Pick a body, and a file in the folder of its creature type (below).
3. Fill in the stat block (the table below). Use printed numbers and do not derive them.
4. Pick the AI root task (below).
5. Give it a ghost role (below), so a player can take it.
6. Write what is not in yet in the comment above the entity, so nobody thinks it was missed.
7. `CFCreatureTest.EveryMonsterPlaysByItsOwnStatBlock` covers it with no change, and compares it with its own YAML, so a monster can be rebalanced freely. Add a test class if the creature has anything more than a natural attack.
8. Check the build printed `Build succeeded`, then run the tests and the linter (`dotnet run --project Content.YAMLLinter -c Tools`).
9. Update the "Still to port" list under "Creatures with PF2e stat blocks" in `roadmap.md`.

## Body and file

Each monster has a file of its own, named after it, in the folder of its creature type under `Resources/Prototypes/_CF/Entities/Mobs/Creatures/`: `Animal/boar.yml`, `Humanoid/kobold-warrior.yml`, `Undead/skeleton-guard.yml`. The folders are Aberration, Animal, Construct, Dragon, Fey, Fiend, Giant, Humanoid, MonstrousHumanoid, Ooze, Outsider, Plant, Spirit and Undead. The same type goes on `CFTrait`, so the folder and the trait agree. Monstrous Humanoid and Outsider are not remaster types; a remaster Beast, Celestial, Elemental, Fungus or Monitor goes in the nearest folder, or a new one. `_base.yml` holds `CFMonsterGhostRole` and `CFBaseCreature`.

| Creature | Parent |
|---|---|
| Animal or beast with one natural attack | `CFBaseCreature`, which is `SimpleMobBase` on top of `CFBaseMonster` |
| Humanoid with hands and weapons | A stock species mob first, such as `MobSkeletonPerson`, then `CFBaseMonster` |

`CFBaseMonster` is what every monster is: GOAP brain, ghost role, Monster faction, combat mode, a `CFCombatant` that dies at 0 Hit Points, `CFAbilityScore`, `CFCombatOptions` and `CFHarvestable`. The monster sets its own numbers on those; it does not need to repeat `diesAtZeroHp` or the faction.

- An entity id starts with `CF`: `CFMobGiantRat`. Variants of one creature share an abstract base, such as `CFMobSkeletonguardBase`, and each sets `name` and `Loadout`.
- When the first parent is a stock mob with its own `HTN`, such as `MobBaseKobold`, nothing needs doing about it: the brain removes the HTN as it initialises (`CFGOAPDriverSystem`).
- `CFBaseMonster` gives `InputMover`, `MobMover`, `NpcFactionMember` with `SimpleHostile`, and `CFFaction` with `Monster`. On a stock body the body's copy wins, so set `NpcFactionMember` and `GhostRole` again on the monster if the body has them.
- Monster parts: `CFBaseMonster` has `CFHarvestable`. List what the body gives in its `parts` (meat, leather, bone; or nothing, for generic monster parts), never a `ToolRefinable` and never `BaseSlicingRefinableButchering`. What they are worth comes from the level.
- Sprites: stock bodies come from the species. Other art stays in its fork folder with its `meta.json`. An RSI with `idle` and `moving` states walks through `SpriteMovement`, and its layer needs the `movement` map as well. See `CFMobHuntingSpider`.

## Ghost role

Every monster can be played. `CFMonsterGhostRole` holds the shared rules and `GhostTakeoverAvailable`. `CFBaseCreature` and `CFMobSkeletonguardBase` already have it as a parent; a creature on any other body adds it as a parent of its own. A stock parent with a `GhostRole` of its own wins over it: a kobold also sets `prob: 1` and `rules`. See `CFKoboldWarrior`.

Each creature sets the name and description a ghost sees, with its text in `ghost-roles.ftl`:

```yaml
- type: GhostRole
  name: cf-ghost-giant-rat-name
  description: cf-ghost-giant-rat-desc
```

The role needs no AI work. The brain sleeps while a player is in the body and wakes when they leave (`CFGOAPDriverSystem`). `CFMonsterGhostRoleTest` fails if a monster has no role or no text.

## Stat block to YAML

| Stat block | Goes on | Notes |
|---|---|---|
| Level, AC, HP | `CFCombatant` `level`, `armorClass`, `maxHp`, `currentHp` | Always set `diesAtZeroHp: true` |
| Fort, Ref, Will, Perception | `CFCombatant` `printedFortitude`, `printedReflex`, `printedWill`, `printedPerception` | |
| Skills | `CFCombatant` `trainedSkills` and `printedSkills` | `printedSkills: { Acrobatics: 6 }`. `printedSkills` wins over rank and `SkillIncreases` |
| Abilities | `CFAbilityScore` `abilityModifiers` | |
| Speed | `CFCombatant` `speedFeet`, and `MovementSpeedModifier` | Walk is feet / 10, sprint is walk x 1.8: 25 feet is 2.5 and 4.5 |
| Melee attack and damage | `CFCombatant` `printedAttack`, `printedDamage` | These replace abilities and proficiency for every melee Strike, weapon or natural, so a creature has one melee number |
| Ranged attack | `CFCombatant` `strikeAdjustment` | Ranged ignores `printedAttack`. Set this to the book's bonus minus Dex minus (level + 2) |
| More than one melee Strike, or a Strike with Grab, Knockdown or Push | `CFMonsterAttacks` `attacks`: each has `name`, `attack` (the printed bonus), `dice`, `die`, `bonus`, `damageType`, `traits` (`Agile`, `Finesse`, `Reach`, `DeadlyD8`...), `followUp` (`Grab`, `Knockdown`, `Push`; `followUpFree` for the improved kind) and `onHit` effects | These numbers replace `printedAttack` and `printedDamage` for that attack. List the main attack first: the AI uses it with no multiple attack penalty and the best of the rest after. An attack with the `Reach` trait reaches 10 feet, and `reachFeet` sets a longer one: the AI uses it on a target the others cannot get to, and the body's swing range follows the chosen attack. A follow-up costs the monster 1 action after a hit. See `CFMobGrizzlyBear` |
| Extra damage or a condition on a hit | `onHit` on the attack, or `CFStrikeRider` for a creature with `natural*` fields | `!type:CFDamageEffect` with `scaleByDegree: false` for "plus 1d4 acid". See `CFMobSewerOoze` |
| A ranged attack of its own, with or without damage (a web, a spit, a thrown rock) | An action parented on `CFActMonsterShotBase` in `Actions/monster_shots.yml`, given with `ActionGrant`. `CFTargeting` `rangeFeet`, `CFCheck` `fixedDc` (the printed attack bonus plus 10), `CFProjectileDelivery` `boltPrototype` for what flies, and `CFDegreeEffects` `landed` for what a hit does | The AI uses it through its Breathe step on a target in range and not within `minFeet` of `CFMonsterShot`. There is no range increment penalty: give it about two increments of range. See `CFActWebShot` |
| A melee Strike with long reach and no damage (a frog's tongue) | The same kind of action, with `CFTargeting` `delivery: Creature` so nothing flies, and `rangeFeet` as its reach | See `CFActTongue` |
| Held on a leash (Tongue Grab) | `!type:CFTetherEffect` with `reachFeet` and `escapeDc` | The creature is grabbed by the monster, so off-guard, but can move as far as the leash. A player pulling against it tries to Escape |
| Stuck until it Escapes (Web Trap) | `!type:CFStickEffect` with `holder` (the entity left on the creature, `CFWebTrap`) and `escapeDc` | The creature is immobilized, not off-guard, and Escape rolls against that DC |
| Natural attack | `CFCombatOptions` `naturalDice`, `naturalDie`, `naturalType`, `naturalAgile`, `naturalFinesse` | Also what a disarmed creature uses |
| Weapons | A `startingGear` with `inhand`, and `Loadout` on the mob | The item supplies dice, type and traits. Use the `CF` weapon, and do not copy its stats to the mob. With a weapon and a shield, list the weapon first, so it is in the active hand that swings |
| Shield | `CFWoodenShield` and so on in `inhand`, and `shieldBlock: true` on `CFCombatOptions` | The brain raises it on seeing an enemy and keeps it up. The printed "AC x (y with shield raised)" is then right with `armorClass` at x |
| Armor | `equipment` in the `startingGear` | For looks and loot. Printed AC already counts it. Check the creature meets its Strength, or it takes a Speed penalty |
| A melee weapon carried as well as a bow | The weapon in a slot, and `CFGOAPStandoff` with `sidearmSlot`. Add `DrawSidearm` to its goals and actions | It draws it when out of arrows or caught in reach. Add `KeepDistance` too for one that backs away. See `CFMobHobgoblinSoldierRanged` |
| Reactive Strike | `reactiveStrike: true` on `CFCombatOptions` | |
| Ammunition | `CFArrowSupply` `arrows` | Leave out the arrow already on the bow, so 20 arrows is `arrows: 19`. Set `ammo: CFBolt` for a crossbow |
| Resistances, weaknesses | `CFResistances` `entries` | `weakness: true` for a weakness |
| Immunity to a damage type | `CFResistances` with `amount: 9999` | Bleed, Mental, Poison, and so on |
| Immunity to a condition | `CFConditionImmunity` `conditions` | Mental means `Frightened`, `Charmed`, `Calmed`, `Confused`, `Controlled`, `Fleeing`. Unconscious means `Asleep` |
| Immunity to a kind of effect: mental, poison, disease, death effects, sleep, visual | `CFConditionImmunity` `traits` | Any effect with one of the traits does nothing, and an affliction with one never takes hold. Add the damage type to `CFResistances` as well |
| Immunity to critical hits, to precision | `CFConditionImmunity` `criticalHits: true`, `precision: true` | A critical hit deals a hit's damage |
| A poison or disease on its Strike | `CFStrikeRider` with `!type:CFExposeAfflictionEffect`, and a `cfAffliction` in `Afflictions/monsters.yml` | Stages printed in rounds use `seconds` (a round is 6), `maxSeconds` is the maximum duration |
| Void healing | `CFVoidHealing`, and `Void` at 9999 in `CFResistances` | Harm and void heal it, and Heal and vitality hurt it |
| Resistance to all damage | A `CFResistances` entry with `all: true`, `amount`, and `except` for the types that get through | For incorporeal creatures: `except: [ Force ]` |
| Fast healing, regeneration | `CFFastHealing` `perPulse`, every 6 seconds. `deactivatedBy` is a list of damage types that stop it for 6 seconds | Regeneration's "cannot die while it is active" is not built |
| A condition it always has | `CFInnateConditions` `conditions`, such as `Slowed: 1` for a zombie | Only conditions kept by value: slowed, quickened, enfeebled, fatigued and the like |
| Elite or weak | `CFAdjustment` `kind`, or `adjustment` on a `CFEncounterSpawner` | +2 or -2 on AC, attacks, damage, saves, Perception and skills, a level, and Hit Points by level. Fixed DCs, such as a breath weapon's, are not adjusted |
| Traits | `CFTrait` `traits` | Always its creature type, from `Traits/creature-type.yml`, plus what the stat block lists. Check each id exists under `Prototypes/_CF/Traits` |
| Darkvision | `NightVision`, copied from an existing creature | |
| Low-light vision | `CFSenses` with `lowLightVision: true`, and `NightVision` with `lightingColor: "#9BB6FF18"` | See `CFMobWolf` |
| Scent, tremorsense, other imprecise senses | `CFSenses` `impreciseFeet` | Stealth reads it: an undetected creature inside it is only hidden |
| Breath weapon, or any area with a recharge | An action parented on `CFActBreathBase` in `Actions/breath.yml`, with `CFAreaEffect`, `CFCheck` (`dcSource: Fixed`) and `CFDegreeEffects`, granted with `ActionGrant` | `CFBreathWeapon` `rechargeDice` and `rechargeDie` are in rounds of 6 seconds. The AI breathes with no more setup. See `CFActPoisonBreath` |
| Draconic Momentum | `CFDraconicMomentum` | A critical hit recharges its breath weapons |
| A creature that keeps a lair | `CFGOAPLair`, a small `homeRange`, and its own `CFGOAP` lists | See "Lairs" in `goap.md` and `CFMobJuvenileHornedDragon` |
| Pack Attack | `CFCombatOptions` `packAttackDie` | The die size, 4 for 1d4. `CFFlankingSystem.AlliesThreatening` counts allies with the target in reach |
| Languages | `CFLanguages` `known` | Only for a creature that talks. Without it, it speaks plainly and understands everything |
| Recall Knowledge DC | Nothing | It comes from level. Say if a creature's printed DC differs |
| HP bar | `MobThresholds` | `0: Alive`, `HP - 1: Critical`, `HP: Dead`. Required for the mirror in `CFDyingSystem` |
| How long it thinks before each attack | `CFCombatant` `thinkingTimer` | Our number, not from the book. Default 1 second. Set it lower for a clever or quick creature, such as a boss |

## AI

Every monster has a GOAP brain from its base, so there is no root task to pick.

- It needs `CFGOAP` (from its base), `CFCombatant`, `CFAbilityScore` and `CFCombatOptions`. A creature with a natural attack, or a held weapon with `CFItemUse`, counts as armed.
- Set its tactics on `CFGOAP`: `tactics: Beast` for animals, `Mindless` for mindless creatures. See `goap-weights.md`. An encounter that names tactics replaces them for its group.
- A leader that fights unlike its group sets `keepOwnLists: true` and its own `CFGOAP` lists, so an encounter leaves it alone. See `CFMobKoboldTrapper`.
- Melee plans `Close`, then `Strike`. Anything holding a loaded gun or bow plans `Shoot` once it has a clear line. With nothing loaded it keeps the weapon and closes in with its natural attack.
- A test that needs the same body on stock HTN calls `CFStockAi.MakeStock` right after spawning it, which takes the brain out and puts an `HTN` back.

## Traps

- A low-HP mob dies on its bar from any stock damage CF does not catch, while its CF HP is full. A skeleton guard's bar is 4. Check `TemperatureDamage`: stock skeletons also take Blunt from heat, which `CFStockHeatSystem` ignores because it only catches plain Heat. Check breathing too: a creature that has no business needing air should not have `MobRespirator`.
- Keep live tests off the arena's lava. A mob that wanders into it at 4 HP dies, and the test then fails for a reason that has nothing to do with the AI. If a mob dies on the arena, log its fire stacks before blaming anything else.
- A test that parents a real monster to pin its numbers must set its own `CFMonsterAttacks` too, or it inherits the monster's real attacks and follow-ups (`CFWolfTest`).
- Abilities that need the AI to choose a moment (charges, rushes, webs, auras it switches on) are still not in. List them as not in.

## Not supported yet

- Cutting a web, or severing a frog's tongue, to free someone.
- Size: a Large creature has a Medium body.
- A creature's own Recall Knowledge skill: it uses the character's best knowledge skill, not "Undead (Religion)".

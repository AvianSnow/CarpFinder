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
| Animal or beast with one natural attack | `CFBaseCreature`, which is `SimpleMobBase` and already has `CFGOAPBrain` |
| Humanoid with hands and weapons | A stock species mob, such as `MobSkeletonPerson`, plus `CFGOAPBrain` and `CFMonsterGhostRole` |

- An entity id starts with `CF`: `CFMobGiantRat`. Variants of one creature share an abstract base, such as `CFMobSkeletonguardBase`, and each sets `name` and `Loadout`.
- When the first parent is a stock mob with its own `HTN`, such as `MobBaseKobold`, the first parent's copy wins, so set `HTN` with `rootTask: CFGOAPCompound` on the monster. See `CFKoboldWarrior`.
- Give it `InputMover`, `MobMover`, `NpcFactionMember` with `SimpleHostile`, and `CFFaction` with `Monster`. `CFBaseCreature` already has these.
- Sprites: stock bodies come from the species. Other art stays in its fork folder with its `meta.json`. An RSI with `idle` and `moving` states walks through `SpriteMovement`, and its layer needs the `movement` map as well. See `CFMobHuntingSpider`.

## Ghost role

Every monster can be played. `CFMonsterGhostRole` holds the shared rules and `GhostTakeoverAvailable`. `CFBaseCreature` and `CFMobSkeletonguardBase` already have it as a parent; a creature on any other body adds it as a parent of its own. A stock parent with a `GhostRole` of its own wins over it: a kobold also sets `prob: 1` and `rules`. See `CFKoboldWarrior`.

Each creature sets the name and description a ghost sees, with its text in `ghost-roles.ftl`:

```yaml
- type: GhostRole
  name: cf-ghost-giant-rat-name
  description: cf-ghost-giant-rat-desc
```

The role needs no AI work. Stock HTN sleeps while a player is in the body and wakes when they leave, and a GOAP brain runs inside HTN. `CFMonsterGhostRoleTest` fails if a monster has no role or no text.

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
| Natural attack | `CFCombatOptions` `naturalDice`, `naturalDie`, `naturalType`, `naturalAgile`, `naturalFinesse` | Also what a disarmed creature uses |
| Weapons | A `startingGear` with `inhand`, and `Loadout` on the mob | The item supplies dice, type and traits. Use the `CF` weapon, and do not copy its stats to the mob |
| Ammunition | `CFArrowSupply` `arrows` | Leave out the arrow already on the bow, so 20 arrows is `arrows: 19`. Set `ammo: CFBolt` for a crossbow |
| Resistances, weaknesses | `CFResistances` `entries` | `weakness: true` for a weakness |
| Immunity to a damage type | `CFResistances` with `amount: 9999` | Bleed, Mental, Poison, and so on |
| Immunity to a condition | `CFConditionImmunity` `conditions` | Mental means `Frightened`, `Charmed`, `Calmed`. Unconscious means `Asleep` |
| Void healing | `CFVoidHealing`, and `Void` at 9999 in `CFResistances` | |
| Traits | `CFTrait` `traits` | Always its creature type, from `Traits/creature-type.yml`, plus what the stat block lists. Check each id exists under `Prototypes/_CF/Traits` |
| Darkvision | `NightVision`, copied from an existing creature | |
| Languages | `CFLanguages` `known` | Only for a creature that talks. Without it, it speaks plainly and understands everything |
| Recall Knowledge DC | Nothing | It comes from level. Say if a creature's printed DC differs |
| HP bar | `MobThresholds` | `0: Alive`, `HP - 1: Critical`, `HP: Dead`. Required for the mirror in `CFDyingSystem` |
| How long it thinks before each attack | `CFCombatant` `thinkingTimer` | Our number, not from the book. Default 1 second. Set it lower for a clever or quick creature, such as a boss |

## AI

Every monster has a GOAP brain from its base, so there is no root task to pick.

- It needs `HTN`, `CFCombatant`, `CFAbilityScore` and `CFCombatOptions`. A creature with a natural attack, or a held weapon with `CFItemUse`, counts as armed.
- Set its tactics on `CFGOAP`: `tactics: Beast` for animals, `Mindless` for mindless creatures. See `goap-weights.md`. An encounter that names tactics replaces them for its group.
- A leader that fights unlike its group sets `keepOwnLists: true` and its own `CFGOAP` lists, so an encounter leaves it alone. See `CFMobKoboldTrapper`.
- Melee plans `Close`, then `Strike`. Anything holding a loaded gun or bow plans `Shoot` once it has a clear line. With nothing loaded it keeps the weapon and closes in with its natural attack.
- A test that needs the same body on stock HTN calls `CFStockAi.MakeStock` right after spawning it.

## Traps

- A low-HP mob dies on its bar from any stock damage CF does not catch, while its CF HP is full. A skeleton guard's bar is 4. Check `TemperatureDamage`: stock skeletons also take Blunt from heat, which `CFStockHeatSystem` ignores because it only catches plain Heat. Check breathing too: a creature that has no business needing air should not have `MobRespirator`.
- Keep live tests off the arena's lava. A mob that wanders into it at 4 HP dies, and the test then fails for a reason that has nothing to do with the AI. If a mob dies on the arena, log its fire stacks before blaming anything else.
- A stat block's `printedAttack` applies to everything melee, so a creature with two melee attack bonuses cannot have both yet.
- Special abilities wait for an AI that can use them. List them as not in.

## Not supported yet

- Immunity to death effects, disease and poison afflictions, critical hits, and precision.
- Void effects that heal undead, such as harm.
- More than one natural attack.
- Size: a Large creature has a Medium body.
- A creature's own Recall Knowledge skill: it uses the character's best knowledge skill, not "Undead (Religion)".

# Importing Pathfinder 2e creatures

How to turn a printed stat block into a CF monster. The worked example is the Skeleton Guard: `Resources/Prototypes/_CF/Entities/Mobs/Creatures/undead.yml`, tested by `CFSkeletonguardTest`.

Monsters run on stock SS14 HTN. Encounters give them GOAP later, so every creature has to work under both. Do not put `CFGOAP` on the creature itself.

## Steps

1. Check the id is not already in `Creatures/`. Get the stat block from https://2e.aonprd.com/ and put its source and page in a comment above the entity. `www.aonprd.com` is Pathfinder 1e, so find the 2e creature and do not use its numbers: the 1e Giant Spider is the 2e Hunting Spider.
2. Pick a body and a file (below).
3. Fill in the stat block (the table below). Use printed numbers and do not derive them.
4. Pick the AI root task (below).
5. Give it a ghost role (below), so a player can take it.
6. Write what is not in yet in the comment above the entity, so nobody thinks it was missed.
7. `CFCreatureTest.EveryMonsterPlaysByItsOwnStatBlock` covers it with no change, and compares it with its own YAML, so a monster can be rebalanced freely. Add a test class if the creature has anything more than a natural attack.
8. Check the build printed `Build succeeded`, then run the tests and the linter (`dotnet run --project Content.YAMLLinter -c Tools`).
9. Update the "Still to port" list under "Creatures with PF2e stat blocks" in `roadmap.md`.

## Body and file

| Creature | Parent | File |
|---|---|---|
| Animal or beast with one natural attack | `CFBaseCreature`, which is `SimpleMobBase` | `Creatures/creatures.yml` |
| Humanoid with hands and weapons | A stock species mob, such as `MobSkeletonPerson` | A file per creature type, such as `Creatures/undead.yml` |

- An entity id starts with `CF`: `CFMobGiantRat`. Variants of one creature share an abstract base, such as `CFMobSkeletonguardBase`, and each sets `name`, `HTN` and `Loadout`.
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
| Traits | `CFTrait` `traits` | Check the id exists under `Prototypes/_CF/Traits` |
| Darkvision | `NightVision`, copied from an existing creature | |
| Languages | `CFLanguages` `known` | Only for a creature that talks. Without it, it speaks plainly and understands everything |
| Recall Knowledge DC | Nothing | It comes from level. Say if a creature's printed DC differs |
| HP bar | `MobThresholds` | `0: Alive`, `HP - 1: Critical`, `HP: Dead`. Required for the mirror in `CFDyingSystem` |
| How long it thinks before each attack | `CFCombatant` `thinkingTimer` | Our number, not from the book. Default 1 second. Set it lower for a clever or quick creature, such as a boss |

## AI

Set the root task on `HTN`.

| Creature | `rootTask` |
|---|---|
| Melee, with a weapon or natural attack | `SimpleHostileCompound` |
| Bow | `CFArcherCompound`, plus `CFArrowSupply` |
| A leader or anything with its own tactics | `CFGOAPBrain` as a second parent, with its own `CFGOAP` lists. See `CFMobKoboldTrapper` |
| A mob that is itself a gun, such as a spitter | `SimpleRangedHostileCompound` |

Do not use `SimpleHumanoidHostileCompound` or `GunCombatCompound` for a bow. `Resources/Prototypes/_CF/NPCs/archer.yml` says why.

## Making it work with GOAP

- It needs `HTN`, `CFCombatant`, `CFAbilityScore` and `CFCombatOptions`. A creature with a natural attack, or a held weapon with `CFItemUse`, counts as armed.
- An encounter gives any mob the brain, and `CFGOAPSystem.OnBrainInit` swaps the root task, so stock AI on the creature cannot get in the way.
- To test a brain on it, add a test prototype `parent: [ CFMobYourCreature, CFGOAPBrain ]`, or list it in a spawner's `mobs`.
- Melee plans `Close`, then `Strike`. Anything holding a loaded gun or bow plans `Shoot` once it has a clear line. With nothing loaded it closes in.
- Write the fitting tactics in the comment, for whoever builds the encounter: `tactics: Mindless` for mindless creatures, `Beast` for animals. See `goap-weights.md`.

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

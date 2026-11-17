# CarpFinder — Code Instructions

CarpFinder (CF) is a fork of Space Station 14 built on RobustToolbox. It uses an ECS architecture with C# on both server and client.

## Editing and Making Documents

Whenever you understand how SS14 is built and structured, add or update docs under `.agent/` (e.g. `ss14.md`). Warnings and errors to avoid are in `ss14rules.md`.

You may edit this document with any rules and information that make the project easier to work on.

Read and follow everything in this folder before making CF or upstream-touching changes.

Character builder + PF2e spawn/combat MVP notes: `character-builder.md`.

How levels stack, how things fall between them, and what the CrystallEdge port changed: `zlevels.md`.

How GOAP is laid out and how to add to it: `goap.md`. What its weights are for, and the plan for mobs casting spells: `goap-weights.md`. Where mappers place AI hints: `investigation-nodes.md`.

How a group lives as a tribe (hunger, farming, stores, talk, parley), and the language model that advises it: `society.md`.

How to test the AI, what `Skipped` really means, and what is known to fail: `ai-testing.md`. Read it before running AI tests.

Map of the codebase: `codebase.md`. Start there to find something.

How to turn a Pathfinder 2e stat block into a monster: `importcreatures.md`.

The ordered feature plan is `roadmap.md`. Check it before starting a feature and tick items off when they land. What is already built, and why, is in `roadmapold.md`. It is not loaded each session, so read it when you need the history.

## Pathfinder Game Rules

CarpFinder is an action-based RPG based on the Pathfinder 2e system. Use https://2e.aonprd.com/ for rule questions (ancestries, classes, backgrounds, feats, skills, combat math, etc.).

## Building

The project takes ~5 minutes for a full build.

The raw build output is flooded with RobustToolbox warnings. To see only errors:

```bash
# Linux / bash
dotnet build -c Tools -clp:ErrorsOnly -nologo 2>&1 | grep -E ': error |Build succeeded|FAILED|Time Elapsed|[0-9]+ Error' | sort -u
```

```powershell
# PowerShell
$out = dotnet build -c Tools -clp:ErrorsOnly -nologo 2>&1; $out | Select-String -Pattern ": error |Build succeeded|FAILED|Time Elapsed|\d+ Error" | Select-Object -Unique
```

Never build only Shared: build Server, Client, or both via a normal `dotnet build`.

`MSB3027` / "file is locked" errors mean Content.Server or Content.Client is running and holding DLLs in `bin/`. Ask the user to close them; rebuilding will not help.

To build and test while the server stays up, send the output to a folder of its own, two levels under the repo root so tests still find `Resources`. Delete it afterwards:

```bash
dotnet build Content.IntegrationTests/Content.IntegrationTests.csproj -c Tools -p:OutputPath=C:\project\CarpFinder\bin\Check\
dotnet test bin/Check/Content.IntegrationTests.dll --filter "FullyQualifiedName~TestName"
```

After XAML moves under `_CF/`, do a rebuild (not incremental) if you see `No precompiled XAML found for …`.

**XAML hot reload:** Debug/Tools can JIT-reload `.xaml` on save, but **open lobby editors are cached** — restart the client (or rebuild + restart) to see `HumanoidProfileEditor` layout changes. Closing the character window is not enough.

```bash
# Default (Tools — XAML hot reload on) — two terminals, from repo root
dotnet run --project Content.Server -c Tools
dotnet run --project Content.Client -c Tools
# or: ./runserver.sh  and  ./runclient.sh
# Rider: Content Server+Client run config (Tools)
```

## Testing

Integration tests that need a world load the meteor arena (`/Maps/Nonstations/meteor-arena.yml`). Do not build grids by hand in tests. Use `Tests/_CF/CFDevMap.Load(Pair, footprint)` to load it and find open floor. It prefers floor with no structures, and only settles for a spot with some when the footprint is too big, as the area spells' is. Keep a footprint as small as the test needs. Pass `avoidStructures: true` to fail rather than settle.

Reuse the Tools build and target the tests you need; without `-c Tools --no-build` the test run rebuilds everything in Debug (~5 min):

```bash
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Tools --no-build --filter "FullyQualifiedName~TestName"
```

Real monsters get rebalanced, so a test must not pin their numbers. To check a stat block, compare the monster with its own YAML. To check a rule, make a test-only child prototype with its own fixed numbers, such as `CFCreatureTestBoar`, and keep `Test` in its id so tests that walk every monster skip it.

Pick the lightest test class that covers what changed: search the test files for `[Test]`. Do not run `SpawnAndDirtyAllEntities`, because spawning every entity takes far too long, and do not filter on `EntityTest`, which includes it.

## Searching

Repo-wide Grep times out (~54k files, mostly textures and maps). Always pass a path: `Content.Client`, `Content.Server`, `Content.Shared`, `Resources/Prototypes`, `Resources/Locale`, or `Resources/Maps` when maps are the target.

## C# Code Style

- File-scoped namespaces: `namespace Content.Shared.Example;`
- 4 spaces indentation (no tabs)
- Private fields: `_camelCase` (underscore prefix)
- Public members: `PascalCase`
- Local variables/parameters: `camelCase`
- Interfaces: `IPascalCase`, type parameters: `TPascalCase`
- Use `var` when type is apparent from the right side
- Allman braces (opening brace on new line)
- Expression-bodied members for simple properties
- Always include final newline in files
- Minimize LINQ in performance-critical paths (allocations)

## CF Code Organization Rules

**Always place CF code in `_CF/` subfolders** and prefix class names with `CF`:

| Location  | Folder                                                     | C# namespace                  |
| --------- | ---------------------------------------------------------- | ----------------------------- |
| Shared    | `Content.Shared/_CF/MyFeature/`                            | `Content.Shared.CF.MyFeature` |
| Server    | `Content.Server/_CF/MyFeature/`                            | `Content.Server.CF.MyFeature` |
| Client    | `Content.Client/_CF/MyFeature/`                            | `Content.Client.CF.MyFeature` |
| Resources | `Resources/Prototypes/_CF/`, `Resources/Locale/en-US/_CF/` | n/a                           |

Folder uses `_CF` (sorts with other fork folders). Namespace drops the underscore: `Content.*.CF.*`.

Class examples: `CFMySystem`, `CFMyServerSystem`, `CFMyClientSystem`, `CFExampleComponent`.

**XAML under `_CF/`:** Robust derives the default type from the _file path_ (`Content.Client._CF.…`). That does **not** match `Content.Client.CF.…` or a replaced upstream type. Always set `x:Class` to the real C# type. See `lobby-overlay.md`.

**Prototype IDs** — the `CF` prefix exists only to avoid name collisions with upstream:

- Prototype kind declared upstream/engine (`entity`, `reagent`, `Tag`, `job`, `soundCollection`, ...): ID **must** start with `CF` — `CFFoodBread`, `CFActionFireball` (not `ActionCFFireball`).
- Prototype kind declared in `_CF` (`cfBackground`, `cfAncestry`, custom kinds, ...): **no** extra `CF` on the instance ID unless the kind itself already encodes it — prefer plain IDs like `Fireball`, `Thief`.
- New `_CF` prototype kinds get plain camelCase names without prefix: `[Prototype("cookingRecipe")]`; check the name is not already taken by an upstream kind.
- Exceptions: IDs that intentionally reuse upstream IDs because upstream code, tests or cvars look them up (`Secret`, `DefaultMapPool`, `Empty`, `Baseline*`), `roleLoadout` (upstream requires `Job<JobId>`, e.g. `JobCFGuard`), and existing `gameMap` IDs.
- Renaming an ID stored in the DB drops players' data unless migrated.

**Shared building blocks** (use these instead of writing new copies):

| Need | Use |
|---|---|
| Degree of success for any check | `CFDegrees.DegreeOfSuccess(d20, modifier, dc)` → `CFDegree` (`Content.Shared.CF.Checks`) |
| Roll a check | `CFDefense.Roll(random, modifier, dc)` |
| Save / Perception / skill modifier | `CFDefense.SaveModifier`, `PerceptionModifier`, `SkillModifier` with `CFDefenseType`. Add `CFConditionSystem.SkillBonus` to a skill modifier |
| Roll a save | `CFConditionSystem.RollDefense`, with the source's traits. Do not roll one with `CFDefense.Roll`, or bonuses against a trait and save upgrades are skipped |
| Proficiency rank | `CFDefense.SaveRank`, `PerceptionRank`, `AttackRank`, `ArmorRank`, `SkillRank`, `ClassDcRank`, `SpellRank`. `CFWeaponSystem.Rank` for a Strike. Never read a class's lists alone: feats raise ranks too |
| What a feat, class feature, heritage, ancestry or background gives | A `grants` list of `CFGrant` entries (`Content.Shared/_CF/Character/Grants/CFGrants.cs`), such as `!type:CFGrantBonus`. Do not add a `switch` on an id. `CFCharacterFeatureSystem` runs them |
| Heat or cold one step less severe | `!type:CFGrantTemperatureTolerance` (`heat`, `cold`, in steps; negative is worse). `CFEnvironmentRules.Eased` applies it after gear |
| Harder to Shove, Trip, Disarm, Grapple or Reposition, or better at it | `CFGrantBonus` with `stat: ManeuverDc` or `stat: Maneuvers`. `CFConditionSystem.RollSkill` reads both for an Athletics check with the attack trait |
| A scent trail, and seeing one | Every character and monster has `CFScent`, which lays `CFScentMarker` marks. The Scent sense gives `CFSmeller` and its free Sniff action, which shows them. Ported from Starlight, under `_CF/Scent/` |
| Finding creatures by hearing or scent | `!type:CFGrantSeek` (`bonus`, `rangeFeet`, `hiddenFlatDc`, `concealedFlatDc`) on `CFSeekSense`. Seek and `PassFlatCheck` read it inside the range |
| Falling or climbing better | `CFGrantSafeFall` (`halfDistance`), `CFGrantClimb` (`bonus`, `successIsCritical`, `climbSpeed`). `CFZLevelDamageSystem` reads the first, `CFZLevelPortalSystem.Climb` the second |
| A map with levels above or below | `CFStationZLevels` on the station with `mapsBelow` and `mapsAbove` (`Resources/Prototypes/_CF/ZLevels/test_map.yml`). `CFSharedZLevelsSystem.TryMapUp`, `TryMapDown`, `TryMove` to read and cross the stack. See `zlevels.md` |
| Something that falls, or must not | `CFZPhysics`: every mob and item has it, and so do crates, chests, barrels, seats, tables, beds and cabinets. A new piece of furniture that can be moved gets it too. `velocityGravity: false` floats, `autoStep: false` ignores stairs. A hole is an empty tile, or `CFZLevelHole`. A soft landing spot is `CFSoftSurface` |
| What a fall does | `CFFallRules.Damage(feet)`, half the feet fallen, through `CFZLevelDamageSystem`. One level is `CFFallRules.LevelFeet` (10) |
| An ability used so many times a day | `CFDailyUses` on the action entity (`uses`). `CFDailyUseSystem` counts it, a long rest gives it back, and the HUD greys it. See `Resources/Prototypes/_CF/Actions/heritages.yml` |
| A choice of energy type | `kind: DamageType` with `damageTypes` in `choices`, then a grant that reads the pick with `CFCharacterFeatureSystem.Pick`, such as `!type:CFGrantChosenResistance` |
| Adding or removing a creature trait | `!type:CFGrantTrait` with `add` and `remove` |
| An option that gives another feat, or lets the player pick one | `!type:CFGrantFeat` with the feat's id, or `kind: Feat` in `choices` with `featSlot` (`GeneralFeat`, `SkillFeat`, `ClassFeat`, `AncestryFeat`) and `featLevel`. `CFCharacterFeatureSystem.ApplyFeat` gives a feat once, and `CFSkills.ExtraFeats` is how the builder counts it as taken |
| An option that gives an animal companion, a familiar or a construct companion | `!type:CFGrantPet` with `kind`. `CFPetMath.BuildGrants` reads it, and the server spawns the body with the character |
| Something about a deity's favored weapon | `favored: true` on the deity's `CFGrantWeaponFamiliarity`, read from `CFCombatant.FavoredWeapons`. Who follows whom is `CFEmblazonSystem.DeityOf` |
| A subclass option (Racket, Instinct, Muse, ...) or a deity | A `cfClassChoice` or `cfDeity` prototype. Read a build's picks through `CFClassChoices`, not from `CFCharacterBuildData` directly |
| Spell / class / fixed DC and attack bonus | `CFCasting.Spellcasting`, `ClassDc`, `Fixed` (`Content.Shared.CF.Checks`). Cantrip rank: `CFCasting.CantripRank(level)` |
| Anything that targets, rolls a check, and applies effects (spells, bombs, traps, auras) | Put `CFTargeting`, `CFAreaEffect`, `CFCheck`, `CFDegreeEffects` (and `CFProjectileDelivery`, `CFModeChoice`, `CFHeighten`) on the source, then call `CFEffectSystem.DeliverToTarget` / `ResolveArea` / `ResolveTarget` (`Content.Shared.CF.Effects`). Do not write a new resolver. |
| Bonuses, fortune, action caps, resistances, persistent damage | `CFBonusSystem` (`AddBonus`, `AddPermanentBonus`, `Total`, `TryConsumeFortune`, `SetActionCap`, `AddResistance`, `AddPermanentResistance`, `ModifyDamage`, `AddPersistent`). A bonus can name a `trait` it counts against, or a `skill` |
| Give a condition | `CFConditionSystem.ApplyCondition(target, CFConditionKind, value, seconds, source, dc)` |
| Counteract a spell, or let one be counteracted | `CFCounteractSystem.TryDispel` and `Counteracts`. A reaction that counters a spell as it is cast is `trigger: EnemyCastsSpell` with `counterspellPrepared` or `counterspellSlot` (and a `trait`). A new lasting spell effect calls `CFCounteractSystem.Note` and files its bonuses under the key it returns, as `CFBonusEffect` and `CFApplyConditionEffect` do |
| Critical specialization | `!type:CFGrantCriticalSpecialization` with a `specialization` (groups, weapons, `minRank`, `when: Raging`, `TargetOffGuard` or `TargetIsPrey`, ...), or `criticalSpecializationLevel` on a `CFGrantWeaponFamiliarity`. `CFCriticalSpecializationSystem` has each group's effect. Do not add a flag to `CFCombatOptions` for it |
| Weapon stats for a Strike | `CFWeaponSystem.TryStats(weapon)` → `CFStrikeWeaponStats`; traits through `CFWeaponTraits` (never add weapon booleans: give the `cfTrait` a `rule`) |
| Worn armor, check penalty, Speed penalty, AC refresh | `CFArmorSystem` (`TryGetWornArmor`, `CheckPenalty`, `SpeedPenalty`, `RefreshArmorClass`) |
| Shields | `CFShieldSystem` (`TryGetRaisedShield`, `TryGetUsableShield`, `SetRaised`, `RaisedBonus`, `Block`). Raised is `CFShieldComponent.Raised` on the item. |
| Bulk and encumbrance | `CFBulk`, `CFEncumbranceSystem`; clumsy from `CFConditionSystem.Clumsy`. A weapon or shield worn on the belt, back or neck counts half (`CFEncumbranceSystem.IsHolstered`) |
| A new weapon or shield | Run `.agent/port-scripts/holsters.py` after adding it, so it can be worn on the belt, back and neck (shields: back and neck) with the right sprites. `CFHolsterTest` fails until you do |
| A group that fights together | An `encounter` prototype in `Resources/Prototypes/_CF/GOAP/encounters.yml`, placed with `CFEncounterSpawner`. Its tactics, goals and actions go onto each member's brain, and a stock mob it spawns gets one |
| A group that lives together: eats, farms, bakes, hunts animals, feeds its fire, stores food, tidies, and talks to anyone who can talk before it fights | An `encounter` with `tribe: true` (`Resources/Prototypes/_CF/GOAP/society.yml`), with its tools in `supplies`. `CFGOAPTribeSystem` holds what it knows, and a new chore is a `CFGOAPErrandStepSystem`. See `society.md` |
| Grinding something at a mortar | `CFMortar` on the mortar with a `grinds` map from a tag to the result (`Content.Shared.CF.Cooking`). Using the item on the mortar is a 2 second do-after, and the result lands in the same hand |
| An ingredient that becomes something else in water, such as flour into dough | `CFMixInto` on the ingredient (`reagent`, `amount`, `result`). Using it on a well or a bucket with enough in it does it at once |
| A fire that burns fuel and leaves ash | `CFFireplace` (`fuel`, `maxFuelLimit`, `ash`, `ashChance`) with a `storagebase` container for `CFFireplaceFuel` items. `CFFireplaceSystem.NeedsFuel`, `TryFeed` (lights it too), `IsBurning`. Each piece burnt spawns the ash beside it |
| Something that heat turns into another thing (dough into bread) | `CFTemperatureTransformation` with `entries`. `CFTemperatureTransformationSystem` swaps it in place, hand or store included. `SetCookedTemperature` sets off the change |
| A fire that heats what is set on it | `CFFireHeater` on the fire (the fireplace base has it). Each second it burns, things on its `ItemPlacer` gain `degreesPerSecond` up to `maxDegrees`, their liquids head for boiling, and each gets `CFHeatedEvent`. Stock `ChangeHeat` is in joules and leaves food lukewarm: do not use it for cooking |
| A dish cooked in a pot, pan or pie plate | A `cookingRecipe` in `Resources/Prototypes/_CF/Recipes/Cooking/` (`foodType`: `Meal`, `Soup` or `Pie`, `requirements` by ingredient tag or reagent, `foodData` for name and look). Nobody picks one: the cooker takes the hardest recipe its contents fit. `CFFoodCooker` cooks while heated, `CFFoodHolder` holds the dish, and a plate or bowl with `canAcceptFood` takes it. `CFSharedCookerSystem.GetRecipe` and `TryTransferFood` for code |
| An ingredient a dish can use | Tags on the item (`CFMeat`, `CFVegetable`, `CFSalad`, `CFDough`, ...). Vegetables and their slices come from `.agent/port-scripts/produce.py` |
| Brewing a drink | `CFFermenter` on a barrel (`CFBaseBarrel` has it): `mashes` maps an ingredient's tag to a mash reagent, made from 10 water, and `ferments` maps the mash to the drink it turns into over time. `CFFermenterSystem.TryMash` and `Ferment` |
| Soil a seed can go in | The `CFFloorDirtSeedbed` tile a fork digs, or anything with `CFSoil`, such as the buildable `CFSeedbed`. `CFFarmingSystem.CanPlant` checks both |
| A monster's AI | Every CF monster thinks with GOAP: `CFBaseMonster`, which every monster has as a parent, has `CFGOAPBrain`. Set `CFGOAP.profile` to a `cfGoapProfile` and `CFGOAP.tactics` to a `goapTactics`. A leader that fights unlike its group sets `keepOwnLists: true`. Stock SS14 mobs keep stock HTN |
| A stance or a flourish | A stance is an action parented on `CFActStanceBase` with a `CFStance` (`Resources/Prototypes/_CF/Actions/stances.yml`). A flourish is any action with `CFFlourish`. Grant either with `CFGrantAction` |
| An option where the player picks a cantrip or skill | A `choices` list on the `cfFeat`, `cfHeritage` or `cfClassChoice`. Read answers through `CFOptionChoices`, not `CFBuild.OptionPicks` directly |
| A pick the player can change each day | `daily: true` on the `choices` entry. The sheet's Daily tab plans it, and `CFDailyChoiceSystem.Apply` swaps it in at a long rest. A new kind of pick needs a line in its `TakeBack` |
| A bonus that only counts while raging, with panache, or unarmored | `!type:CFGrantWhile`, run by `CFWhileBonusSystem` |
| What a Barbarian instinct does to Rage | `!type:CFGrantRage` (`damage`, `attackDie`, `attackType`) |
| An aura, or something an aura does | `!type:CFGrantAura` with a list of `effects`. `CFAuraSystem` runs them. Do not add a physics sensor for one. An aura is a circle of `CFAuraSystem.Radius` around where its owner stands, not tiles, and the client draws that same circle. An effect can set its own `sizeFeet` and `includeSelf` |
| Taunting an enemy | An action with `CFTauntAction` (`Resources/Prototypes/_CF/Actions/taunts.yml`), or `tauntsTarget: true` on a `CFStrikeAction`. `CFTauntSystem` runs it |
| A weapon whose damage type can be switched | `CFDamageMode` with a `types` list. Use in hand cycles it for 1 action, and the hand status shows it. `CFWeaponSystem.TryStats` reads it |
| Something a Thaumaturge needs in hand | `CFImplement` on the item, `CFImplementSystem.IsHolding`, and `CFNeedsImplement` on an action |
| Amping a psi cantrip | `CFPsiCantrip` on the spell's action, with what the amp changes. `CFPsiSystem` spends the Focus Point |
| A Commander's tactic | An action with `CFTacticAction` in `Resources/Prototypes/_CF/Actions/tactics.yml`, and a `cfClassChoice` entry per tactic slot. `CFTacticSystem.Squad` says who answers |
| Something that needs an Inventor's innovation | `CFInnovation` on the item or construct, `CFInventorSystem.IsUsing`, and `unstable: true` on a `CFStrikeAction` |
| A creature a spell or ability calls | `CFSummoned` on it: Summoned trait, a puff of smoke and no body when it dies or is sent away (`CFSummonedSystem.Unsummon`). Bind it with `CFCompanionSystem.Bind` |
| A spell that summons a creature | `!type:CFSummonEffect` with a `creatures` list by rank (`Spells/1/CFSummonAnimal.yml`). Each creature is an entity in `Entities/Mobs/summons.yml` with its monster as first parent and `CFSummonedCreature` second. A feat that lets a prepared caster cast a spell in place of a prepared one is `!type:CFGrantSwapSpell` |
| A familiar, or any companion that must not fight | `peaceful: true` on its `CFCompanion`, as `CFFamiliarBody` has (`Companions/familiars.yml`). A new familiar is a `cfPet` with `kind: Familiar` and an `entity` parented on a stock body and `CFFamiliarBody` |
| A companion's AI | It is a GOAP brain from `CFCompanionBase`. Steer it with `CFCompanionSystem.OrderAttack` (goes for that one first), `ComeAlong` and `StopOrder`. Do not drive it with `NPCMeleeCombat` |
| A Summoner's eidolon | `Choices/summoner.yml` with `!type:CFGrantEidolon`. `CFOnEidolonEffect` runs effects on the manifested eidolon. An eidolon ability the Summoner orders is an action with `CFEidolonStrikeAction`, or `CFEffectAction` |
| A rune that goes on an item at a workbench | A `craftingRecipe` with `attachRune: true`, a `CFRuneItem` on its result and a `CFRuneTargetRequirement` (`count: 0`) in `Crafting/rune_recipes.yml`. `CFRuneSystem.CanAttach` says what can hold it |
| A Runesmith rune | A `cfRune` in `Combat/runes.yml` and a `CFActTrace` action in `Actions/runes.yml`. The action's `CFCheck` and `CFDegreeEffects` are what invoking it does |
| An Exemplar's ikon | A `cfIkon` in `Combat/ikons.yml`, a `CFActShift` action and a `CFActTrans` action with `CFIkonTranscendence` in `Actions/ikons.yml`, and `!type:CFGrantIkon` on a `cfClassChoice` |
| An Animist's apparition, or a spell that is sustained | A `cfClassChoice` pair in `Choices/apparitions.yml` with `!type:CFGrantApparition`. `sustained: true` on `CFSpell` makes any spell sustained: it runs its effects again every 6 seconds for a minute with no input, and only Dispel Magic ends it sooner (`CFSustain`, `CFSpellSystem.IsSustaining`). Never make a player re-use an action to keep a spell going |
| A grave spell, or anything a Necromancer casts through a thrall | A spell in `Spells/Focus/focus.yml` with a whitelist of `CFThrall` and a `CFThrallBlastEffect` |
| An action where the user rolls a skill against a creature's defense DC (Goblin Song, Catfolk Dance) | `CFCheck` with `defense` and `skill`. The degree is the user's, so put the result in `success` and `criticalSuccess` |
| An action that Demoralizes or Feints at its own range | A `CFActivityTarget` action with `CFActivity` `kind: Demoralize` or `Feint` and a `TargetAction` range |
| One of several actions, by what the player picked for the option | `!type:CFGrantChosenAction` with an `actions` map from pick values to action ids, beside a `choices` entry |
| An action that only applies effects | An action parented on `CFActivityInstant` or `CFActivityTarget` with `CFEffectAction` and `CFDegreeEffects` (`Resources/Prototypes/_CF/Actions/effects.yml`). Grant it with `CFGrantAction`. `unstable: true` makes it an Inventor's unstable action |
| A Swashbuckler finisher | A Strike action with `finisher: true` on its `CFStrikeAction`. It needs panache and spends it |
| An action that is a Strike with something extra | An action parented on `CFActStrikeBase` with a `CFStrikeAction` (`Resources/Prototypes/_CF/Actions/strikes.yml`). Say what it needs (`press`, `twoHandedWeapon`, `needsFreeHand`, `groups`, ...) and put effects in `onHit`, `onCritical` and `onFailure`. A Shove is `!type:CFForcedMovementEffect`, a grab `!type:CFGrabEffect`, a Trip `!type:CFTripEffect` |
| A spell that turns the caster into something else (a battle form, a polymorph) | `CFBattleFormSpell` on the spell's action, with a `CFModeChoice` mode per form (`Spells/2/CFAnimalForm.yml`). Every form needs a `sprite`: the client draws it in place of the body (`CFBattleFormVisualsSystem`), and the HUD shows the spell and shape, as "Animal Form: Cat". `CFBattleFormVisualsTest` fails a form without one. `tiers` are its numbers at higher ranks, and a form can set `minRank`, `noStrikes`, `durationSeconds` and `physicalWeakness`. `CFBattleFormSystem` gives the Dismiss Form action |
| A focus spell | An action in `Resources/Prototypes/_CF/Spells/Focus/focus.yml` with `focus: true` on its `CFSpell` and rank 0, and a `cfSpell` with no traditions. Grant it with `CFGrantFocusSpell` |
| A spellshape | An action parented on `CFActSpellshapeBase` with a `CFSpellshape` (`Resources/Prototypes/_CF/Actions/spellshapes.yml`). Grant it with `CFGrantAction` |
| Something that happens as a fight begins (the rules' "roll initiative") | Handle `CFFightStartedEvent`, which `CFFightSystem` raises on a creature at the first attack it makes or takes after 30 seconds without one |
| A condition | `CFConditionSystem.ApplyCondition` with a `CFConditionKind`. Enfeebled, drained, doomed, dazzled, fatigued, deafened and fascinated are read with `CFConditionSystem.Value`. Stunned takes actions at once |
| An action or reaction that moves its user | `closesFeet` or `stepsBackFeet` on a `CFStrikeAction`, `follows: true` on a `CFReaction`, `!type:CFLeapEffect`, and `!type:CFFreeStepEffect` for leaving reach without cost |
| A reaction | A `CFGrantReaction` with a `CFReaction` (trigger, requirements, what it does). `CFReactionSystem` runs it and spends the creature's one reaction. Add a trigger there rather than a new flag on `CFCombatOptionsComponent`. Its place in the order comes from the level of what granted it (`CFCharacterFeatureSystem.SourceLevel`). A feat that improves another feat's reaction uses `!type:CFGrantWidenReaction` with the reaction's `name` |
| A reroll or a bonus on a failed skill check | A reaction on `SkillCheckWouldFail` (`rerolls`, `resultOfProficiency`, `criticalFailureIsFailure`, `skills`, `needsTrained`), or `andSkillChecks` on a `SaveWouldFail` one. Skill rolls go through `CFReactionSystem.AnswerSkillCheck` |
| Actions for code with no player | `CFManeuverSystem.CanUse` and `TryUse` with a `CFBasicAction`, `CFStrikeSystem.TryFeatStrike` |
| Whether a creature can see another | `CFStealthSystem.Detection`, `IsUnseenBy`, `Reveal`. Spending actions reveals a creature unless `TrySpendActions` is told `stealthy` |
| Traps and hazards | `CFHazard` (Stealth DC, detect radius, trigger size, disarm checks) plus `CFCheck` and `CFDegreeEffects` for what it does. `CFHazardSystem` handles noticing, triggering, and disarming. A `CFFaction` on the hazard protects its allies. Disabling it raises `CFHazardDisarmedEvent`; `CFExperienceSystem.AwardHazard` gives the disarmer's company XP for the hazard's `level` (simple or `complex`). See `Resources/Prototypes/_CF/Entities/Hazards/traps.yml`. |
| A breath weapon, or another area a creature uses and then has to recharge | An action parented on `CFActBreathBase` (`Resources/Prototypes/_CF/Actions/breath.yml`). `CFBreathWeaponSystem.TryBreathe` for code with no player. The rolled recharge is the action's cooldown. `CFBreathGas` on the action makes it a gas cloud that spreads over the area like atmos, stops at walls, and makes anyone on a filled tile roll once (`CFBreathGasSystem`). `CFAreaFill` with a `reagent` and `rolls: WhenFilled` makes it pour out as liquid instead, with the save when it has filled. `CFEffectSystem.CreaturesInTiles` and `ResolveTiles` work on a set of tiles |
| An area that can be seen: flames, sparks, puddles, a splash | `CFAreaFill` on the source, beside `CFAreaEffect`: a `tile` effect from `Entities/area_fills.yml`, or a `reagent` for puddles. `lingerSeconds` keeps it, and whoever moves faster than a walk on it rolls again (Grease). `CFEffectSystem.ResolveArea` hands over to `CFAreaFillSystem`, so spells, breaths, bombs and traps all get it. It stops at walls, and only creatures on tiles it reaches roll. For something only seen around one hit, `!type:CFFillEffect` in an effect list. A cloud of gas is `CFBreathGas` |
| A potion that works when thrown | `CFSmashPotion` beside `CFHealConsumable`. `CFSmashPotionSystem` heals a creature it hits, or leaves a `CFHealingPuddle` |
| Mobs that wander in over a round | `CFWildSpawner` in a gameMap's station `components`, with an `entityTable` (see `Prototypes/_CF/Maps/dragonruntest.yml`). `CFWildSpawnerSystem` spawns one roll every `interval` on a listed tile, away from players, up to `maxAlive` |
| A chasm creatures can slip at and grab the edge of | `CFChasmEdge` on the chasm (`grace`, `grabDc`, `leapFeet`). `CFChasmEdgeSystem` takes over stock's fall for anything with `MobState`; items still drop at once |
| A monster that guards one place | `CFGOAPLair` on its brain. See "Lairs" in `goap.md` |
| A creature that builds traps | `CFTrapLayer` with a hazard prototype, and `CFTrapLayerSystem.TryLay`. The trap takes the builder's faction, so allies never set it off. Do not make an AI walk around one. |
| Locks on doors and containers | Add a grade as a second parent: `parent: [ WoodDoor, CFLockSimple ]`, with `forceDc` and `keyId` on `CFLock`. See `Resources/Prototypes/_CF/Entities/Objects/Tools/locks.yml`. `CFLockSystem` blocks stock lock toggles and opening a locked door; the stock `Lock` alone does not stop a door. |
| New crafted item | Add a `craftingRecipe` in `Resources/Prototypes/_CF/Crafting/recipes.yml` with a `category` (`CFRecipeCategory`: Tools, Materials, Weapons, Armor, Clothing, Bags, Building, Food, Farming, Alchemy, Goods, Runes). A common level 0 recipe needs nothing more: the handbook of its category (`CFFormulaBook<Category>`, `categories` on `CFFormula`) teaches it. Above level 0 it also needs a `CFFormula<Id>` item in `Entities/Objects/Formulas/formulas.yml`. Level and rarity come from the result's `CFItem`. Formula prices are the Player Core table by item level. A new station is a `CFBaseWorkbench` child with a `recipeTags` tag. `CFCraftingTest` fails a recipe with no bench or no handbook |
| New monster | Follow `importcreatures.md`. Give it a file of its own in the folder of its creature type under `Resources/Prototypes/_CF/Entities/Mobs/Creatures/`, put that type on `CFTrait`, and set the printed stats from Archives of Nethys. Set `diesAtZeroHp: true`. Give it a `GhostRole` name and description, so a player can take it. Parent `CFBaseCreature` for an animal body, or list a stock body first and `CFBaseMonster` second. That base gives the brain, faction, harvesting and death at 0. |
| Any generated map | Call `CFOutdoorsSystem.ApplySkyWorld(map)` first. CF takes place in the sky, on floating islands with airships, not in space, so every generated map needs the breathable air and `Sky` parallax from the `CFSkyWorld` prototype. |
| Who can hurt whom | `CFFactionSystem.CanHarm` and `GetAlliance`. Two player characters fight only when both have PvP on (`CFPvpComponent`). Do not read factions or PvP yourself |
| A player's body that is going away | `CFPlayerBodySystem.ReleaseBody` drops everything it wore and held, then deletes it, and the job slot comes back. `ReleaseBodiesOf(user)` finds them by player. `DropBelongings` alone for the drop |
| What a level asks for, and checking a level's picks | `CFLevelUp` (`SlotsAt`, `FeatOptions`, `SkillOptions`, `NewSpellSlots`, `NewOptionSlots`, `Unfilled`, `TakeLevel`). The server takes a level through `CFExperienceSystem.TryLevelUp`. Never level a character from `AddXp` |
| What a monster gives when harvested | `parts` on its `CFHarvestable` (any stock `ToolRefinable` or `Butcherable` list is taken over). Every harvest also gives `CFMonsterParts`, and the body is removed, not gibbed. The value comes from its level through `CFHarvestRules`: never price parts by hand |
| Money | Coins in a wallet. `CFCoinSystem.Total`, `TrySpend` (makes change) and `Pay`. `CFWalletSystem.Total` on the client. An item's full worth is `CFMerchantSystem.Worth(item)`; buyers pay a share of it (`CFCoins.Share`). Set `fullResale: true` on `CFItemPrice` for treasure (gems, art objects, monster parts): the cleanup keeps it |
| Languages | `CFLanguageSystem.Understands`, `TryGetSpoken`, `TrySpeak`. A creature with no `CFLanguages` understands everything |
| Skill proficiency rank | `CFDefense.SkillRank(prototypes, combat, skill)` |
| Area tiles (burst, cone, emanation, line, square) | `CFAreaGrid.Collect` (`Content.Shared.CF.Effects.Areas`), 1 tile = 5 ft |
| Damage type data (SS14 type, colour, hit sound) | `cfDamageType` prototypes, resolved with `CFDamageTypes.TryResolve` (`Content.Shared.CF.DamageTypes`). Matching ignores case and checks aliases, so `slashing` resolves to `Slashing`. |
| Sparkles on a creature for magic or healing | `CFVfxSystem` (`Cast`, `Targeted`, `Healing`). `CFEffectSystem.Heal` already plays them |
| How long an NPC hesitates before attacking | `thinkingTimer` on `CFCombatant`. A test that attacks with a mob that has no player needs `thinkingTimer: 0`, or its first attempt only starts the timer |

**Porting from crystall-punk-14:** rename every type, file, prototype id and component to the `CF` prefix, use the `Content.*.CF.*` namespace (their `_CP14` folder becomes our `_CF`, but the namespace has no underscore), drop `readonly` from `[Dependency]` fields, and make those classes `partial`. Keep their textures under `Resources/Textures/_CP14` with the original `meta.json` license.

**Prefer not editing upstream (non-`_CF`) files.** When a change in upstream code is unavoidable, wrap it:

```csharp
// CarpFinder: reason for the edit
... changed multiline ...
... code ...
// CarpFinder end

// or
changed inline 1-line edit // CarpFinder: reason for the edit
```

Never add `// CarpFinder` comments for `using` blocks.

**Do not edit upstream keybinds** (`Resources/keybinds.yml`, input contexts, the rebind menu). New inputs reuse existing keys. Alt+Z is reserved for Swashbucklers choosing a finisher.

**Preferred overlay pattern:** keep stock Lobby/UI (and similar) files identical to upstream; put replacements in `_CF/` and exclude the stock compile/XAML entries from the project file. See `lobby-overlay.md`.

## ECS Architecture

- **Components**: Pure data containers — no logic

```csharp
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFExampleComponent : Component
{
    [DataField]
    public string SomeField = string.Empty;

    [DataField, AutoNetworkedField]
    public int NetworkedValue;
}
```

- **Systems**: All logic lives here

```csharp
namespace Content.Shared.CF.Example;

public sealed partial class CFExampleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    [SubscribeLocalEvent]
    private void OnSomeEvent(Entity<CFExampleComponent> ent, ref SomeEvent args)
    {
        // logic
    }
}
```

- Use `[Dependency]` for dependency injection **without** the `readonly` keyword.
- Use `[Dependency] EntityQuery<T>` for performance-critical component lookups.
- All classes that use `[Dependency]` should be `partial`.

**Event subscriptions:** put `[SubscribeLocalEvent]` / `[SubscribeNetworkEvent]` / `[EventSubscription]` (= SubscribeAllEvent) on the handler method itself — comp/event are inferred from the signature. Don't call `SubscribeLocalEvent<...>()` in `Initialize()`. Use `[SubscribeLocalEvent(before: [typeof(X)], after: [typeof(Y)])]` for ordering. Fall back to a manual `Subscribe*Event()` call only where the attribute can't work: generic systems, `UIController`s, lambda handlers, conditional subscriptions.

**Critical ECS rules:**

- Never store mutable state inside systems — it is not saved/loaded with the game save. All persistent data belongs in components.
- Before subscribing a `Comp+Event` pair (attribute or manual call), verify it is not already subscribed — the engine throws on duplicate subscriptions.

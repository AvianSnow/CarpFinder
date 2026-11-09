# CarpFinder roadmap

Ordered plan for the next stretch of work. Each step builds on the one before it. Tick items off as they land, and move finished work to `roadmapold.md`, which also holds the history of everything already built.

Source of rules: https://2e.aonprd.com/ (Remaster). Source of structure and assets: `C:\project\crystall-punk-14` (**read-only**: copy from it, never edit it).

## Step 1: Make character options work

Players can pick hundreds of options that do nothing. This step gives them effects.

### Where it stands

Measured on 6 November 2026. `python3 .agent/port-scripts/feature_scan.py` prints these numbers, per class and ancestry.

| Option | Total | Works | Does nothing yet |
|---|---|---|---|
| Class feats | 586 | 16, and 2 in part | 568. 481 of them say only "will be implemented later" |
| Ancestry feats | 255 | 30, and 7 in part | 218 |
| Class features | 176 | About 35 | 140 are placeholders |
| Heritages | 149 | 20 | 129 are a line of text |
| Ancestries | 23 | Boosts, HP, Speed, size, darkvision, languages | Traits are labels |
| General and skill feats | 131 | 0 | Pickable in the builder, marked `(TBA)`. They have no effects yet |
| Backgrounds | 324 | Boosts and skills | 261 name a skill feat, from 74 different feats, and none is granted. Only Battle Medicine works |
| Spells | 62 | 57 | Figment, Know the Way, Sigil, Animal Form, Dispel Magic are popups. Prestidigitation has its own system and works |
| Focus spells | 0 | | Hexes, compositions, devotion spells and the rest do not exist |

Some class feature text is wrong. Divine Font says it grants slots, and no code does. Hunt Prey, Exploit Vulnerability, Devise a Stratagem and Inspire Courage still say they are on the wheel. Fix each one in its wave.

### What blocks it

1. **Subclasses have almost no options yet.** The storage and the builder button are built (1c), but only the Rogue has options. About a third of the level 1 class features need one.
2. **The rules text is not in the repo.** The 481 placeholder feats and 140 placeholder features were imported with their text removed, so each needs reading on Archives of Nethys.

### 1a. One pipeline for what an option grants

Built on 6 November 2026, except where marked. Tests: `CFGrantTest`. What changed and why is in `roadmapold.md`.

- [x] `CFGrant`: an abstract data class like `CFEffect`, written in YAML as `!type:CFGrantBonus` and so on.
- [x] A `grants` list on `cfFeat`, `CFClassFeature`, `cfHeritage`, `cfAncestry` and `cfBackground`.
- [x] A `grants` list on the class choices.
- [x] `CFCharacterFeatureSystem.Apply(mob, grants, source)`, called at spawn and on level-up.
- [x] Move both `switch` statements into YAML. The flags on `CFCombatOptionsComponent` stay for now, set by `!type:CFGrantOption`.
- [x] Permanent bonuses in `CFBonusSystem`, and bonuses that only count against a trait. This replaces `SavesVsFear`.
- [x] A hook that lets a grant change a save's degree, matching `AdjustDefense`: `CFBonusSystem.UpgradeSave`, called from `CFConditionSystem.RollDefense`.
- [x] Traits on `CFEffectArgs`, read from the source, so any check can ask what it is rolling against.
- [ ] A bonus against a trait only counts on saves and Perception. Attacks and AC have no source traits to read.
- [ ] Nothing checks the ids in `CFGrantProficiency`, so a typo does nothing. `CFFeatureCoverageTest` in 1g should.
- [ ] Low-light vision is a dimmer darkvision overlay, chosen by eye and not seen in a real client.

| Grant | What it does | First uses |
|---|---|---|
| `CFGrantProficiency` | Raises a rank: a save, Perception, a weapon group, armor, spellcasting | About 30 class features: Weapon Expertise, Reflex Expertise, Perception Expertise |
| `CFGrantBonus` | A bonus to a save, skill or AC, always or against a trait | About 57 ancestry feats |
| `CFGrantSaveUpgrade` | One degree better against a trait | Bravery, Death Warden Dwarf |
| `CFGrantResistance` | Fixed, or half level | Forge Dwarf, Arctic Elf, Strong-Blooded Dwarf |
| `CFGrantWeaponFamiliarity` | Treats listed weapons as simpler | About 22 ancestry feats |
| `CFGrantSense` | Low-light vision, scent, imprecise senses | Heritages, ancestry feats |
| `CFGrantMovement` | `TBA`, not built. Climb, swim and fly all wait | 6 ancestry feats |
| `CFGrantSpell` | An innate spell or a cantrip. A ranked spell is at will until 1f adds daily uses and focus | Seer Elf, Adapted Cantrip, every focus spell |
| `CFGrantItem` | Starting gear | Clan Pistol, Alchemist's potions |
| `CFGrantAction` | Adds an action to the wheel | 236 action and 30 free action feats |
| `CFGrantReaction` | A trigger and what it does. Not built: it needs the trigger list from 1b | 57 reaction feats |
| `CFGrantOption` | Sets a `CFCombatOptionsComponent` flag | Everything already coded |

### 1b. The shapes actions come in

Most action feats share one of a few shapes, so build the shape once. An action is an entity with `CFActivity` and the usual `CFTargeting`, `CFCheck` and `CFDegreeEffects`. Do not write a new resolver.

- [x] Stances (21 feats). `CFStance` on an action, parented on `CFActStanceBase` in `Actions/stances.yml`, run by `CFStanceSystem`. Built on 9 November 2026, with Point-Blank, Mountain, Crane, Dragon and Tiger. The other 16 stances are filled in their waves. Tests: `CFStanceTest`.
  - One at a time, and another stance action waits 6 seconds. Using the same action again is Dismiss, which costs its action and does not wait.
  - It ends on being knocked out, when armor breaks an unarmored requirement, or on another stance action. Real time has no encounters, so it does not end when a fight does.
  - [ ] Asleep and Unconscious from a condition do not end a stance yet, only falling to Critical or Dead does.
  - [ ] Not seen in a real client: the action button lighting up while in a stance.
  - [ ] Stances with other requirements have no check yet, such as Mountain's touching the ground, or a weapon a stance needs.
- [x] Flourishes (36). Add `CFFlourish` to an action. `CFActionPoolSystem` allows one until the pool is next full. Flurry of Blows and Sudden Charge use it, and Vicious Swing keeps its code path. The rest are filled in their waves.
- [ ] Spellshape (34). Changes the next spell cast.
- [ ] Reactions (57). One trigger list, spending the reaction through `TrySpendReaction`. Reactions stay automatic and off the wheel.
- [ ] Auras and emanations, for champion and bard features, on `CFAreaEffect`.

### 1c. Subclasses

Built on 9 November 2026. `CFClassChoices` decides what counts as a valid pick. Tests: `CFClassChoiceTest`. The builder buttons have not been clicked in a real client.

- [x] A `cfClassChoice` prototype with `grants`, and `ClassChoices` on `CFCharacterBuildData`. No database migration: a build saved without the field loads with an empty list.
- [x] A button in the builder, using the same list as Heritage. There is one button per `slot` a class has options in, so a class needs only its options.
- [x] `cfDeity`, for classes with `usesDeity` (Cleric and Champion). Deities are data on the character, not a religion system.
- [x] Rogue's rackets, in `Class/Choices/rogue.yml`. The Thief is finished. The others give their skills, armor and key attribute, and the rest is marked `TBA`.
- [ ] Deity content: our own gods, `TBA`. There are no `cfDeity` entries yet, so the builder shows a disabled `Deity: TBA` button for classes that follow one.
- [ ] A racket always sets the key attribute. The rules say the player can choose it, so a Ruffian cannot keep Dexterity.
- [ ] Mastermind's second skill from a short list needs a pick in the builder.
- [ ] Sneak Attack works with every melee weapon. It should need agile or finesse, also work at range, and the Ruffian's weapon rule depends on that limit.
- [ ] Fill each class's choices in its wave.

### 1d. Level-up

All of this is picked in the lobby and stored on `CFCharacterBuildData`, the way `ClassFeatLevel2` is. `LevelUp` only hands it over. The builder slots have not been clicked in a real client. Tests: `CFLevelChoicesTest`.

- [x] Builder slots for levels 2 to 5: skill increases at 3 and 5, ability boosts at 5, a general feat at 3, and skill feats at 2 and 4.
- [x] The same choices copied to `CFExperienceComponent` at spawn, checked again there because they come from a client, and applied by `LevelUp`.
- [x] Run class features from `LevelUp` when their level is reached.
- [ ] The feat a background names, at level 1.
- [ ] Extra slots for classes that get more: a Rogue gets a skill feat every level and a skill increase from level 2.
- [ ] Changing a skill pick can leave a later feat the build no longer qualifies for. It is dropped at spawn with no warning, so the builder should clear it or say so.
- [ ] The level cap stays at 5, so spells stop at rank 3.

### 1e. Skill and general feats

`Feats/General/feats.yml` holds 131 feats through level 4 from Player Core, Player Core 2 and GM Core, with the site's one line summaries. `.agent/port-scripts/general_feats.py` rewrites that file from Archives of Nethys, so put fixes in the script. Of 74 feats backgrounds name, 65 match by name.

- [ ] Grants for them, in the waves. Battle Medicine moves from a grant on 8 backgrounds to a feat.
- [ ] Prerequisites beyond one skill rank are text, not enforced. 90 feats have a rank the game checks.
- [ ] The 9 background feats with no match: spelling (`Lie To Me`), two feats in one line, or from other books.

### 1f. Spells

- [ ] Dispel Magic, which needs PF2e counteract rules.
- [ ] Animal Form, which needs a battle form.
- [ ] Figment, Sigil and Know the Way. Give each something real to do, or flavour and `TBA`.
- [ ] Focus spells: a `Focus` spell kind, refilling focus points, and one focus spell per class that has them. `CFSpellbook.FocusPoints` exists and only the Animist sets it.
- [ ] Casting from a higher slot. Slotted spells only resolve at their own rank today, so heightening does not work for them.
- [ ] Signature Spells, and Divine Font's slots.
- [ ] Compare each spell's effects with its text. The scan only finds spells with no effect at all, not spells with part of one.

### 1g. Waves

Do 1a to 1d first, then work through these. For each entry: read it on Archives of Nethys, replace the placeholder in its `.ftl` with a short note in our own words, add its `grants`, and add a test where it does more than a stat change. An entry that cannot work in play keeps its flavour text and the word `TBA`. A class is finished when every feature and feat has grants or `TBA`.

| Wave | Options | Feats |
|---|---|---|
| 1 | Ancestries and heritages. Mostly grants, and no subclass needed | 255, and 129 heritages |
| 2 | Fighter, Rogue, Ranger | 81 |
| 3 | Cleric, Wizard, Bard, Druid, Witch | 105 |
| 4 | Barbarian, Champion, Monk, Swashbuckler, Investigator, Alchemist, Sorcerer, Oracle | 179 |
| 5 | Magus, Thaumaturge, Gunslinger, Inventor, Psychic, Commander, Guardian, Necromancer, Animist, Exemplar, Runesmith | 197 |
| 6 | Summoner. Its eidolon is a second body, and it needs companions finished first | 23 |

Witch familiars, Druid animal companions and Summoner eidolons all lean on `cfPet`, and "Companions have no inventory" under Step 4 gets in their way.

- [x] Feats with no effect show `(TBA)` in the builder's lists.
- [ ] Mark unfinished heritages and class features too. A feat with `grants` is finished unless it sets `pendingAction`. One without goes by its placeholder text.
- [ ] `CFFeatureCoverageTest`: fails if a finished class has a placeholder, and if a `grants` entry cannot be read.
- [ ] Kineticist is not included, but one feat file for it exists. Remove it.

### Decided

- **Choices are made in the lobby, up to level 5.** A player picks every feat, skill and boost through level 5 before they play. The server records them, spawns the character at level 1, and hands each level's choices over as XP earns that level. Nothing is chosen in play.
- **Unfinished options are marked in the builder**, not hidden, so a player can see what is coming.
- **Exploration and downtime options run on the world clock** where that works. Otherwise they keep their flavour text with the word `TBA` on its own, so `TBA` can be searched for later.
- **Swimming, flying and climbing are `TBA`.** Swimming will be ported from another fork, because SS14 has water entities. Climb speeds are not built, and `CFZLevelClimb` always rolls.
- **Deities are our own pantheon**, not Golarion's named gods. Speaking to a god is stock SS14 prayer, which sends the message to the admins, so there is no religion system to build.
- **Languages matter**, for roleplay: talk a goblin out of a fight in its own tongue. See "Languages" below.

### Languages

Built on 6 November 2026. `Content.Shared/_CF/Languages`, and `CFSpeech` in `Content.Server/_CF/Languages/ChatSystem.CF.cs`. Tests: `CFLanguageTest`. The builder button and the Speak verb have not been used in a real client.

- [ ] Radio is not translated. `EntitySpokeEvent` still carries the real words, so a radio or anything else listening to it hears them.
- [ ] Show known languages on the character sheet.
- [ ] `CFGrantLanguage`, for Multilingual and other feats that teach one.
- [ ] Recount the extra languages when Intelligence rises at level 5.
- [ ] Give monsters that talk a `CFLanguages`. Without one, a creature speaks plainly and understands everything.
- [ ] Every monster can now be played, so a player in a rat or a skeleton talks in plain words. Animals and mindless undead need a way to make noise only.
- [ ] Languages from a heritage or a background.

### Rest

Built on 6 November 2026. `CFLongRestSystem`. Tests: `CFLongRestTest`.

- [ ] A long rest has no limit. It takes no time, so a player can sleep in a bed mid-fight and be fully restored. PF2e allows one in 24 hours, after 8 hours.
- [ ] It restores hit points, wounded, spell slots and focus. Conditions, Battle Medicine immunity and other daily limits are left alone.

## Step 2: Finish the rebuild

Left from re-importing the other machine's lost work. Details of what was rebuilt are in `roadmapold.md`.

1. Fix the `Chemicals` guide failure under Known bugs.
2. Recipes: `python3 .agent/port-scripts/recipes.py` makes 59 more recipes with formulas. The output was removed before the push because it was never linted, so generate it, lint it, then run `CFCraftingTest`. Known limits: stack groups use the plain material, so birch planks do not count. Cooking recipes wait for a station. 73 results are not ported (clothing, backpacks, vials, CP14's key and lock items). 4 need an unported ingredient: Screwdriver, ElfPlushie, AgaricPlushie, SkeletonPlushie.
3. The Phase 3 weapons mostly have no recipe, because CP14 made them from modular parts. They need recipes of our own.
4. `Sharp` was dropped from 2 items (the component was renamed upstream). `ToggleableLightVisuals` was dropped from 1. Find the replacements.
5. Swap `CFSiteWallStone` back to `CFWallStone`. Add a test for the cost of standing up.
6. Update `codebase.md` and `ss14rules.md` with the new folders and the engine differences from the port.

### Check before trusting

- Weapon stats: armor, shields and 4 weapons were confirmed on Archives of Nethys. The other weapons came from the old table and memory. The buckler and chain shirt are unverified.
- Lock grades and Force Open DCs on ported doors are our choice: wood is Simple, iron and safes are Average, guard and guildmaster are Good. Force Open is 20 for wood and 30 for iron.
- Alcohol: the entry is legacy Gamemastery Guide content. Exposure has a 60 second cooldown, which is our rule so one drink counts once. The +1 against fear only shows a popup. Sickened cannot be cured early because nothing cures it yet, and it does not stop eating. Stupefied affects Will and Perception only, not spell attacks, spell DCs, or skill checks.
- Rings: CP14 dealt out keys at random at round start. Here each ring always holds the same keys, so every blacksmith, merchant and house ring gets the first key of its set.
- CP14 had no key for the Blacksmith 3 and Alchemy 3 doors, so `CFKeyBlacksmith3` and `CFKeyAlchemy3` are ours.
- Graves drop bones and copper instead of CP14 body parts and crate loot. Web cocoons drop copper instead of demiplane loot. `CFWallmountLamp` is gone; `CFWallLamp` replaces it. The lobby music entries were removed, so `CFLobbyMusic` only has the stock track.

### Port scripts

`.agent/port-scripts/` holds the converters. Run from the repo root in this order: `props.py`, `world.py`, `finish.py`, then `recipes.py`. **They rewrite the `Ported` folders, so hand edits there are lost; put fixes in `finish.py`.** They were written for a temp folder: `lint.sh` has that path hard-coded and needs editing.

Traps found while porting:

- `IconSmooth.key` is required, and a missing one drops the entity on the client only.
- CP14 files can start with a BOM, so read them as `utf-8-sig`, or the first prototype is lost with no error.
- The linter skips abstract entities, so a bad field there only shows up in the spawn test.
- `Crayon` needs `selectedState`. `Injector` needs `activeModeProtoId` and `allowedModes`. `Occluder` takes `polygon`, not `boundingBox`.
- Do not run `grep -r` over `Content.*`. It hangs.

## Step 3: Finish porting CP14

The rule for what comes over, and how, is under Phase 3b in `roadmapold.md`: RPG mechanics yes, new game systems no.

- [ ] Tools, materials, keys and locks (Pick a Lock / Force Open).
  - Still to do for locks: portable lock items, repairing a broken toolkit, and shoddy improvised picks. A critical Force Open unlocks but does not swing the door open.
- [ ] Workbenches and recipes on the PF2e Crafting rules.
  - Recipes: 21 of CP14's 179 are in `Resources/Prototypes/_CF/Crafting/recipes.yml`. The rest make items this branch does not have, so add them as those items land. `CFWorkbench` itself has no recipes yet, because all 37 of its results are missing. The 8 vampire recipes stay out. The cooking table waits for cooking.
  - Not done: Alchemical Crafting and Magical Crafting feat checks (no such recipes yet), batches of consumables, repairing items, `CP14StackGroup` and solution requirements, and reverse-engineering formulas.
- [ ] Creatures with PF2e stat blocks. `importcreatures.md` is the guide.
  - Each creature has one Strike, its first printed melee Strike. A second Strike needs a way to pick between attacks. Special abilities wait for the planned better AI, not for this row. They are not in: Grab, venom and disease, Ferocity, charges, Pack Attack, Filth Wave, the ooze's extra acid. The stat block comment on each creature lists what is missing. Immunity to critical hits and precision is not in either.
  - Size does not change a creature's body yet, so the Large bear is a Medium body.
  - Still to port: livestock (pig, sheep, rabbit, cat; there is no printed stat block for most), zombies (they need CP14's humanoid species), watchers, spineguard, flem, cackle, bone hound, mole, myconids, ice spectre, whistler, mosquito, hydra, fairy. CP14-only monsters need a PF2e creature of a matching level chosen first.
  - Boar and bear drop CP14's leather, scraps and bone when butchered. Meat waits for the Consumables port.
- [ ] Raw materials and coins.
  - Not done: raw materials have no price, so they cannot be sold. Giving stack prototypes a Price needs a shop fix first: the shop charges the listed price per prototype, whatever the stack holds, so `CFIronBar10` would be a bargain. Also not done: coin Bulk (1,000 coins is 1 Bulk), art objects, coins as monster loot, and looting a purse from a body.
- [ ] Items left out of the consumables port.
  - Not ported here: monster toxins (creatures row), cooking pots and their recipes (crafting row), smoking pipes and chromium jelly (they need plants from the farming row).
- [ ] Decide on CP14 alchemy.
  - CP14 alchemy (magic essences, `Target Effects` reagents, 67 reactions, cauldron brewing): **open question.** PF2e alchemy is the Crafting skill and formulas, not mixing reagents, so this may be a new game system. Left out until decided.

## Step 4: Close the rules gaps

Smaller gaps found while building the earlier phases. Take them when the system is next touched.

- [ ] `CFDcSource.Item` (needs item level, Phase 2). Line of effect is a `CFTargeting` flag but no spell turns it on yet.
- [ ] Damage type fields are still strings (`damageType: slashing`) on weapons and pets. Spells now use `ProtoId<CFDamageTypePrototype>` with PascalCase ids (Phase 1). Convert weapons and pets when Phase 2 rewrites them.
- [ ] 16 `_CF` systems subscribe in `Initialize()` instead of with `[SubscribeLocalEvent]` attributes. Convert each one when that system is next touched, not as a bulk change.
- [ ] Critical specialization effects by group (later; needs class features).
- [ ] Give the `Trip` weapon trait a use, such as counting as an extra shove.
- [ ] Attack-trait maneuvers (Shove, Disarm, Grapple, Escape) should count toward MAP.
- [ ] A visible raised state. Today it is a popup and an examine line. The riot shield RSI has no raised sprite.
- [ ] Only the spear flies point first and sticks in a target. The dagger, hatchet, light hammer and dart reach the cursor and Strike, but spin and drop. Each needs a `ThrowingAngle` to suit its sprite.
- [ ] A spear bounces off walls, where a stock spear sticks in them.
- [ ] Companions have no inventory, so nothing can wear `CFRatArmor`. Barding also needs an AC path: `ArmorFromEquipment` is off for creatures with printed AC.
- [ ] Take Cover gives only the AC bonus. Cover also adds to Reflex against area effects and to Stealth.
- [ ] CP14's magic staves and the ice bow (spell storage on mana) wait for spellcasting staves.
- [ ] The longbow and shortbow share CP14's one bow sprite.
- [ ] A sling sprite, and its own ammunition. It still shoots arrows and needs wielding.
- [ ] Double Slice applies resistances to each Strike. The rule combines the damage first.
- [ ] Reposition moves as far as stock allows, up to 10 feet. The rule is 5 feet, 10 on a critical success.
- [ ] Aid's critical success is always +2. The rule gives more at master and legendary.
- [ ] Darkness does not count as concealment yet. It waits for the `Light` layer (6c).
- [ ] Recall Knowledge picks a skill by trait once creatures have trait data.
- [ ] Treat Wounds always uses the highest DC the healer's rank allows. The rule lets them choose.
- [ ] Reactive Strike does not fire on movement, so Step does nothing yet.
- [ ] The spin, sprite hiding, instrument, Hide verb, Alt+Z and pointing inputs have only been tested through code, not played in a real client.

## Step 5: AI

The labels are the sections of Phase 6 in `roadmapold.md`, which has the design. `goap.md` is how it works now.

**6a. Steering behaviours (can start now)**

- [ ] Debug overlay for active behaviours. Stock's steering debug already draws interest and danger.

**6b. GOAP planner**

- [ ] NPCs that cannot stand crawl away (see Known bugs).

**6c. Influence maps (heatmaps)**

- [ ] Nothing paints `Threat` yet. Spells and doors make no noise yet.
- [ ] `Light` layer: not built. Stealthy NPCs cannot avoid light, `Shadow` nodes are placed by mappers only, and darkness is not concealment.
- [ ] Scent entering a portal continues on the linked level. Each level paints its own map, but an NPC does not cross a portal to follow it.
- [ ] Debug overlay to view layers.

**6d. Temporal scent vectors**

- [ ] Survival/Track interaction later: trackers read older or fainter scent; Cover Tracks lowers intensity.

**6e. Wayfinding / topological hint nodes**

- [ ] Stairs/hole nodes link to the next Z-level. `linked` exists, but nothing follows it yet, so pursuit stops at the stairs.

**6f. Personalities**

- [ ] Not tested: a real race on one trail, and a stealthy path measured against the walls.

**6g. Verify**

- [ ] A purpose-built test map. See proposal 1 in `ai-testing.md`, which covers this and the corridor, side room and closet.

**6h. Encounters**

- [ ] The locked door scenario reaches the target in about 33 of 40 runs. The suspected causes are in `ai-testing.md`.
- [ ] A kobold alone is slow on glass: Hardness and Multiple Attack Penalty leave it about 1 damage in 13 seconds, so it usually runs out of patience.
- [ ] Choosing a detour over glass is only tested on `CFGOAPFlowFieldBuilder`. The arena has lava either side of its one clear row, so a live test has no room.
- [ ] A field is built in one go. 20 tiles is a few hundred at most, but a much larger `FieldReach` would want spreading over ticks.
- [ ] Group memory lives on the spawner, so deleting the spawner mid-fight stops the group sharing a target.
- [ ] `CFEncounterSpawner` shows to everyone, players included. It left `MarkerBase` as a stand-in, because a `Marker` stays hidden until `showmarkers`, even for an aghost. It should show to aghosts and DMs only.
- [ ] `Shoot` is not weighed against Multiple Attack Penalty, and an encounter's `TakeFlank` has not been tried on an archer.

**6i. Our own structure (6 November 2026)**

- [ ] Check in play that fleeing feels right.

**6j. Tactics (6 November 2026)**

- [ ] Balance: a creature with discipline waits out Multiple Attack Penalty, so it hits much harder over a long fight. Worked out at 4 to 5 times for a +8 attack against AC 18. Check this in play, and drop `discipline` if it is too much.
- [ ] Give the creatures in `creatures.yml` a `tactics` value. They all use `Default` unless an encounter sets one.
- [ ] Choosing spells. See `goap-weights.md`.

**6j2. The kobold trapper (6 November 2026)**

`CFMobKoboldTrapper`, `KoboldAmbush`, `CFConstructedTrap`. Tests: `CFKoboldTrapperTest`. How it works is in `goap.md`.

- [ ] Not played yet: the full fight, a ghost in the body, Construct Trap on the wheel, and how a kobold looks with a crossbow.
- [ ] A ghost cannot call its warriors back. Only attack orders exist: pointing at itself or the floor does nothing.
- [ ] The trapper does not pick its crossbow back up if a fight ends with the hammer out.
- [ ] Its Stealth and disable DCs and its level are ours. Disabling one gives hazard XP, which the rules would not give for a creature's ability.
- [ ] Fleeing goes by the safety map, so it can run over the trapper's own side of the room and past the warriors. It does not head for its traps.

**6k. Furniture, and a run on the maze map (6 November 2026)**

- [ ] A mob cannot follow a trail through a door. A closed door takes no scent, and `FollowScent` does not use `Navigate`, so it never opens one. It also flips between `HuntTarget` and `Investigate` as scent comes and goes.
- [ ] `Investigate` never Seeks, so a hidden creature beside the mob is not looked for without a node.
- [ ] `Wander` runs at full speed and can jam for good in a corridor. With a home it also paces: every trip ends at home, but at a range of 3 tiles it makes about one trip every 2 seconds, and at the default 8 about one every 10. Check in play whether that looks wrong.
- [ ] A routed creature that can still see its enemy never hides, because the flee map ignores line of sight.
- [ ] Swings at obstacles ignore `discipline`, so the pool stays empty and it stays at -10.

**6m. Fire (6 November 2026)**

- [ ] Stock cold, suffocation and pressure still only touch the bar, so they can kill at full CF HP. The same pattern would fix them.
- [ ] A creature rolls even with an enemy beside it. Check in play whether a smart one should finish the fight or get clear first.
- [ ] Players roll for free, as in stock. PF2e has no fixed cost here, so it has no action cost for now.
- [ ] A creature that falls in lava still relies on its steering to get out. The steps it would run there were not tested on real lava.

**Debug code to remove**

- [ ] The decision log, once the AI has settled. `goap.md` lists the steps.

## Known bugs

- `CFSkeletonguardTest.OutOfArrowsItDropsTheBowAndUsesItsClaws` fails in every full CF run (it stops 2.4 away, and the test wants under 2) and passes alone, so it depends on what ran before it.
- `RainWatersExposedCrops` is unreliable, not just in a full run. On 6 November 2026 it showed as `Skipped` in two full runs, then failed once and passed once on its own. The failure message was not captured, so the cause is unknown. It only touches weather and plants, not the AI.
- `GuideEntryPrototypeTests.Validate("Chemicals")` fails with `Hit style update limit. Queued: 1073` from `RecipesContainer`. The guide now has so many recipes that the UI warns while building it, and the test treats that warning as an error. Not investigated further.
- `CFProneTest.NpcWithNoRoomKeepsTryingToStand` fails now and then. The same error also hit `NpcStandsOnceItHasAnAction`: an NPC's stand-up attempt adds a `StatusEffectContainerComponent` to a mob that is already being deleted, and the assert surfaces in the next test's setup. Left alone on purpose. It only affects stock NPCs now. GOAP NPCs are the only ones planned to crawl away, and that is not built.

## Later

- Sky ships with FTL travel that dock on breathable planets below, for exploring. Those planets get dungeons too. Floating caves in the sky are the adventure sites until then. `CFAdventureSiteSystem.Generate` always makes its own map, so planet dungeons need it to build on an existing map as well.
- Alchemical bombs and elixirs ported from CP14 alchemy, built on `CFAreaEffect` + `CFCheck`.
- Hazards and traps (area + check + degree effects). Prototype done: `CFHazard` and the Beginner Box spike trap (`CFSpikeTrap`, `CFSpikeTrapMonster`). Still to do: complex hazards with routines, resetting traps, and area traps (`CFAreaEffect` on the hazard).
- Persistent damage and resistances/weaknesses everywhere damage is dealt.
- AI avoids known traps through the `Threat` layer (6c).
- GOAP for companions and animal companions taking player commands (Command an Animal).
- Mobs that cast spells, chosen by score. `goap-weights.md` has the plan.

## Working notes

- Every step ends with a full build (`dotnet build -c Tools`, errors only) and targeted integration tests.
- No new key functions and no edits to core input code. If something cannot be reached with stock controls, it goes in the verb menu or on the wheel.
- AI systems never cheat with omniscience: tracking uses sight, the influence maps, LKP and nodes only.
- Before subscribing a component and event pair, check `ss14rules.md`: `ProjectileComponent` + `BeforeProjectileHitEvent` already belongs to `CFStrikeSystem`.
- Imported CP14 code keeps its logic but takes CF names, namespaces and `_CF` folders. Credit CP14 in a header comment where a file is mostly theirs.

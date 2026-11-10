# CarpFinder roadmap

Ordered plan for the next stretch of work. Each step builds on the one before it. Tick items off as they land, and move finished work to `roadmapold.md`, which also holds the history of everything already built.

Source of rules: https://2e.aonprd.com/ (Remaster). Source of structure and assets: `C:\project\crystall-punk-14` (**read-only**: copy from it, never edit it).

## Do first: playtest feedback

Complaints from a playtest, noted on 9 November 2026. These come before everything else on this roadmap. Built on 9 November 2026. Tests: `CFPlaytestFeedbackTest`.

- [ ] Not seen in a real client: the sparkles and their colours, the aura border, the faster swing, and aim assist with a real mouse.

### Spell effects

- [x] A creature targeted by a spell with a save gets sparkle particles, coloured by the type of spell. `CFVfxSystem`, with the colour from the spell's damage type, or from its traits if it deals none.
- [x] A caster gets particles while casting, so people around them can tell. A subtle spell shows none.
- [x] Healing puts healing sparkles on both the healer and the one being healed. It is in `CFEffectSystem.Heal`, so anything that heals gets them.
- [x] An aura has a hard line where it ends, drawn as a magic border. `CFAuraOverlaySystem`. It is only drawn while the aura is doing something.

### Combat

- [x] Melee aim assist. `CFMeleeAimSystem`: a left-click hits the closest enemy in reach within 60 degrees of the cursor, wherever the cursor is. Clicking an enemy in reach still hits that enemy. Neutral creatures and hidden ones have to be clicked. Right-click maneuvers do not use it.
- [x] Action pool cooldown goes to 1.2 seconds (`RegenSeconds`). It was really 1, not the 1.45 the docs said, so players are a little slower than before.
- [x] Cut the melee hit animation in half, so it is much faster.

### GOAP

- [x] All mobs have a pool cooldown of 1.5 seconds, stock HTN mobs as well as GOAP ones (`NpcRegenSeconds`). Players stay at 1.2. A monster with a player in it counts as a player.
- [x] All mobs get a `ThinkingTimer`, default 1 second, which gives them a moment to think about their next attack. It is checked where a Strike or shot is attempted, so both AIs get it. Swings at doors and other obstacles do not wait.
- [x] Demoralize and Feint from GOAP wait for it as well (`CFGOAPManeuverStepSystem`). Tests: `CFGOAPTest`.
- [x] `thinkingTimer` is a field on `CFCombatant` in each creature's YAML, so it can differ per mob. A smarter mob with higher reflex, such as a boss, thinks faster. Every creature uses the default until someone sets one.

### Medical kit

- [x] A medic healing someone with a kit force grapples them, so they cannot run away. There is no roll. The patient can still break out of it with Escape, which ends the treatment.
- [x] Rebalance: healing someone takes 12 seconds and costs 3 actions, one every 4 seconds starting at once. Running out of actions ends it.
- [x] The creature being healed gets the magic healing sparkles here too, at each of the 3 actions and when it heals.

### Character setup

Built on 9 November 2026. Tests: `CFCharacterSetupTest`.

- [ ] Not seen in a real client: the ABCJ panel and Accept, the shop inside Character Setup, and how the clothes look on each body.
- [x] A character cannot ready up or join without an ancestry, background, class and job. Late join still opens the job list first, and is refused when a job is picked.
- [x] The ABCJ panel is Ancestry, Sprite, Background, Class, Job, Accept. Everything else stays hidden until Accept, which also saves.
- [x] The shop in Character Setup uses the merchant layout, with search and a quantity. It only sells what the job's loadouts offer, not a merchant's whole catalogue.
- [x] Everyone starts in clothes and shoes, with an adventurer's kit on their back: a backpack holding rope, a torch, flint and steel, a ration and a canteen of water. The kit is where extra copies go. Striking the flint on a torch has not been tried in a real client.
- [x] Extra copies are stored per job (`CFCoins.CopiesOf`), so two jobs that sell the same item do not share a count. Tests: `CFCharacterSetupTest`.

## Step 1: Make character options work

Players can pick hundreds of options that do nothing. This step gives them effects.

### Where it stands

Measured on 9 November 2026. `python3 .agent/port-scripts/feature_scan.py` prints these numbers, per class and ancestry.

| Option | Total | Works | Does nothing yet |
|---|---|---|---|
| Class feats | 585 | 111, and 2 in part | 472, each with its own note and `TBA` |
| Ancestry feats | 255 | 70, and 6 in part | 179 |
| Class features | 181 | No placeholders are left | The parts still missing say `TBA` |
| Heritages | 149 | 67 | 82 |
| Ancestries | 23 | Boosts, HP, Speed, size, darkvision, languages | Traits are labels |
| General and skill feats | 131 | 12 | Pickable in the builder, marked `(TBA)` until they work. See 1e |
| Backgrounds | 324 | Boosts, skills and the skill feat they name | 259 grant one, but few of those feats do anything yet (1e) |
| Spells | 62 | 57 | Figment, Know the Way, Sigil, Animal Form, Dispel Magic are popups. Prestidigitation has its own system and works |
| Focus spells | | The pipeline works (1f) | Filled class by class. Many hexes, compositions and domain spells are still `TBA` |

### What blocks it

Neither of the original blockers is left. Subclass options exist for every class that has them (1g), and the placeholder feats and features now carry their own notes or `TBA`. What is left is building each `TBA`, and `CFFeatureCoverageTest` stops new placeholders coming back.

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
- [x] A bonus against a trait counts on saves, Perception, attacks and AC (`CFBonusSystem.AgainstSource`). Tests: `CFGeneralFeatTest`.
- [ ] On attacks and AC it reads the creature's traits only, not those of the weapon or spell.
- [x] The ids in `CFGrantProficiency` are checked by `CFFeatureCoverageTest`, so a typo fails a test.
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
| `CFGrantReaction` | A trigger and what it does. See 1b | 63 reaction feats |
| `CFGrantOption` | Sets a `CFCombatOptionsComponent` flag | Everything already coded |

### 1b. The shapes actions come in

Most action feats share one of a few shapes, so build the shape once. An action is an entity with `CFActivity` and the usual `CFTargeting`, `CFCheck` and `CFDegreeEffects`. Do not write a new resolver.

- [x] Stances (21 feats). `CFStance` on an action, parented on `CFActStanceBase` in `Actions/stances.yml`, run by `CFStanceSystem`. Built on 9 November 2026, with Point-Blank, Mountain, Crane, Dragon and Tiger. The other 16 stances are filled in their waves. Tests: `CFStanceTest`.
  - One at a time, and another stance action waits 6 seconds. Using the same action again is Dismiss, which costs its action and does not wait.
  - It ends on being knocked out, when armor breaks an unarmored requirement, or on another stance action. Real time has no encounters, so it does not end when a fight does.
  - [x] Falling asleep ends a stance, from the condition or stock sleep. Paralyzed does not, because it is not being knocked out. Tests: `CFStanceTest`.
  - [ ] Not seen in a real client: the action button lighting up while in a stance.
  - [ ] Stances with other requirements have no check yet, such as Mountain's touching the ground, or a weapon a stance needs.
- [x] Flourishes (36). Add `CFFlourish` to an action. `CFActionPoolSystem` allows one until the pool is next full. Flurry of Blows and Sudden Charge use it, and Vicious Swing keeps its code path. The rest are filled in their waves.
- [x] Spellshape (34 feats). `CFSpellshape` on an action, parented on `CFActSpellshapeBase` in `Actions/spellshapes.yml`, run by `CFSpellshapeSystem`. Built on 9 November 2026, with Reach Spell (7 classes) and Widen Spell (5 classes). The other 22 are filled in their waves. Tests: `CFSpellshapeTest`.
  - It waits for the next spell, and is lost after 6 seconds or when an action is spent on anything else. Free actions and reactions do not end it, though the rules say they should.
  - Touch spells have `touch: true` on `CFTargeting`: they reach the next tile in every direction, diagonals too, and Reach Spell makes that 30 feet. Tests: `CFTouchRangeTest`.
  - [ ] Not seen in a real client: the widened area overlay and the action button lighting up.
- [ ] Reactions (63 feats). `CFGrantReaction` holds a `CFReaction`: a trigger, what has to be true, and what it does. `CFReactionSystem` runs them. Built on 9 November 2026, with Nimble Dodge, Flashy Dodge, Deflect Projectile, Charmed Life, Orc Ferocity and Wounded Rage. Tests: `CFReactionTest`.
  - Reactions stay automatic and off the wheel. One only fires when it would change the outcome, so it is not wasted, and the character effectively sees the roll first. That is a deliberate change from PF2e.
  - Triggers so far: `AttackWouldHit`, `SaveWouldFail`, `Damaged`, `WouldDrop`, `EnemyLeftReach`. `perDay` limits are restored by a long rest.
  - `EnemyLeftReach` fires when an enemy leaves the reach circle under its own power. Walking out is PF2e's Step: it costs 1 action and does not provoke, and with no actions left it provokes anyway. It is only charged when the watcher could have answered, and one Step covers everyone left within a second. Moving about inside reach does not provoke either, because in real time that would fire on every footstep. Reactive Strike and Stand Still use it. Tests: `CFLeftReachTest`.
  - `WouldBeDamaged` and `AllyWouldBeDamaged` fire as damage is about to land, through `CFReactionSystem.AnswerDamage`, and need the enemy in the owner's aura. Each Champion cause is one of these. Tests: `CFChampionCauseTest`.
  - [ ] Causes where the rules let the enemy choose always use its refusal, as a mindless creature would: Iron Command, Glimpse of Redemption.
  - [ ] Triggers still needed: an ally being attacked without being damaged yet, a spell being cast (Counterspell), and a critical hit (Scars of Steel).
  - [ ] A response that moves the creature, for No Escape and Reactive Pursuit.
  - [ ] NPCs do not walk to avoid Reactive Strike. They always run.
  - [x] Spell attacks trigger `AttackWouldHit`, as melee, so Deflect Projectile does not answer one. Reactive Shield still only answers Strikes. Tests: `CFReactionTest`.
  - [ ] Reactive Strike, Shield Block, Reactive Shield and You're Next are still hard-coded in `CFReactions`. Move them when their class wave comes.
- [x] Auras. `CFAura` on a creature and `CFAuraEffect` entries, added with `!type:CFGrantAura`, run by `CFAuraSystem`. Built on 9 November 2026 for the Champion's aura, with Aura of Courage, Aura of Despair, Divine Health and Shields of the Spirit. Tests: `CFAuraTest`.
  - It looks up who is inside 4 times a second, through `CFEffectSystem.CollectArea`, only for creatures that have an aura. It is not a physics sensor: a 15-foot sensor would make the problems in `ss14rules.md` about the flanking sensor much worse, and would be a circle where emanations are tiles.
  - "At the end of your turn" is every 6 seconds, on the owner's own timer. A placeholder: the timing will probably change.
  - [ ] Not seen in a real client, nor is its border (`CFAuraOverlaySystem`).
  - [ ] Bard compositions, Hampering Stance and Channeled Protection can use this when their waves come. A choice of when to suppress the aura is not built.

### 1c. Subclasses

Built on 9 November 2026. `CFClassChoices` decides what counts as a valid pick. Tests: `CFClassChoiceTest`. The builder buttons have not been clicked in a real client.

- [x] A `cfClassChoice` prototype with `grants`, and `ClassChoices` on `CFCharacterBuildData`. No database migration: a build saved without the field loads with an empty list.
- [x] A button in the builder, using the same list as Heritage. There is one button per `slot` a class has options in, so a class needs only its options.
- [x] `cfDeity`, for classes with `usesDeity` (Cleric and Champion). Deities are data on the character, not a religion system.
- [x] Rogue's rackets, in `Class/Choices/rogue.yml`. The Thief is finished. The others give their skills, armor and key attribute, and the rest is marked `TBA`.
- [x] Seven placeholder gods in `Character/deities.yml`, for Clerics and Champions. Each gives a skill, a favored weapon (`CFGrantWeaponFamiliarity`, which counts it as simple) and what Divine Font casts. Not clicked in a real client. Tests: `CFClassChoiceTest`.
- [ ] Replace the placeholder names and lore when the setting is written. Domains, bonus spells, edicts and anathema are not built. A Champion's cause is not checked against the deity's holy or unholy side.
- [x] A Ruffian, Scoundrel or Mastermind picks its key attribute with a `KeyAttribute` entry in `choices`, and keeps the racket's until it does. Not clicked in a real client. Tests: `CFGeneralFeatTest`.
- [ ] Fill each class's choices in its wave.

### 1d. Level-up

All of this is picked in the lobby and stored on `CFCharacterBuildData`, the way `ClassFeatLevel2` is. `LevelUp` only hands it over. The builder slots have not been clicked in a real client. Tests: `CFLevelChoicesTest`.

- [x] Builder slots for levels 2 to 5: skill increases at 3 and 5, ability boosts at 5, a general feat at 3, and skill feats at 2 and 4.
- [x] The same choices copied to `CFExperienceComponent` at spawn, checked again there because they come from a client, and applied by `LevelUp`.
- [x] Run class features from `LevelUp` when their level is reached.
- [x] The feat a background names, at level 1: `skillFeats` on `cfBackground`, read through `CFSkills.SelectedFeats`. It needs no qualifying and cannot be taken again in a slot. Three backgrounds name two feats and get both, which has not been checked against the rules (Hermean Heritor and the ones with Hefty Hauler or Diehard). Contract Negotiator and Ravening's Desperation are from other books and are not granted.
- [x] Extra slots for classes that get more: `bonusSkillFeats` and `bonusSkillIncreases` on `cfClass`. Only the Rogue has them (skill feats at 1, 3, 5 and increases at 2, 4). Not clicked in a real client. Check whether the Investigator should too.
- [x] Changing a pick clears any later feat the build no longer qualifies for (`CFSkills.DropUnqualifiedFeats`), in the builder and again at spawn. Changing ancestry goes around it, so that case is only caught at spawn. Tests: `CFLevelChoicesTest`.
- [x] Spell slots grow with level. `laterSlots` on a class's `spellcasting` holds its Spells per Day table from Archives of Nethys, `GrowSpellbook` applies it on level up, and the builder offers every slot through level 5. Tests: `CFSpellSlotTest`. A pick only counts once the slot for it has arrived, by its order in that rank. A Wizard's curriculum slot is not built, and Divine Font's extra slots stay at rank 1 where the table moves them to the highest rank.
- [ ] The level cap stays at 5, so spells stop at rank 3.

### 1e. Skill and general feats

`Feats/General/feats.yml` holds 131 feats through level 4 from Player Core, Player Core 2 and GM Core, with the site's one line summaries. `.agent/port-scripts/general_feats.py` rewrites that file from Archives of Nethys, so put fixes in the script. Of 74 feats backgrounds name, 65 match by name.

- [ ] Effects for the rest. 12 work so far: Battle Medicine, Multilingual, Arcane Sense, Armor Proficiency, Canny Acumen, Diehard, Fleet, Hefty Hauler, Shield Block, Skill Training, Toughness and Weapon Proficiency. `general_feats.py` keeps them in `WORKING`. Weapon Proficiency only gives martial weapons, not the advanced weapon a character already trained in martial would get. Toughness and Fleet do not show in the builder's preview. Tests: `CFGeneralFeatTest`.
- [ ] Prerequisites beyond one skill rank are text, not enforced. 90 feats have a rank the game checks.
- [ ] The 2 background feats with no match, from other books: Contract Negotiator and Ravening's Desperation.

### 1f. Spells

- [ ] Dispel Magic, which needs PF2e counteract rules.
- [ ] Animal Form, which needs a battle form.
- [ ] Figment, Sigil and Know the Way. Give each something real to do, or flavour and `TBA`.
- [x] Focus spells: `focus: true` on `CFSpell` and `CFGrantFocusSpell`, up to 3 Focus Points, refilled by a long rest. Refocus gives one back after 10 world minutes (2.5 real minutes at the default clock), and moving or being hurt interrupts it. In `Spells/Focus/`. Tempest Surge and Force Bolt are the first ones. Tests: `CFFocusTest`.
- [x] Casting from a higher slot. A spell resolves at the rank of the slot it was paid from (`CFSpellComponent.ResolvedRank`). A prepared caster puts it in a higher slot in the lobby or on the sheet. A spontaneous caster has to learn it at that rank (`KnownAtRank`). Tests: `CFSpellSlotTest`. Not seen in a real client.
- [x] A spell castable at several ranks opens a radial menu of them, before its mode or bolt count (`CFRankMenuSystem`). The rank is sent on the perform request and checked when it is paid for. Tests: `CFSpellSlotTest`. Not seen in a real client.
- [ ] Apparition spells still only resolve at their own rank.
- [x] Signature Spells: one spell of each rank, picked in the builder through a `SignatureSpell` entry in `choices`, cast at any rank with a slot from level 3. For Bard, Oracle, Psychic, Sorcerer and Summoner. Not clicked in a real client. Tests: `CFSpellSlotTest`.
- [x] Divine Font's slots sit at the cleric's highest rank and move up as it levels (`FontSlots`), so Heal and Harm heighten. They are still Charisma modifier, at least 1, where the rules say 1 plus Charisma. Kept on purpose. Tests: `CFSpellSlotTest`.
- [ ] Compare each spell's effects with its text. The scan only finds spells with no effect at all, not spells with part of one.

### 1g. Waves

Do 1a to 1d first, then work through these. For each entry: read it on Archives of Nethys, replace the placeholder in its `.ftl` with a short note in our own words, add its `grants`, and add a test where it does more than a stat change. An entry that cannot work in play keeps its flavour text and the word `TBA`. A class is finished when every feature and feat has grants or `TBA`.

| Wave | Options | Feats |
|---|---|---|
| 1 | Ancestries and heritages. Mostly grants, and no subclass needed. Worked through on 9 November 2026. Heritages: 60 have working effects and 77 are marked `TBA`. Ancestry feats: 38 new ones work, and the rest are marked. Entries that only had `TBA` added still need their text checked against the rules when each is built. Tests: `CFHeritageTest`, `CFAncestryFeatTest` | 255, and 149 heritages |
| | Wave 1 waits on: climb, swim and fly, a rule for choosing another ancestry's feats, and a `cfFeat` that needs a heritage (prerequisites are text) | |
| 2 | Fighter, Rogue, Ranger. First pass on 9 November 2026: class features, Hunter's Edge, Sneak Attack limits and 9 passive feats work, and the rest say `TBA`. Tests: `CFWave2Test`. Strike actions: `CFStrikeAction` in `Actions/strikes.yml`, with Intimidating Strike, Dual-Handed Assault, Lunge, Head Stomp, Twin Takedown and Hunter's Aim (tests: `CFStrikeActionTest`). Still to do: Strikes that hit several targets (Swipe, Quick Reversal, Double Shot), Strikes that add a maneuver (Brutish Shove, Slam Down, Sleek Reposition, Combat Grab), and two shots from a bow that holds one arrow (Hunted Shot) | 81 |
| 3 | Cleric, Wizard, Bard, Druid, Witch. Started on 9 November 2026: focus spells, all subclasses as choices, and class features that are rank raises. Most subclasses are still mostly `TBA`: their feats and focus spells. Feats worked through on 9 November 2026: 15 work, and the rest say `TBA`, mostly waiting on composition spells, domains, familiars, battle forms and the Witch's lessons. Feats that need a muse, order, racket or doctrine now require it (`requiresChoice`). Tests: `CFWave3Test` | 105 |
| 4 | Barbarian, Champion, Monk, Swashbuckler, Investigator, Alchemist, Sorcerer, Oracle. First pass on 9 November 2026: every subclass is a choice, level 1 to 5 class features are done or say `TBA`, every feat has its own note, and 22 more feats work. Tests: `CFWave4Test`. A Sorcerer's bloodline sets its tradition (`tradition` on `cfClassChoice`). Still to do: Alchemist versatile vials and Quick Alchemy, blood magic and bloodline focus spells, Oracle curses and revelation spells, temporary Hit Points and a time limit on Rage, and Intelligence on a stored Devise a Stratagem roll. Witch patrons could set their tradition the same way | 179 |
| 5 | Magus, Thaumaturge, Gunslinger, Inventor, Psychic, Commander, Guardian, Necromancer, Animist, Exemplar, Runesmith. First pass on 9 November 2026: subclasses that mean something today are choices, level 1 to 5 class features are done or say `TBA`, and every feat has its own note. Tests: `CFWave5Test`. The systems this wave waited on are built: see the list below, which also says what each still lacks | 197 |
| 6 | Summoner. First pass on 9 November 2026. An eidolon is a companion ordered by pointing, and a ghost role so another player can take it: `CFEidolonServerSystem`, `Companions/eidolons.yml`, and three builder slots in `Choices/summoner.yml` (type, key attribute, attack). Hit Points are one pool, kept in step by comparing both bodies each tick. It has its own action pool, where the rules share one, because a second player would otherwise be spending the Summoner's actions. It stays within 100 feet. Tests: `CFEidolonTest`. All 15 types set a tradition, skills and attributes. Every type uses one placeholder body. Furious Strike, Tendril Strike and Dragon Breath work, the Ooze's resistance and the Undead's void healing work, and the other types' abilities say `TBA`. Not built: the second attack and the primary attack's traits, Act Together, Share Senses, runes and gear on eidolons, and most evolution feats | 23 |

Witch familiars, Druid animal companions and Summoner eidolons all lean on `cfPet`, and "Companions have no inventory" under Step 4 gets in their way.

- [x] Feats with no effect show `(TBA)` in the builder's lists.
- [x] Heritages show `(TBA)` in the builder when they have no effect at all. One with part of its rules working keeps its name and says which part is `TBA` in its text. Tests: `CFHeritageTest`.
- [x] Unfinished class features show `(TBA)` in the class's description in the builder, when nothing about them works yet (`CFClassFeature.Unfinished`). Not seen in a real client.
- [x] `CFFeatureCoverageTest`: fails on a placeholder in any class, on a class feat with no effect and no `TBA`, on a mistyped proficiency, and on an option in the files that did not load.
- [x] Kineticist is not included, and its stray feat file is gone.

### Systems Wave 5 is waiting on

Do all of these before Wave 6. Smallest first, each landing working and tested.

- [x] Recharging Spellstrike. `SpellstrikeSpent` on `CFCombatOptionsComponent`, recharged by its action or by a spell with `conflux: true`. Tests: `CFGuardianMagusTest`. Choosing which spell goes into the Strike, and counting as two attacks for Multiple Attack Penalty: not built. Each hybrid study's own conflux spell: not built.
- [x] Taunt. `CFTauntSystem`, and Intercept Attack as the `InterceptsDamage` trigger. Tests: `CFGuardianMagusTest`. Taunts do not change who an NPC chooses to attack, and an area that also holds the taunter still counts as ignoring them. Intercept Attack does not move the Guardian.
- [x] Firearms. `Entities/Objects/Weapons/Ranged/CFFirearms.yml`, on the crossbow pipeline: each shot pays for its reload. `CFReloadSystem` loads from what the user carries, and a slinger's reload prepays the next shot. Concussive, kickback and scatter are trait rules. Tests: `CFFirearmTest`. Misfires are not built, because nothing tracks cleaning a weapon. Scatter only splashes enemies. Capacity weapons and combination weapons are not built.
- [x] Implements. `CFImplement` on an item, in `Entities/Objects/Misc/CFImplements.yml`, and `CFImplementSystem` for what is held. Tests: `CFImplementTest`. Implement's Empowerment and the initiate benefits of Amulet, Chalice, Lantern, Regalia, Shield, Tome, Wand and Weapon work, in part. Bell, Censer and Mirror give the item only. Adept benefits and esoterica are not built. The Censer has no art of its own and uses the Arabian lamp.
- [x] Psi amps. `CFPsiSystem`. `CFActAmp` is a free spellshape that arms the next psi cantrip, which spends a Focus Point. What an amp does is data on `CFPsiCantrip` on the spell's action. Unleash Psyche and three of four psyche actions work. Tests: `CFPsiTest`. Amps from feats (Mental Balm, Homing Beacon, Warp Space), forbidden thought, distortion lens, warp step, message and telekinetic hand are not built. Telekinetic Rend is one 10-foot burst, not two 5-foot ones. Stunned, the temporary Hit Points on Frostbite and the Oscillating Wave's alternation are not built.
- [x] Tactics and the banner. `CFTacticSystem`, `Actions/tactics.yml`, and the item `CFCommanderBanner`. Three tactics are picked in the builder as `cfClassChoice` slots, because everything is chosen in the lobby. The squad is the Commander plus 2 + Intelligence nearest allies in the banner's 30 feet. Tests: `CFTacticTest`. Seven of the level 1 tactics work. Real time cannot make other players move, so a tactic's Stride or Step is +10 feet Speed for 3 seconds. Double Team, End it!, Coordinating Maneuvers, Mountaineering and Naval Training, Passage of Lines, Protective Screen and Shadows in the Moonlight are not built, and nor are the folio, Plant Banner, Warfare Lore or the penalty for losing the banner.
- [x] Innovations. `CFInventorSystem`, items in `Entities/Objects/Misc/CFInnovations.yml`, the construct in `Companions/constructs.yml`. A class choice picks the innovation and another its modification. Overdrive, Explode and the unstable check work. A broken innovation is usable again after the time Refocus takes. Tests: `CFInnovationTest`. The construct has the prototype's statistics from Guns & Gears, on a stock clean bot body. Its second attack, and being broken and repaired at 0 Hit Points, are not built. Companions level with their owner through `CFCompanionSystem.MatchOwnerLevel`. Tests: `CFCompanionLevelTest`. Construct modifications, Ranged Blunt Shot, weapons other than the longsword base, razing and hampering are not built.
- [x] Thralls. `CFThrallSystem` (Shared) and `CFThrallServerSystem`. `CFThrall` in `Companions/thralls.yml`, a level -1 companion with 1 Hit Point that only acts when commanded. Create Thrall, Thrall Charge, Command a Thrall and the grave spells cast on a thrall through `CFThrallBlastEffect`. Tests: `CFThrallTest`. Thralls spawn beside the Necromancer without checking the tile is free. Their Strikes do not count toward the Necromancer's Multiple Attack Penalty. Inevitable Return, Consume Thrall, Thrall Teamwork, Song of the Soul, Muscle Barrier and regaining Focus by destroying a thrall are not built.
- [x] Apparitions. `CFApparitionSystem`, `CFApparitions` and `CFSustain` in `Spells/`. Two apparitions are picked in the builder (`Choices/apparitions.yml`), the Primary slot and the Second, and `group` keeps a build from taking one twice. Apparition slots are their own pool on `CFSpellbookComponent`, spent like a spontaneous caster's. Sustaining is using the action again: 1 action, no Focus Point, once per 6 seconds, lapsing after 12, for up to a minute. Tests: `CFApparitionTest`. One apparition slot per rank, at levels 1, 3 and 5, from the class table. Most apparition spells do not exist in the game yet, so each option's text says which are missing, and some apparitions teach nothing yet. Traveling Workshop and Darkened Forest Form are not built, so Crafter and Stalker have no vessel. Several vessels are partial, and their summaries say how. Heightening apparition spells waits for casting from a higher slot (1f). Changing primary on Refocus, Relinquish Control, Apparition Sense, spirit familiars and Channeler's Stance are not built.
- [x] Ikons. `CFIkonSystem`, `cfIkon` prototypes in `Combat/ikons.yml`, actions in `Actions/ikons.yml`, and three builder slots in `Choices/exemplar.yml`. A Shift action per ikon moves the spark, and a transcendence is greyed out until it can be used. Weapon ikons are whatever matching weapon is in hand, not a chosen item. Worn ikons need no item at all. After a transcendence the spark goes to the next ikon, because real time has no pause to choose in. Tests: `CFIkonTest`. Not built: Shadow Sheath's copies, Fetching Bangles, Horn of Plenty, the transcendences of Bands, Pelt, Skybearer's Belt, Shadow Sheath and Victor's Wreath, Diehard on the Scar, armor against ranged only and melee only, and every Exemplar feat that imbues an ikon. Root epithets train their skill at level 1, not 3. Only Brave and Radiant do anything more. Spirit damage is dealt as a second hit, so it shows as its own number.
- [x] Runes. `CFRuneSystem`, `Combat/runes.yml`, `Actions/runes.yml`, `Crafting/rune_recipes.yml`, and six builder slots in `Choices/runesmith.yml`. A `craftingRecipe` with `attachRune: true` puts its result on a weapon or armor lying on the bench instead of making an item, so the workbench is how item runes and etched Runesmith runes get on things. Recipes follow the normal level rule, so only +1 weapon potency and striking are attachable to weapons at level cap 5, with armor potency at level 5. Trace Rune is one action per rune, 1 action adjacent and 2 from further away, and lasts 12 seconds. Invoke Rune targets a creature and sets off up to two runes on it and what it holds. Tests: `CFRuneTest`. Passives and invocations that need something unbuilt say so in each option: Baruiel, Ledria, Oljinex, Pluuna, Tilus and Zohk have no invocation yet. Not built: etching on a creature, invoking runes on two different creatures at once, the level 5 diacritic runes, shield and armor property runes, the artisan's toolkit requirement, upgrading a rune by paying the difference, and Magical Crafting. Rune items use a placeholder sprite from CP14's magic trap sheet. Materials to craft a rune item itself are not defined, so they come from the shop or as loot.

### Action HUD

Built on 9 November 2026, between Waves 3 and 4. How it works is under "Game HUD" in `ss14.md`. Tests: `CFDeckLogicTest`, `CFHudDeckTest`. Accepted in a real client on 9 November 2026.

- [ ] Section widths are saved for the deck as a whole, not per character.
- [ ] Custom / Passives only holds the numbered hotbar. Nothing in the game is a passive toggle yet.
- [ ] The Common tab's icons are stock placeholders. Grapple, Feint, Aid and Recall Knowledge do not look like what they do.
- [ ] Throw and Stride have no button, because stock's inputs are already the whole action. Reposition and Sneak have none either.
- [ ] The sheet's Preparation window can overwrite a slot but not empty one.
- [x] Prepared casters can put a lower-rank spell in a higher slot, by dragging it there. Add uses its own rank first.
- [ ] No scrolls exist, so nothing has been listed on the Items tab under `CFSpellScroll`.

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
- [x] Multilingual and Nomadic Halfling teach languages through a `Language` entry in `choices`, so the player picks. Only common languages are offered. `CFGrantLanguage` was not needed. Tests: `CFLevelChoicesTest`.
- [ ] Recount the extra languages when Intelligence rises at level 5.
- [ ] Give monsters that talk a `CFLanguages`. Without one, a creature speaks plainly and understands everything.
- [ ] Every monster can now be played, so a player in a rat or a skeleton talks in plain words. Animals and mindless undead need a way to make noise only.
- [ ] Languages from a heritage or a background.

### Rest

Built on 6 November 2026. `CFLongRestSystem`. Tests: `CFLongRestTest`.

- [x] A long rest is refused with a living enemy within 50 feet and in the open, and has a cooldown of 15 real minutes per character. This replaces PF2e's 8 hours and once a day, on purpose, for pace. It only counts creatures whose `cfFaction` opposes the sleeper's. Tests: `CFLongRestTest`.
- [ ] It restores hit points, wounded, spell slots and focus. Conditions, Battle Medicine immunity and other daily limits are left alone.

## Step 2: Finish the rebuild

Left from re-importing the other machine's lost work. Details of what was rebuilt are in `roadmapold.md`.

- [x] The `Chemicals` guide failure. That entry no longer exists, and all 57 guide entries pass.
- [x] Recipes: the 59 from `recipes.py` are in `recipes_ported.yml`. Known limits: stack groups use the plain material, so birch planks do not count. Cooking recipes wait for a station. 73 results are not ported (clothing, backpacks, vials, CP14's key and lock items). 4 need an unported ingredient: Screwdriver, ElfPlushie, AgaricPlushie, SkeletonPlushie.
- [x] Recipes of our own for the Phase 3 weapons, in `recipes_weapons.yml`.
- [x] `Sharp` has no replacement: butchering goes through the kitchen spike. `ToggleableLightVisuals` is now `ToggleableVisuals`. Both are in `ss14rules.md`.
- [x] `CFSiteWallStone` is back to `CFWallStone`. Standing has a test: `StandingCostsOneActionAndWaitsForIt`.
- [x] `codebase.md` and `ss14rules.md` have the new folders and the engine differences from the port.

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
  - Recipes: `recipes.yml` and `recipes_ported.yml` hold what CP14 had and this branch can make. Add the rest as their items land. `CFWorkbench` itself has no recipes yet, because all 37 of its results are missing. The 8 vampire recipes stay out. The cooking table waits for cooking.
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
- [ ] Damage type fields are still strings on pets (`CFPetPrototype.Damage`). Spells and weapons use `ProtoId<CFDamageTypePrototype>` with PascalCase ids. Convert pets when companions are next touched.
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
- [ ] Measure how much slower GOAP is than stock HTN, with the same mobs on the same map. The result decides whether CF mobs can all move to GOAP. Today ordinary mobs stay on stock HTN.

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

- `CFGOAPObstacleTest.PatienceDependsOnThePersonality` fails about half the time, measured on 9 November 2026 on the commit before the HUD (7 of 15) and with the HUD (8 of 16). After 7 seconds the default mob should be working on the locked door, but its `Obstacle` is another entity created later in the test, or null. What that entity is has not been looked at. The decision log is printed when the test fails. It is the same setup as the locked door notes in `ai-testing.md`, which may or may not be the same cause.
- `CFGOAPEncounterTest.ItChoosesToFlankThenRunsThenHides` sometimes shows as `Skipped` in a full CF run and passes on its own. It runs no game ticks, so the cause is not movement. The failure message is lost, so the cause is unknown.
- `CFSkeletonguardTest.OutOfArrowsItDropsTheBowAndUsesItsClaws` fails in every full CF run (it stops 2.4 away, and the test wants under 2) and passes alone, so it depends on what ran before it.
- `RainWatersExposedCrops` is unreliable, not just in a full run. On 6 November 2026 it showed as `Skipped` in two full runs, then failed once and passed once on its own. The failure message was not captured, so the cause is unknown. It only touches weather and plants, not the AI.
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

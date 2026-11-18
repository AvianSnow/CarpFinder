# CarpFinder roadmap

The plan for the next stretch of work. Tick items off as they land, and move finished work to `roadmapold.md`, which holds the history of everything already built. Everything ticked off up to 15 November 2026 was moved there on that day.

Source of rules: https://2e.aonprd.com/ (Remaster). Source of structure and assets: `C:\project\crystall-punk-14` (**read-only**: copy from it, never edit it).

Order: Step 1 first, working down its lists. The loose ends after it are small and can be taken when that system is next touched.

## Step 1: Make character options work

Players can pick hundreds of options that do nothing, or only part of what they say. Each of those has `TBA` in its text. This step is the list of features those notes are waiting on, from a scan of every `TBA` on 15 November 2026, in the order to work through: heritages, ancestry feats, class feats, class features and subclasses, spells.

The pipeline all of this runs on is built: grants, stances, flourishes, spellshapes, reactions, auras, subclass choices, level-up and focus spells. How each works is in `Projectinstructions.md`, and how it landed is in `roadmapold.md`.

### Where it stands

Measured on 18 November 2026, after difficult terrain (1.11), the Fighter, Rogue and Ranger leftovers, Cleric domains, Heal and Harm, the Wizard's schools and the Bard's compositions (4, 5.2). `python .agent/port-scripts/feature_scan.py` prints the first three columns, per class and ancestry. The last column is a count of lines with `TBA` in the `.ftl` or YAML.

| Option                  | Total | Works                                                                     | Lines with `TBA`                                                                                                              |
| ----------------------- | ----- | ------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| Heritages               | 149   | 115 have an effect                                                        | 91 in `heritages.ftl`: 34 heritages do nothing, the rest are missing a part                                                   |
| Ancestry feats          | 255   | 140, and 1 in part                                                        | 178 in `feats-ancestry.ftl`: every one of the 114 feats that do nothing says so, and what it would do                        |
| Class feats             | 585   | 229, and 2 in part (11 of them only give a familiar's body, see 3.8)      | 422 in `feats-class.ftl`                                                                                                      |
| Class features          | 181   | No placeholders                                                           | 51 in the class files                                                                                                         |
| Subclass options        |       | Every class has its options                                               | 311 in `class-choices.ftl`, up from 306 because the five theses are new options, three of them `TBA`. Runes repeat 6 times, ikons and tactics 3 times, implements twice, so about 150 different options |
| General and skill feats | 131   | 13 in `general_feats.py`'s `WORKING`, and the scan agrees                 | Marked `(TBA)` in the builder. See "General and skill feats"                                                                  |
| Spells                  | 63    | All 63 have an action                                                     | None in `spells.ftl`                                                                                                          |
| Focus spells            | 34    | The pipeline works                                                        | 11 in `Spells/Focus/focus.yml`                                                                                                |
| Deities                 | 7     | Skill, favored weapon, font, three domains each                           | None                                                                                                                          |

Best per class: Fighter 23 of 31 feats, Monk 18 of 29, Rogue 16 of 27. Worst: Exemplar 0 of 13, Runesmith 1 of 19, Thaumaturge 1 of 14, Alchemist 2 of 21 (the one beyond the first is only a familiar's body), Gunslinger 3 of 19. Sprite has no working ancestry feat.

### How to work through it

- Build the feature, then fill every option listed under it. For each option: read it on Archives of Nethys, write its note in our own words, add its `grants`, take `TBA` out of the text, and add a test where it does more than a stat change.
- An option that cannot work in play keeps its flavour text and the word `TBA`.
- Run `feature_scan.py` after each feature and update the table above.
- `CFFeatureCoverageTest` stops placeholders coming back.
- One feature often serves several lists. It is written where it is first met, and later lists point back to it.

### Notes that look out of date

These said something was not built that this roadmap said was built. All were checked on 17 November 2026. Tests: `CFDeityWeaponTest`, `CFReactionTest`, `CFTemporaryHpTest`, `CFInnovationTest`.

- [x] "There are no deities yet". Deadly Simplicity works: a deity's weapon grant has `favored: true`, kept on `CFCombatant.FavoredWeapons`, and a favored simple weapon rolls a die one step larger (Solhara's spear, Morvane's staff, Sivri's dagger). It does not stack with Humble Strikes. Emblazon Armament works: `CFEmblazonSystem` marks the weapon or shield in the active hand, for +1 status damage or +1 Hardness for a follower of that deity. The rules take 10 minutes to draw it: here it is 3 actions and then a 10 minute cooldown. Deity's Domain and Splinter Faith now say that domains are missing (see 4, Cleric).
- [x] Counter Thought: a reaction with `counterspellSlot: true` and `trait: Mental`. It spends the slot the spell would have cost, or the next one left above it. Not checked: that the slot is one the Psychic could cast a mental spell from.
- [x] Form Control and Untamed Form, built on 17 November 2026 (`CFBattleFormTest`). Untamed Form is a focus spell the Untamed order gives through the feat: a pest at rank 1 (10 minutes, no Strikes, AC 15 plus level, weakness 5 to physical), Animal Form's six shapes from rank 2, and Animal Form's rank 3 numbers at level 5 (`tiers` on `CFBattleFormSpell`, which Animal Form from a rank 3 slot uses too). The druid's own attack modifier gets +2 status when it is the better one. Every battle form now comes with a Dismiss Form action. Form Control is a spellshape (`formControl`), which up to level 5 always means an hour as a pest. Left out: the pest's senses and skill modifiers, and what 5.10 lists.
- [x] Searing Restoration: `CFActSearingRestoration`, an effect action with `unstable: true` on `CFEffectAction`. 1d10 and one more die every 2 levels (`diceEveryLevels` on `CFHealEffect`), and a DC 10 flat check against bleed (`CFRecoverPersistentEffect`). It does not tell living creatures from constructs and undead, and a construct innovation cannot use it for its Inventor.
- [x] Prototype Companion already worked: `CFPetMath` matched it by id. Feats now say which pet they give with `!type:CFGrantPet` (`kind`), and `CFPetMath.BuildGrants` reads that. A feat in a level 2, 4 or 5 slot only counts from that level. Advanced Construct Companion is still `TBA`.
- [x] Rage lasts 1 minute (`CFRagingComponent.EndsAt`, `CFActivitySystem.RageLasts`) and ends when the Barbarian drops. The player can still end it sooner, which stands in for the encounter ending. The legacy -1 AC is gone, as Player Core 2 dropped it.
- [x] The ten hybrid studies each say what their benefit is, and Sparkling Targe gives Shield Block. The rest is under 4, Magus.
- [x] Options that name a feat which already works and do not give it: Diehard (Hold-Scarred Orc, Nine Lives Catfolk, Scar of the Survivor, the Guardian's level 3 feature), Hefty Hauler (Laborer Android). Done on 15 November 2026 with `CFGrantFeat` (1.1). Nine Lives' recovery checks and Tough to Kill's once-a-day hold at dying 2 came with it.
- [x] "Familiars are not built yet". What exists: eight `cfPet` familiars picked on the Pet tab, and a body that spawns with the character and follows it, with the master's AC and saves and 5 Hit Points. The eleven feats that give one have `CFGrantPet` and say so. What does not exist is under 3.8.

### 1. Heritages

34 of 149 do nothing, and 57 more are missing a part. What is left needs swimming (1.3), the `Light` layer (1.9), the lineage feats (1.15), skill actions (1.16) or a changing sprite (1.18). Everything else landed on 15 and 16 November 2026: `CFHeritageTest`, `CFGrantFeatTest`, `CFHeritageEffectsTest`.

- [x] **1.1 A grant that gives a feat** (`CFGrantFeat`), and a feat of the player's choice (`kind: Feat` in `choices`, with `featSlot` and `featLevel`). Built on 15 November 2026. Tests: `CFGrantFeatTest`. Still needed by about 20 ancestry feats (2.2) and the Bard's muses, which can use it now.
    - [x] Gives a feat that works today: Hold-Scarred Orc and Nine Lives Catfolk (Diehard), Laborer Android (Hefty Hauler).
    - [x] Gives a feat that is itself `TBA`: Battle-Ready Orc (Intimidating Glare), Runtboss Hobgoblin (Group Coercion), Shortshanks Hobgoblin (Ride), Deep Orc and Cliffscale Lizardfolk (Combat Climber), Woodstalker Lizardfolk (Terrain Stalker), Tunnel Rat (Quick Squeeze), Artisan Android (Specialty Crafting), Scavenger Strix (Forager). Each works as soon as its feat does. Empty Sky Kitsune waits: Kitsune Spell Familiarity is not imported.
    - [x] A slot for a feat of the player's choice: Versatile Human and General Training (a level 1 general feat), Natural Ambition (a level 1 class feat).
    - [ ] Skilled Human: the skill becomes expert at level 5. Needs a grant that waits for a level.
    - Ancient Elf needs multiclass dedications, which do not exist. Leave it `TBA`.
- [x] **1.2 Heat and cold one step less severe**: `CFGrantTemperatureTolerance` (`heat`, `cold` in steps, negative is worse) on `CFTemperatureTolerance`, read by `CFEnvironmentRules.Eased`. All 13 heritages have it. Sandstrider Lizardfolk and Desert Rat also feel cold one step worse.
- [ ] **1.3 Swimming and breathing water.** Decided: port swimming from another fork. Seaweed Leshy, Tunnelflood Kobold, Wetlander Lizardfolk, Wavediver Tengu, Riverside Tripkee, Shoreline Strix, Undine, River Azarketi, Mistbreath Azarketi, Tactile Azarketi, Lotus Leshy, Rainfall Orc.
- [x] **1.4 Climb Speeds and better climbing.** `CFGrantClimb` (`bonus`, `successIsCritical`, `climbSpeed`, which skips the check) on `CFCombatOptions`, read by `CFZLevelSystem.Climb`. Woodland Elf, Cavernstalker Kobold, Cliffscale Lizardfolk and Stickytoe Tripkee count a success as critical, which stands in for climbing faster. Mutated Fleshwarp +1, Rainfall Orc +2. Shortshanks Hobgoblin (not off-guard while climbing) stays `TBA`: climbing is one move here.
- [x] **1.5 Falling**: `halfDistance` on `CFGrantSafeFall`, read by `CFZLevelSystem.FallFeet`. Unbreakable Goblin. The remaster Sylph has no fall rule, only low-light vision, which it now has. Later Cat Fall, Leshy Glide, Dancing Leaf, Plummeting Roll.
- [x] **1.6 Harder to move**: `CFBonusStat.ManeuverDc` and `CFBonusStat.Maneuvers`, read by `CFConditionSystem.RollSkill` for every Athletics check with the attack trait. Rock Dwarf and Root Leshy +2 DC, Stickytoe Tripkee +2 DC, Mutated Fleshwarp +1 DC, Great Kholo +1 to the checks. The remaster Sylph has no such rule. Forced movement cut in half is still `TBA`.
- [x] **1.7 A bonus called up before a save**: `CFGrantReaction` on `SaveWouldFail` with `saveBonus` and `trait: Magical`. Ancient-Blooded Dwarf +1. Elfbane Hobgoblin has two, +2 against Arcane first and +1 against any magic.
- [x] **1.8 Finding creatures**: `CFGrantSeek` on `CFSeekSense` (`bonus`, `rangeFeet`, `hiddenFlatDc`, `concealedFlatDc`). `CFStealthSystem.TrySeek` adds the bonus inside the range, and `CFConditionSystem.PassFlatCheck` uses the easier DCs. Whisper Elf (+2, DC 3 and 9), Sensate Gnome, Sharp-Eared Catfolk, Longsnout Rat, Nyktera, Liminal Fetchling (60 feet), Warrenbred Hobgoblin (the DCs). Hearing is not modelled, so the range stands in for it. Still `TBA`: Point Out, Hunting Catfolk's Track, Tactile Azarketi (water), Deep Fetchling (darkness).
    - [x] **Scent trails for players, from Starlight** (built 16 November 2026, `CFScentTest`). What landed: `CFScent` on every character and monster leaves `CFScentMarker` entities for five minutes, merged while standing still; `CFSmeller`, given by the Scent sense or in YAML (the wolf), has a free Sniff action that shows them on visibility layer 64, each scent in its own colour, fading with age. Left out: tracking one scent, sniffing objects for traces, sneezing, cleaning. Not done: Hunting Catfolk's Track still says `TBA`, and `CFGrantSeek` keeps the range bonus. The plan was: `C:\project\starlight-ss14` has a scent system under `Content.*/_Starlight/Scent/` (about 1,800 lines, credit Starlight in each ported file): a creature with `ScentComponent` leaves a chain of short-lived `ScentMarker` entities, a `SmellerComponent` toggles a scent-vision overlay that draws them, one scent can be tracked for a while, and objects keep `ScentTrace`s that a sniff reveals. GOAP mobs already follow scent through the influence maps (`CFGOAPScentEmitter`), so this gives players with the Scent sense the same thing to see. Port it as `CF*` under `_CF/Scent/`: `CFGrantSense` with `Scent` gives the smeller, every creature emits, and the range is `ImpreciseFeet`. Then Hunting Catfolk's Track, Sensate Gnome and Longsnout Rat read the trail instead of `CFGrantSeek`'s range, and 6d's Survival and Cover Tracks have something to work on. Leave out the sneeze, the sniff window and smoke lockout unless they are wanted.
- [ ] **1.9 Concealment from smoke, fog, rain and darkness**, and heritages that see through it. Smokeworker Hobgoblin, Stormtossed Tengu, Murkeyed Azarketi. Waits for the `Light` layer (Step 5, 6c) for darkness.
- [x] **1.10 Hiding better**: `CFActShiftColors` (`Actions/heritages.yml`), +2 circumstance to Stealth for 10 minutes, for Chameleon Gnome and Unseen Lizardfolk. Wisp Fetchling's Shadow Blur is a daily action (1.13). Take Cover without an obstacle in forest (Woodland Elf, Woodstalker Lizardfolk) waits for terrain.
- [x] **1.11 Difficult terrain.** Built on 18 November 2026 (`CFTerrainTest`). `CFTerrainSystem` (`Content.Shared/_CF/Terrain/`) slows a creature whose centre is on a tile of difficult terrain to half its Speed, or a third on greater. A tile counts when the tile has `cfDifficultTerrain` (a marked field on `ContentTileDefinition`: deep snow, and deeper snow as greater), when something on it has `CFDifficultTerrain` with a `cause` (the high bush and the snowdrift, which had stock's contact slowdown, and the leaf wall as greater), or when the creature is inside an enemy's Hampering Stance. Ignoring a cause makes greater difficult terrain of it only difficult, as the rules say. Jumping and being thrown cross it freely. A Step cannot be taken in difficult terrain: walking out of reach there provokes, unless the creature has Feather Step.
    - Wildwood Halfling, Jungle Catfolk and Forest Passage (plants), Tunnel Rat (tight spaces), Jungle Strider (plants anywhere, where the rules say forests and jungles), Rock Runner (stone), all with `CFGrantIgnoreTerrain`.
    - Left out: nothing on the maps is tight or stony yet, so Tunnel Rat and Rock Runner have nothing to ignore. Tall grass tiles stay easy ground, because a map covered in them would be slow everywhere. Webs and the ice block keep stock's slowdown. GOAP paths do not avoid difficult terrain. The Balance halves of Jungle Strider and Rock Runner are `TBA`.
- [x] **1.12 Natural attacks with traits**: `deadly`, `versatile` and `traitsOnly` on `CFGrantUnarmedAttack`, carried on `CFCombatOptions.NaturalDeadly` and `NaturalVersatile` into the Strike's traits. Dogtooth Tengu (deadly d8 on the beak), Taloned Tengu (versatile piercing), Spined Azarketi (1d6 piercing finesse). A character still has one natural attack, so a smaller one is not added beside a bigger one. The sprites' spark (Draxie, Luminous Sprite, Melixie, Nyktera, Pixie) is a ranged natural attack and stays `TBA`. Also Crunch and Vicious Incisors (2.8).
- [x] **1.13 Abilities used once a day.** `CFDailyUses` on an action entity (`uses`), counted by `CFDailyUseSystem` and given back by a long rest; the HUD greys a spent one. Jinx (two actions, Will save, clumsy 1 or 2), Tail Toxin (`CFActivityKind.TailToxin`: the next piercing or slashing hit within 6 seconds adds persistent poison equal to the level), Shadow Blur (concealed 6 seconds), Grow Fruit (`CFLeshyFruit`, 1d8). Poisonhide Tripkee's Toxic Skin is a `Damaged` reaction with `meleeOnly` and 1d4 poison on the attacker; its growth with level and the save are `TBA`. Innate spells with daily uses (5.8) are not done.
- [x] **1.14 A choice of energy type**: `kind: DamageType` with `damageTypes` in `choices`, and `CFGrantChosenResistance` (`amount`, `halfLevel`) reads the pick through `CFCharacterFeatureSystem.Pick`. Elementheart Kobold, Suli. Later Dragon instinct, Energy Heart, the Elemental bloodline can use the same pick.
- [ ] **1.15 Lineage feats.** Changeling, Nephilim, Aiuvarin, Dromaar, Dhampir, Dragonblood, Duskwalker, Oread and Beastkin give low-light vision and nothing else, because their feats are not imported. Import them. Aiuvarin and Dromaar also need the rule that opens elf and orc feats to another ancestry. Dhampir's void healing can reuse the Undead eidolon's.
- [ ] **1.16 Narrow skill bonuses, and the skill actions they belong to.** Make an Impression, Coerce, Impersonate, Subsist, Track, Identify Magic and Squeeze are not actions. Decide which become actions and which stay `TBA`. Ant Kholo, Sweetbreath Kholo, Witch Kholo, Songbird Strix, Impersonator Android, Shadow Rat, Liminal Catfolk, Seer Elf, Runtboss Hobgoblin, Warmarch Hobgoblin, Scavenger Strix, Grig (jumps).
- [ ] **1.17 Afflictions**: Strong-Blooded Dwarf is done (`StrongBlooded` on `CFCombatOptions`: a success lowers a poison's stage by one more). Still to do: immunity to one disease (Sewer Rat), Created Fleshwarp, Irongut Goblin. `CFAfflictionSystem` has the stages.
- [ ] **1.18 Shedding light and changing shape.** Light: Bright Fetchling, Luminous Sprite, Ancient Scale Azarketi. An alternate form that changes the sprite: Celestial Envoy, Empty Sky and Earthly Wilds Kitsune. Fungus Leshy swaps the plant trait for fungus.
- [ ] **1.19 Small ones that need no new system.**
    - [x] Dog Kholo and Desert Rat: Speed 30 with both hands free (`CFGrantWhile` with `while: HandsFree`).
    - [ ] Hillock Halfling: a long rest already restores every Hit Point, so only the Treat Wounds snack is left.
    - [x] Polyglot Android and Nomadic Halfling: two extra languages through `choices`. One more per Multilingual is still `TBA`.
    - [x] Technological Fleshwarp: electricity resistance.
    - [x] Charhide Goblin: the flat check to end persistent fire is DC 10 (`EasierBurning`, `CFCombatOptions.PersistentFireDc`).
    - [ ] Frilled Lizardfolk and Dark Fields Kitsune: Demoralize has no language penalty here, so that part is moot. Frilled's bigger fright after a Stride and Dark Fields' reaction are `TBA`.
    - [x] Death Warden Dwarf: `CFEffectSystem.SourceTraits` now adds the acting creature's traits to an action's, so a save upgrade against Undead counts against an undead caster's spell.
    - [x] Dragonscaled Kobold: +1 circumstance to saves against Sleep and against Dragon (the breath comes from the dragon, so its traits count). Paralysis has no trait.
    - [x] Fungus Leshy swaps Plant for Fungus with `CFGrantTrait` (`add`, `remove`). Sylph has low-light vision, as the remaster gives it.
    - Jinxed Tengu: curse and misfortune traits. Doomed does not exist (3.13).
    - Fey-Touched Gnome swaps its cantrip at a long rest (3.15). Still open for it and Wellspring Gnome: the tradition of later gnome spells.
    - Gourd Leshy (storage), Snaptongue Tripkee (touch spells at reach), Draxie (telepathy), Melixie (speaking to insects), Shapewrought Fleshwarp, Pixie's size.

### 2. Ancestry feats

114 of 255 do nothing, and every one says `TBA` with a note of what it would do.

- [x] **2.1 Write the missing notes.** Done on 16 November 2026: all 144 ancestry feats with no effect have a note in our words and `TBA`, from the Archives of Nethys text (its search index at `elasticsearch.aonprd.com/aon/_search?q=category:feat AND trait:dwarf` gives a whole ancestry's feats in one page). `CFAncestryFeatTest.EveryAncestryFeatWorksOrSaysTba` keeps it so. Sixteen more got an effect on the way: Climbing Claws and Prodigious Climber (climb Speed), Sure Feet and Athletic Might (Climb success is critical), Fire Savvy and Scar-Thick Skin (DC 10 flat checks), Defy Death (recovery 1 easier), Cringe (a `CriticallyHit` reaction), Unwavering Mien and Dragon's Presence (save upgrades), Ancestral Longevity and Adapted Cantrip (a choice), Benefactor's Resistance (a chosen energy), Kholo Weapon Familiarity, Strix Defender, Friend of the Sea.
- [x] **2.2 Feats that give another feat** (1.1). Done on 16 November 2026 with `CFGrantFeat`: Alchemical Scholar, Anchoring Roots, Uncanny Agility, Animal Accomplice, Clever Improviser, Cultural Adaptability, Runtsage, Gnome Obsession, Pack Stalker, Rough Rider, Seven Changes Performance, Shrouded Mien, Snare Setter, Stonemason's Eye, Story Crooner, Well-Met Traveler, Gecko's Grip (also a climb Speed). Each works as soon as the general feat it gives does. Not done: Friend of the Sea and Harmlessly Cute (Pet and Shameless Request are not imported), Tangle of Limbs (needs the dokkaebi heritage). General Training and Natural Ambition are done (1.1).
- [x] **2.3 Critical specialization by weapon group.** Built on 17 November 2026 (`CFCriticalSpecializationTest`). `CFCriticalSpecializationSystem` has one effect per group and runs after a critical hit's damage. A character has it through `!type:CFGrantCriticalSpecialization`, which says which weapons and when, or through `criticalSpecializationLevel: 5` on a weapon familiarity. It finishes that part of all 12 weapon familiarity feats, the level 5 weapon features of Fighter, Ranger, Rogue, Barbarian, Commander, Gunslinger, Inventor, Monk and Swashbuckler, the Ruffian racket and Student of the Staff.
    - Our changes from the rules: "until your next turn" is 6 seconds. Stunned 1 (firearm, sling) takes one action from the target. A polearm pushes away, where the attacker should choose the direction. An arrow pins for 6 seconds beside a wall, with no check to pull it free. An axe rolls its dice again for the second creature.
    - Not built: the bomb's larger splash, because there are no bombs. Monsters have none.
    - [ ] Access to uncommon weapons is the other half of those feats: decide whether rarity limits what a character can buy.
- [ ] **2.4 Reactions.** Every one has its note (2.1). Done before: Cringe (`CriticallyHit`), Squawk (2.5), Defy Death (not a reaction after all).
    - [x] Done on 17 November 2026 (`CFReactionTest`): Ally's Shelter (`allyModifierFeet` on a `SaveWouldFail` reaction), Hunter's Defense (`acFromSkill: Nature` and `attackerTraits`), Noble Bloom (the `AllyWouldDrop` trigger), Shed Tail (the `Grabbed` trigger: the grab does not take hold), Strix Vengeance (`allyTrait` and `markDamageDie` on a `CriticallyHit` reaction), Vomit Stomach (the `Sickened` trigger with `sickenedLess`). A reaction can now set `cooldownSeconds`, for once an hour or once per 10 minutes, and `leavesOffGuard`.
    - Left out of those: Shed Tail's Stride and the lost tail attack, Strix Vengeance's extra dice with a striking weapon, Vomit Stomach's new save against a poison that was eaten. Ally's Shelter is used after the roll, as every reaction here is.
    - [ ] Each of the rest needs something that is not built: Bounce Boost (an ally's High Jump or Long Jump, and those are not actions), Eat Fortune (fortune and misfortune effects as things to undo, 3.5), Goblin Scuttle and Step Lively (a Step as a response, 3.4), Rekindled Light (the Star Orb), Shadow Blending (darkness).
- [x] **2.5 Rerolls and fortune**: the `SkillCheckWouldFail` trigger, answered by `CFReactionSystem.AnswerSkillCheck` from every skill check against a creature or a DC, Demoralize and Feint. A reaction can set `skills`, `needsTrained`, `resultOfProficiency` (Elven Persistence: 12 plus proficiency) or `criticalFailureIsFailure` (Squawk), and a `SaveWouldFail` reaction with `andSkillChecks` covers both (Halfling Luck). `CFGrantWidenReaction` changes a reaction by name, so Lucky Break widens Cat's Luck without a second daily use. Done on 16 November 2026, `CFSkillLuckTest`. Ask the Bones is a Recall Knowledge bonus, not a reroll, and stays `TBA`. The Barbarian's Spiritual Guides can use the trigger (4).
- [ ] **2.6 Actions.** Most fit `CFEffectAction` or `CFStrikeAction` now their notes are written. `Actions/ancestry.yml`, tests `CFAncestryActionTest`.
    - [x] Done on 16 November 2026: Kobold Breath (six breaths, one per element the player picks, through `CFGrantChosenAction`; the 15-foot cone only), Goblin Song and Loud Singer (`skill: Performance` on `CFCheck`, so the user rolls against the target's Will DC), Catfolk Dance (Acrobatics against Reflex DC), Terrifying Croak and Grovel (`CFActivityKind.Demoralize` and `Feint` at the action's range), Kneecap (a Strike that slows; it still deals damage), Rabid Sprint (`needsBothHandsFree` on `CFEffectAction`, +10 feet for 6 seconds), Envenom Fangs (venom on the next unarmed hit, as Tail Toxin).
    - [x] Done on 17 November 2026 (`CFAncestryFeatActionTest`): Absorb Strength (`needsDeadEnemy` on `CFEffectAction`, `fromTargetLevel` on `CFTemporaryHpEffect`), Frog Geyser (a breath with no recharge, pushing by degree), Leech-Clip (`groups` on `CFStrikeAction`), Left-Hand Blood and Spark Fist (`CFActivityKind`), Energized Font (`CFRegainFocusEffect`), Kobold Momentum (`CFGrantBreathMomentum`, the dragon's Draconic Momentum). Left out: Absorb Strength's one minute since death, Frog Geyser's mouthful of water and spell DC, Kobold Momentum's reroll of 1s.
    - [x] The flail and war flail (`CFFlail`, `CFWarFlail`), added on 17 November 2026 so Leech-Clip and the Kholo's weapon familiarity have something to work on. Both are drawn with Crystall Edge's mace sprite until there is flail art, and neither has a formula or recipe yet. The khopesh and mambele are still missing.
    - [ ] Still to do: Shame the Sin (demons), Ratfolk Roll and Boulder Roll (movement), Scamper, One-Toed Hop, Magpie Snatch (3.3), Extinguish Light, Grasping Reach, Long-Nosed Form, Project Persona, Swamp Stealth, Razzle-Dazzle, Quick Stow.
- [ ] **2.7 Flying and gliding.** Decided on 15 November 2026: flying is over open sky, space tiles, chasms and holes. Open sky must first be something to fall into.
    - [ ] **Falling into the void.** Since the CrystallEdge port (17 November 2026) a creature over an empty tile already falls a level through `CFZPhysics`, and at the bottom of the stack `CFZLevelFallOutOfBoundsEvent` is raised on the map: `CFZLevelChasmComponent` on a map answers it with the stock chasm fall and deletion. The grace and Grab an Edge below should hang off that event instead of `CFChasmEdge`. A tile with no floor, or a space tile, acts as a chasm does today (`CFChasmEdgeSystem`). A creature whose centre is over it slips: "Falling...", weightless, and free to steer back. The grace is 2 seconds, not the chasm's 4. Then it spends its reaction on Grab an Edge: a Reflex save against DC 10, landing on the nearest solid tile within 10 feet. A failure, no reaction, being unconscious or no ledge in reach falls into the void and dies. A chasm drops to the level below. The void has nothing below, so the body is lost: it drops nothing to loot, and the job slot comes back through `CFPlayerBodySystem`. Items still drop and are gone at once. `CFChasmEdge` can hold the grace and DC, on whatever marks open sky. Check how stock already treats an entity off the grid. Tests: a creature over open sky grabs the edge on a success and dies on a failure.
    - [ ] **A `Flying` trait** (`cfTrait`), and a fly Speed on `CFCombatant`. A creature with it never slips, over the void, a chasm or a hole, and nothing below it triggers. Monsters set it in YAML. A character gets it from a grant (`CFGrantMovement` was dropped on 6 November; bring it back for this).
    - [ ] **Flying over only what is empty.** A tile with a wall, a window or a hard structure still blocks a flyer, as it blocks a walker. Floor is fine.
    - [ ] **GOAP.** A flyer's steering and paths may cross open sky. Walkers treat it as lava and chasms are treated now: dangerous and not hard. A flyer that loses the trait mid-air (killed, a spell ends) slips like anyone else.
    - [ ] **Then the options.** Fledgling Flight and Soaring Flight (fly Speed for a while, an action), Leshy Glide and Tripkee Glide (gliding: falls slowly and drifts, so a jump off an island is not death). Air elementals, dragons and other monsters with a printed fly Speed. The Juvenile Horned Dragon's flying, listed under Step 3.
    - Left from chasm edges: a successful grab moves straight onto the ledge, not a leap with an arc. NPCs do not steer themselves out during the slip; they rely on the save. Grab an Edge does not check for bound hands.
- [ ] **2.8 Parts missing from feats that mostly work.**
    - Traits on a natural attack (1.12): Crunch (grapple), Vicious Incisors (backstabber).
    - Dazzled and deafened (3.13): Blast Resistance.
    - Underwater (1.3): Aquatic Elf Warrior, Mariner's Fire, Marsh Runner.
    - A bonus to a DC against one skill: Stone Face.
    - Make an Impression (1.16): Draconic Sycophant, Fey Fellowship.
    - Other Speed penalties: Unburdened Iron. A free check near an illusion: Illusion Sense. Going without food: Parthenogenic Hatchling. Dragon's Presence. Explosive Savant. Climb Speed (1.4): Gecko's Grip.
    - A choice in `choices`: Ancestral Longevity (a skill), Touch of the Sea (the cantrip), Adapted Cantrip and Adaptive Adept.
- [ ] **2.9 Snares.** Snare Setter and Snare Genius can use `CFTrapLayerSystem`, which the kobold trapper already builds traps with.
- [ ] **2.10 Feats that need a heritage the game does not have.** Briny Beverage (kijimuna gnome), Crocodile's Twin and Spirit Coffin (bakuwa or makari lizardfolk), Glorious Gamtu, Phantom Visage, Phantom Resemblance and Tangle of Limbs (dokkaebi goblin), Soaring Flight (skyborn tengu), Tripkee Glide (windweb tripkee), Leshy Glide (leaf leshy). Import those heritages or remove the feats.
- [ ] **2.11 Pets.** Animal Accomplice and Friend of the Sea give a Pet, which is a familiar (3.8).
- [ ] Human, Kitsune and Sprite have no working ancestry feat at all. Android, Azarketi and Fleshwarp have no feats, because theirs are pre-remaster.

### 3. Class feats: features several classes share

Worked through in one run on 17 November 2026. Done or partly done: 3.1 to 3.7, 3.11 to 3.13, 3.16, 3.17, 3.21. 3.10 summoning and the familiars' bodies (3.8) followed the same day. 3.15 daily choices came after them. Not started, because each is a system of its own that has to be seen in play: 3.9 mounts, 3.18 scrolls and wands, 3.19 stealing, 3.20 disguises, 3.22 water (waits for swimming, 1.3), 3.23 (each needs a decision).

354 of 585 do nothing. These are the shapes that come up in more than one class. Feats only one class has are under that class in 4.

- [ ] **3.1 A Strike that adds a maneuver.** `CFStrikeAction` can now ask for `press` (an attack earlier this turn), `twoHandedWeapon` and `needsFreeHand`, and has `onFailure` beside `onHit` and `onCritical`. Effects: `CFForcedMovementEffect` for a Shove, `CFGrabEffect` for a grab that lets go, `CFTripEffect` for a Trip.
    - [x] Done on 17 November 2026 (`CFStrikeActionTest`): Brutish Shove, Combat Grab, Slam Down, Unsteadying Strike, Aggressive Block (Fighter and Guardian, the `AggressiveBlock` option: it always shoves, where the enemy should choose between that and off-guard). Size is not checked.
    - [ ] Still to do: Sleek Reposition (needs a way to choose where the target goes), Powerful Shove (sizes, and knowing what stopped a shove), Flurry of Maneuvers, Topple the Titans (Trip is not an action), Dastardly Dash, Flying Tackle and Leading Dance (movement, 3.3), Mug (Steal, 3.19), Lunge with a maneuver, Wolf Stance's trip.
- [ ] **3.2 One action that Strikes several targets, or twice.** `CFStrikeAction` has `second` (`BesideTarget`, `BehindTarget`, `OtherInReach`, `OtherInRange`), `needsSecond` and `secondIsFree`. The second Strike is made at the same multiple attack penalty, with its own attack and damage roll, where the rules roll once for both.
    - [x] Done on 17 November 2026 (`CFStrikeActionTest`): Swipe (Barbarian, Fighter), Quick Reversal (any two enemies in reach, not only flankers), Double Shot, Hunted Shot (the second arrow is nocked from the quiver), Impaling Finisher.
    - [ ] Still to do: Paired Shots (no one-handed ranged weapon exists), Rebounding Toss (a thrown weapon is a real object in flight), Split Shot (a spell attack at two targets), Devastating Spellstrike. Combining the damage of two Strikes before resistance: Twin Takedown, Hunted Shot, Double Slice (Step 4), and two ikon transcendences.
- [ ] **3.3 Movement inside an action.** The user really moves: `closesFeet` on `CFStrikeAction` rushes it toward the target before the Strike and `stepsBackFeet` hops it away (both throw the user, as Leap does). `!type:CFFreeStepEffect` opens a few seconds in which leaving reach costs nothing and sets off nothing, and `!type:CFLeapEffect` is a jump.
    - [x] Done on 17 November 2026: Defensive Advance (Champion, Guardian), Barreling Charge (Barbarian, Fighter: no Athletics check, because creatures do not block each other), Flying Kick, Parting Shot, Retreating Finisher, Ghostly Stride, Explosive Leap, Guarded Movement (`GuardedMovement`), Dancing Leaf (`DancingLeaf`, the Leap only), Hit the Dirt's prone landing.
    - [ ] Still to do: Bashing Charge and Underhanded Assault (they need a path to follow), Black Powder Boost (no High Jump or Long Jump), Tiger Stance's Step. Sudden Charge still Strikes from 3 tiles away without moving: move it to `closesFeet`.
- [ ] **3.4 A reaction that moves the creature.** A reaction with `follows: true` rushes its owner to whoever set it off, up to its Speed, without setting off reactions itself. `whileRaging` limits one to a Rage.
    - [x] Done on 17 November 2026: No Escape, Reactive Pursuit (both on `EnemyLeftReach`), Rapid Response (the new `AllyDropped` trigger). Tests: `CFStrikeActionTest.RapidResponseRunsToAFallenAlly`.
    - [ ] Still to do: Defensive Swap (two creatures trading places and the attack changing target), Exploit Blunder (a plain miss as a trigger, and only the Stratagem's target), Clever Gambit (needs to know a creature was identified), Nimble Reprisal, Unimpeded Step, Divert Streamflow, Distant Waterbird's Poise, Intercept Attack's Step, and from 2.4 Goblin Scuttle and Step Lively.
- [ ] **3.5 New reaction triggers.**
    - [x] Done: Spiritual Guides (it already worked, on `SkillCheckWouldFail`), Emergency Targe (it gives Reactive Shield, so Strikes only), Emotional Push (the `EmotionalPush` option, read where a save against a spell is rolled: it does not use the reaction), Rapid Response (3.4).
    - [ ] Forced movement: Blade Brake. A misfortune effect: Turn Away Misfortune, Eat Fortune. A creature dying nearby: Inevitable Return, Portents of the Haruspex. The damage's type: Void Siphon. A critical hit on a reaction Strike stopping the enemy: Disrupt Prey, Stand Still.
    - [ ] Each needs its note first: Body Shield, Fake Out, Not So Fast, Rune Ward, Reactive Dismissal, No! No! I Created You!, Divine Aegis.
- [ ] **3.6 Drawing as part of an action.**
    - [x] Quick Draw (Gunslinger, Ranger, Rogue), done on 17 November 2026: `drawsWeapon` on `CFStrikeAction` takes a weapon off the belt, back or neck into a free hand and Strikes with it, melee or ranged.
    - [ ] Still to do: Brandishing Draw, Lightning Swap (Exemplar, Fighter), Quick Bomber, Call Implement, Call Wizardly Tools, Spirit Sheath, Instant Backup.
- [ ] **3.7 Parries and guards.** `CFEffectAction` can ask for `needsTwoHanded` (`Melee` or `Ranged`) and `needsWeaponAndFreeHand`.
    - [x] Done on 17 November 2026: Dueling Parry, Raise Haft, Defensive Armaments (each an action that gives AC for 6 seconds), Elegant Buckler (`ElegantBuckler`: a shield that gives +1 gives +2), Shield Warfare (`ShieldWarfare`), Shield Your Eyes (`CFGrantWhile` with the new `ShieldRaised`).
    - [ ] Still to do: Raise a Tome, Covering Stance, Shielded Attrition, Haft-Striker Stance (Fighter, Ranger, Rogue), Phalanx Formation, Area Armor (an aura). The +2 with a weapon that has the parry trait is missing from Twin Parry, Extravagant Parry and Raise Haft. Elegant Buckler's panache, and Shield Your Eyes turning a critical failure into a failure.
- [ ] **3.8 Familiars.** Bodies built on 17 November 2026 (`CFFamiliarTest`).
    - [x] Each of the eight `cfPet` familiars has its own body in `Companions/familiars.yml`, on a stock animal: bat, cat, fox and snake as themselves, the raven and owl as a parrot, the rat as a mouse and the toad as a frog, until there is art for them. A familiar is `peaceful` on `CFCompanion`: the sight sensor gives it no enemy and an order to attack is ignored, so it follows, comes when called and fetches, and never fights.
    - [x] A pet from a feat another option hands out counts (`CFPetMath.BuildGrants` reads `CFSkills.ExtraFeats`), so the Leaf order's Leshy Familiar and the Animal order's Animal Companion work: both orders now give their feat. A pet gained from a level taken in play appears at once (`CFCharacterSpawnSystem.EnsureCompanion`, from `CFExperienceSystem.LevelUp`).
    - [ ] Familiar and master abilities picked each day (3.15): two, and more from Enhanced Familiar (7 classes) and its Glyph and Undead versions.
    - [ ] What each kind adds: Alchemical, Glyph, Undead, Spirit and Leshy Familiar, Familiar's Language, Scatter Swarm, Leshy Familiar Secrets, the Witch's patron familiar and its gift, the Shaman practice, and Pet (2.11). Every familiar feat gives the same choice of eight bodies.
    - [ ] A familiar that drops stays down: it does not come back the next day. The Pet tab has no tab for a construct, so it is always the first one.
- [ ] **3.9 Animal companions through feats, and mounts.** Animal Companion (Ranger says `TBA`; check the Druid's), Mature Animal Companion, Companion's Cry, Commander's Companion, the Animal order. Mounts and the Ride feat: Faithful Steed, Acclimated Mount, Galloping Launch, Steed Form, Rough Rider. "Companions have no inventory" (Step 4) is in the way of barding.
- [ ] **3.10 Summoning.** Built on 17 November 2026 (`CFSummonTest`). `!type:CFSummonEffect` calls a creature for a while: it raises `CFSummonRequestEvent`, and the server's `CFSummonedSystem` spawns it, binds it to the caster as a companion and sends it back when `CFSummoned.Until` passes. A summoned creature is its real monster, listed first as a parent, with `CFSummonedCreature` second (`Entities/Mobs/summons.yml`).
    - [x] Summon Animal (`Spells/1/CFSummonAnimal.yml`, arcane and primal): a giant rat at rank 1, a wolf at rank 2, a boar at rank 3, for a minute. It fights the caster's enemies by itself and goes first for what the caster points at.
    - [x] Call of the Wild and Rites of Convocation: `!type:CFGrantSwapSpell`, kept in `CFSpellbook.SwapSpells`. Summon Animal is cast in place of any prepared spell, at that spell's rank, and spends it (the last one prepared at that rank). The rules take 10 minutes for the swap. Thoughtform Summoning: the summoned creature resists physical damage and is weak to force and mental.
    - Our changes from the rules: the creature stays its minute and acts on its own with no input, as every sustained spell here does, and only Dispel Magic sends it back sooner (`CFCounteractSystem.TryDispel` on the creature, against the spell's rank and DC). A caster can have several out, one per cast. The caster does not choose the animal: the rank does.
    - [ ] Still to do: the other summon spells (Plant or Fungus, Elemental, Construct, Fey, Undead and the rest), a choice of creature at each rank, Elemental Summons, Primal Evolution (a slot tied to one spell). A summoned creature still has its monster's ghost role, so a ghost can take it.
- [ ] **3.11 Recall Knowledge inside another action, and what it tells.** `CFManeuverSystem.TryRecall` takes `free: true`, and `!type:CFRecallEffect` runs one from an effect list.
    - [x] Done on 17 November 2026: Combat Assessment (Commander, Fighter), Monster Hunter (with Hunt Prey), Known Weaknesses (with Devise a Stratagem), the Mastermind racket (an identified creature is off-guard to the Rogue for 6 seconds, a minute on a critical success), the Outwit edge's +2 to Recall Knowledge.
    - [ ] Still to do: the bonuses those feats give on a critical success, Combat Reading, Monster Warden, Observational Analysis, Strategic Assessment, Whispers of Weakness, Predictable, Breached Defenses, Esoteric Warden. Recall Knowledge still tells only level, weakest save and AC, so feats that reveal weaknesses or resistances have nothing to show. Lore skills that do not exist: Warfare, Undead, Esoteric (Diverse Lore), Bardic, Underworld.
- [ ] **3.12 The start of a fight.** Decided and built on 17 November 2026: a fight begins for a creature with the first attack it makes or takes after 30 seconds without either. `CFFightSystem` raises `CFFightStartedEvent` on it, which says who the opponent is, whether this creature is the attacker, and whether the opponent had been quiet too. Only Strikes are reported so far, not spells. Tests: `CFConditionValuesTest.AFightBeginsWithTheFirstAttack`.
    - [x] Quick-Tempered (the Barbarian rages for free), Surprise Attack (a creature that has not been fighting is off-guard to the Rogue who starts the fight).
    - [ ] Still to do, each on that event: After You, Swaggering Initiative, Even the Odds, Favored Prey, Oracular Warning, Rapid Assessment, Scout's Warning (Ranger, Rogue), Recognize Ambush, Desperate Prayer, Moment of Clarity, the Gunslinger's initial deeds. Anything that only changes the initiative roll has nothing to change.
- [ ] **3.13 Missing conditions.** Built on 17 November 2026 (`CFConditionValuesTest`): enfeebled, drained, doomed, dazzled, fatigued, deafened and fascinated are in `CFConditionComponent.Values`, read with `CFConditionSystem.Value` and shown on the HUD. Stunned is not kept: `ApplyCondition` with `Stunned` takes that many actions at once.
    - What each does here: enfeebled lowers melee attack and damage and Strength checks. Drained lowers Fortitude, without the lost Hit Points. Doomed lowers the dying value that kills. Dazzled is a DC 5 flat check to target anything. Fatigued is -1 AC and saves, lasts 1 real minute with no rest needed (the user's choice on 17 November 2026: the rules say until a 10 minute rest), and stops a Rage. Deafened is -2 Perception. Fascinated is -2 Perception and skill checks. Status penalties from these do not stack with clumsy, stupefied or each other on the same roll.
    - [x] Options that use them: Second Wind and Furious Finish (fatigued), Stunning Blows, Tiger Stance's bleed on a critical hit (`criticalBleedDie` on a stance attack), Scalpel's Point.
    - [ ] Still to do: Cruelty (Touch of the Void has no two-action version), the Psychic's stunning cantrips, the Pluuna rune and Blast Resistance and the Mournful epithet (dazzled), the Spirit fascination (drained), Jinxed Tengu (doomed), Fascinating Performance (Perform is not an action), Bloodletting Claws (an eidolon's critical hit), Clotting Elixirs. Deafened does not make a creature immune to auditory effects, and fascinated does not end when the creature is attacked.
- [ ] **3.14 Difficult terrain** (1.11).
    - [x] Done on 18 November 2026 (`CFTerrainTest`): Favored Terrain (a `Terrain` pick in `choices`, read by `CFGrantFavoredTerrain`; each terrain ignores the causes found there, anywhere, because the game has no map of terrains), Hampering Stance (`CFActHamperingStance`, and an aura effect with `needs: Stance` and `difficultTerrain: true`), Dragon Stance (`freeTerrainFeet` on `CFStance`: the first 5 feet of each stretch are free, as long as it takes to cross one tile), Forest Passage, Snowdrift Spell (a spellshape: the first creature an air, water or cold spell affects gets snow on its tile and the eight around it, `CFFillSnowdrift`, for 6 seconds), Light Paws (2 actions: 6 seconds ignoring all difficult terrain, and a free Step in the first 3), Feather Step.
    - [ ] Still to do: Not So Fast (a reaction Strike whose degree slows the target, which `CFReactionSystem` cannot read yet), Unimpeded Step (Liberating Step's Step), the Lyskel rune, the Flesh fascination's fallen thralls, clearing Snowdrift's snow by hand and fire melting it.
- [ ] **3.15 Choices made each day.** Built on 17 November 2026 (`CFDailyChoiceTest`). A `choices` entry with `daily: true` can be changed each day. The character window has a Daily tab, shown only to a character with such a pick: each row is what it is today and what it becomes after the next long rest. The plan is `CFExperienceComponent.DailyPlan`, checked by `CFOptionChoices.IsValidDailyPlan`, and `CFDailyChoiceSystem.Apply` swaps it in from `CFLongRestSystem`: the old skill, cantrip or signature spell is taken back and the new one given.
    - [x] Flexible Studies (a skill), Arcane Evolution (a skill, fixed, and a signature spell each day), Fey-Touched Gnome (the primal cantrip).
    - Left out: Flexible Studies' second skill at expert, Arcane Evolution's other choice of a spell added to the repertoire, and Fey-Touched Gnome's 10 minute swap once a day.
    - [ ] Still to do, each needs its own effect first: Oath of the Defender, Oath of the Slayer, Favored Prey, Favored Terrain (3.14), Gadget Specialist, Munitions Crafter, Talisman Esoterica (3.18), familiar abilities (3.8), the Animist's primary apparition. A daily pick can only take back a skill, a cantrip or a signature spell: a new kind needs a line in `CFDailyChoiceSystem.TakeBack`.
- [x] **3.16 Extra damage on a Strike after casting a spell.** Done on 17 November 2026 with `!type:CFGrantBespell` (`die`, `damageType`, `cantrips`): Bespell Strikes (Oracle, Sorcerer, Wizard), Psi Strikes, Apparition's Enhancement. It is automatic, and the next Strike that hits within 6 seconds gets the die, where the rules say every Strike until the end of the turn. Not done: taking the spell's own damage type, the tradition trait on the Strike, and Psi Strikes lasting while the psyche is unleashed.
- [ ] **3.17 Spellshapes left.**
    - [x] Done on 17 November 2026: Energy Ablation (`energyAblation`: resistance to the spell's own energy, where the caster should choose), Spiritual Expansion Spell (it widens), Grasping Spirits Spell (30 feet more range, without the pull). Cryptic Spell already worked.
    - [ ] Vampiric Spell needs a spellshape that reads the spell's damage. Nonlethal Spell, Snowdrift Spell (3.14), Incriminating Spell, Elemental Defense (a resistance to a trait). Knowledge of Shapes makes Reach or Widen free.
- [ ] **3.18 Scrolls, talismans, staves and wands** as items. Scroll Thaumaturgy, Striker's Scroll, Talisman Esoterica, the Wand implement, CP14's magic staves (Step 4), the Noble Branch ikon's staves.
- [ ] **3.19 Stealing and sabotage**: a Steal action, and damaging what a creature holds. Mug, Plant Evidence, Sabotage, Tamper, the Deft epithet.
- [ ] **3.20 Disguises and performing in a fight.** Scoundrel's Surprise, Wolf in Sheep's Clothing, Quick Disguise. Perform as an action: Fascinating Performance, Focused Fascination, Enjoy the Show, the Battledancer style.
- [ ] **3.21 Trap finding.**
    - [x] Trap Finder (Investigator, Rogue), done on 17 November 2026: +1 circumstance to Perception to notice traps, to AC against them and to saves. `CFHazardSystem.TryNotice` now counts bonuses tied to the Trap trait, which it skipped before. Every creature already gets a check on coming near a trap, so "finding without searching" has nothing to add.
    - [ ] The +2 at master, and traps that need master to disable. Seek the Hidden Glyphs. Haunt Ingenuity needs haunts.
- [ ] **3.22 Water** (1.3). Convergent Tides, Flood Stance, Student of Water, Swan Dive, Diving Armor, Amphibious Form, Raging Athlete, Flamboyant Athlete.
- [ ] **3.23 Needs a game master, or exploration and downtime.** Decide for each: a DM tool through `CFSceneSystem`, the world clock, or flavour text with `TBA`. Glean Lore, That's Odd, the five investigation feats (Detective's Readiness, Eliminate Red Herrings, Solid Lead, Underworld Investigator, Ongoing Investigation) and Pursue a Lead, Sacral Lord (Champion, Oracle), Ritual Researcher, Undead Creator, Lie Detector, Animal Empathy (Druid, Ranger), Plant Empathy, Zoophonic Communication.

### 4. Class features and subclasses

What each class still lacks: class features, subclass options, and the feats only that class has. Shared features are named by their number in 3.

- [ ] **Fighter.** Feats: 3.1, 3.2, 3.3, 3.6, 3.7.
    - [x] Done on 18 November 2026 (`CFFeintTest`): a weapon group's rank is one step lower for advanced weapons, so Fighter Weapon Mastery makes them expert (`CFWeaponSystem.Rank`). Overextending Feint was listed here, but it is a Rogue feat (below).
- [ ] **Rogue.** Poison Weapon (see Alchemist), Head Stomp's kin, 3.19, 3.20.
    - [x] Done on 18 November 2026 (`CFFeintTest`). Feint was the legacy one, with the Scoundrel's results for everyone, and is now the remaster's: a success leaves the target off-guard to the user's next melee attack within 6 seconds, a critical success to all the user's melee attacks for 6 seconds, and only melee attacks count (`CFConditionComponent.FeintedUntil`, used up in `CFConditionSystem.NoteAttackRoll`). The Scoundrel racket (`ScoundrelFeint`): 6 seconds on a success, every creature's melee attacks on a critical success, and a free Step with an agile or finesse weapon. Distracting Feint (-2 circumstance to Perception and Reflex while feinted), Overextending Feint (its own action, `CFActOverextendingFeint`, where the rules let any Feint use it: -2 circumstance to the target's next attack roll against the Rogue, or all of them for 6 seconds on a critical success, which does not stack with prone), Twin Distraction (both Twin Feint Strikes hit: Will against class DC or stupefied 1 for 6 seconds).
    - [x] A creature is now off-guard to an attacker hidden from it, a core rule that was missing, so Sneak Attack works from hiding. Deny Advantage stops it for attackers of the Rogue's level or lower, as it already did for flankers. Not tested on its own: `CFStealthSystem` has no simple way to make a creature hidden in a test.
    - Surprise Attack and Mastermind were already done (3.11, 3.12).
- [ ] **Ranger.** Being hard to track. Warden spells: Initiate Warden, Advanced Warden, Natural Conduit. Hunter's Aim ignoring lesser cover, Crossbow Ace, Running Reload, Cover-style feats, 3.9, 3.11.
    - [x] Done on 18 November 2026 (`CFFeintTest`): Outwit's +2 circumstance to Deception, Intimidation and Stealth against the prey (`CFConditionSystem.OutwitBonus`: Feint, Demoralize, and each Stealth roll against the prey as an observer, not stacking with cover). Hunter's Aim ignores the prey's concealment (`ignoresConcealed` on `CFStrikeAction`). There is no cover system, so lesser cover has nothing to ignore.
- [ ] **Cleric.** Domain Acumen (an Oracle's mystery has no domains). Advanced domain spells wait for level 8. Versatile Font, Warpriest's Armor later ranks. Heal and Harm shaping still to do: Channel Smite and Restorative Strike (a Strike that spends a spell), Divine Castigation (the Cleric's holy or unholy side is not recorded), Sacred Ground, Overwhelming Harm, Invert Harm. Divine Disharmony. Replace the placeholder pantheon when the setting is written.
    - [x] Done on 18 November 2026 (`CFHealHarmTest`): Communal Healing (the caster first, else the nearest hurt ally within 30 feet), Panic the Dead (frightened 1; fleeing is `TBA`), Sap Life (once per cast), Directed Channel (a fourth wheel choice, `requires: DirectedChannel` on the mode, aimed at the creature clicked), Divine Infusion (a spellshape; the extra dice are `Infusion*` on `CFCombatOptions`, added to melee hits). Versatile Font adds nothing here, because a deity that allows both fonts already gives both: picking one font at level 1 is `TBA`.
    - [x] Domains, built on 18 November 2026 (`CFDomainTest`). A `cfDomain` (`Character/domains.yml`) has `grants`; each `cfDeity` lists three. A `Domain` pick in `choices` offers the deity's, `anyDomain: true` offers all of them, and Splinter Faith's four picks replace the deity's. `CFGrantDomainSpell` runs the picked domain's grants. Domain Initiate (and the Cloistered doctrine, which gives it), Expanded Domain Initiate, Deity's Domain and both Splinter Faiths work.
    - The deities' placeholder domains were mapped to remaster ones: Justice is Truth, Secrets is Secrecy, Shadow is Darkness, and Mercy, which has no domain, is Repose. Check this when the setting is written.
    - The 21 domain spells (`Spells/Focus/domains.yml`): Dazzling Flash, Healer's Blessing (`SpellHealing` bonus), Word of Truth, Swear Oath (+1 to attacks and skill checks for 6 seconds, where the rules Ready an action), Creative Splash (`CFRandomOptionEffect`, one colour per cast), Hurtling Stone, Soothing Words, Pushing Gust, Agile Feet, Bit of Luck (`save: true` on `CFFortuneEffect`), Read Fate (`CFReadFateEffect`, a word from a list of 12), Eject Soul (stunned only), Cloak of Shadow (concealed outright, as there is no light layer), Touch of Undeath (`HealingHalved` condition), Touch of Obedience, Athletic Rush. Four are reactions that spend a Focus Point (`focusPoints` on `CFReaction`): Share Burden (`AllySaveWouldFail`), Protector's Sacrifice (`InterceptsDamage` with `redirectPerRank`), Death's Call (`CreatureDied`, `tempHpFromDeath`), Sudden Shift (`MeleeMissed` and `onSelf`).
    - Left out: Whispering Quiet (Secrecy) gives nothing and says `TBA`, because nothing muffles speech by area. Dazzling Flash's larger cone at rank 3, Swear Oath's +2 at rank 5, the 24 hour and 1 hour immunities, Soothing Words' counteract, Eject Soul's soul form, rubbing eyes to end blinded. Reaction spells fire automatically like every reaction, so Protector's Sacrifice spends its Focus Point on the first ally hurt nearby.
- [ ] **Wizard.** The extra curriculum slot (5.12), the other three theses (Spell Blending, Spell Substitution, Staff Nexus), the Universalist's extra Drains. Linked Focus, Spellbook Prodigy, Call Wizardly Tools, Spell Protection Array.
    - [x] Done on 18 November 2026 (`CFWizardSchoolTest`). Each school's initial spell (`Spells/Focus/schools.yml`): Protective Wards (a sustained emanation that grows 5 feet each pulse to 30, `growsFeet` and `maxFeet` on `CFAreaEffect`), Fortify Summoning, Earthworks (1 to 3 actions on the wheel for a 5, 10 or 15-foot burst of difficult terrain for a minute, `sizeFeet` on a mode), Charming Push (`CFOverextendEffect`: -1 on a success, charmed toward the Wizard on a failure), Scramble Body, Hand of the Apprentice (`CFHurledWeaponEffect`, `CFNeedsMeleeWeapon`). Unified Magical Theory also gives a level 1 wizard feat. Drain Bonded Item (`CFActDrainBondedItem`, once a day): for 6 seconds a prepared spell already cast today can be cast once more (`DrainUntil` on the spellbook). The thesis is a choice: Experimental Spellshaping (a level 1 spellshape feat, `featTrait` on a `Feat` choice) and Improved Familiar Attunement (the Familiar feat) work.
    - Left out: Charming Push's -1 to damage, Fortify Summoning's DCs, Hand of the Apprentice's critical specialization and its check that the weapon is one the Wizard is trained in, clearing Earthworks' barriers, the bonded item as an object.
- [ ] **Bard.** Uplifting Overture (an Aid prepared ahead), Directed Audience and In Tune (an aura that is a cone, or centred on an ally), Courageous Advance (an ally's Stride), Zoophonic Composition and Bestial Snarling (the zoophonia muse), the Polymath's phantasmal minion, Multifarious Muse, Versatile Signature, Rallying Anthem's resistance.
    - [x] Done on 18 November 2026 (`CFCompositionTest`). Compositions: `CFComposition` on an action (`Actions/compositions.yml`) gives the Bard's aura a set of effects that run while it is performed. One runs at a time, starting another ends it, and a held instrument keeps the last one going (`CFAuraSystem.Compose`; Courageous Anthem is the composition `Anthem`). Each lasts a minute, where the rules give a round, as the anthem already did. Rallying Anthem, Triple Time, Song of Strength. Hymn of Healing (sustained for 24 seconds, `sustainSeconds` on `CFSpell`). Each muse gives its feat. Versatile Performance (Demoralize with Performance), Loremaster's Etude (a free reroll of a failed knowledge check for a Focus Point), Esoteric Polymath (a daily signature spell). Lingering Composition and Martial Performance add nothing, because a composition already lasts a minute here.
    - [x] A positive Speed bonus never sped anyone up: `CFArmorSystem` ignored any total that was not a penalty. Rabid Sprint, Agile Feet, Athletic Rush, the ikon's +10, Triple Time and battle forms faster than their caster now work.
- [ ] **Druid.** Anthropomorphic Shape, and the other feats that add shapes to Untamed Form. Each order's feat and order spell: Animal (heal animal), Leaf (Leshy Familiar), Storm (Storm Born). Order Explorer and Order Magic. Verdant Weapon. Anathema, Wildsong and the bond are flavour: decide.
- [ ] **Witch.** Hexes and the hex cantrip. Each of the seven patrons: its tradition (as a bloodline sets it, with `tradition` on `cfClassChoice`), hex cantrip and familiar gift. Lessons (Basic Lesson), Cackle, Witch's Armaments, Sympathetic Strike. Familiars (3.8).
- [ ] **Barbarian.** Fatigue, then Second Wind and Furious Finish. Instincts: Animal (choosing the animal, no weapons in hand), Dragon (the breath's damage type, 1.14), Fury (the extra feat), Giant (oversized weapons), Spirit (spirit damage), Superstition (damage against casters). Morph effects: Brutal Crush, Creature Comforts, Rip and Tear. Senses while raging (`CFGrantWhile` with a sense): Acute Scent, Acute Vision, Supernatural Senses. Adrenaline Rush's Bulk, Raging Intimidation's feats, Oversized Throw, Shake It Off, The Harder They Fall.
- [ ] **Champion.** The deity's boon (blessed weapon or shield). The cause is not checked against the deity's holy or unholy side. Causes where the enemy chooses always use its refusal. Cause feats: Brilliant Flash, Iron Repercussions, Ongoing Selfishness, Vicious Vengeance, Weight of Guilt, Nimble Reprisal, Unimpeded Step. Two-action versions: Mercy, Cruelty, Security. Oaths (3.15, and Oath of the Avenger). Faithful Steed (3.9). Sanctified Soul.
- [ ] **Monk.** Qi spells: Qi Spells, Elemental Fist, Harmonize Self. The monk trait on weapons: Monastic Weaponry, Shooting Stars Stance, Waterfowl Stance, Monastic Archer Stance. The missing half of each stance: Crane (jumps), Dragon (backswing), Tiger (bleed, Step), Wolf (trip), Mountain, Cobra (poison resistance), Rushing Goat, Stumbling, Reflective Ripple, Twisting Petal. The other 11 stances. Stunning Blows (3.13). Mystic Strikes needs something that resists non-magical attacks.
- [ ] **Swashbuckler.** Panache from each style's own action: Battledancer (Perform), Fencer (Create a Diversion), Gymnast (Reposition), Rascal (Dirty Trick), Wit (Bon Mot). The +1 to bravado checks. Opportune Riposte's Disarm. Half Speed bonus without panache. Slots for the extra skill feat and increase. Flying Blade, Twirling Throw, Goading Feint, Disarming Flair, Cheaters Always Prosper, One for All, 3.3, 3.12.
- [ ] **Investigator.** Intelligence on the stored Devise a Stratagem attack. Certain Stratagem, Athletic Strategist, Shared Stratagem, Person of Interest, Takedown Expert, Scalpel's Point. Methodologies: Alchemical Sciences (vials), Empiricism, Forensic Medicine, Interrogation. Slots for the extra skill feats at odd levels (as the Rogue's `bonusSkillFeats`). Investigations (3.23).
- [ ] **Alchemist.** The class barely works: 1 of 21 feats. Decide on CP14 alchemy (Step 3) first. Then versatile vials, Quick Alchemy, infused items and daily preparation. The four research fields. Bombs, with a zone that keeps its own check (see "Loose ends", spell visuals): Quick Bomber, Far Lobber, Smoke Bomb, Healing Bomb, Alchemical Shot. Mutagens: Regurgitate Mutagen, Revivifying Mutagen. Poisons on `CFAfflictionSystem`, applied to a weapon: Poison Weapon, Blowgun Poisoner, Pernicious Poison, Tenacious Toxins, Personalized Poisoning. Elixirs: Clotting Elixirs, Invigorating Elixir, Soothing Vials, Fermenting Liquors, Numbing Spice Exhalation, Cauldron. Alchemical Assessment, Efficient Alchemy, Enduring Alchemy, Improvise Admixture, Alchemical Discoveries, Alchemical Familiar, Reverse Engineer.
- [ ] **Sorcerer.** Blood magic for each of the 13 bloodlines, and their focus spells. Anoint Ally, Bleed Out, Blood Rising, Propelling Sorcery, Tap into Blood. The four Evolution feats. Demonic's caustic blast, Hag's illusory disguise, an element other than fire for Elemental (1.14).
- [ ] **Oracle.** The curse and the cursebound condition. Revelation spells for the eight mysteries, and the rank 1 spell of Ancestors, Bones, Cosmos and Lore. Foretell Harm, Thousand Visions, Meddling Futures, Trance of Celerity's curse part, Domain Acumen, Divine Aegis.
- [ ] **Magus.** Each hybrid study's benefit and conflux spell (ten studies: each note says what, and only Sparkling Targe's Shield Block works). Choosing which spell goes into a Spellstrike, and counting it as two attacks. Arcane Cascade matching the last spell's type. Study feats: Burning Cascade, Devastating Spellstrike, Distracting Spellstrike, Shattering Spellstrike, Exploding Bullet, Starlit Eyes, Student of the Staff, Heaven-Earth Encompassing Sleeves, Emergency Targe. Expansive Spellstrike, Striker's Scroll (3.18), Running Recharge, Steady Spellcasting (nothing disrupts a spell).
- [ ] **Thaumaturge.** 0 of 14 feats. Esoterica and adept benefits. Implements that give only the item: Bell, Censer, Mirror. Missing parts: Chalice's sip, Lantern revealing the unseen, Shield surviving destruction, Tome's skills, Wand spending more. A Censer sprite. Feats: Ammunition Thaumaturgy, Call Implement, Paired Link, Divine Disharmony, Root to Life, 3.11, 3.18.
- [ ] **Gunslinger.** Misfires and cleaning, then Risky Reload and Instant Backup. Each way's deed and the other half of its reload. Spellshot. Combination weapons for the Triggerbrand. Capacity weapons. Sword and Pistol, Pistol Twirl (Feint at range), Warning Shot (Demoralize by gunshot), Cover Fire, Assisting Shot, Guiding Shot, Fake Out, Blast Lock (Force Open with `CFLockSystem`), Crossbow Crack Shot, Dual-Weapon Reload, Munitions Crafter, the second save against being controlled.
- [ ] **Inventor.** Construct modifications, its second attack, and being broken and repaired. Weapon modifications' razing and hampering, and bases other than the longsword. Rebuilding a destroyed innovation, changing it in downtime, the Inventor skill feat. Feats: Built-in Tools, Collapse Armor, Collapse Construct, Diving Armor, Oil Fire, Dual-Form Weapon, Wukong Extension, Duo Dragon Kick, Haphazard Repair, Variable Core, Gadget Specialist, No! No! I Created You!.
- [ ] **Psychic.** The missing psi cantrips and amps: telekinetic hand, message, forbidden thought, warp step, distortion lens, and amps for detect magic, guidance, figment and shield. Each conscious mind's rank 1 spell. Oscillating Wave alternating. Gathered Lore's Recall the Teachings, Emotional Acceptance's bonus. Amps from feats: Mental Balm, Homing Beacon, Warp Space, Astral Tether. Violent Unleash, Mental Buffer's increase, Ancestral Mind, Psychic Rapport.
- [ ] **Commander.** A folio of five and swapping tactics (Adaptive Stratagem, Tactical Expansion). The unbuilt tactics: Double Team, End It!, Coordinating Maneuvers, Mountaineering Training, Naval Training, Passage of Lines, Protective Screen, Shadows in the Moonlight. A real Stride or Step for the squad. Plant Banner, Banner Twirl, Banner's Inspiration's save, losing the banner. Warfare Lore, and Deceptive Tactics on it. Officer's Medical Training on Intelligence, Shielded Recovery, Armored Regiment Training.
- [ ] **Guardian.** Taunts do not change who an NPC attacks. Intercept Attack's Step, Ever Ready. Bodyguard (choosing the ally), Larger Than Life (size in maneuvers), Not So Fast, Shoulder Check, Body Shield, 3.7.
- [ ] **Necromancer.** Regaining Focus by destroying a thrall. Consume Thrall (Puppeteer), Thrall Teamwork (Reaper), Inevitable Return (3.5). Thralls check the tile is free, and their Strikes count for the Necromancer's penalty. Swapping void and vitality. Fascinations: Flesh (slowed, difficult terrain), Spirit (drained). Grave spells and feats: Muscle Barrier, Song of the Soul, Bony Barrage's armor, Widespread Fascination, The Hallowed Dead, The Unholy Dead, Mobile Thralls, Reach of the Dead, Osteo Armaments, Draining Strike, Death Speaker, Bone Speaker. Thrall blasts draw nothing on the floor.
- [ ] **Animist.** The 40 or so apparition spells that do not exist (5). The Crafter's and Stalker's vessel spells. Changing the primary apparition on Refocus, Relinquish Control (the Medium practice), sensing spirits and the AC bonus (Seer), a spirit familiar (Shaman). Apparition spells cast from a higher slot. Channeler's Stance and Channeled Protection, Walk the Wilds, Apparition Sense.
- [ ] **Exemplar.** 0 of 13 feats. Transcendences for Bands of Imprisonment, Fetching Bangles, Horn of Plenty, Skybearer's Belt, Victor's Wreath, Pelt of the Beast and Shadow Sheath. Shadow Sheath's copies, Fetching Bangles' aura, Horn of Plenty's elixirs. AC against ranged only (Gaze Sharp as Steel). Skin Hard as Horn should not count against critical hits. Epithets at level 3: Cunning, Deft, Mournful, Proud. Every feat still needs its own note: Hurl at the Horizon, Leap the Falls, Only the Worthy, Red-Gold Mortality, Steel on Steel, Through the Needle's Eye, Twin Stars, Energized Spark, Sanctified Soul, Vow of Mortal Defiance.
- [ ] **Runesmith.** 0 of 19 feats. Invocations for Baruiel, Ledria, Oljinex, Pluuna (dazzled), Tilus and Zohk (a teleport). Passives: lowering a resistance (Atryl, Thullax), damage on concentrating or standing still (Camonica, Ranshu), a bonus while a shield is raised (Holtrik), light (Pluuna), scent (Rehgog), a shared language (Tilus), Speed (Zohk), Marssyl's push, Lyskel's clumsy and terrain. The level 5 diacritic runes. Etching on a creature, invoking on two creatures, Magical Crafting and property rune formulas, materials for rune items. Every feat still needs its own note: Backup Runic Enhancement, Engraving Strike, Fortifying Knock, Remote Detonation, Rune-Singer, Rune Ward, Runic Tattoo, Smithing Weapons, Transpose Etching, Invisible Ink, Ghostly Resonance, Terrifying Invocation, Song of Glorious Invocation, Artist's Attendance, Pattern Flight, Writing on the Wall.
- [ ] **Summoner.** Each eidolon type's ability: Aberrant, Angel, Beast, Construct, Demon, Devotion Phantom, Elemental, Fey, Psychopomp, Swarm, and the rest of Dragon, Ooze and Undead. The second, agile attack and the primary's traits. Act Together, Share Senses, runes and gear on an eidolon. One body per type instead of the placeholder. Protect Companion counting only in reach. Evolution feats: Advanced Weaponry, Amphibious Form, Bloodletting Claws, Dual Studies, Energy Heart, Dual Energy Heart, Expanded Senses, Glider Form, Magical Understudy, Ranged Combatant, Shrink Down, Skilled Partner, Steed Form, Vibration Sense, Meld into Eidolon, Reactive Dismissal. Link spells: Extend Boost, Lifelink Surge, Unfetter Eidolon.

### 5. Spells

All 62 spells in the catalog have an action and an effect. What is left is spells that do not exist, and parts of ones that do.

- [ ] **5.1 Compare each spell with its text.** The scan only finds a spell with no effect at all. Fixed on 18 November 2026: Soothe was a legacy 1-to-3 action spell for 1d10+8 and is the remaster's 2-action 1d10+4, heightened, with +2 to saves against mental effects; Resist Energy only ever resisted fire, and now picks its energy from the wheel. Areas were compared with Archives of Nethys on 17 November 2026, for every catalog and focus spell, and five were wrong: Floating Flame is one 5-foot square, not a 5-foot burst ("1 5-foot square" had been read as one number); Bless is a 15-foot emanation and Bane a 10-foot one, where both were 5 feet; Bless gave +1 to saves as well as attack rolls, which it does not; Sleep is a 5-foot burst, not one creature; Telekinetic Rend is two 5-foot bursts, which had become one 10-foot burst, and is now one 5-foot burst with the second `TBA`. Ranges, targets, damage and durations have not been compared yet. `feature_scan.py` still lists Animal Form and Prestidigitation as having none, because both run on their own systems: teach it about them.
- [x] **5.2 Heal and Harm with 1, 2 or 3 actions.** Built on 18 November 2026 (`CFHealHarmTest`). Casting either opens the action wheel: 1 action is a touch for 1d8, 2 actions reach 30 feet and heal 1d8+8, 3 actions hit everyone in a 30-foot emanation around the caster for 1d8, at once with no click. `CFVitalEnergyEffect` heals the creatures that energy heals and damages the ones it harms with a basic Fortitude save, so Heal now hurts undead and Harm heals them (before, Heal did nothing to undead and Harm did nothing either, as undead resist void). Only a creature that is harmed rolls the save (`harmedBy` on `CFCheck`). `RestoreHitPoints` takes `voidHealing`. A creature with no player casts the 2-action version.
    - Left out: the emanation does not draw on the floor yet, and the 1-action version keeps the manipulate trait.
- [ ] **5.3 Bless and Bane as auras** that follow the caster for a minute. They are one pulse today.
- [ ] **5.4 Summon spells** (3.10). Summon Animal is in. The others are not.
- [ ] **5.5 Spells other options name and the game lacks.**
    - Animist apparitions: Mending, Knock, Ghostly Weapon, Protector Tree, Gentle Breeze, Safe Passage, Deja Vu, Curse of Lost Time, Telekinetic Hand, Ill Omen, Veil of Privacy, Message, Blistering Invective, Hypercognition, Caustic Blast, Grim Tendrils, Acid Grip, Aqueous Orb, Create Food, Enthrall, Dizzying Colors, Hypnotize, Slashing Gust, Tailwind, Propulsive Breeze, Wall of Wind, Puff of Poison, Stupefy, Gouging Claw, Runic Body, Vomit Swarm, Wall of Thorns, Interposing Earth, Exploding Earth, Rousing Splash, Mist, Crashing Wave. Lurker, Shepherd, Speaker and Stalker teach nothing yet.
    - Sorcerer: caustic blast, illusory disguise. Bard: phantasmal minion. Druid: heal animal. Heritages and ancestry feats: ghost sound, spout.
    - Psychic: telekinetic hand, message, forbidden thought, warp step, distortion lens.
    - Walls (Wall of Wind, Wall of Thorns) and a teleport are new shapes for the effect pipeline.
- [ ] **5.6 Focus spells that are missing.** Witch hexes, Sorcerer bloodlines (13), Oracle revelations (8), domains beyond the 21 our deities have, Whispering Quiet, Druid order spells (3), Ranger warden spells, Monk qi spells, Magus conflux spells (10), Summoner link spells (3), the Champion's two-action versions.
- [ ] **5.7 Focus spells missing a part** (`Spells/Focus/focus.yml`).
    - Lay on Hands: damaging undead and the AC bonus. Touch of the Void: healing undead. Both need the undead trait read on the target.
    - Embodiment of Battle: martial training, Reactive Strike, the penalty to spells.
    - River Carving Mountains: cover against ranged attacks, difficult terrain.
    - Trickster's Mirrors: the mirrors and glass shards.
    - Devouring Dark Form: reach, grapple, the shark form.
    - Darkened Forest Form's bite: free Recall Knowledge, spitting venom.
    - Gift of the Anemos (the Step, Shove, jump and off-guard aura).
    - Crown of Prophets: the second bonus, free Seek, Recall Knowledge and its price.
    - Protect Companion's resistance, and Boost Eidolon's other evolutions.
- [ ] **5.8 Daily uses for innate spells.** A ranked spell from `CFGrantSpell` is at will (1.13).
- [ ] **5.9 Counteracting** cannot end asleep, paralyzed, sickened, darkvision, resistances, quickened or slowed, and does not suppress magic items.
- [ ] **5.10 Battle forms.** They add Strength to damage where the rules give the form's +1 only. Each animal has one attack. Frog, shark and snake, climb and swim Speeds, Athletics +9 and senses are not built. A pest's Acrobatics and Stealth +10 and Athletics -4 are not either. The caster is drawn as the animal on a stock sprite (the bull is a cow, the canine a corgi, the deer a reindeer, the pest a mouse), and the HUD shows "Animal Form: Cat". The sprite has no dead or lying state, because a form ends when the creature drops.
- [ ] **5.11 Command, Figment, Sigil.** A commanded player only sees the order. Figment has no sound mode, and no NPC reacts to it. Sigil cannot go on a creature or be hidden.
- [ ] **5.12 Slots.** Apparition spells only resolve at their own rank. A Wizard's curriculum slot is not built. The level cap stays at 5, so spells stop at rank 3.
- [ ] **5.13 Spells without a fill still pass through walls.** Line of effect is a `CFTargeting` flag that no spell turns on.

### General and skill feats

`Feats/General/feats.yml` holds 131 feats through level 4 from Player Core, Player Core 2 and GM Core, with the site's one line summaries. `.agent/port-scripts/general_feats.py` rewrites that file from Archives of Nethys, so put fixes in the script. Of 74 feats backgrounds name, 65 match by name. 259 of 324 backgrounds grant one.

- [ ] Effects for the rest. 13 work: Battle Medicine, Multilingual, Arcane Sense, Armor Proficiency, Canny Acumen, Diehard, Feather Step, Fleet, Hefty Hauler, Shield Block, Skill Training, Toughness and Weapon Proficiency. `general_feats.py` keeps them in `WORKING`. Tests: `CFGeneralFeatTest`.
- [ ] First the ones other options hand out (1.1, 2.2): Intimidating Glare, Group Coercion, Ride, Combat Climber, Terrain Stalker, Quick Squeeze, Specialty Crafting, Forager, Steady Balance, Untrained Improvisation, Adopted Ancestry, Assurance, Shameless Request, Quick Disguise, Lengthy Diversion, Snare Crafting, Impressive Performance, Titan Wrestler, Hobnobber, Alchemical Crafting, Cat Fall, Pet, Bon Mot, Dirty Trick, Fascinating Performance, Pickpocket.
- [ ] Weapon Proficiency only gives martial weapons, not the advanced weapon a character already trained in martial would get. Toughness and Fleet do not show in the builder's preview.
- [ ] 119 prerequisites are still text only: familiars, class features such as Spellstrike, an innovation, a focus pool, an age.
- [ ] The 2 background feats with no match, from other books: Contract Negotiator and Ravening's Desperation.
- [ ] Three backgrounds name two feats and get both, which has not been checked against the rules (Hermean Heritor and the ones with Hefty Hauler or Diehard).

### Left open in the pipeline

Gaps in the parts of Step 1 that are built. Take them when the system is next touched.

**Grants**

- [ ] A bonus against a trait: on attacks and AC it reads the creature's traits only, not those of the weapon or spell.

**Stances, spellshapes, reactions and auras**

- [ ] Reactions (63 feats). What is left is the list below, and the triggers under 3.4 and 3.5. `CFGrantReaction` holds a `CFReaction`: a trigger, what has to be true, and what it does. `CFReactionSystem` runs them. Built on 9 November 2026, with Nimble Dodge, Flashy Dodge, Deflect Projectile, Charmed Life, Orc Ferocity and Wounded Rage. Tests: `CFReactionTest`.
    - Reactions stay automatic and off the wheel. One only fires when it would change the outcome, so it is not wasted, and the character effectively sees the roll first. That is a deliberate change from PF2e.
    - Triggers so far: `AttackWouldHit`, `SaveWouldFail`, `SkillCheckWouldFail`, `Damaged`, `WouldDrop`, `EnemyLeftReach`, `AttackCriticallyMissed`, `CriticallyHit`, `WouldBeDamaged`, `AllyWouldBeDamaged`, `InterceptsDamage`, `ExploitedWouldDamage`, `AllyWouldBeHit`, `EnemyCastsSpell`, `AllyWouldDrop`, `Grabbed`, `Sickened`. `perDay` limits are restored by a long rest, and `cooldownSeconds` is a wait in real time.
    - `EnemyLeftReach` fires when an enemy leaves the reach circle under its own power. Walking out is PF2e's Step: it costs 1 action and does not provoke, and with no actions left it provokes anyway. It is only charged when the watcher could have answered, and one Step covers everyone left within a second. Moving about inside reach does not provoke either, because in real time that would fire on every footstep. Reactive Strike and Stand Still use it. Tests: `CFLeftReachTest`.
    - `WouldBeDamaged` and `AllyWouldBeDamaged` fire as damage is about to land, through `CFReactionSystem.AnswerDamage`, and need the enemy in the owner's aura. Each Champion cause is one of these. Tests: `CFChampionCauseTest`.
    - [ ] Causes where the rules let the enemy choose always use its refusal, as a mindless creature would: Iron Command, Glimpse of Redemption.
    - [ ] Shield Block runs before the other damage reactions in code. It waits when a reaction ranked above it could answer, but one ranked below it never gets the chance once it fires.
    - [ ] A response that moves the creature, for No Escape and Reactive Pursuit.
    - [ ] NPCs do not walk to avoid Reactive Strike. They always run.
    - [ ] Reactive Strike, Shield Block and You're Next are still flags on `CFCombatOptionsComponent`, though they take their place in the order. Reactive Shield is answered in `CFReactionSystem.AnswerAttack`, in its place.
- [ ] Auras: Bard compositions, Hampering Stance and Channeled Protection can use this when they are built (4). A choice of when to suppress the aura is not built.

**Subclasses and deities**

- [ ] Replace the placeholder names and lore when the setting is written. Domains, bonus spells, edicts and anathema are not built. A Champion's cause is not checked against the deity's holy or unholy side.

**Level-up**

- [ ] The level cap stays at 5, so spells stop at rank 3.

**Action HUD**

- [ ] Section widths are saved for the deck as a whole, not per character.
- [ ] Custom / Passives only holds the numbered hotbar. Nothing in the game is a passive toggle yet.
- [ ] The Common tab's icons are stock placeholders. Grapple, Feint, Aid and Recall Knowledge do not look like what they do.
- [ ] Throw and Stride have no button, because stock's inputs are already the whole action. Reposition and Sneak have none either.
- [ ] The sheet's Preparation window can overwrite a slot but not empty one.

**Languages**

- [ ] Not taken from Starlight: speaking one line in another language with a chat prefix, languages understood but not spoken, translators, and language on the radio.
- [ ] Radio is not translated. `EntitySpokeEvent` still carries the real words, so a radio or anything else listening to it hears them.
- [ ] Languages from a heritage or a background.

**Rest**

- Refocus takes 1 real minute and no longer follows the world clock, where it was 10 world minutes, about 2.5 real ones. Changed on 17 November 2026 at the user's request. An Inventor's innovation that breaks down is out for the same minute.

- [ ] It restores hit points, wounded, spell slots and focus. Conditions, Battle Medicine immunity and other daily limits are left alone.

### Decided

- **A spell with options asks on the action wheel.** Decided by the user on 18 November 2026. Heal and Harm's action count, Command's order, Resist Energy's energy and the like are a `CFModeChoice` on the spell, as Force Barrage asks for its bolts. A mode can set its own `actions`, `rangeFeet` or `touch`, an `areaShape` with `areaFeet` (around the caster when `immediate`, otherwise aimed at the creature clicked), a `damageType` for effects that read it, and a `requires` that hides it from creatures without a feat.

- **Sustained spells keep themselves going.** Decided by the user on 17 November 2026. A spell with `sustained: true` on its `CFSpell` runs its effects again every 6 seconds for a minute, with no input and at no cost (`CFSustain`, `CFSustainedCasts`, the pulse in `CFSpellSystem.Update`). On the caster it runs on the caster, an emanation follows the caster, an aimed area stays where it was aimed, and a spell on one creature tries that creature again. It cannot be cast again while it runs, except one with `movesWhenRecast` (Floating Flame): casting that again moves it to the new spot for its actions and no slot, anywhere in range where the rules allow 10 feet, and it burns there from its next pulse on. Only Dispel Magic ends one early: on the caster, or on a creature carrying what the spell left, and a summoned creature counts as its spell. So far: the Animist's twelve vessel spells, Floating Flame, Laughing Fit and Summon Animal. Tests: `CFApparitionTest.ASustainedSpellRunsItselfUntilItIsDispelled`, `CFSummonTest`.
    - [ ] Spells the catalog calls Sustained that are still one cast: Bless (5.3), Calm, Forbidding Ward, Figment, Prestidigitation. Give each `sustained: true` when it is next touched, and check what repeating its effect does.

- **The lobby builds level 1, and each later level is picked in play.** Changed on 11 November 2026, from picking every level up to 5 in the lobby. When a character has the XP, the player picks the level on the character sheet's Level Up tab and accepts it.
- **Unfinished options are marked in the builder**, not hidden, so a player can see what is coming.
- **Exploration and downtime options run on the world clock** where that works. Otherwise they keep their flavour text with the word `TBA` on its own, so `TBA` can be searched for later.
- **Flying is over open sky, space tiles, chasms and holes**, and open sky is a fall into the void with a 2 second grace and Grab an Edge (2.7). Decided on 15 November 2026.
- **Swimming and climbing are `TBA`.** Swimming will be ported from another fork, because SS14 has water entities. Climb speeds are not built, and `CFZLevelClimb` always rolls.
- **Z-levels are CrystallEdge's**, ported on 17 November 2026 with eshhhed's permission, which covers that code alone and asks for the credit "eshhhed". The CP14 stack of portal-linked maps is gone; our stairs, ladders and holes now sit on the CrystallEdge network, and fall damage is Player Core's. What landed and what was left out is in `zlevels.md`.
- **Deities are our own pantheon**, not Golarion's named gods. Speaking to a god is stock SS14 prayer, which sends the message to the admins, so there is no religion system to build.
- **Languages matter**, for roleplay: talk a goblin out of a fight in its own tongue. See "Languages" below.

## Loose ends from finished work

What was left open when each of these was built. The finished parts are in `roadmapold.md` under the same names.

### Spell visuals

- [ ] No bomb exists yet to use `CFFillEffect`. A lingering fill ends if its source is deleted, so a thrown bomb that should leave a zone with a save needs the zone to keep its own check and effects.

- [ ] Heal and Harm's emanation and Directed Channel's cone draw nothing on the floor. A `CFAreaFill` on a mode is not supported yet.

### GOAP movement

- [ ] A recalled companion ignores enemies even when hit on the way back. `StayAlive` can still make one run from an ordered target.
- [ ] A companion's place behind its master only follows the way they walked. Teleporting or being pulled leaves it on its own side.

### Bard

- [ ] The virtuoso's lute keeps stock's MIDI limits, so a very busy song can still give finger cramps.

### Level up

- [ ] Read the next report that comes in, and fix what it shows.

### Holstering

- [ ] The back slot is where the adventurer's kit goes, so a shield or weapon there means carrying the kit some other way.

### HUD

- [ ] The keys follow the chosen tab even while the deck is slid shut. Check in play whether they should only work while it is open.

### Levels in game, and respawn

- [ ] Older saved characters still hold the level 2 to 5 picks the lobby used to take. The Level Up tab starts from them, and the player can change them.

### Monsters, summons and companions

- [ ] A companion with a player in it gets nothing from pointing but the popup.

### Money

- [ ] What a merchant charges is not on the counter: they sell by hand, trading coins and goods.

### Monster parts and harvesting

- [ ] Merging two stacks of harvested parts keeps the receiving stack's value per item, so their worth can drift.
- [ ] Kobolds, goblins and hobgoblins give generic monster parts. Decide whether humanoids should give anything.
- [ ] Later: Battlezoo's refining and imbuing, crafting parts into gear. Bulk by size, and Gathering More through Earn Income, are not built.

### PvP

- [ ] A player's companion, eidolon or thrall follows its faction, so it never fights another player even with PvP on.

### Respawning and SSD players

- [ ] Drop the wallet too, once it exists (1).

## Step 2: Finish the rebuild

The checklist is done and in `roadmapold.md`. These notes stay, because they are still true.

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
    - Done on 17 November 2026: every recipe has a `category`, and the one book that taught everything is replaced by a handbook per category at 5 sp each (`CFFormulaBookTools`, `Materials`, `Weapons`, `Armor`, `Clothing`, `Bags`, `Building`, `Food`, `Farming`, `Alchemy`, `Goods`). The bench window groups its list by category. The cooking table has its 12 recipes (dough, cutlets, seeds from produce), and the anvil makes the cooking pot, frying pan and pie plate. Tests: `CFCraftingTest`.
    - 77 of CP14's recipes are in `recipes_ported.yml`, through `recipes.py`. 68 results are still not ported, so the Clothing, Bags and Alchemy handbooks teach nothing yet: clothing (22), backpacks, satchels and belts, vials, crossbolts of other metals, CP14's key and lock items, the smoking pipe, the wrench. 4 need an unported ingredient (Screwdriver and three plushies).
    - `world.py`, `props.py` and `finish.py` no longer reproduce the tree: on 17 November 2026 `world.py` deleted `Ported/Keys/keys.yml`, `Ported/Misc/extras.yml` and a torch graph, and `finish.py` stopped on a missing file. Do not run them without fixing that. `recipes.py` and the new `produce.py` are safe. The plates, bowls and fireplace base were edited by hand, in the generated folders.
    - Not done: Alchemical Crafting and Magical Crafting feat checks (no such recipes yet), batches of consumables, repairing items, `CP14StackGroup` and solution requirements, and reverse-engineering formulas. Structures are still the stock construction menu, with no book of their own.
- [ ] Creatures with PF2e stat blocks. `importcreatures.md` is the guide.
    - Each creature has one Strike, its first printed melee Strike. A second Strike needs a way to pick between attacks. Special abilities wait for the planned better AI, not for this row. They are not in: Grab, venom and disease, Ferocity, charges, Filth Wave, the ooze's extra acid. Pack Attack is in (`packAttackDie`), on the wolf and the velociraptor. The stat block comment on each creature lists what is missing. Immunity to critical hits and precision is not in either.
    - Size does not change a creature's body yet, so the Large bear is a Medium body.
    - Goblin Warrior, as a melee and a ranged variant on the `_CE` goblin body, with the new `CFDogslicer`. Goblin Scuttle is not in. Tests: `CFGoblinWarriorTest`.
    - Hobgoblin Soldier, on the same body: longsword and shield, or shortbow with the longsword drawn when out of arrows. Reactive Strike and Shield Block are in, and GOAP now raises a held shield on seeing an enemy (`RaiseShield`). Formation is not in. Tests: `CFHobgoblinSoldierTest`.
    - Wolf, on the stock corgi sprite, and a `WolfPack` encounter of four that flank. Pack Attack, low-light vision and scent are in. Knockdown is not, because Trip is not an action. Tests: `CFWolfTest`.
    - Juvenile Horned Dragon, from the Beginner Box Adventure on Demiplane, on a sprite made for CarpFinder. Jaws, Poison Breath (`CFBreathWeapon`, used by the AI and by a player in the body) and Draconic Momentum. Since 12 November 2026 the breath is a toxic cloud (`CFBreathGas`, `CFBreathGasSystem`): gas let out over 1.5 seconds and shared between neighbouring tiles as atmos does, but only over the cone's tiles it can reach, so walls, closed doors and open sky stop it. It fills the cone in about 2 seconds. Anyone on a tile once it has thickened rolls, once per breath, including someone who walks in while it lingers, and it is gone in about 12 seconds. Each tile is drawn with the stock tritium gas sheet (`CFBreathCloudPoison`), thicker with more gas. It is not real atmos, which cannot keep to a cone and whose tritium burns and irradiates. A liquid version, Toxic Spew (`CFActToxicSpew`, `CFAreaFill`, stock puddles of `CFToxicSpew`), is kept for another creature and granted to nothing yet. Tests: `CFToxicSpewTest`. It keeps its lair (`CFGOAPLair`): breathes when charged, waits in the lair while it recharges, closes only within 3 tiles, and goes home after 8 tiles or 10 seconds away. Draconic Frenzy, Twisting Tail, the poison on its jaws, and flying and swimming are not in. Tests: `CFJuvenileHornedDragonTest`.
    - Still to port: livestock (pig, sheep, rabbit, cat; there is no printed stat block for most), zombies (they need CP14's humanoid species), watchers, spineguard, flem, cackle, bone hound, mole, myconids, ice spectre, whistler, mosquito, hydra, fairy. CP14-only monsters need a PF2e creature of a matching level chosen first.
    - Monsters are harvested for priced parts with a survival knife and a Survival check, through `CFHarvestable` (see Monster parts under Do first). Boar, bear, wolf and dragon give leather, bone and meat.
- [ ] Raw materials and coins.
    - Done on 18 November 2026: ores, bars, hides, furs, wood, stone, glass, cloth, string, cotton, bone, ash and a few tools and goods have prices from Kris's Compendium of Trade Goods (`.agent/kctg-trade-goods.txt`, used with Kris's permission), through `.agent/port-scripts/trade_prices.py`. Each bigger stack prototype has its own price, the unit price times its count, so `CFIronBar10` is no bargain.
    - Not done: prices for what the compendium has no match for (mithril, scrap, dyes, shards, dirt, compost, wheat, sage, floor tiles, carpet, wallpaper, tableware, books: the list is at the top of `trade_prices.py`). Gold ore at 30 gp and a gold bar at 50 gp are the compendium's prices and have not been played with. Also not done: coin Bulk (1,000 coins is 1 Bulk), art objects, and coins as monster loot.
- [ ] Food and drink production.
    - Done on 17 November 2026 (`CFCookerTest`): a fire heats what is set on it (`CFFireHeater`), so dough on a bonfire bakes without Prestidigitation. CP14's pot, pan and pie plate cook the dish their contents fit (25 `cookingRecipe`s: meals, soups, pies), and a plate or bowl takes it. A baked pie is cut with a knife into its pieces, and the plate is left. Vegetables have slices and the tags the recipes read, and there is an apple. Sacks for produce and seeds.
    - Brewing is ours, because CP14 only had drinks ready made: wheat, fruit or pumpkin used on a barrel with 10 water becomes wort, must or pumpkin mash, and that ferments into beer, wine or ale, 1 unit every 3 seconds (`CFFermenter`).
    - Fixed on the way: every barrel used `SolutionContainerManager` and held nothing, the water and beer barrels included.
    - Our changes from CP14: a pot is heated by degrees, not joules, and cooks while it gets `CFHeatedEvent`. A dish that is cut does not use stock's `ToolRefinable`, which other systems may not write to.
    - [ ] Not played yet: the look of a dish in the pot and on a plate (the client layers and displacement maps are untested), cooking and burning sounds, and how long a fire takes in play.
    - [ ] No apple tree, so wine needs stock fruit. Sandwiches (`foodSequenceElement`) are not ported. Bread on a fire never burns. A pot keeps cooking on a furnace as on a bonfire, with no difference between them.
    - [ ] Not ported: monster toxins (creatures row), smoking pipes and chromium jelly, wild plants.
- [ ] Decide on CP14 alchemy.
    - CP14 alchemy (magic essences, `Target Effects` reagents, 67 reactions, cauldron brewing): **open question.** PF2e alchemy is the Crafting skill and formulas, not mixing reagents, so this may be a new game system. Left out until decided.

## Step 4: Close the rules gaps

Smaller gaps found while building the earlier phases. Take them when the system is next touched.

- [ ] `CFDcSource.Item` (needs item level, Phase 2). Line of effect is a `CFTargeting` flag but no spell turns it on yet.
- [ ] Damage type fields are still strings on pets (`CFPetPrototype.Damage`). Spells and weapons use `ProtoId<CFDamageTypePrototype>` with PascalCase ids. Convert pets when companions are next touched.
- [ ] 16 `_CF` systems subscribe in `Initialize()` instead of with `[SubscribeLocalEvent]` attributes. Convert each one when that system is next touched, not as a bulk change.
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

## Step 5: AI

The labels are the sections of Phase 6 in `roadmapold.md`, which has the design. A label with nothing open is left out. `goap.md` is how it works now.

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
- [ ] Measure the stress test on an open map too, where the calm open floor skip can show.
- [ ] More steering speedups, listed in `ai-testing.md` under the stress test. Steering is still about 0.45 ms of GOAP's 0.77 at 30 idle mobs.

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
- [ ] A routed creature that can still see its enemy never hides, because the flee map ignores line of sight.
- [ ] Swings at obstacles ignore `discipline`, so the pool stays empty and it stays at -10.

**6m. Fire (6 November 2026)**

- [ ] Stock cold, suffocation and pressure still only touch the bar, so they can kill at full CF HP. The same pattern would fix them.
- [ ] A creature rolls even with an enemy beside it. Check in play whether a smart one should finish the fight or get clear first.
- [ ] Players roll for free, as in stock. PF2e has no fixed cost here, so it has no action cost for now.
- [ ] A creature that falls in lava still relies on its steering to get out. The steps it would run there were not tested on real lava.

**6n. Tribes (16 November 2026)**

A kobold tribe that eats, drinks, farms, stores food, shares it, talks, and asks strangers for food before fighting, with a local language model that may pick its goals. How it works, and the full list of what is left, is in `society.md`. Tests: `CFGOAPSocietyTest`, `CFSocietyPromptTest`.

- [ ] Not played yet: the tribe on `KoboldSocietyTest2.yml`, and a parley with a real player.
- [x] The language model tried for real, 16 November 2026: llama.cpp with Llama 3.2 3B on the GPU, through `runllm.sh`. The grammar now parses (a newline in a character class broke every request) and keeps each goal to its own kind of target. Not tried: the model in a live round with a player.
- [x] Goals that need no language model, 16 November 2026: bread from wheat (mortar, water, fire, knife, as `PrepareFood` stages), seeds clipped from a grown plant when none are about (`PlantSeed` with stock clippers), and `TidyNest` (rubbish thrown out of the nest, puddles wiped, coins and potions stored). The encounter's `supplies` lay the tools down with the tribe. Stores are counted into `CFGOAPTribe.Stock`, so nobody opens a crate to know what it holds, and wheat nobody can grind is no longer food, which ends the carry-in-and-out loop. Tests: `CFGOAPSocietyTest`, `CFCookingTest`. Not done: pests and weeds, cleaning a puddle with a mop rather than by hand.
- [x] The fire is fed and lit, 16 November 2026: `KeepNest` (the old `TidyNest`) feeds a CF fireplace that is out or nearly through its fuel, one plank at a time from the ground or a store, and lights it as it does. Every piece of fuel a fireplace burns now leaves ash beside it, and the tribe stokes the fire for ash when a plot is poor, so `FeedSoil` has fertilizer. Tests: `ADeadFireIsFedAndLit`, `AshFromTheFireFeedsPoorSoil`. Not done: building a fire where there is none, and any fertilizer but ash.
- [ ] Tribute is only food dropped from a hand: not thrown, and not handed over.
- [x] Hunting, 16 November 2026: an animal (speaks no language, nobody's character, can be butchered) is never greeted. A tribe that wants food marks it hostile and the fighting goals kill it; a fed one lets it be for a minute. The carcass is loose food when the tribe has a survival knife: `PickUpFood` butchers it and takes a piece, the rest is stocked. Butcherable commoners now die at 0 Hit Points. Every kobold wears a `CFKoboldBackpack` (the `CFKobold` inventory template adds pocket, belt and back) and stashes in it what no store has room for. Tests: `AHungryTribeHuntsAPigAndEatsIt`, `AFedTribeLeavesTheAnimalsAlone`. Not done: stalking or choosing prey, telling tribemates where the kill lies.
- [x] Fixed 16 November 2026, from play: a tribe sat hungry beside a full crate, because a loaf with no knife about or raw meat with no fire did not count as food after the bread chain came in. Anything edible as it is counts again. A hungry member that knows of no food also goes and looks in a chest nobody has checked for 90 seconds, in case a player filled it, and what it finds is what the tribe knows. Tests: `AHungryMemberTakesFoodOutOfTheCrate`, `AHungryMemberLooksInAChestItThinksIsEmpty`.
- [ ] Sleeping in beds, and roles that change what a member does.
- [ ] `CFGOAPFact` has no bit left: `MessNearby` took the last. The next tribe fact has to fold into a sensor or replace one.
- [ ] Other `_CF` prototype files still use `SolutionContainerManager`. The well, the bucket and (17 November 2026) every barrel held nothing because of it, and were fixed. Check the rest.

**Debug code to remove**

- [ ] The decision log, once the AI has settled. `goap.md` lists the steps.

## Known bugs

- Fixed on 15 November 2026: a Debug server stopped at round start in `AtmosphereSystem.ShuntTileVolume`. The Dragon Run map held 63 floor tiles saved with the sky's unchanging air, from flooring over open sky in the editor, and a wall stood on one. See "Air is not worked out while a map is paused" in `ss14rules.md`. Tests: `CFMapAirTest`. Starlight was checked for a fix and has none: its atmos is older and has no `ShuntTileVolume`.

- The YAML linter (`dotnet run --project Content.YAMLLinter -c Tools`) reports 190 errors, seen on 17 November 2026 and not looked into. Most are guidebook entries that do not exist, stock ones and those named in `_CF/Guidebook/newplayer.yml`. `_CF/Entities/Effects/scent.yml` sets `icon` and `iconOn` on `Action`, which has no such fields. `CFMapAirTest.RoundMap` names an unknown prototype kind.

- `CFGOAPSocietyTest.AHungryTribeHuntsAPigAndEatsIt` is unreliable. Measured on 17 November 2026, alone: it failed 1 run in 5 on commit `25f19f9ea9` and 6 in 12 with the familiar work on top, which touches nothing a tribe or a pig uses, so the difference may be chance. Which of its steps fails was not caught. Not looked into.

- `MaterialArbitrageTest.NoMaterialArbitrage` (stock) fails inside `ConstructionGraphPrototype.Path`, which means a construction graph has a node it cannot reach. Found on 11 November 2026 and not looked into. It is not from butchering: the harvest change touches no construction.

- `CFGOAPObstacleTest.PatienceDependsOnThePersonality` fails about half the time, measured on 9 November 2026 on the commit before the HUD (7 of 15) and with the HUD (8 of 16). After 7 seconds the default mob should be working on the locked door, but its `Obstacle` is another entity created later in the test, or null. What that entity is has not been looked at. The decision log is printed when the test fails. It is the same setup as the locked door notes in `ai-testing.md`, which may or may not be the same cause.
- `CFGOAPEncounterTest.ItChoosesToFlankThenRunsThenHides` sometimes shows as `Skipped` in a full CF run and passes on its own. It runs no game ticks, so the cause is not movement. The failure message is lost, so the cause is unknown.
- `CFEconomyTest.ShopTabFollowsAccess` fails: `SetShopVisible(false)` leaves the Shop tab visible. Found on 10 November 2026 and not looked into. It is client UI, not the AI.
- `RainWatersExposedCrops` is unreliable, not just in a full run. On 6 November 2026 it showed as `Skipped` in two full runs, then failed once and passed once on its own. The failure message was not captured, so the cause is unknown. It only touches weather and plants, not the AI.
- `CFProneTest.NpcWithNoRoomKeepsTryingToStand` fails now and then. The same error also hit `NpcStandsOnceItHasAnAction`: an NPC's stand-up attempt adds a `StatusEffectContainerComponent` to a mob that is already being deleted, and the assert surfaces in the next test's setup. Left alone on purpose. It only affects stock NPCs now. GOAP NPCs are the only ones planned to crawl away, and that is not built.

## Later

- Sky ships with FTL travel that dock on breathable planets below, for exploring. Those planets get dungeons too. Floating caves in the sky are the adventure sites until then. `CFAdventureSiteSystem.Generate` always makes its own map, so planet dungeons need it to build on an existing map as well.
- Alchemical bombs and elixirs ported from CP14 alchemy, built on `CFAreaEffect` + `CFCheck`.
- Hazards and traps (area + check + degree effects). Prototype done: `CFHazard` and the Beginner Box spike trap (`CFSpikeTrap`, `CFSpikeTrapMonster`). Still to do: complex hazards with routines, resetting traps, and area traps (`CFAreaEffect` on the hazard).
- Persistent damage and resistances/weaknesses everywhere damage is dealt.
- AI avoids known traps through the `Threat` layer (6c).
- Animal companions taking player commands as an action (Command an Animal). Pointing already steers them.
- Mobs that cast spells, chosen by score. `goap-weights.md` has the plan.

## Working notes

- Every step ends with a full build (`dotnet build -c Tools`, errors only) and targeted integration tests.
- No new key functions and no edits to core input code. If something cannot be reached with stock controls, it goes in the verb menu or on the wheel.
- AI systems never cheat with omniscience: tracking uses sight, the influence maps, LKP and nodes only.
- Before subscribing a component and event pair, check `ss14rules.md`: `ProjectileComponent` + `BeforeProjectileHitEvent` already belongs to `CFStrikeSystem`.
- Imported CP14 code keeps its logic but takes CF names, namespaces and `_CF` folders. Credit CP14 in a header comment where a file is mostly theirs.

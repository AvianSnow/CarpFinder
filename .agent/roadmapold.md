# CarpFinder roadmap: what is done

The finished phases, moved out of `roadmap.md` on 6 November 2026. It is history, so it is not loaded each session. Look here for why something was built the way it is.

Items that were still open went to `roadmap.md`, and their lines were removed here. A "How it landed" note can still mention a gap, and where that gap is still open it is listed in `roadmap.md`.

## Why this order

1. **Effects first.** Spells, alchemical bombs, traps, auras, falling damage and property runes all need "hit an area, roll a save, apply something per degree of success". Building that once stops every later feature from growing its own copy, which is what happened to `CFSpellComponent`.
2. **Item rules before item ports.** If weapons and armor are ported before traits, Bulk and runes exist, every ported prototype has to be edited twice.
3. **Z-levels last, but independent.** They touch maps and transforms, not combat. They can start any time someone is free. Falling damage uses the Phase 1 damage effect.
4. **The rest of CP14 comes over too, under PF2e rules (Phase 3b).** Mapping furniture and props can start any time. Items you carry wait for Bulk and price from Phase 2. Nothing is ported that adds a game system PF2e does not have.
5. **Stock mechanics over HUD buttons (Phase 5).** The custom HUD stays, but anything SS14 already has a mechanic for moves onto it. The AI uses the same entry points, so this comes before it.
6. **AI last (Phase 6).** GOAP goals like "find a weapon" or "drink a healing potion" need actions it can call and items that say what they are. Steering and influence maps (6a, 6c) depend on neither and can start early. The AI respects Z-levels: one influence grid per map.

## Rebuild status (read this first)

The other machine's unpushed work was lost, so the items it had were re-imported from `crystall-punk-14` on this machine. The `[x]` ticks and "How it landed" notes below were written on that machine. Where this section disagrees with them, this section is right.

### Rebuilt here

- Foundation: CP14 audio (now with `Voice`), damage modifier sets, tools, sound collections, tags.
- Phase 2b maneuvers (`CFManeuverSystem`, shields, prone, grapple, pointing marks) and Phase 3 gear. Tested by `CFManeuverTest`, `CFGearPortTest`, `CFItemRulesTest`.
- Mapping props, world items, reagents, stacks, materials and construction graphs: about 820 prototypes under `Resources/Prototypes/_CF/Entities/Structures/Ported/` and `_CF/Ported/`.
- 127 `cp14-` locale strings in `Resources/Locale/en-US/_CF/ported.ftl`. They keep their `cp14-` keys.
- Roofs (`CFRoof`, client `CFRoofSystem`, `cf_toggleroof`) and wallmounts (`CFWallmount`, `CFWallRequired`, `CFNoRoofInTile`). Neither has its own test yet.
- Locks: every ported door, gate, safe and showcase has a grade and `keyId`. 38 keys, keyrings (`CFKeyRing`), and `CFPourDrawMode` / `CFPourInjectMode` for buckets and bottles. Tested by `CFPortedLockTest`.
- Afflictions: `cfAffliction`, `CFAfflictionSystem`, `!type:CFExposeAffliction`, `CFKillEffect`, printed Alcohol (`_CF/Afflictions/alcohol.yml`), timed clumsy, stupefied and sickened. Tested by `CFAfflictionTest`.

### Last check results

- Build: succeeded after the last C# change.
- YAML linter: 0 errors apart from "No localization". About 2,300 of those remain, nearly all feats and backgrounds.
- `CFPortedLockTest` 3 of 3, `CFAfflictionTest` 7 of 7. The other `CFLockTest` tests passed before the last lock data changes.
- `SpawnAndDirtyAllEntities` passes (5 November 2026, after Phase 5).
- Full CF suite, `SpawnAndDirtyAllEntities` and the guidebook tests, 5 November 2026: 420 of 422 passed. `RainWatersExposedCrops` showed as skipped and passes alone. `GuideEntryPrototypeTests.Validate("Chemicals")` failed (see Known bugs).

## Phase 0 — Housekeeping (done)

- [x] Fix doc paths. `carpfinder.mdc` now points at `.agent/`. The missing `hot-reload.md` and `CURSOR.md` references are gone; their content is in `Projectinstructions.md`.
- [x] Tile tool files renamed to `CFEditTileTool*.cs`, namespace `Content.Shared.CF.TileEditTool`.
- [x] Namespaces fixed: `Content.Server._CF.Temperature`, `Content.Server._CF.Materials` → `Content.Server.CF.*`.
- [x] RA0051: `[Dependency] private readonly` removed from every ported CP14 file (Temperature, Materials, Wallpaper, TileEditTool); those classes are `partial`.
- [x] `Content.Shared/_CF/Checks/`: `CFDegree` (was `CFStrikeDegree`), `CFDegrees.DegreeOfSuccess` (was on `CFMelee`), `CFDefenseType`, and `CFDefense` (moved from `Combat`). Skills, activities, Strikes and sanctuary use `CFDefenseType`.
- [x] `CFSpellSystem` uses `CFDefense.SaveModifier` instead of its own copy of the save math. `CFSpellDefense` stays for spell YAML and converts with `ToDefenseType()`.
- [x] `cfDamageType` prototype (`Resources/Prototypes/_CF/Damage/pf2e_damage_types.yml`, 15 Remaster types) with SS14 type, colour, hit sound and aliases. The SS14 damage mapping, default hit sounds, floating-number colours and resistance matching now read it.

Left over from Phase 0:

- [x] Delete the stub files on disk: `Content.Shared/_CF/Combat/CFDefense.cs`, `Content.Shared/_CF/TileEditTool/CP14EditTileToolComponent.cs`, `Content.Shared/_CF/TileEditTool/CP14EditTileToolSystem.cs` (each is a one-line comment).

## Phase 1 — General effect pipeline (split `CFSpellComponent`) (done)

`CFSpellComponent` holds about 50 fields. Area, save, damage, heal, buffs, riders, projectiles and the Prestidigitation menu are all on one component and in one 30 KB system. Split it into reusable pieces modeled on CP14's modular effects (`CP14SpellEffect`, `CP14SpellArea`, `CP14*ModularEffectEvent`), but resolved by PF2e degree of success.

### 1a. Effect base

- [x] `Content.Shared/_CF/Effects/CFEffect.cs`: `[ImplicitDataDefinitionForInheritors] abstract partial class CFEffect` with `Apply(IEntityManager, CFEffectArgs)`.
- [x] `CFEffectArgs`: `User`, `Source` (spell action, item, hazard), `Target`, `Position`, `Degree`, `Rank`/`Level`, `Dc`.
- [x] `CFEffectSystem`: runs effect lists. It must not keep mutable state (ECS rule).

### 1b. Components (each does one job)

| Component | Replaces on `CFSpellComponent` | Also used by |
|---|---|---|
| `CFAreaEffectComponent`: shape (burst, cone, emanation, line, square), `SizeFeet`, origin (caster or point), `IncludeSelf`, target filter (enemies, allies, all, whitelist), `MaxTargets` | `Area`, `AreaFeet`, `IncludeSelf`, `Friendly` targeting | bombs (splash), traps, hazards, auras, breath weapons |
| `CFTargetingComponent`: `RangeFeet`, delivery (self, creature, point, projectile), `ExtraTargets`, line of effect | `Delivery`, `RangeFeet`, `ExtraTargets` | thrown items, wands, activations |
| `CFCheckComponent`: defense (`CFDefenseType`), `BasicSave`, DC source (spell DC, class DC, item DC, fixed), attack/AC modifiers | `Defense`, `BasicSave`, `AttackBonus`, `TargetAcPenalty`, `FlatCheckDc` | bombs, poisons, hazards, skill actions |
| `CFDegreeEffectsComponent`: `criticalSuccess`, `success`, `failure`, `criticalFailure`, each a `List<CFEffect>` | every bool rider (`Sleep`, `Blind`, `Command`, `Charm`, `Calm`, `Frightened`, `Immobilize`, …) and `CFSpellRider` | anything with a check |
| `CFProjectileDeliveryComponent`: bolt prototype, homing, `MaxBolts` and the volley fields | `BoltPrototype`, `MaxBolts`, `ChosenBolts`, `Volley*` | magic missiles, thrown splash |
| `CFModeChoiceComponent`: generic "pick a mode before casting" | `TrickMenu`, `ChosenTrick` | variable-action spells, item activations |
| `CFHeightenComponent`: `perRank` and `atRank` effect deltas | hard-coded heightening | spells only |
| slim `CFSpellComponent`: spell id, rank, action cost, slot or focus spend | — | — |

Area geometry moves from `Spells/CFSpellGrid.cs` to `Effects/Areas/CFAreaGrid.cs`. The math is already right; it needs a new home and name, not a rewrite.

### 1c. Effect types (first set)

- [x] `Damage` (dice, flat, `cfDamageType`, add ability mod, basic save scaling, persistent damage)
- [x] `Heal` (dice, flat, vitality)
- [x] `ApplyCondition` (condition id + value + duration). Uses the existing `CFConditionSystem`.
- [x] `Bonus` (AC, attack, save, skill; type: status, circumstance or item; duration). Replaces `AcBonus`, `SaveBonus`, `ExtraWeaponDie`, `EnlargeDamage`.
- [x] `Resistance` / `Weakness`
- [x] `ActionCap` (quickened or slowed)
- [x] `ForcedMovement` (push or pull in feet)
- [x] `Spawn`, `Sound`, `Popup` (cosmetic telegraphy, as in CP14)
- [x] `Nested` area: an effect that runs an area, so a bomb can hit a primary target and splash.

### 1d. Migrate and verify

- [x] Convert every spell in `Resources/Prototypes/_CF/Spells/{0,1,2,3}` to the new components.
- [x] Integration tests for one spell per delivery: Force Barrage (projectile volley), a cone, a burst, an emanation, a Will save with condition, a heal.
- [x] Delete the dead fields and enums from `CFSpellComponent`.
- [x] Update `ss14.md` (HUD and spell section) with the new layout.

### How it landed

- Code is in `Content.Shared/_CF/Effects/` (`CFEffectSystem` plus `.Projectiles` and `.Damage` partials). Effect classes are in `Effects/Types/` and are written in YAML as `!type:CFDamageEffect`, `!type:CFApplyConditionEffect`, and so on.
- `CFDegreeEffectsComponent` has two lists besides the four degrees. `effects` runs on every target, and damage there scales itself (attack: miss 0, crit ×2; basic save: 0 / ½ / 1 / ×2). `landed` runs when the check went the user's way.
- Heightening data lives on the effects (`heightenDice`, `heightenFlat`). `CFHeightenComponent` holds the schedule (`baseRank`, `interval`) and `atRank` extras. Cantrips resolve at half the caster's level, rounded up. Slotted spells resolve at their own rank until casting from a higher slot exists.
- Bonuses are in `CFBonusesComponent` and `CFBonusSystem` and use remaster stacking: per type, the highest bonus and the worst penalty count. They replace `CFSpellStatusComponent`. Resistances, weaknesses and persistent damage (DC 15 flat check every 6 s) are in the same system.
- Prestidigitation is `CFModeChoice` plus a `CFPrestidigitation` marker. Object modes use a per-mode whitelist.
- Every spell kept its old numbers and degree behaviour, so the conversion is checkable. Places where the old behaviour is not remaster rules (fear on a success, Bless also adding to saves) are now YAML edits.
- Not done: `CFDcSource.Item` (needs item level, Phase 2). Line of effect is a `CFTargeting` flag but no spell turns it on yet.
- Tests: `Content.IntegrationTests/Tests/_CF/Effects/CFEffectPipelineTest.cs` runs on the dev map (`/Maps/Test/dev_map.yml`, gameMap `Dev` in `Resources/Prototypes/Maps/debug.yml`).

## Phase 2 — Item rules foundation (done)

Today melee and ranged weapon components duplicate the damage fields, traits are split between booleans (`Finesse`, `Agile`) and a `CFTrait` list, and armor has no check penalty, Speed penalty or Strength requirement.

### Weapons

- [x] `CFWeaponComponent` (shared): damage dice and die, `cfDamageType`, category (unarmed, simple, martial, advanced), group (sword, axe, bow, …), hands (1, 1+, 2), traits with values.
- [x] `cfWeaponTrait` prototype with an optional value: agile, finesse, deadly d10, fatal d12, reach 10, thrown 20, versatile P, two-hand d12, forceful, sweep, backstabber, trip, shove, disarm, propulsive, volley 30, reload 1, nonlethal.
- [x] `CFMeleeWeaponComponent` keeps reach only. `CFRangedWeaponComponent` keeps range increment, reload and ammo.
- [x] `CFStrikeWeaponStats` is built from traits, not booleans. Each trait is applied by a handler in `CFStrikeSystem`.

### Armor and shields

- [x] `CFArmorComponent`: category (unarmored, light, medium, heavy), AC bonus, Dex cap, check penalty, Speed penalty, Strength threshold, group (cloth, leather, composite, chain, plate), traits (bulwark, flexible, noisy, comfort).
- [x] Armor Speed and check penalties apply unless Strength meets the threshold. This replaces CP14's `ClothingSpeedModifier`.
- [x] `CFShieldComponent`: add HP, Broken Threshold and a broken state. Raise a Shield and Shield Block already exist.

### Runes, Bulk, other item data

- [x] `CFWeaponRunesComponent`: potency +1 to +3, striking (1 to 3 extra dice), property rune ids. Property runes are `CFEffect` lists on hit or crit.
- [x] `CFArmorRunesComponent`: potency, resilient.
- [x] `CFBulkComponent` (negligible, L, 1, 2, …). Encumbered at more than 5 + Str Bulk (clumsy 1, −10 ft Speed); you cannot carry more than 10 + Str. SS14 item size stays for storage grids.
- [x] Item level and rarity on items (`CFItemComponent` or on `CFItemPrice`).

### How it landed

- Weapons: `CFWeapon` (dice, die, `cfDamageType`, category, group, hands) with `CFMeleeWeapon` (reach) or `CFRangedWeapon` (range increment, reload). Reload is a weapon stat, not a trait; each shot costs 1 + reload actions.
- Traits: instead of a new `cfWeaponTrait` kind, the existing `cfTrait` prototypes got `rule`, `value` and `damageType` fields (`DeadlyD10` is `rule: Deadly, value: 10`). Weapons and armor list them on `CFTrait`, which the shop already shows. `CFWeaponTraits.From` resolves them, and `CFStrikeSystem` applies each rule. Versatile picks the type the target is weakest to.
- Strikes: critical hits now double everything (they used to double only the dice), then add deadly or fatal dice. Fists are agile, finesse and nonlethal, as in the remaster. Weapon potency is an item bonus and does not stack with other item bonuses to attack. Striking and the runic weapon spell do not stack.
- Armor: `CFArmorSystem` recomputes AC on equip and unequip, and only for characters (`CFCombatant.ArmorFromEquipment`). Check penalty, Speed penalty (from the new `CFCombatant.SpeedFeet`), Strength, flexible and noisy are all applied. Bulwark and comfort are labels until Reflex saves against specific effects exist.
- Shields: hit points, Broken Threshold, broken (no AC, no block), destroyed at 0. The shield takes the damage that gets past its Hardness.
- Runes: `CFWeaponRunes` (potency, striking, property runes as `cfPropertyRune` prototypes with `CFEffect` lists; Flaming, Frost, Shock, Corrosive, Thundering) and `CFArmorRunes` (potency, resilient as lasting item bonuses in `CFBonusSystem`).
- Items: `CFItem` (level, rarity) and `CFBulk` in `Content.Shared/_CF/Items`. `CFEncumbranceSystem` makes creatures encumbered (clumsy 1, −10 ft) and refuses pickups over the maximum. Clumsy lowers AC, Reflex, ranged attacks and Dex checks.
- Monster stat blocks: the kobold's +1 attack moved from the spear and sling items (which gave players +1 too) to `CFCombatant.StrikeAdjustment`.
- Tests: `Content.IntegrationTests/Tests/_CF/Combat/CFItemRulesTest.cs` on the dev map. Shared map helper: `Tests/_CF/CFDevMap.cs`.

## Phase 2b — Action mechanics (done)

Shield and Shove came off the action bar and moved to the hands, as in SS14. See "Shields" and "Shove and Disarm" in `codebase.md`.

- [x] Remove `CFActRaiseShield` and `CFActShove` from the radial actions.
- [x] Shields have an active state, `CFShieldComponent.Raised`. Use in hand raises (1 action) or lowers (free). It lasts until lowered, let go of, or broken, and works from either hand.
- [x] Combat mode right-click with an empty hand: Disarm if the target holds something, otherwise Shove. Rolled with PF2e rules instead of stock's random chance.
- [x] Combat mode right-click with a shield: Shove.
- [x] Shields are d4 melee weapons (shield bash), so left-click works with one in hand.
- [x] Weapons with the `Shove` or `Disarm` trait use that maneuver on right-click. A weapon with `Sweep` keeps its wide swing instead.
- [x] Remove Trip from the HUD. Three successful shoves in a row knock the target prone. This strays from PF2e on purpose.
- [x] Remove Grapple from the HUD. Pulling a living enemy rolls Grapple; friends, allies, downed creatures, and objects pull for free.
- [x] Remove Hunt Prey, Exploit Vulnerability, and Devise a Stratagem from the HUD. Pointing at a living enemy marks them.
- [x] Add Escape. A grabbed creature that tries to move spends an action to break free. NPCs try once a turn.
- [x] Prone is the stock knockdown: off-guard, −2 to attack rolls, and 1 action to stand. Players stand with C; NPCs stand once they have an action.
- [x] Shove degrees match Player Core: 5 feet, 10 feet on a critical success, and the shover falls prone on a critical failure.

Tests: `Content.IntegrationTests/Tests/_CF/Combat/CFManeuverTest.cs` on the dev map.

## Phase 3 — Port CP14 weapons, armor and gear (done)

Approach for every item:

1. New prototype under `Resources/Prototypes/_CF/Entities/...` with a `CF` id, parented to a `CFBaseWeapon<Group>` or `CFBaseArmor<Category>`.
2. Keep the sprite path pointing at `Resources/Textures/_CP14/...` with its `meta.json` license. Do not copy art under `_CF` (same rule as ancestry sprites).
3. Drop CP14-only rules: sharpening, combo bonus damage, self-damage, mana-cost modifiers, flat `Armor` coefficients, `ClothingSpeedModifier`. Replace them with PF2e stats.
4. Price from the PF2e item. Add it to shop or loadouts.

### Mapping (first pass)

| CP14 | CarpFinder (PF2e) |
|---|---|
| `dagger.yml` | Dagger: d4 P, agile, finesse, thrown 10, versatile S |
| `rapier.yml` | Rapier: d6 P, deadly d8, disarm, finesse |
| `skimitar.yml` | Scimitar: d6 S, forceful, sweep |
| `sword.yml` | Longsword (the current `CFLongsword` moves onto the CP14 sprite) |
| `twoHandedSword.yml` | Greatsword: d12 S, versatile P |
| `warAxe.yml` | Battle axe: d8 S, sweep |
| `warHammer.yml` | Warhammer: d8 B, shove |
| `spear.yml` | Spear: d6 P, thrown 20 (merge with `CFSpear`) |
| `Tools/hammer`, `pickaxe`, `shovel` | Light hammer and pick as weapons; shovel as improvised (−2, d6 B) |
| `shield.yml` / `shieldBuckler.yml` / `towerShield.yml` | Wooden shield (+2, H3, 12 HP) / Buckler (+1, H3, 6 HP) / Tower shield (+2, take cover +4) |
| `bow.yml` | Shortbow, plus the longbow (merged with `CFLongbow`). The ice bow waits for magic items. |
| `lightCrossbow.yml` / `crossbow.yml` | Crossbow / heavy crossbow (reload 1 / reload 2) |
| `Ammunition/arrows`, `crossbowBolts`, `spike` | Arrows, bolts; spikes as darts |
| `Magic/twoHandedStaffs.yml` | Staff (d4 B, two-hand d8); spellcasting staves later |
| `OuterClothing/armor.yml` bone armor | Hide armor (medium, +3, Dex +2) |
| chainmail | Chain shirt (light) and chain mail (medium) |
| cuirass | Breastplate (medium, +4, Dex +1) |
| infantry cuirass | Half plate (heavy, +5, Dex +1, Str +3) |
| Head / Gloves / Shoes armor pieces | Cosmetic, or folded into a full armor set: PF2e has no per-slot AC |
| `PetArmor` | Light barding (+1, Dex +5, −1, −5 ft, Str +3, Bulk 2, 10 gp) |

Not ported now: modular crafting (`ModularCraft`), chemical weapon coating (keep for injury poisons later), execution.

### How it landed

Every number was checked against the Player Core rows on Archives of Nethys. `CFGearPortTest` holds them as a table.

- Weapons: 14 melee, 5 ranged and the dart, on `CFBaseWeapon<Group>` plus a carry base (`CFBaseWeaponCarryBelt` or `CFBaseWeaponCarryNeck`) in `Objects/Weapons/_base.yml`. `CFLongsword`, `CFSpear`, `CFLongbow` and `CFSling` moved onto the same bases and CP14 sprites. The hatchet and sickle came along from `dagger.yml`.
- CP14's two-handed war axe, war hammer and spear are one-handed here, because their PF2e items are. They lost `Wieldable`.
- Hands: `hands: Two` weapons need `Wieldable` plus `MeleeRequiresWield` or `GunRequiresWield`, so the hand count is enforced. A test checks every prototype.
- Improvised: `CFWeapon.Improvised` is a −2 item penalty to attack (`CFMelee.AttackBonus`). The shovel is simple, as Player Core says. It has no price because none is printed.
- Crossbows: both are 2 hands, with their own `CFBolt` tag, `CFBolt` ammo and `CFBoltCaseFilled`. CP14's big bolts and gold, copper and mithril bolts are gone; PF2e has one bolt.
- Armor: `CFChainShirt`, `CFHideArmor`, `CFChainMail`, `CFBreastplate`, `CFHalfPlate` on `CFBaseArmor<Category>`. The bascinet and iron girdles are cosmetic.
- Shields: `CFWoodenShield`, `CFBuckler`, `CFTowerShield`. `CFShield.SpeedPenalty` applies while held and Strength does not reduce it. `CFShield.TakeCoverBonus` adds a Take Cover verb to a raised tower shield: 1 action, +4 instead of +2, ended by moving, attacking or lowering it.
- Loadouts: everything with a price is in the builder shop. The staff and sling cost nothing, so the shop hides them.
- Fixed on the way: the sling was d4, and its printed die is d6. `CFRatWearable`, `CFFireplaceFuel` and `CFFitInMortar` were used but never defined, which stopped the spawn-everything tests at the first entity that used them.

Left over from Phase 3:

## Phase 3b — Port the rest of CP14 (world, mapping and RPG content)

Goal: bring over every CP14 prototype file that fits a Pathfinder 2e game, together with the components it needs. The first priority is general items for mapping: beds, barrels, boxes, crates, tables, chairs, shelves, doors, windows, walls, floors, flora and decorations. Each one is updated to our code and our rules. CP14's own game systems do not come with them.

### The rule: RPG mechanics yes, new game systems no

Port it if:

- PF2e already has a rule for it. Use that rule: a lock is Pick a Lock (Thievery against the lock's DC), forcing a door is Force Open (Athletics), and fire is fire damage plus persistent fire.
- It is world flavour that works through checks, conditions and effects we already have. Drinking from a barrel and getting drunk is fine: a drink is a consumable whose `CFDegreeEffects` applies a condition (for example sickened or stupefied) after a Fortitude save.

Do not port it if it adds a mechanic that steers away from PF2e:

- New antagonists or win conditions: Vampire (blood drinking, vampire clans), BloodMoon, CP14 game rules, objectives and antag roles.
- A second progression or resource system: mana (`MagicEnergy`, `MagicManacostModify`, `MagicWeakness`, `MagicEssence`), CP14 skill trees (`Skill`), memory points. PF2e uses spell slots, focus points, skills and feats.
- CP14 religion and gods (`Religion`, god touch and renounce spells). PF2e deities come later as data on the character (cleric, champion).
- Server or meta features: `Sponsor`, `JoinQueue`, `Respawn`, `Discord`.

If you can't tell which side something falls on, add it to "Decided" below as an open question and leave it out until the user decides.

### Approach for every prototype

1. New file under `Resources/Prototypes/_CF/...` with `CF` ids, following the prototype-id rules in `Projectinstructions.md`. Sprites stay under `Resources/Textures/_CP14/...` with their `meta.json` license.
2. Use our components first: `CFEffect` lists for anything a consumable or interaction does, `CFCheck` for any roll, `cfDamageType` for damage, `CFBonusSystem` for bonuses, Phase 2 Bulk, price, item level and rarity on carried items, and `CFCombatant` stat blocks (AC, HP, saves at the creature's level) for creatures.
3. Port a CP14 component only when the prototype needs it and we have nothing equivalent. Rename it to `CF`, use the `Content.*.CF.*` namespace, drop `readonly` from `[Dependency]` and make the class `partial`. Add a header comment crediting CP14. Strip any part of it that belongs to a rejected system above, such as mana costs or skill-point gates.
4. Remove CP14-only fields from the YAML instead of leaving them dead.
5. Check the result against https://2e.aonprd.com/ (price, Bulk, level, DCs).

### What to bring over

| CP14 | Treatment |
|---|---|
| `Entities/Structures/Furniture`, `Storage`, `Decorations`, `Flora`, `Fence`, `Roof`, `Windows`, `Walls`, `Floor`, `Doors`, `well.yml`, `lighthouse.yml` | Port for mapping. Objects get Hardness and HP from the material (`CFCombatBridge`). Locked doors and chests use Pick a Lock / Force Open. |
| `Tiles`, `Decal`, `Palettes`, `Parallax`, `Maps` | Port for mapping. |
| `Objects/Consumable`, `Reagents`, `Flavors`, `Chemistry`, `Cooking` | Port. Food and drink effects become `CFEffect` lists (alcohol: Fortitude save, a condition on failure). Healing items use the PF2e potion and elixir numbers. |
| `Objects/Tools`, `Materials`, `Stacks`, `Bureaucracy`, `Carpet`, `Keys`, `LockKey`, `Misc`, `Fun`, `Wallpaper` | Port with Bulk, price and level. Tools that are also weapons follow Phase 3. |
| `Workbench`, `Recipes` | Port as crafting stations. Crafting uses the PF2e Crafting check and formula rules, not CP14 skill gates. |
| `Farming`, `DayCycle`, `Weather`, `Temperature` | Port. Extreme heat and cold are PF2e environmental damage (GM Core gives no save). |
| `Economy`, `Currency`, `Trading` | Port onto PF2e coins (cp, sp, gp, pp) and our shop. |
| `Mobs` (animals, monsters) | Port bodies and sprites. Stats come from a PF2e creature of a matching level. Drop CP14 special abilities that have no PF2e equivalent. |
| `ZLevel`, `zLevelLadders.yml` | Phase 4. |
| `Demiplane`, `Procedural` | Port as an adventure-site generator (see Decided). |
| `MagicLantern`, `MagicVision`, `AuraImrint` | Rework as light, detect magic and read aura (see Decided). |

### Decided

- `Demiplane` and `Procedural` (generated dungeon instances): **keep, as an adventure-site generator.** Rooms, hazards, traps and creatures inside follow PF2e: creature and hazard levels chosen from the party level (encounter budget), traps and hazards on `CFAreaEffect` + `CFCheck` + `CFDegreeEffects`, and loot from PF2e treasure by level. CP14 mana, skill-point and religion hooks in these systems are removed.
- `MagicLantern`, `MagicVision`, `AuraImrint`: **rework as PF2e items and spells.** Magic lanterns become light sources (the light cantrip, an everburning torch, or a lantern item). Magic vision becomes detect magic. Aura imprints become read aura. They use our spell actions and effect pipeline, not CP14 mana.

- CP14 alchemy (magic essences, `Target Effects` reagents, 67 reactions, cauldron brewing): **open question.** PF2e alchemy is the Crafting skill and formulas, not mixing reagents, so this may be a new game system. Left out until decided.

### Checklist

- [x] Mapping props: furniture, storage (barrels, boxes, crates), decorations, scenery flora, doors, windows, fences, roofs, walls, floors, carpets, tiles, decals and palettes.
  - Doors are unlocked until locks land (the row below). Gatherable plants and crops wait for farming. Furnaces and workbenches wait for crafting. Crystal wall lamps wait for magic lanterns. The blood parallax stays out with BloodMoon.
  - Mannequins use the human inventory template: CP14's partial template has slots we do not.
  - Objects get Hardness and HP through `CFWood`, `CFWoodStructural`, `CFRock`, `CFRockStructural`, `CFMetallic` and `CFMetallicStructural` in `Damage/materials.yml`.
  - `CFRoofComponent` and the client `CFRoofSystem` hide roofs while you stand under one. `cf_toggleroof` toggles them.
- [x] Consumables, reagents and drinks on `CFEffect` (alcohol and drunkenness as a save and a condition).
  - Food, drinks, mugs, bottles, cutlery, plates, flavors and the food and drink reagents are ported. Food and drink fill hunger and thirst as stock does. CP14's SS14 healing, mana and stamina effects are removed.
  - Alcohol is the printed item (Gamemastery Guide): a `cfAffliction` with DC 12 Fortitude, 10 minute onset, and 7 stages up to unconscious and death. Stage 1's bonus against fear has no stat to land on yet, so it only shows a popup. Stock slurred speech stays as flavour.
  - `CFAfflictionSystem` runs any staged affliction, and `!type:CFExposeAffliction` lets a reagent trigger one. Poisons and diseases can use the same pieces.
  - New conditions: timed clumsy, stupefied and sickened. Stupefied does not yet affect spell attacks, spell DCs, or the flat check to cast.
  - Not ported here: monster toxins (creatures row), cooking pots and their recipes (crafting row), smoking pipes and chromium jelly (they need plants from the farming row).
- Part done, the rest is in `roadmap.md`: Tools, materials, keys and locks (Pick a Lock / Force Open).
  - Doors were ported without their locks. `Entities/Structures/Doors/Locked` and every `CP14Lock` were left out, so all doors open freely until this row adds locks back.
  - Locks landed in `Content.Shared/_CF/Locks`: `CFLock` on top of the stock `Lock`, `CFKey`, `CFThievesToolkit`, `CFCrowbar`, Pick a Lock and Force Open verbs, and the five Player Core grades as parents (`CFLockPoor` to `CFLockSuperior`). CP14's key shapes, key file and lock editor are dropped: PF2e has no such minigame, and a key matches on `keyId`.
  - Still to do for locks: give the ported doors, chests, gates and safes a grade and a `keyId` (`Doors/Locked`, `Keys/Catalog`), keyrings, portable lock items, repairing a broken toolkit, and shoddy improvised picks. A critical Force Open unlocks but does not swing the door open.
  - Tests: `Content.IntegrationTests/Tests/_CF/Locks/CFLockTest.cs`.
- Part done, the rest is in `roadmap.md`: Workbenches and recipes on the PF2e Crafting rules.
  - The system landed: `Content.Shared/_CF/Crafting`, `Content.Server/_CF/Crafting/CFWorkbenchSystem`, `Content.Client/_CF/Crafting`. A character learns a formula by reading a formula item in hand, and a bench lists only the known recipes it can make. Materials are placed on the bench. Materials and craft seconds are CP14's.
  - Kept from PF2e: trained in Crafting, item level no higher than yours, master from item level 9, legendary from 17, DC by level plus rarity, failure salvages everything, critical failure ruins 10% (rounded up).
  - Dropped on purpose: every rule measured in days (setup, extra days to cut cost) and paying half the Price in materials. A critical success therefore gives nothing extra over a success.
  - The stock crafting menu needs training in Crafting. `CFConstructionGateSystem` hides it on the client and `ConstructionSystem` refuses the requests on the server. Things without `CFCombatant`, such as admin ghosts, keep it.
  - Recipes: 21 of CP14's 179 are in `Resources/Prototypes/_CF/Crafting/recipes.yml`. The rest make items this branch does not have, so add them as those items land. `CFWorkbench` itself has no recipes yet, because all 37 of its results are missing. The 8 vampire recipes stay out. The cooking table waits for cooking.
  - Not done: Alchemical Crafting and Magical Crafting feat checks (no such recipes yet), batches of consumables, repairing items, `CP14StackGroup` and solution requirements, and reverse-engineering formulas.
  - Decided: the basic crafter's book keeps teaching every common level 0 recipe, as in PF2e. Learned formulas last one round, because nothing carries over between rounds yet.
  - The anvil (5 iron bars) and furnace (10 stone) construction costs are ours. CP14 has no anvil graph, and its furnace needs iron, which only a furnace makes.
  - Tests: `Content.IntegrationTests/Tests/_CF/Crafting/CFCraftingTest.cs`.
- [x] Farming, weather, day cycle, temperature.
  - One map component runs all of it: `CFOutdoors` (`Content.Shared/_CF/Outdoors`, `Content.Server/_CF/Outdoors`). Put it on a map, or run `cf_outdoors [CFOutdoorsTemperate|CFOutdoorsArctic|CFOutdoorsDesert]` on the map you stand on. Presets are in `Resources/Prototypes/_CF/Outdoors/presets.yml`. The Dev map does not have it, because it is a station in space.
  - Day cycle: `CFWorldClock` is the single source (14 world hours of day, 10 of night, 6 real hours in all, starting at dawn). `CFOutdoorsSystem.IsDay` reads it. The map gets a stock `LightCycle` of the same length with noon lined up to the middle of the day. The stock curve is symmetric, so light is close to the 14/10 split, not exact.
  - Temperature: GM Core's table in `CFEnvironmentRules`, on the world clock, so "every 10 minutes" is 2.5 real minutes. Damage uses the low end of each category (minor 1d6, moderate 4d6). GM Core gives no save, so there is none. Fatigue intervals are left out: they are for overland travel and there is no fatigued condition yet. Cold-weather and desert clothing work as printed; desert clothing does nothing over armor. Both use the outer clothing slot, so they replace armor there for now.
  - Sheltered means `SharedWeatherSystem.CanWeatherAffect`: a stock `Roof`, a `BlockWeather` entity, or a tile without `weather: true`. When the CF roofs from the mapping row arrive, they need `BlockWeather`.
  - Weather: rain, storm, mist, light snow and heavy snow are stock weather status effects (`Outdoors/weather.yml`). Rain and storms water exposed crops and put out exposed fires. Storm lightning is not ported. PF2e weather penalties (concealment in fog, Perception in rain) are not added; mist and snow are visual.
  - Farming: `Content.Server/_CF/Farming/CFFarmingSystem`. The fork turns dirt or grass into a seedbed, seeds go in the seedbed, sun gives energy, water gives resource, and crops are picked by hand. All 10 CP14 crops are in `Entities/Structures/Farming/crops.yml`. Cucumber, pepper and tomato regrow. CP14's metabolizer prototype is folded into `CFPlant`: only water counts.
  - Whole vegetables are in `Entities/Objects/Farming/produce.yml`. If the Consumables port uses the same `CFFood*` ids, delete that file. Slices and cooked versions wait for cooking. Wild plants wait for the alchemy decision.
  - Seed (5 cp) and fork (2 sp) prices are ours: PF2e does not print them.
  - Tests: `Content.IntegrationTests/Tests/_CF/Outdoors/CFOutdoorsTest.cs`.
- [x] Currency and trading on PF2e coins.
  - Physical coins: `CFCopperCoin`, `CFSilverCoin`, `CFGoldCoin`, `CFPlatinumCoin` (`Entities/Objects/Economy/coins.yml`) with the `CFCoin` value. The purse stays the money the shop spends. Use a coin stack in hand to pocket it; the "Take out coins" verb on yourself turns purse money back into coins to hand over or drop. `CFCoinSystem` does both, and nothing is lost going either way.
  - Selling: `CFMerchantCounter`. Place goods on it, open it, and press Sell. `CFMerchantSystem` pays half Price into the seller's purse. `CFItemPrice.FullResale` marks what sells in full (Player Core: gems, art objects, raw materials). A bag with things in it, or anything without a price, stays on the counter. Browse the shop opens `CFShopWindow`.
  - Gems: ruby, emerald, sapphire and topaz from CP14's jewelry, as Player Core precious stones. Each grade's Price is a roll, so these use the average (250 gp, or 125 gp for topaz).
  - Left out on purpose: CP14's trade factions, reputation, contracts, shifting station prices and buy positions. PF2e has none of them. The currency converter and wallet are not needed, because the purse makes change.
  - Not done: raw materials have no price, so they cannot be sold. Giving stack prototypes a Price needs a shop fix first: the shop charges the listed price per prototype, whatever the stack holds, so `CFIronBar10` would be a bargain. Also not done: coin Bulk (1,000 coins is 1 Bulk), art objects, coins as monster loot, and looting a purse from a body.
  - The in-game shop is for merchants, who are players. The `CFMerchant` job grants `CFShopAccess`, which shows the character menu Shop tab and the counter's Browse button, and `CFShopSystem.Purchase` refuses anyone without it. Merchants can buy anywhere. Everyone else buys from them with coins, and can still sell at a counter. The lobby builder shop is separate and open to all.
  - Tests: `Content.IntegrationTests/Tests/_CF/Shop/CFEconomyTest.cs`.
- Part done, the rest is in `roadmap.md`: Creatures with PF2e stat blocks.
  - The stat block system landed. `CFCombatant` has `printedFortitude`, `printedReflex`, `printedWill`, `printedPerception`, `printedAttack` and `printedDamage`, which override the derived values. Before this, every monster rolled Commoner saves and Perception, whatever its YAML comment said.
  - Monsters set `diesAtZeroHp`: PF2e monsters die at 0 HP, with no dying track. Defeating one raises `CFCreatureDefeatedEvent`, and `CFExperienceSystem.AwardCreature` gives the killer's company XP from the adversary table. A kill by a hazard or the climate gives none.
  - Immunities and printed resistances are `CFResistances` entries in YAML. `expires` 0 means permanent. Immunity to a damage type is `amount: 9999`.
  - Seven creatures so far, in `Entities/Mobs/Creatures/creatures.yml`, all from Monster Core or Monster Core 2: giant rat, boar, grizzly bear, giant frog, hunting spider, sewer ooze, velociraptor. The kobold warrior has its printed saves now too. `CFCreatureTest` holds their numbers as a table.
  - Each creature has one Strike, its first printed melee Strike. A second Strike needs a way to pick between attacks. Special abilities wait for the planned better AI, not for this row. They are not in: Grab, venom and disease, Ferocity, charges, Pack Attack, Filth Wave, the ooze's extra acid. The stat block comment on each creature lists what is missing. Immunity to critical hits and precision is not in either.
  - Size does not change a creature's body yet, so the Large bear is a Medium body.
  - No CP14 AI differences: all use `SimpleHostileCompound` and hunt on sight. CP14's retaliating herd animals are not ported.
  - Skeleton Guard (Monster Core p. 312) landed as `CFMobSkeletonguardMelee` and `CFMobSkeletonguardRanged` on the stock skeleton body, in `Creatures/undead.yml`. They run on stock AI and work with GOAP. Stat blocks with hands and weapons now have what they need: `printedSkills`, `CFConditionImmunity`, `CFVoidHealing`, `CFArrowSupply`, and `CFArcherCompound` for bows. `importcreatures.md` is the guide. Tests: `CFSkeletonguardTest`. They have not been looked at in a real client.
  - Still to port: livestock (pig, sheep, rabbit, cat; there is no printed stat block for most), zombies (they need CP14's humanoid species), watchers, spineguard, flem, cackle, bone hound, mole, myconids, ice spectre, whistler, mosquito, hydra, fairy. CP14-only monsters need a PF2e creature of a matching level chosen first.
  - Boar and bear drop CP14's leather, scraps and bone when butchered. Meat waits for the Consumables port.
- [x] Adventure-site generator from `Demiplane` / `Procedural` with PF2e encounter levels, hazards and treasure.
  - `CFAdventureSiteSystem` (`Content.Server/_CF/Adventure`) builds a site on a new map from an `adventureSite` prototype (`Resources/Prototypes/_CF/Adventure/sites.yml`). `CFAdventureRift` generates it the first time someone touches it, sized to their company (highest level, number of characters), then becomes a two-way portal.
  - It uses stock dungeon layers, not CP14's. CP14's location and modifier selection, demiplane map UI, ghost-role danger, mana and religion hooks are all left out.
  - Encounters: `CFEncounterRules` holds GM Core's XP budget (Low 60, Moderate 80, Severe 120, Extreme 160, with the character adjustment) and Treasure by Encounter. `PlanEncounter` spends the budget on the site's creature pool, only within 4 levels of the party, and never overspends. The site's `encounters` list runs from the entrance to the deepest point by walking distance.
  - Treasure is all coin at each encounter's spot. GM Core splits it into permanent items, consumables and currency; items by level are not in yet.
  - Hazards are placed apart from the budget, within 4 levels of the party. GM Core counts them inside an encounter's budget.
  - Only the largest connected patch of floor is used, so nothing spawns on an island nobody can reach. `WallIn` encloses the floor.
  - One site so far: `Cave`. It has no rooms: stock room layouts need prefab atlas maps. Its pool tops out at level 3, so a party above level 7 gets no creatures. More creatures fix that.
  - `CFSiteWallStone` exists only because `CFWallStone` needs `CFRockStructural`, from the materials port on another branch. Swap it back after merging.
  - Generated maps are not cleaned up, so each rift's map stays until the round ends.
  - Every generated map calls `CFOutdoorsSystem.ApplySkyWorld`: CF is floating islands in the sky, so maps get breathable air (`CFSkyWorld`), never space.
  - Tests: `Content.IntegrationTests/Tests/_CF/Adventure/CFAdventureSiteTest.cs`.
- [x] Magic lanterns, magic vision and aura imprints reworked as light, detect magic and read aura.
  - `CFMagicSenseSystem` (`Content.Shared/_CF/Magic`). The three cantrips were popups; now they work. `IsMagical` means the `Magical` trait, the `CFMagical` tag, or any weapon or armor rune.
  - Light: an orb attached to a creature or object, as printed. Bright 20 feet and dim 20 more, 60 and 60 from rank 4, four at once, and casting it on your own light dismisses it. It cannot be placed on bare ground, and Sustain to move it is not in: recast instead.
  - Detect Magic: a 30-foot pulse that skips what you carry and your own lights. Rank 3 tells the strongest level. Rank 4 marks the source with a popup only you see. The illusion rank rule is not in, because no illusions exist yet.
  - Read Aura: whether the target is magical. Cast time is the usual action, not the printed 1 minute, and it reads one target at any rank. Identify Magic and its +2 are not in.
  - Lanterns: `CFEverburningTorch` (15 gp, level 1, magical), `CFHoodedLantern` (7 sp, no oil use), and `CFWallLamp` for mapping, on CP14's torch, crystal lamp and wall lamp art. CP14's crystal slots and energy are gone.
  - Left out on purpose: CP14's magic vision overlay, status effect and residue markers, and aura imprints. PF2e has no trail of spell residue or per-person aura signature.
  - The everburning torch entry found is the legacy Core Rulebook one. Check its numbers if a remaster entry turns up.
  - Tests: `Content.IntegrationTests/Tests/_CF/Magic/CFMagicSenseTest.cs`.

## DM scene tools (done)

- `CFNarateTrigger` (`CFNarate`) tells each player its text once when they come within `radiusFeet` (default 15 feet, 3 tiles). Only living bodies with a player count. It is a hidden marker: use `showmarkers` to see it.
- `CFSkillCheckTrigger` (`CFSkillCheck`) offers each skill in `skills` on right-click, rolls against `dc`, and shows the message for that degree. It has to stay visible, because players cannot right-click what they cannot see. Add `CFSkillCheck` to any object to put the check on that instead.
- Both are edited live with VV and save with the map. `skills` is one comma-separated line, because VV cannot add to a list. Perception works as a skill. `oncePerPlayer` (default true) limits each player to one roll. Who has heard or rolled is not saved, so a loaded map starts fresh.
- Signals: the ports `CFNarated`, `CFSkillCriticalSuccess`, `CFSkillSuccess`, `CFSkillFailure` and `CFSkillCriticalFailure` link to anything with a sink, such as doors. Code can listen to `CFNaratedEvent` and `CFSkillCheckRolledEvent` instead. Traps and monster spawners have no sink yet.
- Code: `Content.Server/_CF/Scene/CFSceneSystem`. Tests: `Content.IntegrationTests/Tests/_CF/Scene/CFSceneTest.cs`.
- The dev map cannot be saved cleanly: a stock `PackageDelivery` entity points at a missing entity.

## Phase 4 — Z-levels (verticality) (done)

Levels are separate maps in a stack, linked by stairs, ladders and holes. No engine change was needed.

- [x] `CFStationZLevels` (`Content.Server/_CF/ZLevels`) on a station: `defaultLevel`, `levels` (level number to map path), `levelHeightFeet` (10). `CFZLevelSystem` loads them on `StationPostInitEvent`.
- [x] `CFZLevelAutoPortal`: place an `AutoLink` stair or ladder and it builds the other end at the same position on the next level.
- [x] `cf_combinezlevels <map> <map> ...` (bottom up) stacks maps that are already loaded.
- [x] `CFZLevelMover`: ghosts and admins get Move up and Move down actions. It is one marked line in stock `observer.yml`.
- [x] Stairs (stone, wood, marble) are stock portals you walk through. `CFLadderDownAutoLink` and `CFLadderUpAutoLink` are clicked to Climb. `CFZLevelHole` is a drop. CP14 has no trapdoor, so there is none.
- [x] Test station: `forcemap CFZLevelTest` loads `Resources/Maps/_CF/ZLevels/ground.yml` and `cellar.yml`, with stairs, a ladder and a hole.
- [x] Falling: `CFFallRules` is Player Core: half the distance over 5 feet, up to 750, prone if it hurt. A fall carries on through levels with no floor below. Falling onto a creature, soft landings and Grab an Edge are not in.
- [x] Climb: one action and two free hands. `CFZLevelClimb.dc` 0 is a plain move, which the book allows for a trivial climb. With a DC it is Athletics; a failure stays put, and a critical failure falls on the way down or leaves you prone on the way up. The critical success bonus and being off-guard while climbing are not in.
- [x] Levels are separate maps, so area effects cannot cross (tested). Flanking sensors and NPC targeting use physics and lookups on one map, so they should stay on a level too, but they have no test of their own.
- `CFStationBasic` is a minimal CF station: jobs and spawning, with no Nanotrasen shuttles, cargo or evacuation. Use it for CF maps; `StandardNanotrasenStation` makes the stock map test try to dock an emergency shuttle. `CFSpawnPoint<Job>` markers are in `Roles/spawn_points.yml`, and every job on a map needs one.
- Only creatures fall through holes, not dropped items.
- Saved `CFStationZLevels.Maps` is runtime data: it is rebuilt on load, not saved.

## Phase 5 — Move actions off the HUD onto stock mechanics (done)

Goal: the custom HUD (`CFGameHud`, `CFRadialMenu`) stays, but holds as little as possible. Anything SS14 already has a mechanic for uses that mechanic, the way empty-hand right-click in combat mode is Shove or Disarm. The wheel is left with spells and the few activities that have no natural stock input. This carries on from Phase 2b, which moved the first set.

Rules for a move:

- Hook the stock event and resolve the PF2e check there (`CFManeuverSystem.OnDisarmAttempt` is the pattern). Stock mobs keep stock behaviour.
- Never charge a cost or roll in a "could I?" event. See `ss14rules.md`.
- No new key functions. Alt+Z stays reserved for Swashbuckler finishers.
- If no stock mechanic fits, use the verb menu. If that is clumsy in a fight, it stays on the wheel.
- Update the input table in `codebase.md` with each move.

`CFActionPool`, `CFActivity` and `CFRadialSlot` own the action economy. No new action component or budget system replaces them. Reactions stay automatic, resolved in code when triggered.

### 5a. Move these off the wheel

All agreed. Details are in 5b.

| Activity | Mechanic | Notes |
|---|---|---|
| Snagging Strike | Rider on a normal Strike | Fighters with the feat, when the other hand is free and the target is in reach |
| Exacting Strike | Rider on a normal Strike | Fighters with the feat, when the Strike has MAP. A plain miss does not add to MAP |
| Double Slice | Right-click with a melee weapon in each hand | One target in the arc. Two or more is a normal wide swing |
| Twin Feint | The same input, for Rogues | |
| Vicious Swing | Right-click wide swing with one target in the arc | Fighters with the feat. Two or more targets is a normal wide swing |
| Demoralize | Scream emote in combat mode | Targets the enemy closest to your aim |
| Feint | Point at an enemy in melee reach in combat mode | Class marks: the first point marks, the second Feints |
| Confident Finisher | Alt+Z | Already reserved |
| Inspire Courage | Start playing a held instrument | 1 action, repeating every 12 seconds. Stays on the wheel for a Bard without one |
| Tumble Behind | Rider on a successful Tumble Through | The enemy is off-guard afterwards. Needs Tumble Through first |

- [x] Move each activity and delete its `CFActivityKind` entry.
- [x] Remove `CFRadialSlot` from anything moved.

Staying on the wheel: spells, Rage, Flurry of Blows, Spellstrike, Sudden Charge, Point-Blank Stance, Leap, and Courageous Anthem for a Bard with no instrument.

### 5b. Agreed designs

Check Feint, Reposition, Aid, Battle Medicine and Courageous Anthem against Archives of Nethys before building them. Their details below are from memory.

**Fixes to existing activities**

- [x] Exacting Strike: the code also refunds MAP on a critical failure. The rule only refunds a plain failure.
- [x] Double Slice: the code strikes twice with one weapon. Use one weapon from each hand, combine the damage, apply resistances and precision once, and count it as two attacks for MAP.
- [x] Twin Feint: the code rolls a Feint and then one Strike. The rule is one Strike with each weapon, with the target automatically off-guard against the second.
- [x] Inspire Courage: the remaster name is Courageous Anthem. Check the numbers.

**Pointing in combat mode**

- An enemy in melee reach: Feint (1 action).
- An enemy further away: the class mark.
- A class with a mark pointing at an enemy in reach: the first point marks, the second Feints.
- The ground: Seek (1 action).
- Outside combat mode, pointing at anything but a markable enemy stays a free gesture. Companion orders keep working.

**Vicious Swing** (Fighters with the feat)

- One target in the arc of a right-click wide swing: Vicious Swing. 2 actions, one extra weapon die, counts as two attacks for MAP. `HeavyAttackEvent` carries the targets.
- Two or more targets: a normal wide swing, so nobody loses it.
- Fewer than 2 actions, or the flourish already used: a normal wide swing.
- Any melee weapon works. The feat has no two-handed requirement.
- A weapon with `Shove` or `Disarm` and no `Sweep` keeps its maneuver, so it cannot use Vicious Swing. This is accepted.

**Demoralize**

- Scream in combat mode. Combat mode turns the character to the cursor, so facing is the aim.
- Target: the enemy closest to the aim line, within a cone of about 45° and 30 feet, with line of sight. Skip enemies this character demoralized in the last 60 seconds.
- No valid target: a normal scream that costs nothing. Outside combat mode it is always a normal scream.
- Only the emote menu triggers it (`PlayEmoteMessage`), so typed roleplay and automatic pain screams never spend an action.

**Tumble Through**

- Keep pushing into an enemy for about half a second while running. It costs 1 action and rolls Acrobatics against their Reflex DC.
- Success: mob pushing is off for 1.5 seconds, so the creature slides through. Mobs only push each other, so this applies to every creature it meets, not just the target. Tumble Behind applies its rider here.
- Failure: the creature stops dead, and the action is spent.
- Walking never tumbles. Retries wait 1 second, as Escape does.
- Visual cue: the sprite spins a full 360 degrees when the action is performed, so it looks like a tumble and everyone nearby can see it happened.

**Double Slice and Twin Feint**

- Right-click with a melee weapon in each hand. Double Slice for Fighters, Twin Feint for Rogues.
- One target in the arc triggers it, as with Vicious Swing. Two or more targets is a normal wide swing.
- A Fighter with both Double Slice and Vicious Swing gets Double Slice while dual wielding.

**Inspire Courage**

- Starting a held instrument (`InstrumentStartMidiEvent`) costs 1 action, and it repeats every 12 seconds while the Bard plays.
- Stock instruments use a MIDI window, which is awkward in a fight. If that feels bad in play, this stays on the wheel.

**Leap**

- A stock throw (`ThrowingSystem`), as the stock jump does, on a new `CFActLeap` wheel action. `JumpAbilityComponent` is locked to its own system and could not follow Speed.
- Distance from Speed: 2 tiles at 15 feet or more, 3 tiles at 30 feet or more, no jump below 15 feet.
- It stays on the wheel, because a jump needs its own input and we add no keys.
- The vertical part of Leap is roughly covered by the stock climb onto tables, so it needs nothing new.
- A creature in the air clears a `CFZLevelHole`, and falls if it lands in one.

**Stealth states** (remaster rules)

- A new component on the sneaker holds Observed, Hidden or Undetected for each creature. GOAP reads the same data later.
- Other players: hide the sprite per viewer on the client. A modified client could reveal hidden players, and that is accepted.
- A cover calculation is needed, since none exists. Darkness as concealment can wait for the `Light` layer (6c).
- Build now: the states, player Sneak and Hide, and a filter in `ApplyHostileQuery` so NPCs ignore undetected targets.
- Wait for GOAP: NPCs searching, following scent and using Seek (6b, 6e). Until then NPCs simply lose an undetected player.

**Sneak**

- Walk while hidden from or undetected by at least one creature.
- One Sneak every `RegenSeconds` of walking, costing 1 action. The pool refills at that rate, so a creature can keep sneaking but has nothing to spare. Stock walking is already about half the run speed.
- At the end of each stretch, a secret Stealth check against the Perception DC of each creature it was hidden from or undetected by at the start.
- Success: undetected. Failure: hidden. Critical failure: observed, but an invisible creature that was hidden stays hidden.
- No cover or concealment from a creature at the end: automatically observed by it, with no roll. Cover throughout gives +2, greater cover +4. Take Cover does not count.
- Anything except Hide, Sneak or Step makes it observed, including running. A Strike keeps the target off-guard for that attack first. Speaking or a loud noise drops undetected to hidden.

**Hide**

- A verb on a wall, crate or other cover: 1 action, a secret Stealth check against each observer's Perception DC. Cover gives +2, greater cover +4.
- Stepping into a closet counts as hiding, which fits the closet search in 6e.

**Treat Wounds and Battle Medicine**

- A new healer's toolkit, because stock `HealingComponent` heals SS14 damage and not CF HP.
- Outside combat mode: Treat Wounds, with a do-after of about 30 seconds instead of 10 minutes. DC 15, or 20, 30 and 40 at higher Medicine ranks.
- In combat mode with the feat, use it in hand: Battle Medicine on the nearest hurt ally, for 1 action. A combat-mode click is always a swing, so it cannot be a click. Eight backgrounds grant the feat (`battleMedicine: true`).

**Seek**

- Point at the ground in combat mode: 1 action, a secret Perception check in a 30-foot area.
- Against Stealth DCs: on a success undetected becomes hidden and hidden becomes observed. On a critical success both become observed. It also finds hazards.
- Plain examine is free and constant, so it cannot cost an action.
- This took one marked upstream line in `SharedPointingSystem`.

**Other actions**

- Reposition: stock `MovePulledObject` (Ctrl+right-click) on a grabbed enemy rolls Athletics and costs 1 action. On a critical failure the target shoves the user 5 feet.
- Recall Knowledge: 1 action, a secret roll, and the result appears in the examine text. It uses the character's best of Arcana, Nature, Occultism, Religion and Society, because creatures have no trait data yet. One try per creature.
- Aid: spend the action now, and it resolves on the ally's next check within 6 seconds. It rolls the helper's Perception against DC 15, since nothing says which skill fits.
- Create a Diversion: built like Demoralize. One Deception check against nearby enemies, 4 harder for a minute after any attempt on them. The character is hidden from those it beats for 6 seconds, unless it Hides or Sneaks.
- Step: nothing to build yet, because Reactive Strike only fires on ranged shots and spells, not movement. Walking counts as Step for stealth.

### 5c. New actions, built on stock mechanics from the start

All agreed. None of these exist yet. Details are in 5b.

| Action | Mechanic |
|---|---|
| Sneak | Walk while hidden or undetected |
| Step | Walking. Nothing to build yet |
| Reposition | Stock Ctrl+right-click on a grabbed enemy |
| Treat Wounds, Battle Medicine | Use a new healer's toolkit on a creature |
| Recall Knowledge | Examine tab on a creature (`GetVerbsEvent<ExamineVerb>`) |
| Seek | Point at the ground in combat mode |
| Hide | Verb on cover, or step into a closet |
| Aid | Verb on an ally |
| Create a Diversion | Whistle emote in combat mode |
| Tumble Through | Run into an enemy and keep pushing, then spin the sprite 360 degrees (5b) |
| Leap | Stock jump, on the wheel (5b) |

Ready is cut: it has no meaning without turns.

- [x] Build each action.

### 5d. Shared entry points

Needed by the AI (6b), which has to do everything a player does without clicking.

- [x] One `CanUse` path per action, for the AI planner and the wheel (`CFManeuverSystem.CanUse` with a `CFBasicAction`). Wheel actions still use `ActionAttemptEvent`, and only Leap reads `CanUse` so far.
- [x] A way to call each moved action directly, so an NPC does not need a fake input event (`TryUse`, `CFStrikeSystem.TryFeatStrike`).

### 5e. Finish

- [x] Players can find the moved actions, since the wheel no longer lists them: the guidebook page `CFActions`.
- [x] Integration tests: `CFFeatSwingTest`, `CFSocialActionTest`, `CFTumbleTest`, `CFStealthTest`, `CFSupportActionTest`, and the leap cases in `CFZLevelTest`.
- [x] Update `ss14.md` (HUD section) and the input table in `codebase.md`.

## Phase 6 — AI: GOAP on top of SS14 HTN (done)

SS14 NPCs run on HTN (`HTNComponent`, compound/primitive tasks, operators, `NPCBlackboard`), with `NPCSteeringSystem` doing context steering (interest/danger directions) over the pathfinder. We **extend, not replace**. Stock HTN stays the AI for simple enemies, including most CF mobs. GOAP is opt-in per prototype, for advanced enemies: mini bosses and special encounters. Do not put it on ordinary CF mobs. All of this is server-only, under `Content.Server/_CF/GOAP/`.

Depends on the Phase 5d entry points and on `CFItemUseComponent` (6b). Steering (6a) and influence maps (6c) depend on neither and can start early.

### 6a. Steering behaviours (can start now)

Implement as contributors to SS14's context steering (interest/danger maps, hooked through the steering event) instead of a second movement system, so collisions, doors and pathing keep working.

- [x] `CFGOAPSteeringComponent`: list of weighted `CFGOAPSteeringBehavior` data entries, swapped by GOAP actions.
- [x] Behaviours: **Seek**, **Flee**, **Arrive** (slow inside radius), **Wander** (jittered circle target), **Pursuit** (seek predicted position from target velocity), **Evade** (flee predicted position), **Cohesion** (toward faction-mates' centre; separation as its negative), **Obstacle Avoidance** (feeler rays into the danger map; reuse what SS14 already does, add only what's missing).
- [x] **Vector Field Follow** (used by 6c): interest toward the strongest neighbouring influence cell.
- [x] Test: pursuit intercepts a moving dummy faster than seek; flee never ends inside a wall.

### 6b. GOAP planner

- [x] **AI item tags**: `CFItemUseComponent` describing what an item does in planner terms (`Heals`, `Weapon`, `Ranged`, `Light`, `Key`, `Thrown`, `Consumable`) plus the action that uses it.
- [x] **World state**: compact fact set (bitflags + a few numbers) built each think tick by **sensors**: `HealthFraction` (from HP/dying/wounded, the "health tag"), `HasWeapon`, `HasRangedWeapon`, `HasHealingItem`, `TargetVisible`, `TargetInReach`, `KnowsLKP`, `AtLKP`, `ScentNearby`, `AlliesNearby`, `ActionsLeft`, conditions (frightened, off-guard, prone).
- [x] **Goals** (`cfGoapGoal` prototypes) with priority from a utility curve over world state: `KillTarget`, `StayAlive` (rises as health falls), `Heal`, `ArmSelf`, `HuntTarget` (lost contact), `Investigate`, `Regroup`, `Idle`.
- [x] **Actions** (`cfGoapAction` prototypes): preconditions, effects, cost, and an executor. Executors are either a player action through the 5d entry points (Strike, Raise a Shield, Demoralize, cast a spell, drink potion) or a movement/utility step (`FindItem`, `PickUp`, `UseItem`, `MoveTo`, `FollowScent`, `GoToLKP`, `CheckNode`, `Flee`, `Wander`). The 5d `CanUse` checks become extra preconditions automatically.
- [x] **Planner**: backward A* from the goal, depth and node limits. It runs inline, not as a job: GOAP NPCs are rare and a plan is small, so a job is not worth it. See the timing test. Replan on: goal priority change, plan step failure, or a watched fact flipping (target seen, health threshold crossed).
- [x] **HTN bridge**: `CFGOAPOperator` (HTN primitive operator) that owns plan → execute → replan. A CarpFinder NPC's root compound task can be "GOAP" alone, or HTN with GOAP inside one branch (e.g. HTN handles idle/patrol, GOAP takes over in combat).
- [x] **Item behaviours**: `FindItem(tag)` queries nearby entities with `CFItemUseComponent`, scored by distance and usefulness; `UseItem` runs the item's action.
- [x] **Personality** via weights, not code: `cfGoapProfile` scales goal priorities and action costs and overrides steering parameters (see 6f).
- [x] NPC detection reads the stealth states from 5b: `TargetVisible` is Observed, `KnowsLKP` covers Hidden, and NPCs use Seek. NPCs can Sneak and Hide through the 5d entry points. This replaces the `ApplyHostileQuery` filter.
- [x] Tests: low-health NPC with a potion drinks it; unarmed NPC picks up a nearby weapon before attacking; coward flees at 30% HP.

### 6c. Influence maps (heatmaps)

Players and other tracked entities "paint" values onto an invisible grid as they move. The tracking AI doesn't pathfind to them: it uses Vector Field Follow, turning toward the highest neighbouring intensity until it reaches the epicentre.

- [x] `CFGOAPInfluenceMapComponent` on each grid (one per map, so Z-levels stay separate). Cell = 1 tile (one 5-ft square). Stored in chunks (e.g. 8×8, like SS14 pathfinding/atmos chunks), allocated only where something has painted.
- [x] `CFGOAPScentEmitterComponent`: layer (scent, noise, light, threat), strength, radius. Sneak/Hide and conditions scale it (invisible ≠ scentless).
- [x] Layers: `Scent` (trail), `Noise` (one-shot spikes from melee, shots and speech), `Ally`, `Threat`.
- [x] `CFGOAPInfluenceMapSystem`: paint on movement, decay over time, optional cheap diffusion, updated at a fixed rate (e.g. 4 Hz), and only in chunks near awake NPCs. Data lives on the component; the system holds no state.
- [x] Paint only walkable cells, so following the gradient naturally goes around walls the way the target did.

### 6d. Temporal scent vectors

Each cell (breadcrumb) also stores **when** the target was there and **which way it was heading**.

- [x] Cell data: `Intensity`, `LastVisit` (`TimeSpan`), `Heading` (normalised velocity), `Emitter` (entity or faction id).
- [x] On reaching any cell with scent, the NPC steers along `Heading` (blended with the gradient) instead of crawling the trail backward or toward older crumbs.
- [x] Freshness rule: prefer cells with newer `LastVisit` over higher intensity when they disagree, so it never follows an old loop.

### 6e. Wayfinding / topological hint nodes

Mappers place hidden **Investigation Nodes** near corners, hiding spots and doorways. When the AI reaches the LKP (last known position) and loses the target, it checks nearby nodes.

- [x] `CFGOAPInvestigationNodeComponent` marker entity (invisible in game, visible in mapping mode): `Weight`, tags (`Closet`, `Corner`, `Doorway`, `Corridor`, `Vent`, `Stairs`, `Shadow`), optional linked nodes (graph edges). `Shadow` can be placed by mappers or auto-flagged when the `Light` layer is low at the node.
- [x] LKP stored on the blackboard when sight is lost, with the target's last heading (from 6d).
- [x] Node query from LKP: candidates within range and reachable, score = base weight × distance falloff × direction match with last heading × scent bonus × "not checked recently" (per-NPC and per-pack memory). Pick by **weighted random** (`IRobustRandom`) so it isn't predictable.
- [x] `CheckNode` GOAP action: go there, Seek (Perception vs Stealth DC), open containers/closets tagged `Closet`, mark the node checked, then pick the next node (graph edge or new query). Give up after N nodes → `Idle` / return to patrol.
- [x] Packs split nodes between members instead of all checking the same closet.
- [x] Mapping guide in `.agent/` on where to place nodes.

### 6f. Personalities

A personality is one `cfGoapProfile` prototype. It changes numbers (steering parameters, behaviour weights, goal priorities, node scoring) and adds a few **reflex rules** (trigger → behaviour swap). No personality gets its own system or code path.

- [x] `cfGoapProfile` fields: `MaxSpeed` multiplier, per-behaviour weights and parameters (Arrive deceleration radius, Wander jitter/radius, avoidance spacing, separation, wall-follow), LKP dwell time, LKP keep-out radius, node tag multipliers, goal priority multipliers, reflex list.
- [x] `CFGOAPReflex` data: trigger (`NoiseNearby` from the Noise layer, `AllyDown`, `HealthBelow`, `TargetSeen`, `LightOn`) → action (swap behaviour A for B, add weight, force a goal) with a duration.

| Personality | Hunting (6c–6e) | Steering | Reflexes / goals |
|---|---|---|---|
| **Brave** | Checks the LKP briefly (short dwell), then pushes forward along the scent trail using the stored heading. Node search is a fallback only | High `MaxSpeed`; low deceleration on Arrive (barely slows before contact); zero obstacle-avoidance spacing (pushes past objects and through crowds) | `StayAlive` priority scaled down; `HuntTarget` and `KillTarget` scaled up |
| **Stealthy** | Prefers nodes tagged `Shadow`; avoids crossing high-`Light` cells; approaches the LKP along edges | Low `MaxSpeed`; high wall-hugging weight (keeps to walls and edges) | Uses Sneak/Hide actions while moving; may wait at a `Shadow` node to ambush |
| **Cowardly** | Keeps a large keep-out radius around the LKP (watches it from a distance instead of standing on it) | High Wander jitter (erratic movement) | `NoiseNearby` flips Arrive to Flee for a few seconds; `StayAlive` scaled up; flees at a higher health threshold |

- [x] Note on "wall hugging": classic Separation pushes away from neighbouring agents. Hugging walls is a separate **wall-follow / edge-attraction** term (interest toward the nearest wall direction, kept at a small offset). Stealthy uses high wall-follow; Separation can still be used to keep a stealthy pack spread out.
- [x] Brave's zero avoidance spacing applies to objects and agents, not walls (otherwise it gets stuck on geometry). Collision still stops it; it just doesn't steer away early.
- [x] Profiles can be combined with a base creature profile (e.g. "wolf" + "brave"); multipliers stack.
- [x] Tests: brave prefers the trail, cowardly stays out of its keep-out radius and flees a noise, stealthy prefers the `Shadow` node.

### 6g. Verify

- [x] Scenario test on the dev map, per personality: the target goes undetected, and the NPC goes to the LKP, checks the node, Seeks, and finds them. Cowardly is tested on its keep-out radius instead.

### 6h. Encounters

- [x] `CFEncounterSpawner`: spawns an `encounter` when a player's character comes within `triggerFeet`. As an aghost, VV `encounter`, `mobs` (ids separated by commas), `count`, `triggerFeet`, and `armed` to spawn again.
- [x] The encounter gives any mob the GOAP brain at spawn, so plain kobolds elsewhere stay on stock HTN.
- [x] `SmallGroup`: 4 kobolds on one target, in paired slots on opposite sides. `routAt: 0.5` breaks them at half, and they run, then Hide once they cannot see an adventurer.
- [x] Signals on the spawner, for the multitool: `CFEncounterMemberDied`, `CFEncounterRouted`, `CFEncounterCleared`.
- [x] A routed creature runs along a safety map, so it goes round corners toward open space. It does not look for cover or a node to hide at yet.
- [x] Advanced NPCs move by feelers instead of A*: a three-ray fan along their heading, cast out to `feelerSight` (12 tiles), with obstacles counting inside `feelerLength` (2.5) for the center ray and `feelerSideLength` (1.75) for the side rays at ±30°. Rays touch walls, anything with `CFDangerousTile` (lava, liquid plasma, chasms), and enemies they can detect. An enemy adds interest when chasing and danger when fleeing. Stock pathing is unchanged, and simple mobs still use it.
- [x] Safety circle (`panicRadius`, 0.42 tiles, and wider when closing on a dangerous tile at speed): a wall, dangerous tile or mob inside it overrides all other steering and snaps the NPC away, with no easing. The current target and enemies being chased do not count. It covers what rays miss, such as sliding alongside lava.
- [x] The flanking reach sensor feeds the same vectors through `CFFlankingSystem.EnemiesInReach`, covering enemies beside and behind the fan.
- [x] Feeler safeguards: blocked straight ahead, it turns to the freer side, or right if they balance, so a flat wall cannot freeze it. Danger eases over a few frames so it does not shake along a wall, and snaps at once when something is close. Length scales with speed against `feelerFullSpeed`, so a fast creature sees further, down to `feelerMinLength`.
- [x] Cooperative steering with flow fields. While a ray to the goal is clear, an NPC heads straight there. When it is blocked, the group's navigator (its first living member, or a lone NPC itself) builds a `CFGOAPFlowField`: arrows on each tile out to 20 tiles, with walls and `CFDangerousTile` blocked. Every member reads the arrow under its feet and blends it with its feelers. There is no path to fall behind on, and chasing uses no A*. Long trips to a known place do, see 6l.
- [x] Doors and glass in a field. A closed door, a locked door and a window are slow tiles (`CFGOAPSteeringSystem.Obstacles`), not blocked ones, so a short detour still wins. An arrow leading into one makes the creature open it if it has hands and the door is unlocked, and Strike it otherwise. A smashed window leaves a frame to climb, and a smashed door a frame to break.
- [x] Patience. `smashPatience` on a `cfGoapProfile` is how many seconds it keeps at one door or window: Default 20, Brave 40, Stealthy 10, Cowardly 5. Then the obstacle is off the field for 60 seconds, shared by the group. With no other way, the `NoWay` fact stops `Close`, `TakeFlank`, `GoToLkp` and `CheckNode` for that long.
- [x] Sight through glass. Plain windows have no `Occluder`, so a creature sees through them and heads for the target. `TargetInReach` and `AtFlank` need a clear ray, so it does not swing at someone behind glass.
- [x] With no node or scent, a creature that loses sight goes to the last known position, looks around, and forgets it (the `Investigate` goal).
- [x] Fleeing goes through doors it can open. It treats glass and locked doors as walls.
- [x] Shooting. `Shoot` action and step, the `HasShot` fact and `CFGOAPShotSensorSystem`. See "Shooting" in `goap.md`.
- [x] Perf check: 30 NPCs + 10 emitters thinking once, on the dev map, cost logged by `ThinkingStaysCheapWithAFullEncounter`.

### 6i. Our own structure (6 November 2026)

GOAP moved from `_CF/NPC` to `_CF/GOAP`, with `CFGOAP` names. See `goap.md`. The build and the YAML linter pass. The AI tests ran clean on 6 November 2026, apart from the lava failure in 6k.

A port of Crystall Edge's GOAP came first. Its code is all rights reserved, so that port was thrown away before any commit, and this was rebuilt from our own commit `46ffa81597`. Do not copy from `crystall-edge`.

- [x] Folders and `CFGOAP` names, on the server, in Shared, in YAML and in tests.
- [x] Sensors in their own systems, handling `CFGOAPSenseEvent`, replacing the single `Sense`.
- [x] Steps in their own systems, replacing the `switch` on `CFGOAPStep`.
- [x] Navigation state and code moved from the brain to `Steering/`.
- [x] New: being hit tells a creature where the attacker is, through a new `CFDamagedEvent`. A cornered creature fights back. `Investigate`.
- [x] Fleeing uses a safety map on the flow fields instead of steering straight away. A routed group shares one.
- [x] Fixed: a node more than a tile from the last known position could never be reached. Walking to it flipped `AtLkp` and sent the creature back.
- [x] Fixed: the planner's heuristic counted each missing fact as a flat 1, though costs go below that.
- [x] Flow fields stop at the edge of a grid.
- [x] Run the AI tests and fix what they find. New tests: `ItStaysOnItsWayToANodeAwayFromTheLastKnownPosition`, `WithNothingElseToGoOnItLooksAtTheLastKnownPosition`, `PainSaysWhereAnUnseenAttackerIs`, `ACorneredNpcFightsBack`, `EveryKindOfStepHasASystem`, `TwoCheapStepsBeatOneDearOne`, `ARunnerGoesThroughTheGapAndNotIntoTheNearestCorner`, `ADeadEndIsAsSafeAsItGets`.

### 6j. Tactics (6 November 2026)

Weights for how a creature fights, reading what Baldur's Gate 3's AI weighs and doing it in PF2e terms. See `goap.md` and `goap-weights.md`. It builds and lints. **Not tested in a fight.**

- [x] `goapTactics` prototypes, with `parent`: `Default`, `Beast`, `Mindless`, `Brute`, `Skirmisher`, `Tactician`.
- [x] Target choice by score instead of the nearest enemy.
- [x] What an action is worth feeds the planner's costs. Demoralize, Feint and `HoldBack` compete with Strike.
- [x] `TryPreviewMelee`, `TryGetDemoralizeCheck` and `TryGetFeintCheck`, so the AI reads the same numbers as the real roll.

### 6k. Furniture, and a run on the maze map (6 November 2026)

- [x] `npc_test_map.yml` has air: `MapAtmosphere`, and tile air on all 769 tiles. `fixgridatmos` changes nothing further.
- [x] Flow fields see every hard body on a tile, so tables, crates and barrels are no longer open floor. See "Getting past things" in `goap.md`.
- [x] Climbable things are climbed or gone around. A creature that cannot climb goes around.
- [x] What it cannot hurt is a wall to it, which covers grilles. It walks round instead of swinging at them.
- [x] The safety circle leaves a working creature alone, so it is not thrown off a climb.
- [x] Melee reach goes over a table.
- [x] A flank slot it cannot stand on is `FlankBlocked`, so it walks in and Strikes instead of circling.

Found by that run, not fixed:

- [x] Mobs bounced off corners in one-tile corridors. Fixed in 6l.
- [x] `ARealGroupRunsWhenHalfAreDown` fails now and then, about 2 of 30 runs, and that rate is accepted: kobolds are not that smart. It used to be lava deaths with full CF HP, which 6m fixed. Now a kobold that caught fire stops to roll instead of running. See `ai-testing.md`. Do not chase it.

### 6l. Corners, routes and going home (6 November 2026)

Corner bouncing, routes and going home are done and tested. Still to do: see them in a real client.

Fixed and tested:

- [x] `Flow` aims at the middle of the next tile, not along the arrow, so a creature stays on a corridor's centre line.
- [x] On a tile with an arrow, walls and anchored furniture no longer trigger feelers or the safety circle (`OnArrows`). The circle is 0.42 and a corridor's middle is 0.5 from each wall, so 0.08 of drift threw it back. Lava, mobs and loose bodies still count. Rays still stop at walls, so nothing is felt through one.
- [x] Stock `CollisionAvoidance` is skipped for GOAP mobs, in one marked line in `NPCSteeringSystem.cs`. With no stock path its filter is skipped, so a cable or wall light underfoot counted as a blocker at full weight and froze the mob.
- Results on `npc_test_map`: `FarAcrossTheMaze` reaches the hero in about 12 s (59 s at best before). `GrilleOnly` went from 0 HP dealt to 208.

Routes and going home. Tests: `CFGOAPMazeTest`, and the `ARoute...` cases in `CFGOAPPlannerTest`.

- [x] `CFGOAPFlowFieldBuilder.TryFindRoute`: a tile A* on `TileCost`, with the same step rules as the arrows. Our own instead of stock's, because stock paths cross lava, are async and need the path graph, and ours gets doors, tables and given-up obstacles for free.
- [x] `CFGOAPSteeringSystem.Travel` (`.Travel.cs`): walks a route on short field legs. It takes a place, never a creature, so it cannot find a player. `GoToLkp` and `CheckNode` now use it, so **those two steps changed as well**.
- [x] `CFGOAPComponent.Home`, `HomeRange` (8), `GoingHome`, `TryGetHome`, `AtHome` fact, `CFGOAPHomeSensorSystem`, `CFGOAPGoHomeStepSystem`, and the `GoHome` goal (priority 1.5) and action. Home is set at the first `Start`, relative to the grid. A group member goes home on its own field.

Running it found two bugs, both fixed:

- [x] A creature with a clear line home never set off. `Navigate` keeps the steering it has while the line is clear, and `GoHome` starts none. All 24 runs of a sweep ended stuck like this. `HeadingElsewhere` fixes it, and also moves a walk on to the next stop of a route.
- [x] A creature within 14 tiles of a sealed place stood still for good. An empty field looked like one not built yet, so no route was tried and `NoWay` was never set. This also applied to `GoToLkp` and `CheckNode`.

The decision log was also lost in the move between computers, because `.gitignore` drops any `Debug/` folder. It is rebuilt in `DecisionLog/` with new names. On 9 November 2026 the old `Debug/` copy was merged into it and those folders were removed.

`.agent/ai-trial/CFGOAPMapTrial.cs.txt` is the throwaway trial with `CorridorProbe`, `TileProbe`, `FarAcrossTheMaze` and `GrilleOnly`. To use it, copy it to `Content.IntegrationTests/Tests/_CF/GOAP/CFGOAPMapTrial.cs`, set `CF_TRIAL_OUT` to a folder, and remove it again afterwards. Always check the build printed `Build succeeded` first: `dotnet test --no-build` happily runs the old binary after a failed build.

### 6m. Fire (6 November 2026)

- [x] Stock fire, lava and body heat land on CF HP, through `CFStockHeatSystem` (`Content.Server/_CF/Temperature`). No upstream file changed. Resistances, dying and death all work on it now. It takes hold of a creature with `CFCombatant` and `MobState`, for damage that is only Heat and has no origin. Tests: `CFStockHeatTest`.
- [x] `HeatPerHp` is 5 and damage lands once a round. Both are our choice: 2 fire stacks come to about 1d6 a round.
- [x] `PutOutFire` goal and action. It calls `FlammableSystem.Resist`, the same thing as clicking the on fire alert, so it drops what it holds and may take several rolls. It comes before every other goal, and it waits until the creature is off lava. Tests are in `CFGOAPMazeTest`. A kobold with 5 stacks was out in about 5 seconds, against 50 left alone.

## Languages (6 November 2026)

- `language` prototypes hold the name, rarity and nonsense syllables. Rarity is from Archives of Nethys.
- A creature without `CFLanguages` speaks plainly and understands everything, so stock mobs, machines and ghosts need nothing.
- The Speak verb on yourself changes language, because no new keys are allowed.
- `ChatSystem` wraps each line twice and picks per listener. Speech and all three whisper ranges do this. The speech bubble follows, because it comes from the same message.
- Garbling hashes each word, so a word always sounds the same and a listener can start to recognise it. Common is not tagged in chat; other languages show as `(Goblin) hello` to those who know them.
- Picks come from a client, so `EnsureValid` trims them and `CFLanguageRules.Known` rechecks rarity and count at spawn.

## Grants (6 November 2026, Step 1a)

- A character option now says what it gives in YAML: a `grants` list on `cfFeat`, `CFClassFeature`, `cfHeritage`, `cfAncestry` and `cfBackground`. `CFCharacterFeatureSystem` runs each list once, keyed by a source name, so gaining an option twice does nothing the second time.
- Both `switch` statements are gone from `CFCharacterSpawnSystem`. Classes went onto the level 1 feature that names the ability. Bard's anthem sits on Composition Spells, and the Alchemist's potions on Alchemy. `battleMedicine: true` on 8 backgrounds became a grant.
- `CFCombatant.ProficiencyRanks` holds ranks raised in play. `CFDefense` reads the better of that and the class's rank.
- Rolls used to reduce a rank to trained or not, so an Expert rolled as Trained. Saves, Strikes and AC now use the real rank. **This changed numbers:** a Fighter hits at +2 from level 1 and has +2 Fortitude and Reflex, and other classes with expert ranks gain the same. The sheet already showed the real rank, so it and the rolls now agree.
- `CFBonusStat.SkillChecks` was written and never read, so skill bonuses and the penalty from a condition did nothing. Every skill check now reads it through `CFConditionSystem.SkillBonus`.
- There were two save rolls. `CFEffectSystem.RollDefense` is gone and everything uses `CFConditionSystem.RollDefense`. Spell saves now take stupefied, and a Sanctuary save takes bonuses.
- Demoralize resists as Emotion, Fear and Mental, and Feint as Mental, so bonuses against those raise the DC.
- A timed resistance used to merge into a lasting one and end it, and a timed bonus could merge into an equipment bonus. Lasting entries are now left out of that merge.
- Stabilize by Medicine counted neither skill increases nor bonuses. It uses `CFDefense.SkillModifier` now.
- `CFGrantMovement` was dropped on request: climbing is `TBA`.

## Long rest and HUD bonuses (6 November 2026)

- Falling asleep in a bed is a long rest, through `SleepStateChangedEvent`. A bed is a strap with `HealOnBuckle`, which is what gives the Sleep action. Sleeping anywhere else does nothing. The Sleep spell is a knockdown and raises no such event.
- The spell pips moved inside the action panel, so the HUD no longer hides them on a narrow screen and `SetViewportWidth` is gone.
- The HUD and sheet now show bonuses. `CFBonusesComponent.Bonuses` reaches the client, and `ArmorClassModifier` and `DefenseModifier` are the sums rolls use too, so they cannot disagree.
- Raging's -1 AC only counted against Strikes. It now counts against spell attacks too, because both use `ArmorClassModifier`.
- `SickenedValue` is new, only so the HUD can name the condition. Its penalties are still timed bonuses.

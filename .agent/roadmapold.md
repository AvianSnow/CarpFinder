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

## Moved out of `roadmap.md` on 15 November 2026

Everything ticked off between 6 and 15 November 2026, under the headings it had in `roadmap.md`. The open items that sat beside these stayed there. "Not seen in a real client" notes stayed there too.

### Do first: thrown healing potions, 15 November

Asked for on 15 November 2026. Built the same day. Tests: `CFSmashPotionTest`.

- [x] A thrown healing potion breaks. On a living creature other than the thrower it heals them as if drunk, friend or enemy, with no roll and no action cost beyond the throw (`CFSmashPotion`, `CFSmashPotionSystem`).
- [x] On a wall, an object or the floor it leaves a puddle that keeps the potion's healing for 5 minutes (`CFHealingPuddle`). A hurt creature lying prone on it is healed and the puddle is gone. Alt-click, Drink, does the same after 2 seconds. Mopping it up loses it. A second potion on the same puddle does not add up.

### Do first: spell visuals, 15 November

Asked for on 15 November 2026: areas that can be seen. Built the same day. Tests: `CFAreaFillTest`, `CFToxicSpewTest`.

- [x] `CFAreaFill` on any area source fills its tiles with a tile effect or puddles, nearest first (`CFAreaFillSystem`). It took over the breath's liquid flow: `CFBreathFlowSystem` is gone, and Toxic Spew uses `CFAreaFill` with `rolls: WhenFilled`.
- [x] Fire: Fireball, Breathe Fire, Floating Flame and Earth's Bile leave flames for 2.5 seconds (`CFFillFlame`). They light the room and burn nothing. Swap the tile for `CFFire` for fire that burns and spreads.
- [x] Grease leaves slick puddles for a minute. Everyone on them rolls as it is cast, then whoever moves faster than a walk, at most once every 3 seconds. A mopped puddle leaves the area.
- [x] Lightning Bolt (sparks), Gust of Wind and Gale Blast (dust), and motes in the spell's colour for Bless, Bane, Calm, Telekinetic Rend, Discomfiting Whisper, Nymph's Grace and Garden of Healing.
- [x] `!type:CFFillEffect` leaves something seen around one hit: Acid Splash an acid puddle, Hydraulic Push water. Meant for alchemical bombs too.
- [x] A spell with a fill stops at walls, as the breath does. Spells without one still pass through them.

### Do first: GOAP movement, 13 November

Asked for on 13 November 2026: mobs moved twitchily. Built the same day. How it works is under "Moving smoothly" and "Companions" in `goap.md`. Tests: `CFGOAPMovementTest`, `CFGOAPSteeringMathTest`.

- [x] Mobs aim between stock's 12 steering directions, so walking at an angle no longer zigzags (a marked edit in `NPCSteeringSystem`).
- [x] Arriving slows down and stops, and a stopped mob stays put until its goal moves off. A mob that had stopped used to walk on, because stock keeps old interest.
- [x] Idle: pick a spot inside the home range, walk there slowing down, rest 1.5 to 4 seconds, pick another.
- [x] Attacking: closing slows down and stops just out of the target's body, inside reach. Melee is ready while closing, so it swings the moment it is in reach, and while striking it walks with a target that moves instead of waiting for the next think.
- [x] Following: a companion keeps 1.5 tiles behind its master, walks at their pace, and stops behind them when they stop. Several fan out to the sides.
- [x] Pointing is an order. At a creature: the companion is told where it is and is not leashed back while it lives. At the master: it drops the fight and ignores enemies until it is back.
- [x] The safety circle pushed creatures off anything within about 1.67 tiles of another combatant, because it measured to the flanking sensor. It measures to bodies now.

### Do first: chasm edges, 13 November

Asked for on 13 November 2026. Built the same day. Tests: `CFChasmEdgeTest`.

- [x] Touching a chasm no longer drops a creature. Stock's step trigger counted the 2.5 tile flanking sensor as the body, so one pixel over the edge was a fall. A creature now slips only once its centre is over the hole (`CFChasmEdgeSystem`, `CFChasmEdge` on `FloorChasmEntity`).
- [x] A slip pops up "Falling...", stops the creature, and makes it weightless for 4 seconds, so it drifts and can steer back onto solid ground. Getting its centre off the hole ends the slip.
- [x] After 4 seconds still over the hole, it spends its reaction on Grab an Edge: a Reflex save against DC 10 (untrained). A success puts it on the nearest solid, unwalled tile within 10 feet with nothing in the way. A failure, no reaction left, being unconscious, or no ledge in reach falls as stock does. Players and monsters alike; items still drop at once.

### Do first: Bard fixes, 13 November

Asked for on 13 November 2026. Built the same day. Tests: `CFBardTest`, `CFSocialActionTest`, `CFAuraTest`.

- [x] Auras are drawn as a ring on the owner's drawn position (`CFAuraOverlaySystem`), so they glide with the owner instead of jumping a tile at a time. Who is inside is the same circle on the server: a creature counts when its centre is within `CFAuraSystem.Radius` (the size, plus half the owner's square), not by tiles.
- [x] Courageous Anthem is an aura on the Bard (`CFAuraNeeds.Anthem`, 60 feet, the Bard included). The action runs it for a real minute (`CFActivitySystem.AnthemLasts`). Playing a held instrument costs 1 action to start it and then keeps it going while the music plays, ending 2 seconds after. Using it again never shortens or stacks it.
- [x] Every Bard starts with a virtuoso's lute (`CFVirtuosoLute`) in the backpack: percussion and program changes are on, so a MIDI file plays each channel in its own voice. `CFGrantItem` now puts what it grants in the backpack, or at the character's feet without one.
- [x] Closing the instrument window no longer stops the song. It plays on while the player holds the instrument in either hand and is conscious, or stays within reach of a placed one such as a piano, and reopening the window shows it playing so Play stops it (`CFInstrumentSystem`, marked edits in the stock `InstrumentSystem`, its BUI and `InstrumentMenu`). Fixed again after the first playtest: stock's window stopped the song from the client as it closed (`InstrumentMenu.ExitedTree`), which no server test could see, because the test client has no MIDI.
- [x] Aura bonuses show in the HUD's status row. They have no end time while a creature stands inside, and the row skipped every such bonus as a lasting one from a feat or equipment. `CFBonusSystem.TimedTotal` now counts a bonus whose source is an aura (`CFAuraSystem.IsAuraSource`). The anthem's +1 against fear is still not shown, as it only counts against fear.
- [x] The item status panels: a change in one hand redrew both panels with that hand's item (missing braces in `HandsUIController.UpdateHandStatus`).

### Do first: level up bug, 13 November

Reported on 13 November 2026: a Sorcerer reaching level 5 was told to pick a language with no language to pick, and could not accept. Fixed the same day. Tests: `CFLevelUpTest`.

- [x] The language at level 5 only counts as a pick when there is a language left to learn. The tab listed only languages the character does not already speak, but the count ignored those, so a character who already spoke every common language was stuck. Both now use `CFLevelUp.LanguageOptions`. Whether this is the exact case seen is not certain: it was not reproduced in a real client.
- [x] Accept is never blocked. Picks left open are skipped, and the tab says so.
- [x] Every level taken with picks open writes a JSON report to `logs/cf-levelup/` beside the server's log files (`CFLevelUpReportSystem`), and a warning in the server log naming it. It holds what was left open, each slot's pick and options, the character's class, ancestry, level, Hit Points, ability modifiers and known languages before the level, and the builds before, as sent and after.

### Do first: holstering, 13 November

Asked for on 13 November 2026. Built the same day. Tests: `CFHolsterTest`.

- [x] Every weapon can be worn on the belt, back or neck, and every shield on the back or neck. `.agent/port-scripts/holsters.py` writes each one's `Clothing` block. A slot its sprite has no state for borrows another carry state: a sword on the belt or back uses CP14's back sheath (`equipped-NECK`), a dagger on the back uses its belt state. Firearms, the dart and the steel shield have no carry sprite and are not drawn while worn.
- [x] A weapon or shield worn in those slots counts half its own Bulk, rounded up, so a light item stays light (`CFEncumbranceSystem.IsHolstered`). What is inside it, such as a loaded bolt, counts in full. Our rule, not PF2e's.

### Do first: HUD fixes, 11 November

Asked for on 11 November 2026, after the HUD and menus list. Built the same day. Tests: `CFHudDeckTest`, `CFDeckLogicTest`.

- [x] The separated chat layout lost the ID, belt, bag, pocket and suit storage slots, leaving only the hands. A screen change left the old screen's containers holding those slot groups. The HUD now claims them whenever it appears (`CFGameHud.ClaimSlotGroups`).
- [x] The deck has no Items tab or section. All is Basic, Spells & Features and Custom, with two dividers. A saved layout with four sections falls back to the default.
- [x] With Basic, Class or Spells chosen, 1 to 0 use that tab's first row and Shift with 1 to 0 its second, and each button shows its key. Asked for as Alt: Alt with a number has no stock bind and keybinds are not edited, so it is Shift, which stock already binds for the second hotbar row. All and Custom keep the keys on the numbered hotbar.

### Do first: HUD and menus, 11 November

Asked for on 11 November 2026. Built the same day. Tests: `CFLevelUpTest`, `CFUpkeepTest`.

- [x] **The lobby builds level 1 only.** The level 2 to 5 panels are hidden, option choices are level 1's, and the spells tab offers level 1's slots (`CFSpellSlots.ForClass(cls, level)`).
- [x] The spells tab lists spells the character already gets from its ancestry, heritage, background, class, subclass, feats and their cantrip choices (`CFLevelUp.GrantedSpells`), so they are not picked again. The Level Up tab shows the same list.
- [x] **Levels are taken in game.** XP no longer levels a character: reaching 1000 shows a popup, and the character sheet's Level Up tab (`CFLevelUpTab`) asks for that level's picks: class, ancestry, skill and general feats, skill increases, the four level 5 boosts and the language an Intelligence boost brings, the new spell slots, and the option choices the new feats and features have. Accept stays off until everything that has something to choose is chosen. It sends `CFLevelUpRequestEvent`. The server reads only that level's picks (`CFLevelUp.TakeLevel`), checks them as it checks a lobby build (`CFCharacterSpawnSystem.CopyLevelPicks`), and runs `LevelUp`. The build lives on `CFExperienceComponent.Build`. `CFExperienceSystem.TryLevelUp` with no picks takes the ones the character already has, which tests use through `AddXpAndLevelUp`.
- [x] **Respawn from the ghost.** A Respawn button left of Return to body, which asks once more before running `respawn`. The old body drops everything it wore and held where it lay, then goes, and its job slot opens. The body remembers its player (`CFPlayerBodyComponent.User`), so it is found even when the ghost could not return to it. SSD and cleanup removals now drop everything too, not only the bag, weapons and wallet.

### Do first: monsters, summons and companions, 11 November

Asked for on 11 November 2026, after the 10 November playtest list. Built the same day. Tests: `CFMonsterTest`, `CFThrallTest`, `CFCompanionGoapTest`, `CFHarvestTest`.

- [x] **One monster base.** `CFBaseMonster` in `Mobs/Creatures/_base.yml` holds what every monster is: GOAP brain, ghost role, Monster faction, combat mode, `CFCombatant` that dies at 0 Hit Points, `CFAbilityScore`, `CFCombatOptions` and `CFHarvestable`. `CFBaseCreature` is the animal body on top of it. Humanoid monsters list their stock body first and `CFBaseMonster` second. `CFMonsterTest` fails a monster that cannot be harvested or does not die at 0. A monster on a stock body still sets its own `HTN`, `NpcFactionMember` and `GhostRole`, because the stock body's copy wins.
- [x] **Summoned creatures.** `CFSummoned` gives the Summoned trait. When one dies or its time is up it vanishes in a puff of smoke (`CFSummonPuff`) and leaves no body, so it cannot be looted or harvested (`CFSummonedSystem`). Thralls have it.
- [x] **Companions think with GOAP.** `CFCompanionBase` has `CFGOAPBrain` as a parent, so animal companions, familiars, eidolons, constructs and thralls all do. They attack enemies they see by themselves: only the Opposition alliance, so neutral creatures are left alone unless pointed at. Their home is their master (`CFGOAPCompanionSensorSystem`), within 3 tiles, and the lair leash (`CFGOAPLair`, 10 tiles or 20 seconds) brings them back mid-chase, though they still fight what is in reach.
- [x] Pointing helps. At a creature, it goes for that one first while it can see it. At the master, it comes straight back. At an item, it fetches it, with its brain paused for the trip. At the floor, it forgets the creature it was sent at.
- [x] **Thralls work.** They stood still because they ran the old companion task, which waited for an order, and pointing skipped them on purpose. Now they fight on their brains like any companion, follow, and answer pointing. Command a Thrall and Thrall Charge still pick the nearest one.
- [x] Create Thrall has a 60-second cooldown, as long as a thrall lasts (`useDelay` on the action).

### Do first: playtest feedback, 10 November

#### 1. Money in a wallet

Built on 11 November 2026. `CFWallet`, `CFWalletSystem`, `CFCoinSystem`, `CFMerchantSystem`. Tests: `CFEconomyTest`, `CFCharacterSetupTest`.

- [x] A wallet (`CFWallet`, CP14's sprite) that goes in a pocket and only takes coins. Every character spawns with one in a pocket, holding their leftover starting coin.
- [x] The wallet is the money. The shop, the counter and the guild spend from the coins in the first wallet the character carries (held, worn, in a pocket, or in the bag), breaking coins and putting the change back. Coins elsewhere do not count. Using coins in hand puts them in the wallet. `CFPurseComponent` and the right-click-yourself verb are gone.
- [x] A wallet in a dead body can be looted like any item, and SSD and respawned bodies drop it (5).

Two ways to sell, and both are always there:

- [x] **The guild pallets.** `CFWoodenPalletSell` and `CFWoodenPalletBuy` get `CFGuildPallet` (in `finish.py`, since the file is ported). Opening a sell pallet lists the goods lying on it and sells them all to the Merchants' Guild, which keeps 50% because it teleports them away by magic, `fullResale` items included. A merchant is a guild member and keeps the full worth. No console was needed: the pallet opens the trading window itself.
- [x] A merchant standing within 2 tiles of a buy pallet gets their shop orders on it instead of in hand. Opening a buy pallet says so. Only merchants can order.
- [x] **A merchant's counter** (`CFMerchantCounter`) is for selling to the player merchant, and only a merchant can open it. An adventurer places goods on it. The merchant's window lists each one with its Market price and Your price in separate columns. Your price is four boxes (pp, gp, sp, cp), filled with the market price, which the merchant can type over. Buy sends what is typed (`CFCounterBuyMessage.Copper`), takes it from the merchant's wallet and lays the coins on the counter for the adventurer to pick up. Bought goods wait under Items stored, a window laid out like the shop, where the merchant takes each one or sells it to the guild at full worth. Changed on 11 November 2026: no till, no counter-wide rate, and no record of who placed what. Any merchant can work any counter, since the job has one slot. Tests: `CFEconomyTest`.

#### 2. Monster parts and harvesting

Built on 11 November 2026. `CFHarvestSystem`, `CFHarvestRules`, `CFHarvestable`, `CFSurvivalKnife`. Tests: `CFHarvestTest`.

- [x] Each monster's parts value is Battlezoo Bestiary's monster parts by creature level (Battlezoo Bestiary p. 133, Tables 2A to 2C), read from its level, so a rebalance moves it. We use the Light column (2A), because CarpFinder still drops coin as treasure and parts come on top of it. The table is in `CFHarvestRules`, from https://pf2easy.com/index.php?id=19435. Battlezoo gathers parts with no check; the Survival roll is ours.
- [x] Harvesting is butchering. Use a survival knife on a dead monster, or its Harvest verb: 5 seconds, once per body, and only for a character trained in Survival. Every monster has `CFHarvestable`. It replaces stock butchering: a stock mob that could be butchered by knife (`ToolRefinable`) or on the kitchen spike (`Butcherable`) gets `CFHarvestable` instead, with that list as its parts, so nothing is butchered without the check. A finished harvest removes the body: everything it wore or held drops, and its parts are scattered where it lay. It is not gibbed, so no organs are left. Every harvest gives generic monster parts (`CFMonsterParts`) plus the meat, bone and leather the monster lists, and the generic parts keep what rounding leaves so the total is exact. A critical failure removes the body with no parts. Player characters are never harvested. Changed on 11 November 2026; the body used to stay.
- [x] The harvest rolls Survival against the DC for the monster's level: a critical failure ruins the parts, a failure keeps half the value, a success all of it, and a critical success one and a half times.
- [x] The meat, leather, scraps and bone a monster gives are its parts and share its value (`CFPartsValue`, per item in a stack). Every monster also gives `CFMonsterParts`. Parts are treasure: the cleanup keeps them, and they sell to a merchant at their counter at the merchant's price, or on a guild sell pallet for half, or full for a merchant.
- [x] Meat on the drop lists: `CFFoodMeatBoar` for the boar, `CFFoodMeatMonster` for the bear, wolf and dragon.
- [x] A survival knife (`CFSurvivalKnife`, 5 sp, a d4 slashing agile finesse knife) is in every adventurer's kit and in the merchant's shop. It uses the dagger's sprite.

#### 3. PvP only by choice

Built on 11 November 2026. `CFPvpComponent`, `CFFactionSystem.CanHarm`, the `pvp` command (`CFPvpCommand`). Tests: `CFPvpTest`.

- [x] The four groups are a party with each other (`factions.yml`). Monsters still oppose everyone.
- [x] A `pvp` console command, typed in chat as `/pvp on`, `/pvp off`, or `/pvp` to toggle. Off by default. Two player characters can harm each other only when both have it on. This covers merchants too, who used to be fair game as Neutral.
- [x] It covers everything that asks `CanHarm` or `GetAlliance`: Strikes, spells and areas, projectiles, thrown weapons, maneuvers, reactions and flanking. Players always fight monsters: only player characters get `CFPvpComponent`, at spawn, so a ghost in a monster body is still a monster.
- [x] Crossed swords on the left of a player with PvP on (`CFPvpIcon`), and an examine line.
- [x] Turning it off waits 30 seconds after the player last hurt, or was hurt by, another player with PvP on (`CFDamagedEvent`).

#### 4. The dungeon wizard cleans up

Built on 11 November 2026. `CFDungeonCleanupSystem`, on `CFStationBasic` so every CF map has it. Tests: `CFUpkeepTest`.

- [x] Every hour a cleanup runs, with announcements from "The Dungeon Wizard" 15 minutes, 1 minute and 10 seconds before, then one saying how much was swept away. The interval and warnings are fields on `CFDungeonCleanup`, so a DM can change them in VV.
- [x] Cleaned, on every map that is not paused: dead monsters, blood and other puddles, and loose weapons and items.
- [x] Kept: anything inside a container; containers themselves; living creatures; bodies whose player can still return to them; coins and `fullResale` treasure, which monster parts will be; anchored things apart from puddles.
- [x] Kept: everything the mapper placed, spawners' loot included. Anything created within a second of its map counts as the mapper's.
- [x] Dead adventurers whose player can no longer return drop their bag and weapons, then go, as in 5.

#### 5. Respawning and SSD players

Built on 11 November 2026. `CFPlayerBodySystem`. Tests: `CFUpkeepTest`.

- [x] `respawn` deletes the old body after dropping its bag and weapons. Marked lines in `RespawnCommand.cs`.
- [x] Any player body gives its job slot back when it is deleted, however that happens (`CFPlayerBodyComponent`, through `ServerStationJobsSystem.TryAdjustJobSlot`).
- [x] A body left without its player for 15 minutes is removed: its bag and weapons are dropped on the ground first. Ghosting out of a body counts as leaving it, as stock's SSD does, so a dead player who waits 15 minutes as a ghost loses the body. The time is `SsdLimit` on the component.

### Playtest feedback, 9 November

#### Spell effects

- [x] A creature targeted by a spell with a save gets sparkle particles, coloured by the type of spell. `CFVfxSystem`, with the colour from the spell's damage type, or from its traits if it deals none.
- [x] A caster gets particles while casting, so people around them can tell. A subtle spell shows none.
- [x] Healing puts healing sparkles on both the healer and the one being healed. It is in `CFEffectSystem.Heal`, so anything that heals gets them.
- [x] An aura has a hard line where it ends, drawn as a magic border. `CFAuraOverlaySystem`. It is only drawn while the aura is doing something.

#### Combat

- [x] Melee aim assist. `CFMeleeAimSystem`: a left-click hits the closest enemy in reach within 60 degrees of the cursor, wherever the cursor is. Clicking an enemy in reach still hits that enemy. Neutral creatures and hidden ones have to be clicked. Right-click maneuvers do not use it.
- [x] Action pool cooldown goes to 1.2 seconds (`RegenSeconds`). It was really 1, not the 1.45 the docs said, so players are a little slower than before.
- [x] Cut the melee hit animation in half, so it is much faster.

#### GOAP

- [x] All mobs have a pool cooldown of 1.5 seconds, stock HTN mobs as well as GOAP ones (`NpcRegenSeconds`). Players stay at 1.2. A monster with a player in it counts as a player.
- [x] All mobs get a `ThinkingTimer`, default 1 second, which gives them a moment to think about their next attack. It is checked where a Strike or shot is attempted, so both AIs get it. Swings at doors and other obstacles do not wait.
- [x] Demoralize and Feint from GOAP wait for it as well (`CFGOAPManeuverStepSystem`). Tests: `CFGOAPTest`.
- [x] `thinkingTimer` is a field on `CFCombatant` in each creature's YAML, so it can differ per mob. A smarter mob with higher reflex, such as a boss, thinks faster. Every creature uses the default until someone sets one.

#### Medical kit

- [x] A medic healing someone with a kit force grapples them, so they cannot run away. There is no roll. The patient can still break out of it with Escape, which ends the treatment.
- [x] Rebalance: healing someone takes 12 seconds and costs 3 actions, one every 4 seconds starting at once. Running out of actions ends it.
- [x] The creature being healed gets the magic healing sparkles here too, at each of the 3 actions and when it heals.

#### Character setup

Built on 9 November 2026. Tests: `CFCharacterSetupTest`.

- [x] A character cannot ready up or join without an ancestry, background, class and job. Late join still opens the job list first, and is refused when a job is picked.
- [x] The ABCJ panel is Ancestry, Sprite, Background, Class, Job, Accept. Everything else stays hidden until Accept, which also saves.
- [x] The shop in Character Setup uses the merchant layout, with search and a quantity. It only sells what the job's loadouts offer, not a merchant's whole catalogue.
- [x] Everyone starts in clothes and shoes, with an adventurer's kit on their back: a backpack holding rope, a torch, flint and steel, a ration and a canteen of water. The kit is where extra copies go. Striking the flint on a torch has not been tried in a real client.
- [x] Extra copies are stored per job (`CFCoins.CopiesOf`), so two jobs that sell the same item do not share a count. Tests: `CFCharacterSetupTest`.

### Step 1: Make character options work

#### 1a. One pipeline for what an option grants

Built on 6 November 2026, except where marked. Tests: `CFGrantTest`. What changed and why is under "Grants" above.

- [x] `CFGrant`: an abstract data class like `CFEffect`, written in YAML as `!type:CFGrantBonus` and so on.
- [x] A `grants` list on `cfFeat`, `CFClassFeature`, `cfHeritage`, `cfAncestry` and `cfBackground`.
- [x] A `grants` list on the class choices.
- [x] `CFCharacterFeatureSystem.Apply(mob, grants, source)`, called at spawn and on level-up.
- [x] Move both `switch` statements into YAML. The flags on `CFCombatOptionsComponent` stay for now, set by `!type:CFGrantOption`.
- [x] Permanent bonuses in `CFBonusSystem`, and bonuses that only count against a trait. This replaces `SavesVsFear`.
- [x] A hook that lets a grant change a save's degree, matching `AdjustDefense`: `CFBonusSystem.UpgradeSave`, called from `CFConditionSystem.RollDefense`.
- [x] Traits on `CFEffectArgs`, read from the source, so any check can ask what it is rolling against.
- [x] A bonus against a trait counts on saves, Perception, attacks and AC (`CFBonusSystem.AgainstSource`). Tests: `CFGeneralFeatTest`.
- [x] The ids in `CFGrantProficiency` are checked by `CFFeatureCoverageTest`, so a typo fails a test.

| Grant                      | What it does                                                                               | First uses                                                                        |
| -------------------------- | ------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------- |
| `CFGrantProficiency`       | Raises a rank: a save, Perception, a weapon group, armor, spellcasting                     | About 30 class features: Weapon Expertise, Reflex Expertise, Perception Expertise |
| `CFGrantBonus`             | A bonus to a save, skill or AC, always or against a trait                                  | About 57 ancestry feats                                                           |
| `CFGrantSaveUpgrade`       | One degree better against a trait                                                          | Bravery, Death Warden Dwarf                                                       |
| `CFGrantResistance`        | Fixed, or half level                                                                       | Forge Dwarf, Arctic Elf, Strong-Blooded Dwarf                                     |
| `CFGrantWeaponFamiliarity` | Treats listed weapons as simpler                                                           | About 22 ancestry feats                                                           |
| `CFGrantSense`             | Low-light vision, scent, imprecise senses                                                  | Heritages, ancestry feats                                                         |
| `CFGrantMovement`          | `TBA`, not built. Climb, swim and fly all wait                                             | 6 ancestry feats                                                                  |
| `CFGrantSpell`             | An innate spell or a cantrip. A ranked spell is at will until 1f adds daily uses and focus | Seer Elf, Adapted Cantrip, every focus spell                                      |
| `CFGrantItem`              | Starting gear                                                                              | Clan Pistol, Alchemist's potions                                                  |
| `CFGrantAction`            | Adds an action to the wheel                                                                | 236 action and 30 free action feats                                               |
| `CFGrantReaction`          | A trigger and what it does. See 1b                                                         | 63 reaction feats                                                                 |
| `CFGrantOption`            | Sets a `CFCombatOptionsComponent` flag                                                     | Everything already coded                                                          |

#### 1b. The shapes actions come in

Most action feats share one of a few shapes, so build the shape once. An action is an entity with `CFActivity` and the usual `CFTargeting`, `CFCheck` and `CFDegreeEffects`. Do not write a new resolver.

- [x] Stances (21 feats). `CFStance` on an action, parented on `CFActStanceBase` in `Actions/stances.yml`, run by `CFStanceSystem`. Built on 9 November 2026, with Point-Blank, Mountain, Crane, Dragon and Tiger. The other 16 stances are filled in their waves. Tests: `CFStanceTest`.
    - One at a time, and another stance action waits 6 seconds. Using the same action again is Dismiss, which costs its action and does not wait.
    - It ends on being knocked out, when armor breaks an unarmored requirement, or on another stance action. Real time has no encounters, so it does not end when a fight does.
    - [x] Falling asleep ends a stance, from the condition or stock sleep. Paralyzed does not, because it is not being knocked out. Tests: `CFStanceTest`.
    - [x] A stance can need a ranged weapon, a melee weapon, a free hand or a shield in hand (`requiresRangedWeapon` and so on on `CFStance`), and ends when the creature lets go. Point-Blank Stance needs a ranged weapon. Mountain Stance's touching the ground is not checked, because nothing in the game flies yet. Tests: `CFStanceTest`.
- [x] Flourishes (36). Add `CFFlourish` to an action. `CFActionPoolSystem` allows one until the pool is next full. Flurry of Blows and Sudden Charge use it, and Vicious Swing keeps its code path. The rest are filled in their waves.
- [x] Spellshape (34 feats). `CFSpellshape` on an action, parented on `CFActSpellshapeBase` in `Actions/spellshapes.yml`, run by `CFSpellshapeSystem`. Built on 9 November 2026, with Reach Spell (7 classes) and Widen Spell (5 classes). The other 22 are filled in their waves. Tests: `CFSpellshapeTest`.
    - It waits for the next spell, and is lost after 6 seconds or when an action is spent on anything else. Free actions and reactions do not end it, though the rules say they should.
    - Touch spells have `touch: true` on `CFTargeting`: they reach the next tile in every direction, diagonals too, and Reach Spell makes that 30 feet. Tests: `CFTouchRangeTest`.
- [x] Auras. `CFAura` on a creature and `CFAuraEffect` entries, added with `!type:CFGrantAura`, run by `CFAuraSystem`. Built on 9 November 2026 for the Champion's aura, with Aura of Courage, Aura of Despair, Divine Health and Shields of the Spirit. Tests: `CFAuraTest`.
    - It looks up who is inside 4 times a second, through `CFEffectSystem.CollectArea`, only for creatures that have an aura. It is not a physics sensor: a 15-foot sensor would make the problems in `ss14rules.md` about the flanking sensor much worse, and would be a circle where emanations are tiles.
    - "At the end of your turn" is every 6 seconds, on the owner's own timer. A placeholder: the timing will probably change.

Reactions are still open in `roadmap.md`. These parts of them are done:

- [x] Reactions go in an order (`CFReactionOrderComponent`). For each trigger, the first one in the order that would change the outcome fires. A new reaction goes above every lower-level one, and the player can reorder them on the character window's Reactions tab. Reactive Strike, Shield Block, Reactive Shield and You're Next have places in it too. Tests: `CFReactionTest`. The tab has not been seen in a real client.
- [x] Triggers `CriticallyHit` (Scars of Steel) and `AllyWouldBeHit` (Guardian's Deflection). Tests: `CFReactionTest`.
- [x] Trigger `EnemyCastsSpell`. Counterspell (Wizard and Witch) spends a prepared charge of the same spell to counteract it, at that charge's rank, when an enemy within 60 feet casts it in sight. A countered spell's slot is still spent. Subtle spells cannot be countered. Tests: `CFReactionTest`.
- [x] Spell attacks trigger `AttackWouldHit`, as melee, so Deflect Projectile does not answer one. Reactive Shield still only answers Strikes. Tests: `CFReactionTest`.

#### 1c. Subclasses

Built on 9 November 2026. `CFClassChoices` decides what counts as a valid pick. Tests: `CFClassChoiceTest`. The builder buttons have not been clicked in a real client.

- [x] A `cfClassChoice` prototype with `grants`, and `ClassChoices` on `CFCharacterBuildData`. No database migration: a build saved without the field loads with an empty list.
- [x] A button in the builder, using the same list as Heritage. There is one button per `slot` a class has options in, so a class needs only its options.
- [x] `cfDeity`, for classes with `usesDeity` (Cleric and Champion). Deities are data on the character, not a religion system.
- [x] Rogue's rackets, in `Class/Choices/rogue.yml`. The Thief is finished. The others give their skills, armor and key attribute, and the rest is marked `TBA`.
- [x] Seven placeholder gods in `Character/deities.yml`, for Clerics and Champions. Each gives a skill, a favored weapon (`CFGrantWeaponFamiliarity`, which counts it as simple) and what Divine Font casts. Not clicked in a real client. Tests: `CFClassChoiceTest`.
- [x] A Ruffian, Scoundrel or Mastermind picks its key attribute with a `KeyAttribute` entry in `choices`, and keeps the racket's until it does. Not clicked in a real client. Tests: `CFGeneralFeatTest`.

#### 1d. Level-up

All of this is picked in the lobby and stored on `CFCharacterBuildData`, the way `ClassFeatLevel2` is. `LevelUp` only hands it over. The builder slots have not been clicked in a real client. Tests: `CFLevelChoicesTest`.

- [x] Slots for levels 2 to 5: skill increases at 3 and 5, ability boosts at 5, a general feat at 3, and skill feats at 2 and 4. Picked on the Level Up tab since 11 November 2026, not in the lobby.
- [x] The same choices copied to `CFExperienceComponent` at spawn, checked again there because they come from a client, and applied by `LevelUp`.
- [x] Run class features from `LevelUp` when their level is reached.
- [x] The feat a background names, at level 1: `skillFeats` on `cfBackground`, read through `CFSkills.SelectedFeats`. It needs no qualifying and cannot be taken again in a slot. Three backgrounds name two feats and get both, which has not been checked against the rules (Hermean Heritor and the ones with Hefty Hauler or Diehard). Contract Negotiator and Ravening's Desperation are from other books and are not granted.
- [x] Extra slots for classes that get more: `bonusSkillFeats` and `bonusSkillIncreases` on `cfClass`. Only the Rogue has them (skill feats at 1, 3, 5 and increases at 2, 4). Not clicked in a real client. Check whether the Investigator should too.
- [x] Changing a pick clears any later feat the build no longer qualifies for (`CFSkills.DropUnqualifiedFeats`), in the builder and again at spawn. Changing ancestry goes around it, so that case is only caught at spawn. Tests: `CFLevelChoicesTest`.
- [x] Spell slots grow with level. `laterSlots` on a class's `spellcasting` holds its Spells per Day table from Archives of Nethys, `GrowSpellbook` applies it on level up, and the builder offers every slot through level 5. Tests: `CFSpellSlotTest`. A pick only counts once the slot for it has arrived, by its order in that rank. A Wizard's curriculum slot is not built, and Divine Font's extra slots stay at rank 1 where the table moves them to the highest rank.

#### 1e. Skill and general feats

`Feats/General/feats.yml` holds 131 feats through level 4 from Player Core, Player Core 2 and GM Core, with the site's one line summaries. `.agent/port-scripts/general_feats.py` rewrites that file from Archives of Nethys, so put fixes in the script. Of 74 feats backgrounds name, 65 match by name.

- [x] Prerequisites that name another feat, an attribute or a heritage are data (`requiresFeats`, `requiresAbility`, `requiresHeritage`), written by `.agent/port-scripts/prerequisites.py`. Run it after `general_feats.py`. The builder only lists feats the build qualifies for, and `CFSkills.DropUnqualifiedFeats` clears one that stops qualifying, for class and ancestry slots too. Tests: `CFLevelChoicesTest`.

#### 1f. Spells

- [x] Dispel Magic, on PF2e counteract rules (`CFCounteractSystem`). Spells cast through a spell action remember what they put on a creature: bonuses, and conditions `EndCondition` can end. Dispel Magic aims at an enemy's spell first, then the highest rank. Tests: `CFCounteractTest`.
- [x] Animal Form, as a battle form (`CFBattleFormSystem`). A mode menu picks ape, bear, bull, canine, cat or deer. For a minute the caster fights with the animal's attack at +9 to hit and +1 damage, has AC 16 plus level and 5 temporary Hit Points, and moves at the animal's Speed. Its own attack and AC count instead when higher. No spells or weapons in the form, and it ends at 0 Hit Points. Tests: `CFBattleFormTest`. Not seen in a real client.
- [x] Figment conjures a ghostly copy of a random shop item that vanishes when touched and fades after a minute. Sigil paints a glowing crayon rune on the floor, one per caster. Know the Way puts an arrow a tile north of the caster that points north on any screen rotation. Tests: `CFUtilityCantripTest`. Not seen in a real client, so the arrow's direction and the tint are unchecked by eye.
- [x] Command picks Approach, Drop, Fall, Flee or Halt from a radial menu, then a creature. On a failed Will save it obeys for 6 seconds, 12 on a critical failure. GOAP brains plan `Obey`. Stock HTN mobs have their HTN paused and are walked by GOAP steering. It is linguistic, so a creature that does not understand the caster's language ignores it, and animals know only Bestial. Tests: `CFCommandTest`. Not seen in a real client.
- [x] Focus spells: `focus: true` on `CFSpell` and `CFGrantFocusSpell`, up to 3 Focus Points, refilled by a long rest. Refocus gives one back after 10 world minutes (2.5 real minutes at the default clock), and moving or being hurt interrupts it. In `Spells/Focus/`. Tempest Surge and Force Bolt are the first ones. Tests: `CFFocusTest`.
- [x] Casting from a higher slot. A spell resolves at the rank of the slot it was paid from (`CFSpellComponent.ResolvedRank`). A prepared caster puts it in a higher slot in the lobby or on the sheet. A spontaneous caster has to learn it at that rank (`KnownAtRank`). Tests: `CFSpellSlotTest`. Not seen in a real client.
- [x] A spell castable at several ranks opens a radial menu of them, before its mode or bolt count (`CFRankMenuSystem`). The rank is sent on the perform request and checked when it is paid for. Tests: `CFSpellSlotTest`. Not seen in a real client.
- [x] Signature Spells: one spell of each rank, picked in the builder through a `SignatureSpell` entry in `choices`, cast at any rank with a slot from level 3. For Bard, Oracle, Psychic, Sorcerer and Summoner. Not clicked in a real client. Tests: `CFSpellSlotTest`.
- [x] Divine Font's slots sit at the cleric's highest rank and move up as it levels (`FontSlots`), so Heal and Harm heighten. They are still Charisma modifier, at least 1, where the rules say 1 plus Charisma. Kept on purpose. Tests: `CFSpellSlotTest`.

#### 1g. Waves

Do 1a to 1d first, then work through these. For each entry: read it on Archives of Nethys, replace the placeholder in its `.ftl` with a short note in our own words, add its `grants`, and add a test where it does more than a stat change. An entry that cannot work in play keeps its flavour text and the word `TBA`. A class is finished when every feature and feat has grants or `TBA`.

| Wave | Options                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                          | Feats                  |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------- |
| 1    | Ancestries and heritages. Mostly grants, and no subclass needed. Worked through on 9 November 2026. Heritages: 60 have working effects and 77 are marked `TBA`. Ancestry feats: 38 new ones work, and the rest are marked. Entries that only had `TBA` added still need their text checked against the rules when each is built. Tests: `CFHeritageTest`, `CFAncestryFeatTest`                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   | 255, and 149 heritages |
|      | Wave 1 waits on: climb, swim and fly, a rule for choosing another ancestry's feats, and a `cfFeat` that needs a heritage (prerequisites are text)                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                |                        |
| 2    | Fighter, Rogue, Ranger. First pass on 9 November 2026: class features, Hunter's Edge, Sneak Attack limits and 9 passive feats work, and the rest say `TBA`. Tests: `CFWave2Test`. Strike actions: `CFStrikeAction` in `Actions/strikes.yml`, with Intimidating Strike, Dual-Handed Assault, Lunge, Head Stomp, Twin Takedown and Hunter's Aim (tests: `CFStrikeActionTest`). Still to do: Strikes that hit several targets (Swipe, Quick Reversal, Double Shot), Strikes that add a maneuver (Brutish Shove, Slam Down, Sleek Reposition, Combat Grab), and two shots from a bow that holds one arrow (Hunted Shot)                                                                                                                                                                                                                                                                                                                              | 81                     |
| 3    | Cleric, Wizard, Bard, Druid, Witch. Started on 9 November 2026: focus spells, all subclasses as choices, and class features that are rank raises. Most subclasses are still mostly `TBA`: their feats and focus spells. Feats worked through on 9 November 2026: 15 work, and the rest say `TBA`, mostly waiting on composition spells, domains, familiars, battle forms and the Witch's lessons. Feats that need a muse, order, racket or doctrine now require it (`requiresChoice`). Tests: `CFWave3Test`                                                                                                                                                                                                                                                                                                                                                                                                                                      | 105                    |
| 4    | Barbarian, Champion, Monk, Swashbuckler, Investigator, Alchemist, Sorcerer, Oracle. First pass on 9 November 2026: every subclass is a choice, level 1 to 5 class features are done or say `TBA`, every feat has its own note, and 22 more feats work. Tests: `CFWave4Test`. A Sorcerer's bloodline sets its tradition (`tradition` on `cfClassChoice`). Still to do: Alchemist versatile vials and Quick Alchemy, blood magic and bloodline focus spells, Oracle curses and revelation spells, a time limit on Rage, and Intelligence on a stored Devise a Stratagem roll. Witch patrons could set their tradition the same way                                                                                                                                                                                                                                                                                                                 | 179                    |
| 5    | Magus, Thaumaturge, Gunslinger, Inventor, Psychic, Commander, Guardian, Necromancer, Animist, Exemplar, Runesmith. First pass on 9 November 2026: subclasses that mean something today are choices, level 1 to 5 class features are done or say `TBA`, and every feat has its own note. Tests: `CFWave5Test`. The systems this wave waited on are built: see the list below, which also says what each still lacks                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               | 197                    |
| 6    | Summoner. First pass on 9 November 2026. An eidolon is a companion ordered by pointing, and a ghost role so another player can take it: `CFEidolonServerSystem`, `Companions/eidolons.yml`, and three builder slots in `Choices/summoner.yml` (type, key attribute, attack). Hit Points are one pool, kept in step by comparing both bodies each tick. It has its own action pool, where the rules share one, because a second player would otherwise be spending the Summoner's actions. It stays within 100 feet. Tests: `CFEidolonTest`. All 15 types set a tradition, skills and attributes. Every type uses one placeholder body. Furious Strike, Tendril Strike and Dragon Breath work, the Ooze's resistance and the Undead's void healing work, and the other types' abilities say `TBA`. Not built: the second attack and the primary attack's traits, Act Together, Share Senses, runes and gear on eidolons, and most evolution feats | 23                     |

Witch familiars, Druid animal companions and Summoner eidolons all lean on `cfPet`, and "Companions have no inventory" under Step 4 gets in their way.

- [x] Feats with no effect show `(TBA)` in the builder's lists.
- [x] Heritages show `(TBA)` in the builder when they have no effect at all. One with part of its rules working keeps its name and says which part is `TBA` in its text. Tests: `CFHeritageTest`.
- [x] Unfinished class features show `(TBA)` in the class's description in the builder, when nothing about them works yet (`CFClassFeature.Unfinished`). Not seen in a real client.
- [x] `CFFeatureCoverageTest`: fails on a placeholder in any class, on a class feat with no effect and no `TBA`, on a mistyped proficiency, and on an option in the files that did not load.
- [x] Kineticist is not included, and its stray feat file is gone.
- [x] A pass on 10 November 2026: Twin Parry (Fighter, Ranger), Raise Symbol (Cleric) and Story Crooner's Performance bonus work. `CFEffectAction` can need a melee weapon in each hand. Tests: `CFEffectActionFeatTest`. Most of the 465 class feats still marked `TBA` wait on a missing system (mounts, climb and swim, compositions, familiars, temporary Hit Points from actions), not on data.
- [x] Temporary Hit Points (`CFTemporaryHpSystem`, `CFTemporaryHpEffect`). Damage takes them first, they do not stack, and they can run out or end with their source. Rage gives level plus Constitution, once a minute, ending with the Rage. Second Wind and Armored Courage work. Battle forms use them too. Tests: `CFTemporaryHpTest`.

#### Systems Wave 5 is waiting on

Do all of these before Wave 6. Smallest first, each landing working and tested.

- [x] Recharging Spellstrike. `SpellstrikeSpent` on `CFCombatOptionsComponent`, recharged by its action or by a spell with `conflux: true`. Tests: `CFGuardianMagusTest`. Choosing which spell goes into the Strike, and counting as two attacks for Multiple Attack Penalty: not built. Each hybrid study's own conflux spell: not built.
- [x] Taunt. `CFTauntSystem`, and Intercept Attack as the `InterceptsDamage` trigger. Tests: `CFGuardianMagusTest`. Taunts do not change who an NPC chooses to attack, and an area that also holds the taunter still counts as ignoring them. Intercept Attack does not move the Guardian.
- [x] Firearms. `Entities/Objects/Weapons/Ranged/CFFirearms.yml`, on the crossbow pipeline: each shot pays for its reload. `CFReloadSystem` loads from what the user carries, and a slinger's reload prepays the next shot. Concussive, kickback and scatter are trait rules. Tests: `CFFirearmTest`. Misfires are not built, because nothing tracks cleaning a weapon. Scatter only splashes enemies. Capacity weapons and combination weapons are not built.
- [x] Implements. `CFImplement` on an item, in `Entities/Objects/Misc/CFImplements.yml`, and `CFImplementSystem` for what is held. Tests: `CFImplementTest`. Implement's Empowerment and the initiate benefits of Amulet, Chalice, Lantern, Regalia, Shield, Tome, Wand and Weapon work, in part. Bell, Censer and Mirror give the item only. Adept benefits and esoterica are not built. The Censer has no art of its own and uses the Arabian lamp.
- [x] Psi amps. `CFPsiSystem`. `CFActAmp` is a free spellshape that arms the next psi cantrip, which spends a Focus Point. What an amp does is data on `CFPsiCantrip` on the spell's action. Unleash Psyche and three of four psyche actions work. Tests: `CFPsiTest`. Amps from feats (Mental Balm, Homing Beacon, Warp Space), forbidden thought, distortion lens, warp step, message and telekinetic hand are not built. Telekinetic Rend is one 10-foot burst, not two 5-foot ones. Stunned, the temporary Hit Points on Frostbite and the Oscillating Wave's alternation are not built.
- [x] Tactics and the banner. `CFTacticSystem`, `Actions/tactics.yml`, and the item `CFCommanderBanner`. Three tactics are picked in the builder as `cfClassChoice` slots, because everything is chosen in the lobby. The squad is the Commander plus 2 + Intelligence nearest allies in the banner's 30 feet. Tests: `CFTacticTest`. Seven of the level 1 tactics work. Real time cannot make other players move, so a tactic's Stride or Step is +10 feet Speed for 3 seconds. Double Team, End it!, Coordinating Maneuvers, Mountaineering and Naval Training, Passage of Lines, Protective Screen and Shadows in the Moonlight are not built, and nor are the folio, Plant Banner, Warfare Lore or the penalty for losing the banner.
- [x] Innovations. `CFInventorSystem`, items in `Entities/Objects/Misc/CFInnovations.yml`, the construct in `Companions/constructs.yml`. A class choice picks the innovation and another its modification. Overdrive, Explode and the unstable check work. A broken innovation is usable again after the time Refocus takes. Tests: `CFInnovationTest`. The construct has the prototype's statistics from Guns & Gears, on a stock clean bot body. Its second attack, and being broken and repaired at 0 Hit Points, are not built. Companions level with their owner through `CFCompanionSystem.MatchOwnerLevel`. Tests: `CFCompanionLevelTest`. Construct modifications, Ranged Blunt Shot, weapons other than the longsword base, razing and hampering are not built.
- [x] Thralls. `CFThrallSystem` (Shared) and `CFThrallServerSystem`. `CFThrall` in `Companions/thralls.yml`, a level -1 summoned companion with 1 Hit Point that fights on a GOAP brain since 11 November. Create Thrall, Thrall Charge, Command a Thrall and the grave spells cast on a thrall through `CFThrallBlastEffect`. Tests: `CFThrallTest`. Thralls spawn beside the Necromancer without checking the tile is free. Their Strikes do not count toward the Necromancer's Multiple Attack Penalty. Inevitable Return, Consume Thrall, Thrall Teamwork, Song of the Soul, Muscle Barrier and regaining Focus by destroying a thrall are not built.
- [x] Apparitions. `CFApparitionSystem`, `CFApparitions` and `CFSustain` in `Spells/`. Two apparitions are picked in the builder (`Choices/apparitions.yml`), the Primary slot and the Second, and `group` keeps a build from taking one twice. Apparition slots are their own pool on `CFSpellbookComponent`, spent like a spontaneous caster's. Sustaining is using the action again: 1 action, no Focus Point, once per 6 seconds, lapsing after 12, for up to a minute. Tests: `CFApparitionTest`. One apparition slot per rank, at levels 1, 3 and 5, from the class table. Most apparition spells do not exist in the game yet, so each option's text says which are missing, and some apparitions teach nothing yet. Traveling Workshop and Darkened Forest Form are not built, so Crafter and Stalker have no vessel. Several vessels are partial, and their summaries say how. Heightening apparition spells waits for casting from a higher slot (1f). Changing primary on Refocus, Relinquish Control, Apparition Sense, spirit familiars and Channeler's Stance are not built.
- [x] Ikons. `CFIkonSystem`, `cfIkon` prototypes in `Combat/ikons.yml`, actions in `Actions/ikons.yml`, and three builder slots in `Choices/exemplar.yml`. A Shift action per ikon moves the spark, and a transcendence is greyed out until it can be used. Weapon ikons are whatever matching weapon is in hand, not a chosen item. Worn ikons need no item at all. After a transcendence the spark goes to the next ikon, because real time has no pause to choose in. Tests: `CFIkonTest`. Not built: Shadow Sheath's copies, Fetching Bangles, Horn of Plenty, the transcendences of Bands, Pelt, Skybearer's Belt, Shadow Sheath and Victor's Wreath, Diehard on the Scar, armor against ranged only and melee only, and every Exemplar feat that imbues an ikon. Root epithets train their skill at level 1, not 3. Only Brave and Radiant do anything more. Spirit damage is dealt as a second hit, so it shows as its own number.
- [x] Runes. `CFRuneSystem`, `Combat/runes.yml`, `Actions/runes.yml`, `Crafting/rune_recipes.yml`, and six builder slots in `Choices/runesmith.yml`. A `craftingRecipe` with `attachRune: true` puts its result on a weapon or armor lying on the bench instead of making an item, so the workbench is how item runes and etched Runesmith runes get on things. Recipes follow the normal level rule, so only +1 weapon potency and striking are attachable to weapons at level cap 5, with armor potency at level 5. Trace Rune is one action per rune, 1 action adjacent and 2 from further away, and lasts 12 seconds. Invoke Rune targets a creature and sets off up to two runes on it and what it holds. Tests: `CFRuneTest`. Passives and invocations that need something unbuilt say so in each option: Baruiel, Ledria, Oljinex, Pluuna, Tilus and Zohk have no invocation yet. Not built: etching on a creature, invoking runes on two different creatures at once, the level 5 diacritic runes, shield and armor property runes, the artisan's toolkit requirement, upgrading a rune by paying the difference, and Magical Crafting. Rune items use a placeholder sprite from CP14's magic trap sheet. Materials to craft a rune item itself are not defined, so they come from the shop or as loot.

#### Action HUD

Built on 9 November 2026, between Waves 3 and 4. How it works is under "Game HUD" in `ss14.md`. Tests: `CFDeckLogicTest`, `CFHudDeckTest`. Accepted in a real client on 9 November 2026.

- [x] Prepared casters can put a lower-rank spell in a higher slot, by dragging it there. Add uses its own rank first.
- [x] The Items tab is gone (11 November 2026): items are used in hand. The All tab is Basic, Spells & Features and Custom, with two dividers.

#### Languages

Built on 6 November 2026. `Content.Shared/_CF/Languages`, and `CFSpeech` in `Content.Server/_CF/Languages/ChatSystem.CF.cs`. Tests: `CFLanguageTest`. The builder button has not been used in a real client.

- [x] A player in a kobold could not read their own speech (15 November 2026). It was not the language check: the stock kobold body has a `ReplacementAccent` that swaps every line for "Yip" or "Grrar" before languages are looked at, so everyone heard that. A creature with `CFLanguages` now loses an accent that replaces whole lines (`CFLanguageAccentSystem`), and a speaker is never treated as a foreign listener of their own words.
- [x] The language menu, after Starlight's (`C:\project\starlight-ss14`, used with permission and credited in each file): a speech bubble button on the top bar opens a list of the languages the body knows, with a Speak button each (`CFLanguageMenuWindow`, `CFLanguageMenuUIController`, `CFLanguageMenuSystem`). The Speak verb on yourself is gone. The button has no key, because upstream keybinds are not edited.
- [x] Known languages are on the character sheet, beside Speed. Not seen in a real client.
- [x] Multilingual and Nomadic Halfling teach languages through a `Language` entry in `choices`, so the player picks. Only common languages are offered. `CFGrantLanguage` was not needed. Tests: `CFLevelChoicesTest`.
- [x] A level 5 boost that raises Intelligence teaches one more language. The builder adds a slot for it, and `LevelUp` hands it over at level 5 (`xp.LanguageLevel5`). Tests: `CFLanguageTest`.
- [x] Kobolds speak Draconic and Sakvroth, not Common, so a party needs one of those to talk to them. Tests: `CFLanguageTest`.
- [x] Animals speak `Bestial` and mindless undead and oozes `Mindless`: noises no one can learn, so a player in one of their bodies cannot talk or understand speech. Tests: `CFLanguageTest`.

#### Rest

Built on 6 November 2026. `CFLongRestSystem`. Tests: `CFLongRestTest`.

- [x] A long rest is refused with a living enemy within 50 feet and in the open, and has a cooldown of 15 real minutes per character. This replaces PF2e's 8 hours and once a day, on purpose, for pace. It only counts creatures whose `cfFaction` opposes the sleeper's. Tests: `CFLongRestTest`.

#### 1.1 A grant that gives a feat (15 November 2026)

Tests: `CFGrantFeatTest`.

- [x] `CFGrantFeat`: a feat with everything it gives. `CFCharacterFeatureSystem.ApplyFeat` moved to Shared from the spawn system, and a feat is applied once per character, so a heritage's feat picked again in a slot is not doubled. `HasFeat` says whether one was given.
- [x] `kind: Feat` in `choices`, with `featSlot` (general, skill, class or ancestry) and `featLevel`. `CFOptionChoices.Allowed` lists what the build qualifies for. `CFSkills.ExtraFeats` collects feats given or chosen this way, `SelectedFeats` includes them, and the Level Up tab and the lobby do not offer one again. Validation of a feat pick reads skill ranks, which read the picks, so `Valid(checkFeats: false)` takes a feat pick on kind alone on that path.
- [x] Heritages: Hold-Scarred Orc, Nine Lives Catfolk, Laborer Android, Versatile Human, and nine that give a feat still marked `TBA`. Human feats: General Training, Natural Ambition. Guardian's Tough to Kill: Diehard, and once a day the first time dying would reach 3 or more it stays at 2 (`ToughToKill` on `CFCombatOptions`, given back by a long rest). Nine Lives: recovery checks ignore the dying value. Exemplar's Scar of the Survivor gives Diehard.

#### 1.2 to 1.19 The heritages (16 November 2026)

Tests: `CFHeritageEffectsTest`. 113 of 149 heritages have an effect, up from 84.

- [x] `CFGrantTemperatureTolerance` on `CFTemperatureTolerance`, read by `CFEnvironmentRules.Eased` after gear.
- [x] `CFBonusStat.ManeuverDc` and `Maneuvers`, applied in `CFConditionSystem.RollSkill` for Athletics checks with the attack trait.
- [x] `CFGrantSeek` on `CFSeekSense`: a Seek bonus in range, and easier flat checks against concealed and hidden creatures in range.
- [x] `halfDistance` on `CFGrantSafeFall`; `CFGrantClimb` on `CFCombatOptions`, read by `CFZLevelSystem.Climb`.
- [x] `deadly`, `versatile`, `traitsOnly` on `CFGrantUnarmedAttack`.
- [x] `kind: DamageType` in `choices`, and `CFGrantChosenResistance`, which reads the pick with `CFCharacterFeatureSystem.Pick`.
- [x] `CFDailyUses` on an action entity, `CFDailyUseSystem`, restored by a long rest, greyed on the HUD. `Actions/heritages.yml`: Shift Colors, Jinx, Tail Toxin, Shadow Blur, Grow Fruit (`CFLeshyFruit`).
- [x] `CFBonusWhile.HandsFree`; `meleeOnly` on a reaction; `CFGrantTrait`; `StrongBlooded` and `EasierBurning` options; an action's effects carry its creature's traits.

#### 1.8 Scent trails, from Starlight (16 November 2026)

Tests: `CFScentTest`. Ported from Starlight's Scent system with permission, credited in each file, cut down to what CF needs.

- [x] `Content.*/_CF/Scent/`: `CFScent` (every character at spawn, and `CFBaseMonster`) emits `CFScentMarker` marks every 1.5 seconds, merging into the last one within a tile. They last 300 seconds and sit on visibility layer 64, so only a smeller that is sniffing is sent them.
- [x] `CFSmeller` with the free `CFActSniff` action, from `CFGrantSense` with `Scent` and on the wolf. `CFScentServerSystem` adds the layer to the eye's mask; `CFScentClientSystem` colours each mark by its scent id, scales it by strength and fades it.
- [x] The two icons are in `Resources/Textures/_Starlight/Interface/Actions/actions_scent.rsi`, with attribution.

### Step 2: Finish the rebuild

Left from re-importing the other machine's lost work. Details of what was rebuilt are under "Rebuild status" above.

- [x] The `Chemicals` guide failure. That entry no longer exists, and all 57 guide entries pass.
- [x] Recipes: the 59 from `recipes.py` are in `recipes_ported.yml`. Known limits: stack groups use the plain material, so birch planks do not count. Cooking recipes wait for a station. 73 results are not ported (clothing, backpacks, vials, CP14's key and lock items). 4 need an unported ingredient: Screwdriver, ElfPlushie, AgaricPlushie, SkeletonPlushie.
- [x] Recipes of our own for the Phase 3 weapons, in `recipes_weapons.yml`.
- [x] `Sharp` has no replacement: butchering goes through the kitchen spike. `ToggleableLightVisuals` is now `ToggleableVisuals`. Both are in `ss14rules.md`.
- [x] `CFSiteWallStone` is back to `CFWallStone`. Standing has a test: `StandingCostsOneActionAndWaitsForIt`.
- [x] `codebase.md` and `ss14rules.md` have the new folders and the engine differences from the port.

### Step 5: AI

The labels are the sections of Phase 6 above, which has the design. `goap.md` is how it works now.

**6g. Verify**

- [x] Measure how much slower GOAP is than stock HTN, with the same mobs on the same map. Measured on 10 November 2026 with `CFGOAPStressTest`, results in `ai-testing.md`. At 30 mobs GOAP costs about 1 ms per tick against stock's 0.1 to 0.4, about 3% of a 30 tick server's time. That is cheap enough to move every mob to GOAP.
- [x] Sleep mode: a brain sleeps with no living player within 60 feet, and wakes when one comes in. It waits 10 seconds after the last player leaves before sleeping, and stays awake while it can see an enemy. Being hurt wakes it and its group. A sleeping mob costs nothing. `CFGOAPSleepSystem`, tests: `CFGOAPSleepTest`.
- [x] GOAP steering is most of its cost. Three speedups on 10 November 2026: feelers look every third tick, long sight rays every 0.25 seconds, and walls and lava are kept on the grid instead of searched for each tick. GOAP AI time fell about 30% idle and 10 to 20% in fights, 0.77 ms for 30 idle mobs. See "Feelers and the safety circle" in `goap.md`.
- [x] Calm goals (`Idle`, `GoHome`) think once a second instead of twice, and cast no long rays. On open floor they cast no rays at all. GOAP AI for 30 idle mobs went from 0.77 to 0.64 ms. The open floor part has not been measured, because the stress map is a maze.
- [x] A mob that opens a door walks through it, then builds its field again from the far side. Tests: `ItWalksThroughADoorThenBuildsANewField`. Not seen in a real client.
- [x] GOAP kobolds deal far less damage than stock ones in the stress test (226 against 1,191 at 30 mobs). This is intended: stock AI spams attacks, and GOAP flanks, repositions, holds back and runs.

**6j. Tactics (6 November 2026)**

- [x] Every CF monster thinks with GOAP, from its base (10 November 2026). Animals use `Beast` tactics, the sewer ooze and skeleton guards `Mindless`, kobold warriors `Default`. Encounters replace tactics only when they name some, and a leader with `keepOwnLists` keeps its own. Stock SS14 mobs keep their own AI. Companions moved to GOAP on 11 November. Tests: `CFMonsterTest`, `CFGOAPEncounterTest`, `CFSkeletonguardTest`. Not seen in a real client.
- [x] Monsters live in a folder per creature type under `Entities/Mobs/Creatures/`, with the type on `CFTrait`. `MonstrousHumanoid` and `Outsider` were added as traits, though the remaster has neither.
- [x] GOAP for companions, eidolons, thralls and constructs (11 November 2026). See the first Do first section. Tests: `CFCompanionGoapTest`.

**6k. Furniture, and a run on the maze map (6 November 2026)**

- [x] `Wander` ran at full speed, could jam in a corridor, and paced at the edge of its home range. Replaced on 13 November 2026 by walks to spots it can reach in a straight line, inside its range, with rests between (see GOAP movement under Do first).

### Seen in a real client

These were open as "not seen in a real client". They were looked at in play by 15 November 2026 and seem to work for now, so they are closed. The text is the old note, with where it came from.

- [x] GOAP movement, 13 November: Not seen in a real client: how any of this looks, and whether the rests and slowing distances feel right. They are profile fields (`arriveRadius`, `wanderRestMin`, `wanderRestMax`, `wanderDistance`).
- [x] chasm edges, 13 November: Not seen in a real client: the drift, the popups, and the Weightless alert that shows while slipping.
- [x] Bard fixes, 13 November: Not seen in a real client: the ring, the lute's tint and glow, songs playing on with the window shut.
- [x] holstering, 13 November: Not seen in a real client: how each weapon looks on the belt and back, and which side it hangs on.
- [x] HUD fixes, 11 November: Not seen in a real client: the slots on the separated layout, and the keys on a deck tab.
- [x] HUD and menus, 11 November: Not seen in a real client: the Level Up tab, its feat and spell windows, and the popup.
- [x] HUD and menus, 11 November: Not seen in a real client: the Respawn button.
- [x] monsters, summons and companions, 11 November: Not seen in a real client: companions fighting by themselves, coming back, fetching, and the smoke puff.
- [x] 1. Money in a wallet: Not seen in a real client: the wallet in a pocket, the counter's till controls, and opening a pallet. A pallet is drawn under the goods on it, so it may need right-click, Open, when something lies on it.
- [x] 2. Monster parts and harvesting: Not seen in a real client: the Harvest verb, the do-after, and the popups.
- [x] 3. PvP only by choice: Not seen in a real client: the icon, and `/pvp` typed in chat.
- [x] Playtest feedback, 9 November: Not seen in a real client: the sparkles and their colours, the aura border, the faster swing, and aim assist with a real mouse.
- [x] Character setup: Not seen in a real client: the ABCJ panel and Accept, the shop inside Character Setup, and how the clothes look on each body.
- [x] 1a. One pipeline for what an option grants: Low-light vision is a dimmer darkvision overlay, chosen by eye and not seen in a real client.
- [x] 1b. The shapes actions come in: Stances: Not seen in a real client: the action button lighting up while in a stance.
- [x] 1b. The shapes actions come in: Spellshape: Not seen in a real client: the widened area overlay and the action button lighting up.
- [x] 1b. The shapes actions come in: Auras: Not seen in a real client, nor is its border (`CFAuraOverlaySystem`).
- [x] Languages: Not seen in a real client: the button and the menu.
- [x] Step 4: Close the rules gaps: The spin, sprite hiding, instrument, Hide verb, Alt+Z and pointing inputs have only been tested through code, not played in a real client.

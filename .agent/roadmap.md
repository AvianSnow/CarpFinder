# CarpFinder roadmap

Ordered plan for the next stretch of work. Each phase builds on the one before it. Tick items off here as they land.

Source of rules: https://2e.aonprd.com/ (Remaster). Source of structure and assets: `C:\project\crystall-punk-14` (**read-only**: copy from it, never edit it).

## Why this order

1. **Effects first.** Spells, alchemical bombs, traps, auras, falling damage and property runes all need "hit an area, roll a save, apply something per degree of success". Building that once stops every later feature from growing its own copy, which is what happened to `CFSpellComponent`.
2. **Item rules before item ports.** If weapons and armor are ported before traits, Bulk and runes exist, every ported prototype has to be edited twice.
3. **Z-levels last, but independent.** They touch maps and transforms, not combat. They can start any time someone is free. Falling damage uses the Phase 1 damage effect.
4. **The rest of CP14 comes over too, under PF2e rules (Phase 3b).** Mapping furniture and props can start any time. Items you carry wait for Bulk and price from Phase 2. Nothing is ported that adds a game system PF2e does not have.

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
- [ ] Damage type fields are still strings (`damageType: slashing`) on weapons and pets. Spells now use `ProtoId<CFDamageTypePrototype>` with PascalCase ids (Phase 1). Convert weapons and pets when Phase 2 rewrites them.
- [ ] 16 `_CF` systems subscribe in `Initialize()` instead of with `[SubscribeLocalEvent]` attributes. Convert each one when that system is next touched, not as a bulk change.

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
- [ ] Critical specialization effects by group (later; needs class features).

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
- [ ] Give the `Trip` weapon trait a use, such as counting as an extra shove.
- [ ] Attack-trait maneuvers (Shove, Disarm, Grapple, Escape) should count toward MAP.
- [x] Shove degrees match Player Core: 5 feet, 10 feet on a critical success, and the shover falls prone on a critical failure.
- [ ] A visible raised state. Today it is a popup and an examine line. The riot shield RSI has no raised sprite.

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

- [ ] Companions have no inventory, so nothing can wear `CFRatArmor`. Barding also needs an AC path: `ArmorFromEquipment` is off for creatures with printed AC.
- [ ] Take Cover gives only the AC bonus. Cover also adds to Reflex against area effects and to Stealth.
- [ ] CP14's magic staves and the ice bow (spell storage on mana) wait for spellcasting staves.
- [ ] The longbow and shortbow share CP14's one bow sprite.
- [ ] A sling sprite, and its own ammunition. It still shoots arrows and needs wielding.

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
- [ ] Tools, materials, keys and locks (Pick a Lock / Force Open).
  - Doors were ported without their locks. `Entities/Structures/Doors/Locked` and every `CP14Lock` were left out, so all doors open freely until this row adds locks back.
  - Locks landed in `Content.Shared/_CF/Locks`: `CFLock` on top of the stock `Lock`, `CFKey`, `CFThievesToolkit`, `CFCrowbar`, Pick a Lock and Force Open verbs, and the five Player Core grades as parents (`CFLockPoor` to `CFLockSuperior`). CP14's key shapes, key file and lock editor are dropped: PF2e has no such minigame, and a key matches on `keyId`.
  - Still to do for locks: give the ported doors, chests, gates and safes a grade and a `keyId` (`Doors/Locked`, `Keys/Catalog`), keyrings, portable lock items, repairing a broken toolkit, and shoddy improvised picks. A critical Force Open unlocks but does not swing the door open.
  - Tests: `Content.IntegrationTests/Tests/_CF/Locks/CFLockTest.cs`.
- [ ] Workbenches and recipes on the PF2e Crafting rules.
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
- [ ] Creatures with PF2e stat blocks.
  - The stat block system landed. `CFCombatant` has `printedFortitude`, `printedReflex`, `printedWill`, `printedPerception`, `printedAttack` and `printedDamage`, which override the derived values. Before this, every monster rolled Commoner saves and Perception, whatever its YAML comment said.
  - Monsters set `diesAtZeroHp`: PF2e monsters die at 0 HP, with no dying track. Defeating one raises `CFCreatureDefeatedEvent`, and `CFExperienceSystem.AwardCreature` gives the killer's company XP from the adversary table. A kill by a hazard or the climate gives none.
  - Immunities and printed resistances are `CFResistances` entries in YAML. `expires` 0 means permanent. Immunity to a damage type is `amount: 9999`.
  - Seven creatures so far, in `Entities/Mobs/Creatures/creatures.yml`, all from Monster Core or Monster Core 2: giant rat, boar, grizzly bear, giant frog, hunting spider, sewer ooze, velociraptor. The kobold warrior has its printed saves now too. `CFCreatureTest` holds their numbers as a table.
  - Each creature has one Strike, its first printed melee Strike. A second Strike needs a way to pick between attacks. Special abilities wait for the planned better AI, not for this row. They are not in: Grab, venom and disease, Ferocity, charges, Pack Attack, Filth Wave, the ooze's extra acid. The stat block comment on each creature lists what is missing. Immunity to critical hits and precision is not in either.
  - Size does not change a creature's body yet, so the Large bear is a Medium body.
  - No CP14 AI differences: all use `SimpleHostileCompound` and hunt on sight. CP14's retaliating herd animals are not ported.
  - Still to port: livestock (pig, sheep, rabbit, cat; there is no printed stat block for most), undead (zombie, skeleton: these need CP14's humanoid species), watchers, spineguard, flem, cackle, bone hound, mole, myconids, ice spectre, whistler, mosquito, hydra, fairy. CP14-only monsters need a PF2e creature of a matching level chosen first.
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

## Phase 4 — Z-levels (verticality)

CP14 implements Z-levels as separate maps stacked in a station, linked by portals. No engine change is needed.

### Port (server)

- [ ] `CP14StationZLevelsComponent` → `Content.Server/_CF/ZLevels/CFStationZLevelsComponent` (levels list with a map path per level, default level).
- [ ] `CP14StationZLevelsSystem` (+ `.Portals`, `.Actions`) → `CFStationZLevelsSystem`: loads the level maps on `StationPostInitEvent`, `GetMapOffset`, auto-portals linked through `LinkedEntitySystem`.
- [ ] `CP14ZLevelAutoPortalComponent` → `CFZLevelAutoPortalComponent`.
- [ ] `CP14CombineMapsIntoZLevelsCommand` → `cf-combineMapsIntoZLevels` admin command.

### Port (shared and prototypes)

- [ ] `CP14ZLevelMoverComponent` and `ZLevelActions` → `CFZLevelMover`, for ghosts and admins.
- [ ] Find and port the stairs, ladder, hole and trapdoor portal prototypes and their actions (search `Resources/Prototypes/_CP14` for `CP14ZLevelAutoPortal` and `CP14ActionZLevel`).
- [ ] A test station: two or three small maps (cellar, ground, upper floor) in a `gameMap` with `CFStationZLevels`.

### PF2e hooks

- [ ] Falling through a hole: bludgeoning equal to half the distance in feet, land prone. Uses the Phase 1 `Damage` effect.
- [ ] Ladders and steep stairs: Climb (Athletics) check option, or a plain move action.
- [ ] Confirm area effects, flanking sensors and NPC targeting stay on one level (separate maps should guarantee it; add a test).

## Known bugs

- `CFProneTest.NpcWithNoRoomKeepsTryingToStand` fails now and then. The same error also hit `NpcStandsOnceItHasAnAction`: an NPC's stand-up attempt adds a `StatusEffectContainerComponent` to a mob that is already being deleted, and the assert surfaces in the next test's setup. Left alone on purpose. The planned GOAP AI replaces this behaviour, and NPCs that cannot stand will crawl away instead.

## Later (after these phases)

- Sky ships with FTL travel that dock on breathable planets below, for exploring. Those planets get dungeons too. Floating caves in the sky are the adventure sites until then. `CFAdventureSiteSystem.Generate` always makes its own map, so planet dungeons need it to build on an existing map as well.
- Alchemical bombs and elixirs ported from CP14 alchemy, built on `CFAreaEffect` + `CFCheck`.
- Hazards and traps (area + check + degree effects). Prototype done: `CFHazard` and the Beginner Box spike trap (`CFSpikeTrap`, `CFSpikeTrapMonster`). Still to do: complex hazards with routines, resetting traps, area traps (`CFAreaEffect` on the hazard), and a kobold trapper that lays `CFSpikeTrapMonster`.
- Auras and emanations for class features (champion, bard compositions).
- Class features and feats marked `TBA` turned into effect lists.
- Persistent damage and resistances/weaknesses everywhere damage is dealt.

## Working notes

- Every phase ends with a full build (`dotnet build -c Tools`, errors only) and targeted integration tests.
- Before subscribing a component and event pair, check `ss14rules.md`: `ProjectileComponent` + `BeforeProjectileHitEvent` already belongs to `CFStrikeSystem`.
- Imported CP14 code keeps its logic but takes CF names, namespaces and `_CF` folders. Credit CP14 in a header comment where a file is mostly theirs.

# CF Character Builder + combat (MVP)

## Character builder (lobby)

- Overlay UI lives under `Content.Client/_CF/Lobby/UI/` (see `lobby-overlay.md`).
- Profile field: `HumanoidCharacterProfile.CFBuild` (`CFCharacterBuildData`) — background, class, ancestry free boosts, background choice/free boosts, four finishing free boosts, background/class trained skill picks.
- Ancestry is `Profile.Species` resolved through `cfAncestry` prototypes.
- Attribute math is PF2e modifiers (+0 base, boost +1, flaw −1) via `CFAttributeMath`.
- Level 1 buttons: **Abilities** (`BoostAssignmentView`), **Skill** / **Class** (`SkillAssignmentView`).
- Skill Training: one slot per background `trainedSkills`/`skillBoosts`; player assigns distinct skills (defaults from background).
- Backgrounds: `Resources/Prototypes/_CF/Character/Background/`. Player Core and the other Archives of Nethys backgrounds. Each restricted attribute boost is listed (the builder always adds one free boost). A Lore subcategory is the `Lore` skill; the specific lore is named in the description. Skill feats are named only; they are not granted.
- Class Skill: `additionalSkillTrainings + max(0, Int)` free picks; excludes fixed class + background skills.
- Sheet preview (HP / AC / saves) uses `CFAttributeMath.Snapshot`.
- Spells tab (`SpellsTab.xaml`) lists slots by rank. A class with `spellcasting` uses its cantrips and slot counts (Bard: 5 cantrips, two rank-1 spells). Other classes show a preview sheet (5 cantrips, 3/2/2). Slot buttons use the standard button style. `SpellSelectionPopup.xaml` filters `cfSpell` prototypes by tradition and rank. Choices are stored on `CFCharacterBuildData.Spells`. Skill feats and spell descriptions are short mechanical notes.

## Spawn

- `Content.Server/_CF/Character/CFCharacterSpawnSystem` listens to `PlayerSpawnCompleteEvent` and applies:
  - `CFAbilityScoreComponent` (modifiers)
  - `CFCombatantComponent` (level, AC, HP, class/background ids, trained skill picks)

## In-game character sheet

- Overlay: `Content.Client/_CF/UserInterface/Systems/Character/` (same csproj Remove pattern as lobby — stock `CharacterWindow` / `CharacterUIController` stay on disk).
- Sheet UI: `Content.Client/_CF/Character/CFCharacterSheetTab` — abilities, AC/HP/saves, 3-column skills. Roll targets are white `Label`s (hand cursor, `UIClick`), same pattern as crystall-edge `DndSheetSkillRow`. A Font Awesome d20 (`CFDiceLabel`, U+F6CF, `Resources/Fonts/_CF/FontAwesome/fa-solid-900.ttf`) sits beside each ability, save, and skill name. Click the die, the name, or an ability modifier to emote a d20 check.
- Skill/save math: `CFSkills` + class/background proficiency lists. Ability “scores” shown as `10 + 2×mod` (CF stores modifiers).

## Combat (lean PF2e Strike)

- `CFMeleeWeapon` on weapons + `CFCombatant` + `CFAbilityScore` on attackers/targets.
- `CFStrikeSystem` handles `MeleeHitEvent` on CF weapons: cancels stock damage, rolls d20+mod vs AC, applies CF HP. Marking the hit handled skips the stock sounds. A melee hit plays `soundHit`. A melee miss plays `soundSwing` at the target. A CF weapon with no `soundHit` uses a slash or smash sound from its damage type. A swing that touches nothing still uses the stock swing sound.
- Floating numbers (`CFDamageNumberSystem` / `CFCombatNumberEvent`): **HIT / MISS / CRIT HIT / CRIT MISS**, then damage amount on hits.
- Entities:
  - `CFLongsword` — d8 slashing martial weapon
  - `CFLeatherArmor` — +1 AC, Dex cap 3, LightArmor
  - `CFTrainingDummy` — AC 15, 20 HP (`spawn CFTrainingDummy`)
- Vanilla targets (`CFCombatBridgeSystem`):
  - **Mobs** (have `MobState`): treated as PF2e **Commoner** (−1, AC 13, HP 10) on first Strike.
  - **Objects**: inferred `cfMaterial` (hardness/HP) from `damageModifierSet` / `damageContainer`, or set `CFMaterial` explicitly. Hardness subtracted from damage; 0 HP breaks/destroys via Destructible.
  - CF HP is the source of truth. `DamageableSystem.SetDamage` rewrites the SS14 bar as Blunt from missing CF HP, kept under the Critical threshold. Dying sets that bar to Critical; death sets it to Dead.
- **Unarmed**: empty hands (body or gloves `MeleeWeapon`) use PF2e fist Strike — 1d4 bludgeoning, `Unarmed` proficiency — instead of stock SS14 blunt.
- **Ranged**: `CFRangedWeapon` on bows; on projectile collide (`BeforeProjectileHitEvent`) resolve Dex Strike (range increments, volley −2, deadly die). `CFLongbow` — 1d8 P, range 100 ft, deadly d10, volley 30 ft (wield to shoot, nock `CFArrow`).
- **Dying (PF2e)**: at 0 HP → SS14 `Critical` + dying 1 (2 on crit knockout) + wounded. Recovery flat check every 6s (DC 10 + dying). Dying 4 → `Dead`. Strikes on downed raise dying (+2 on crit). Alt-click **Stabilize (Medicine)** (DC 5 + dying). Stabilize revives at 1 HP and clears bleed; `RestoreHitPoints` heals CF HP + `HealEvenly` across SS14 damage types. Stock rejuvenate restores CF HP to max and clears dying, then flanking is checked again.
- **Alchemy / potions**: `CFAlchemicalItem` + optional `CFHealConsumable` under `Content.Shared/_CF/Alchemy/`. PF2e traits as Tags (`CFConsumable`, `CFHealing`, `CFMagical`, `CFPotion`, `CFVitality`, …) in `Resources/Prototypes/_CF/tags.yml`. `CFHealingPotionMinor` — 1d8 HP (`spawn CFHealingPotionMinor`).
- **Flanking / off-guard**: `CFFlankingSystem`. Positions are world coordinates, not tiles. Two allies flank a foe when each is within reach (`CFMeleeWeapon.ReachFeet`, default 5 ft = 1.25 units; reach trait 10) and the line between them passes within `CFFlanking.BodyRadius` (0.5) of the foe, so the foe is between them. Sharing a tile still counts. Both must be able to act and melee (weapon or free hand / unarmed). The foe gains `CFOffGuardComponent`: −2 circumstance AC against those flankers' melee Strikes only. Dead and dying creatures (`MobState` not Alive, or CF dying / 0 HP) never receive it, and it is cleared when they drop. Allies are `CFAlliance.Party` (two Alphas flank together; Alpha and Beta do not). Creatures with no CF faction fall back to stock NPC friends, or two players with no NPC faction. The `CFFlankedIcon` status icon is drawn on the right. While debugging, `CFOffGuardLabelSystem` also draws green `OffGuard` text above any creature with the condition active.
- **Factions**: `CFFactionSystem` and `cfFaction` prototypes in `Resources/Prototypes/_CF/Factions/factions.yml`. Alliances are Party, Opposition, and Neutral. Same-id members use the prototype `same` field (adventure companies are Party; Neutral's `same` is Neutral). A Party target is dropped before the strike: no AC, no d20, no damage. Melee cancels in `GettingAttackedAttemptEvent`; projectiles zero damage and return in `CFStrikeSystem.OnBeforeProjectileHit`. Opposition can damage and is added to NPC hostile queries. Neutral is not hunted and does not hunt other neutrals, but can still be struck. Jobs map on spawn: `CFAlpha`/`CFBeta`/`CFCharlie`/`CFDelta` to those factions; merchant and arcane jobs to Neutral.

## Loadouts (Weapons / Defense / Gear)

- Prototypes: `Resources/Prototypes/_CF/Loadouts/cf_loadouts.yml`
- `roleLoadout` ids are `JobCFAlpha` … (must match `Job` + job id).
- Groups use `editorTab: Weapons|Defense|Gear` so builder tabs populate.
- Default: longsword (required) + leather armor selected.

## Dev / Sandbox spawn checklist

`cfJobGroup` only fills the **character builder** job list. Round join needs:

1. Job on the map's `StationJobs.availableJobs` — Dev is patched in `Resources/Prototypes/Maps/debug.yml`.
2. A real `department` listing those jobs — `Resources/Prototypes/_CF/Roles/departments.yml` (late-join GUI).
3. Development preset: `Resources/ConfigPresets/Build/development.toml` must keep `ic.random_characters = false` or spawn replaces your profile (and wipes `CFBuild`).
4. Set High priority on a CF job (e.g. Alpha), save, Ready / Join.

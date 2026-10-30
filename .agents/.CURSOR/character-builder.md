# CF Character Builder + combat (MVP)

## Character builder (lobby)

- Overlay UI lives under `Content.Client/_CF/Lobby/UI/` (see `lobby-overlay.md`).
- Profile field: `HumanoidCharacterProfile.CFBuild` (`CFCharacterBuildData`) — background, class, ancestry free boosts, background choice/free boosts, four finishing free boosts.
- Ancestry is `Profile.Species` resolved through `cfAncestry` prototypes.
- Attribute math is PF2e modifiers (+0 base, boost +1, flaw −1) via `CFAttributeMath`.
- **Set Abilities** opens `BoostAssignmentView` for free/choice boost slots.
- Sheet preview (HP / AC / saves) uses `CFAttributeMath.Snapshot`.

## Spawn

- `Content.Server/_CF/Character/CFCharacterSpawnSystem` listens to `PlayerSpawnCompleteEvent` and applies:
  - `CFAbilityScoreComponent` (modifiers)
  - `CFCombatantComponent` (level, AC, HP, class id, background id)

## In-game character sheet

- Overlay: `Content.Client/_CF/UserInterface/Systems/Character/` (same csproj Remove pattern as lobby — stock `CharacterWindow` / `CharacterUIController` stay on disk).
- Sheet UI: `Content.Client/_CF/Character/CFCharacterSheetTab` — abilities, AC/HP/saves, 3-column skills; click name/mod to emote a d20 check.
- Skill/save math: `CFSkills` + class/background proficiency lists. Ability “scores” shown as `10 + 2×mod` (CF stores modifiers).

## Combat (lean PF2e Strike)

- `CFMeleeWeapon` on weapons + `CFCombatant` + `CFAbilityScore` on attackers/targets.
- `CFStrikeSystem` handles `MeleeHitEvent` on CF weapons: cancels stock damage, rolls d20+mod vs AC, applies CF HP.
- Floating numbers (`CFDamageNumberSystem` / `CFCombatNumberEvent`): **HIT / MISS / CRIT HIT / CRIT MISS**, then damage amount on hits.
- Entities:
  - `CFLongsword` — d8 slashing martial weapon
  - `CFLeatherArmor` — +1 AC, Dex cap 3, LightArmor
  - `CFTrainingDummy` — AC 15, 20 HP (`spawn CFTrainingDummy`)
- Vanilla targets (`CFCombatBridgeSystem`):
  - **Mobs** (have `MobState`): treated as PF2e **Commoner** (−1, AC 13, HP 10) on first Strike.
  - **Objects**: inferred `cfMaterial` (hardness/HP) from `damageModifierSet` / `damageContainer`, or set `CFMaterial` explicitly. Hardness subtracted from damage; 0 HP breaks/destroys via Destructible.
  - CF damage is mirrored into SS14 `Damageable` (scaled to mob dead threshold).
- **Unarmed**: empty hands (body or gloves `MeleeWeapon`) use PF2e fist Strike — 1d4 bludgeoning, `Unarmed` proficiency — instead of stock SS14 blunt.
- **Ranged**: `CFRangedWeapon` on bows; on projectile collide (`BeforeProjectileHitEvent`) resolve Dex Strike (range increments, volley −2, deadly die). `CFLongbow` — 1d8 P, range 100 ft, deadly d10, volley 30 ft (wield to shoot, nock `CFArrow`).
- **Dying (PF2e)**: at 0 HP → SS14 `Critical` + dying 1 (2 on crit knockout) + wounded. Recovery flat check every 6s (DC 10 + dying). Dying 4 → `Dead`. Strikes on downed raise dying (+2 on crit). Alt-click **Stabilize (Medicine)** (DC 5 + dying). Stabilize revives at 1 HP and clears bleed; `RestoreHitPoints` heals CF HP + `HealEvenly` across SS14 damage types.
- **Alchemy / potions**: `CFAlchemicalItem` + optional `CFHealConsumable` under `Content.Shared/_CF/Alchemy/`. PF2e traits as Tags (`CFConsumable`, `CFHealing`, `CFMagical`, `CFPotion`, `CFVitality`, …) in `Resources/Prototypes/_CF/tags.yml`. `CFHealingPotionMinor` — 1d8 HP (`spawn CFHealingPotionMinor`).

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

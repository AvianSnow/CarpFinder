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
  - `CFCombatantComponent` (level, AC, HP, class id)

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

# CarpFinder — Code Instructions

CarpFinder (CF) is a fork of Space Station 14 built on RobustToolbox. It uses an ECS architecture with C# on both server and client.

## Editing and Making Documents

Whenever you understand how SS14 is built and structured, add or update docs under `.agent/` (e.g. `ss14.md`). Warnings and errors to avoid are in `ss14rules.md`.

You may edit this document with any rules and information that make the project easier to work on.

Read and follow everything in this folder before making CF or upstream-touching changes.

Character builder + PF2e spawn/combat MVP notes: `character-builder.md`.

How GOAP is laid out and how to add to it: `goap.md`. What its weights are for, and the plan for mobs casting spells: `goap-weights.md`. Where mappers place AI hints: `investigation-nodes.md`.

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
| A subclass option (Racket, Instinct, Muse, ...) or a deity | A `cfClassChoice` or `cfDeity` prototype. Read a build's picks through `CFClassChoices`, not from `CFCharacterBuildData` directly |
| Spell / class / fixed DC and attack bonus | `CFCasting.Spellcasting`, `ClassDc`, `Fixed` (`Content.Shared.CF.Checks`). Cantrip rank: `CFCasting.CantripRank(level)` |
| Anything that targets, rolls a check, and applies effects (spells, bombs, traps, auras) | Put `CFTargeting`, `CFAreaEffect`, `CFCheck`, `CFDegreeEffects` (and `CFProjectileDelivery`, `CFModeChoice`, `CFHeighten`) on the source, then call `CFEffectSystem.DeliverToTarget` / `ResolveArea` / `ResolveTarget` (`Content.Shared.CF.Effects`). Do not write a new resolver. |
| Bonuses, fortune, action caps, resistances, persistent damage | `CFBonusSystem` (`AddBonus`, `AddPermanentBonus`, `Total`, `TryConsumeFortune`, `SetActionCap`, `AddResistance`, `AddPermanentResistance`, `ModifyDamage`, `AddPersistent`). A bonus can name a `trait` it counts against, or a `skill` |
| Give a condition | `CFConditionSystem.ApplyCondition(target, CFConditionKind, value, seconds, source, dc)` |
| Weapon stats for a Strike | `CFWeaponSystem.TryStats(weapon)` → `CFStrikeWeaponStats`; traits through `CFWeaponTraits` (never add weapon booleans: give the `cfTrait` a `rule`) |
| Worn armor, check penalty, Speed penalty, AC refresh | `CFArmorSystem` (`TryGetWornArmor`, `CheckPenalty`, `SpeedPenalty`, `RefreshArmorClass`) |
| Shields | `CFShieldSystem` (`TryGetRaisedShield`, `TryGetUsableShield`, `SetRaised`, `RaisedBonus`, `Block`). Raised is `CFShieldComponent.Raised` on the item. |
| Bulk and encumbrance | `CFBulk`, `CFEncumbranceSystem`; clumsy from `CFConditionSystem.Clumsy` |
| A group that fights together | An `encounter` prototype in `Resources/Prototypes/_CF/GOAP/encounters.yml`, placed with `CFEncounterSpawner`. It gives any mob the brain at spawn, so mobs do not need their own GOAP prototype |
| An advanced NPC (mini boss, special encounter) | Add `CFGOAPBrain` as a second parent. Do not use it for ordinary mobs: stock HTN stays their AI. Set `CFGOAP.profile` to a `cfGoapProfile` and `CFGOAP.tactics` to a `goapTactics` |
| A stance or a flourish | A stance is an action parented on `CFActStanceBase` with a `CFStance` (`Resources/Prototypes/_CF/Actions/stances.yml`). A flourish is any action with `CFFlourish`. Grant either with `CFGrantAction` |
| An option where the player picks a cantrip or skill | A `choices` list on the `cfFeat`, `cfHeritage` or `cfClassChoice`. Read answers through `CFOptionChoices`, not `CFBuild.OptionPicks` directly |
| A bonus that only counts while raging, with panache, or unarmored | `!type:CFGrantWhile`, run by `CFWhileBonusSystem` |
| What a Barbarian instinct does to Rage | `!type:CFGrantRage` (`damage`, `attackDie`, `attackType`) |
| An aura, or something an aura does | `!type:CFGrantAura` with a list of `effects`. `CFAuraSystem` runs them. Do not add a physics sensor for one |
| Taunting an enemy | An action with `CFTauntAction` (`Resources/Prototypes/_CF/Actions/taunts.yml`), or `tauntsTarget: true` on a `CFStrikeAction`. `CFTauntSystem` runs it |
| A weapon whose damage type can be switched | `CFDamageMode` with a `types` list. Use in hand cycles it for 1 action, and the hand status shows it. `CFWeaponSystem.TryStats` reads it |
| Something a Thaumaturge needs in hand | `CFImplement` on the item, `CFImplementSystem.IsHolding`, and `CFNeedsImplement` on an action |
| Amping a psi cantrip | `CFPsiCantrip` on the spell's action, with what the amp changes. `CFPsiSystem` spends the Focus Point |
| A Commander's tactic | An action with `CFTacticAction` in `Resources/Prototypes/_CF/Actions/tactics.yml`, and a `cfClassChoice` entry per tactic slot. `CFTacticSystem.Squad` says who answers |
| Something that needs an Inventor's innovation | `CFInnovation` on the item or construct, `CFInventorSystem.IsUsing`, and `unstable: true` on a `CFStrikeAction` |
| An action that only applies effects | An action parented on `CFActivityInstant` or `CFActivityTarget` with `CFEffectAction` and `CFDegreeEffects` (`Resources/Prototypes/_CF/Actions/effects.yml`). Grant it with `CFGrantAction` |
| A Swashbuckler finisher | A Strike action with `finisher: true` on its `CFStrikeAction`. It needs panache and spends it |
| An action that is a Strike with something extra | An action parented on `CFActStrikeBase` with a `CFStrikeAction` (`Resources/Prototypes/_CF/Actions/strikes.yml`). Say what it needs and put effects in `onHit` and `onCritical` |
| A focus spell | An action in `Resources/Prototypes/_CF/Spells/Focus/focus.yml` with `focus: true` on its `CFSpell` and rank 0, and a `cfSpell` with no traditions. Grant it with `CFGrantFocusSpell` |
| A spellshape | An action parented on `CFActSpellshapeBase` with a `CFSpellshape` (`Resources/Prototypes/_CF/Actions/spellshapes.yml`). Grant it with `CFGrantAction` |
| A reaction | A `CFGrantReaction` with a `CFReaction` (trigger, requirements, what it does). `CFReactionSystem` runs it and spends the creature's one reaction. Add a trigger there rather than a new flag on `CFCombatOptionsComponent` |
| Actions for code with no player | `CFManeuverSystem.CanUse` and `TryUse` with a `CFBasicAction`, `CFStrikeSystem.TryFeatStrike` |
| Whether a creature can see another | `CFStealthSystem.Detection`, `IsUnseenBy`, `Reveal`. Spending actions reveals a creature unless `TrySpendActions` is told `stealthy` |
| Traps and hazards | `CFHazard` (Stealth DC, detect radius, trigger size, disarm checks) plus `CFCheck` and `CFDegreeEffects` for what it does. `CFHazardSystem` handles noticing, triggering, and disarming. A `CFFaction` on the hazard protects its allies. Disabling it raises `CFHazardDisarmedEvent`; `CFExperienceSystem.AwardHazard` gives the disarmer's company XP for the hazard's `level` (simple or `complex`). See `Resources/Prototypes/_CF/Entities/Hazards/traps.yml`. |
| A creature that builds traps | `CFTrapLayer` with a hazard prototype, and `CFTrapLayerSystem.TryLay`. The trap takes the builder's faction, so allies never set it off. Do not make an AI walk around one. |
| Locks on doors and containers | Add a grade as a second parent: `parent: [ WoodDoor, CFLockSimple ]`, with `forceDc` and `keyId` on `CFLock`. See `Resources/Prototypes/_CF/Entities/Objects/Tools/locks.yml`. `CFLockSystem` blocks stock lock toggles and opening a locked door; the stock `Lock` alone does not stop a door. |
| New crafted item | Add a `craftingRecipe` in `Resources/Prototypes/_CF/Crafting/recipes.yml` and a `CFFormula<Id>` item in `Entities/Objects/Formulas/formulas.yml`. Level and rarity come from the result's `CFItem`. Formula prices are the Player Core table by item level. A new station is a `CFBaseWorkbench` child with a `recipeTags` tag. |
| New monster | Follow `importcreatures.md`. Copy an entry in `Resources/Prototypes/_CF/Entities/Mobs/Creatures/` and set the printed stats from Archives of Nethys. Set `diesAtZeroHp: true`. Give it a `GhostRole` name and description, so a player can take it. |
| Any generated map | Call `CFOutdoorsSystem.ApplySkyWorld(map)` first. CF takes place in the sky, on floating islands with airships, not in space, so every generated map needs the breathable air and `Sky` parallax from the `CFSkyWorld` prototype. |
| Money | `CFShopSystem.Purse(user)` for a seeded purse. Sale value is `CFMerchantSystem.SaleValue(item)`. Set `fullResale: true` on `CFItemPrice` for gems, art objects and raw materials. |
| Languages | `CFLanguageSystem.Understands`, `TryGetSpoken`, `TrySpeak`. A creature with no `CFLanguages` understands everything |
| Skill proficiency rank | `CFDefense.SkillRank(prototypes, combat, skill)` |
| Area tiles (burst, cone, emanation, line, square) | `CFAreaGrid.Collect` (`Content.Shared.CF.Effects.Areas`), 1 tile = 5 ft |
| Damage type data (SS14 type, colour, hit sound) | `cfDamageType` prototypes, resolved with `CFDamageTypes.TryResolve` (`Content.Shared.CF.DamageTypes`). Matching ignores case and checks aliases, so `slashing` resolves to `Slashing`. |

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

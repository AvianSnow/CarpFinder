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

The ordered feature plan is `roadmap.md`. Check it before starting a feature and tick items off when they land.

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
| Save / Perception / skill modifier | `CFDefense.SaveModifier`, `PerceptionModifier`, `SkillModifier` with `CFDefenseType` |
| Spell / class / fixed DC and attack bonus | `CFCasting.Spellcasting`, `ClassDc`, `Fixed` (`Content.Shared.CF.Checks`). Cantrip rank: `CFCasting.CantripRank(level)` |
| Anything that targets, rolls a check, and applies effects (spells, bombs, traps, auras) | Put `CFTargeting`, `CFAreaEffect`, `CFCheck`, `CFDegreeEffects` (and `CFProjectileDelivery`, `CFModeChoice`, `CFHeighten`) on the source, then call `CFEffectSystem.DeliverToTarget` / `ResolveArea` / `ResolveTarget` (`Content.Shared.CF.Effects`). Do not write a new resolver. |
| Timed bonuses, fortune, action caps, resistances, persistent damage | `CFBonusSystem` (`AddBonus`, `Total`, `TryConsumeFortune`, `SetActionCap`, `AddResistance`, `ModifyDamage`, `AddPersistent`) |
| Give a condition | `CFConditionSystem.ApplyCondition(target, CFConditionKind, value, seconds, source, dc)` |
| Weapon stats for a Strike | `CFWeaponSystem.TryStats(weapon)` → `CFStrikeWeaponStats`; traits through `CFWeaponTraits` (never add weapon booleans: give the `cfTrait` a `rule`) |
| Worn armor, check penalty, Speed penalty, AC refresh | `CFArmorSystem` (`TryGetWornArmor`, `CheckPenalty`, `SpeedPenalty`, `RefreshArmorClass`) |
| Shields | `CFShieldSystem` (`TryGetRaisedShield`, `TryGetUsableShield`, `SetRaised`, `RaisedBonus`, `Block`). Raised is `CFShieldComponent.Raised` on the item. |
| Bulk and encumbrance | `CFBulk`, `CFEncumbranceSystem`; clumsy from `CFConditionSystem.Clumsy` |
| A group that fights together | An `encounter` prototype in `Resources/Prototypes/_CF/GOAP/encounters.yml`, placed with `CFEncounterSpawner`. It gives any mob the brain at spawn, so mobs do not need their own GOAP prototype |
| An advanced NPC (mini boss, special encounter) | Add `CFGOAPBrain` as a second parent. Do not use it for ordinary mobs: stock HTN stays their AI. Set `CFGOAP.profile` to a `cfGoapProfile` and `CFGOAP.tactics` to a `goapTactics` |
| Actions for code with no player | `CFManeuverSystem.CanUse` and `TryUse` with a `CFBasicAction`, `CFStrikeSystem.TryFeatStrike` |
| Whether a creature can see another | `CFStealthSystem.Detection`, `IsUnseenBy`, `Reveal`. Spending actions reveals a creature unless `TrySpendActions` is told `stealthy` |
| Traps and hazards | `CFHazard` (Stealth DC, detect radius, trigger size, disarm checks) plus `CFCheck` and `CFDegreeEffects` for what it does. `CFHazardSystem` handles noticing, triggering, and disarming. A `CFFaction` on the hazard protects its allies. Disabling it raises `CFHazardDisarmedEvent`; `CFExperienceSystem.AwardHazard` gives the disarmer's company XP for the hazard's `level` (simple or `complex`). See `Resources/Prototypes/_CF/Entities/Hazards/traps.yml`. |
| Locks on doors and containers | Add a grade as a second parent: `parent: [ WoodDoor, CFLockSimple ]`, with `forceDc` and `keyId` on `CFLock`. See `Resources/Prototypes/_CF/Entities/Objects/Tools/locks.yml`. `CFLockSystem` blocks stock lock toggles and opening a locked door; the stock `Lock` alone does not stop a door. |
| New crafted item | Add a `craftingRecipe` in `Resources/Prototypes/_CF/Crafting/recipes.yml` and a `CFFormula<Id>` item in `Entities/Objects/Formulas/formulas.yml`. Level and rarity come from the result's `CFItem`. Formula prices are the Player Core table by item level. A new station is a `CFBaseWorkbench` child with a `recipeTags` tag. |
| New monster | Copy an entry in `Resources/Prototypes/_CF/Entities/Mobs/Creatures/creatures.yml` and set the printed stats from Archives of Nethys. Add its row to `CFCreatureTest.StatBlocks`. Set `diesAtZeroHp: true`. |
| Any generated map | Call `CFOutdoorsSystem.ApplySkyWorld(map)` first. CF takes place in the sky, on floating islands with airships, not in space, so every generated map needs the breathable air and `Sky` parallax from the `CFSkyWorld` prototype. |
| Money | `CFShopSystem.Purse(user)` for a seeded purse. Sale value is `CFMerchantSystem.SaleValue(item)`. Set `fullResale: true` on `CFItemPrice` for gems, art objects and raw materials. |
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

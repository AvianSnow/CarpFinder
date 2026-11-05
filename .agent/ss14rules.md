# SS14 rules — warnings and errors to avoid

CarpFinder code should compile without these. Do not patch `RobustToolbox/` to silence them. That fork is shared with non-modded clients.

## RA0051 — `[Dependency]` must not be `readonly`

Robust assigns `[Dependency]` fields after the object is constructed. `readonly` will become a compile error.

```csharp
[Dependency] private CFDyingSystem _dying = default!;
```

Do not write `[Dependency] private readonly`. Code copied from crystall-punk-14 always has it; strip it when porting, and make the class `partial`.

## Namespaces never contain `_CF`

The folder is `_CF`; the namespace is `CF`. `Content.Server/_CF/Temperature/` is `namespace Content.Server.CF.Temperature;`. Ported CP14 files arrive as `Content.*._CP14.*`, so a find-and-replace to `._CF.` is still wrong.

## Do not name a CF namespace after an upstream one

Inside `namespace Content.Shared.CF.Combat`, a bare `Damage` resolves to `Content.Shared.CF.Damage` before any `using`. That is why damage types live in `Content.Shared.CF.DamageTypes`, not `CF.Damage`.

## No collection expressions for `List<T>`

The client sandbox rejects `CollectionsMarshal.SetCount`. A collection expression that builds a `List<T>` (`return [item, item];`) compiles to that call and the client dies at startup with `Assembly Content.Client failed type checks`.

Build the list with `new List<T>()` and `Add`. Array expressions (`new[] { "a" }`) are fine.

## No `stackalloc` or `Span<T>` locals in content code

`Span<int> x = stackalloc int[4];` compiles to IL the client sandbox cannot verify. The server and integration tests run fine, but the client dies at startup with `ILVerify: Instruction cannot be verified` and `Assembly Content.Shared failed type checks`. Use plain locals or a normal array. Only a real client start catches this, so start the client after adding low-level code.

## Do not sum SS14 damage

`DamageableSystem.GetTotalDamage`, `GetAllDamage`, and `GetDamagePerGroup` are obsolete: do not treat damage as one number. `DamageableComponent.TotalDamage` and `Damage` are access-restricted to `DamageableSystem`.

Pathfinder hit points are the source of truth. Write the SS14 bar with `DamageableSystem.SetDamage` (one `Blunt` value derived from CF HP and mob thresholds). Do not read the current total and add a delta.

## Do not dispose UI controls

`Control.Dispose()` and `Control.Dispose(bool)` are obsolete (`CS0618`). Overriding `Dispose(bool)` also warns `CS0672`.

Remove a control from the UI tree. Do not add an empty `Dispose` override that only calls `base.Dispose`. The lobby editor overlay (`HumanoidProfileEditor` under `_CF`) follows this.

## Event subscriptions

One directed component + event pair. A second subscriber throws `Duplicate Subscriptions` at startup.

- `MobStateComponent` + `MobStateChangedEvent` is already taken by `SharedStunSystem`. Subscribe to the broadcast, not the component.
- `MobStateChangedEvent` has no `[ByRefEvent]`. Existing handlers are by-value. A `ref` handler throws `Attempted to subscribe by-ref and by-value to the same broadcast event`.
- `ProjectileComponent` + `BeforeProjectileHitEvent` is already handled by `CFStrikeSystem`. Do not add another subscriber. Party blocks and strike resolution belong in that handler.
- Melee friendly-fire cancels `GettingAttackedAttemptEvent` (raised from `CanAttack`) before the strike runs.
- `StandUpAttemptEvent` runs for "could it stand?" checks and several times per stand. Never charge a cost there. A finished stand is `TryStandDoAfterEvent`.
- `CanPull` and `BeingPulledAttemptEvent` run for every verb menu. Anything with a cost or a roll goes on `PullAttemptEvent`, which only `TryStartPull` raises.

## Do not use obsolete APIs

`CS0618` means the API will go away, and code written against it breaks on a later upstream merge. `_CF` code builds with no `CS0618`, `CS0612`, or `RA` warnings. Keep it that way, and fix ported CP14 code as it arrives, because it often uses the old calls.

| Do not use | Use | Notes |
|---|---|---|
| `Spawn(proto, EntityCoordinates)`, `SpawnEntity(proto, EntityCoordinates)` | `SpawnAttachedTo(proto, coords)` | The same behaviour. Use `SpawnAtPosition` to parent to the grid or map under those coordinates instead. |
| `SpawnEntity(proto, MapCoordinates)` | `Spawn(proto, mapCoords)` | |
| `EntityPrototype.TryGetComponent<T>(out comp)`, with or without a factory | `proto.TryComp<T>(out comp, factory)` | In a system use `Factory`. Elsewhere inject `IComponentFactory`. Static helpers take it as a parameter (`CFCoins`). |
| `SharedStackSystem.SetCount(uid, count, comp)` | `SetCount((uid, comp), count)` | Entity overloads replace `EntityUid, Comp?` ones across upstream. |
| `PopupClient`, `PopupPredicted` | `PopupEntity` | Popups predict on their own. `PopupEntity(self, others, uid, recipient)` shows two messages. |
| `xform.LocalRotation = x` | `SharedTransformSystem.SetLocalRotation(uid, x, xform)` | Do not write to `TransformComponent` directly. |
| `sprite.RemoveLayer`, `LayerMapReserveBlank`, `LayerSetSprite`, other `SpriteComponent` methods | `SpriteSystem.RemoveLayer`, `LayerMapReserve`, `LayerSetSprite` | Take `Entity<SpriteComponent?>`. |

The same goes for these, which stock code still uses in bulk:

- `Component.Owner`: pass `Entity<T>` or `EntityUid`.
- `DamageableSystem.GetTotalDamage`, `GetAllDamage`, `GetDamagePerGroup`, `DamageGroupPrototype`, `DamageChangedEvent`: see "Do not sum SS14 damage".
- `StatusEffectsComponent`, the old `StatusEffectsSystem`: use `Content.Shared.StatusEffectNew`.
- `SharedBatterySystem.GetCharge(Entity)`: use the tuple version.
- `PredictedDel`: use `Del`.
- String literals as sounds: use a `SoundSpecifier`.

Stock `Content.*` files have about 860 of these warnings. Leave them: upstream fixes them, and editing them costs merge conflicts. A line you add to a stock file must still use the new API.

## Analyzer warnings

- `RA0049`: a class with `[Dependency]` fields must be `partial`, including `Control` and `UIController` types.
- `RA0003`: a class must be `sealed`, `abstract`, `static`, or `[Virtual]`. A system base class must be `abstract`, with a subclass on each side (`CFSharedWallpaperSystem`, `CFServerWallpaperSystem`, `CFClientWallpaperSystem`). `[Virtual]` is not enough: both the base and the subclass are created, so the base's subscriptions run twice.
- `RA0028`: overrides of `Initialize` and `Shutdown` must call `base`. If that throws `Duplicate Subscriptions`, the base is not `abstract`.
- `RA0022`: `[AutoGenerateComponentPause]` needs an `[AutoPausedField]` on the time it tracks.
- `RA0026`: do not call static `Regex` methods with a pattern. Keep a `static readonly Regex`.

To check, run a full rebuild with a file log, because an incremental build hides warnings from projects it skips:

```powershell
dotnet build -c Tools --no-incremental -nologo -clp:ErrorsOnly "-flp:logfile=build.log;verbosity=normal"
Select-String -Path build.log -Pattern ': warning ' | Where-Object { $_.Line -match '_CF' }
```

## Ported prototypes must bring what they reference

A ported item that names a `stackType`, a `materialComposition` key, or a `ReagentId` that was not ported crashes the moment it is spawned or previewed (`Unknown StackPrototype prototype: CFAsh`, opening the character sheet). Port the CP14 `stack`, `material` and `reagent` prototypes with the items (CF stacks: `Resources/Prototypes/_CF/Stacks/`, materials: `_CF/Materials/`).

Tags are the same: a `Tag` component naming an undefined tag asserts on spawn (`Unknown tag: CFFireplaceFuel`). Port the CP14 tag into `Resources/Prototypes/_CF/tags.yml`.

CP14's `Clothing` has fields ours does not, such as `breakOnMove`. Its `MeleeWeapon` has `cPAnimationLength` and `cPAnimationOffset`, and its arcs are `CP14WeaponArcThrust` and `CP14WeaponArcSlash`. Drop those and use the stock arcs. The YAML linter reports unknown fields.

This engine differs from CP14's fork in more places. A ported prototype needs these changes:

- `damageContainer` lives on `Injurable`, not `Damageable`.
- `IconSmooth` takes `key` and a `states` list (`!type:CornerSpriteSmoothing` with `mask` and `base`), not `base`, `mode` and `additionalKeys`.
- `Drink` is `Edible` with `edible: Drink`.
- Construction has no `stackGroup` step or `crystallPunkAllowed` field. Use a `material` step.
- Tile effects are `!type:Flammable` and `!type:Extinguish`.
- `whitelist` is a mapping with `tags:`, never a bare list.
- A `Box2` must have left below right and bottom below top. CP14 has reversed ones, and Debug builds assert.
- An entity with an empty `components:` key crashes inheritance. Delete the key.
- `Food` is `Edible`. `SliceableFood` is `ToolRefinable` with a `refineResult` list.
- `SatiateHunger` and `SatiateThirst` are `!type:Satiate` with `satiationType`. `ChemVomit` is `Vomit`.
- Reagents metabolize in `Digestion`, not `Food`, `Drink` or `Poison`. `BaseAlcohol` turns into Ethanol there, so put a drink's effects in `Digestion`, not `Alcohol`.
- `SolutionContainerManager` is the obsolete form and can leave an entity with no usable solution, with no error. Use a `Solution` parent plus `type: Solution` with `id:` and `solution:` (see `Entities/Objects/base_solution.yml`). `FoodBase` already gives `Edible` and a `food` solution. Test that a ported entity really holds its reagents.
- `Injector` has no `injectOnly`, `transferAmounts` or `toggleState`. `Temperature` has no `currentTemperature`. `InternalTemperature` has no `thickness`, `area` or `conductivity`.

A prototype that fails to read is logged once and dropped: `Reading ...EntityPrototype(id) threw the following exception`. The first symptom is often an unrelated "invalid id" error from something that refers to it.

To catch this, run `EntityTest.SpawnAndDirtyAllEntities`: it spawns every prototype on server and client and reports unknown prototypes.

## VV cannot add to a list

The VV menu edits strings, numbers, booleans and enums, but it has no way to add an item to a `List` or `HashSet`. A field a DM must extend live has to be plain text, such as comma-separated names. Prototype data is copied per entity on spawn, so editing one entity never changes another.

`EntityQueryEnumerator` skips paused entities, and a map just loaded is paused. Use `AllEntityQueryEnumerator` in tests that read one.

## Fixtures added in code

An entity spawned with an empty `Fixtures` component has `CanCollide` turned off. Creating fixtures later with `FixtureSystem.TryCreateFixture` does not turn it back on, so sensors never report contacts. After adding fixtures, call `SharedPhysicsSystem.SetCanCollide(uid, true)` (see `CFHazardSystem`).

Bodies that stand still go to sleep and make no new contacts. Integration tests that spawn a creature beside a sensor must `WakeBody` it.

## Build

- Never build `Content.Shared` alone. Build `Content.Server` and/or `Content.Client`.
- `MSB3027` means the client or server is locking DLLs. Close them, then rebuild.

## Leave these alone

- `NU1903` on ImageSharp and `CS1668` for a missing `C:\sqlite\lib` path are environment / package warnings, not CF code.
- Warnings whose source file is under `RobustToolbox/` stay upstream.

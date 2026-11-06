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

## Stock damage does not reach CF HP

Anything that calls `TryChangeDamage` writes to the bar, and CF HP never hears of it, so a creature can die with full hit points. `CFStockHeatSystem` catches heat. Cold, suffocation and pressure are not caught yet.

To catch one, handle `BeforeDamageChangedEvent` and cancel it. `DamageModifyEvent` is not enough: stock temperature damage passes `ignoreResistances: true`, which skips that event. Deal the CF damage later, in `Update`, not in the handler, so the bar is not rewritten in the middle of a damage call.

## Do not sum SS14 damage

`DamageableSystem.GetTotalDamage`, `GetAllDamage`, and `GetDamagePerGroup` are obsolete: do not treat damage as one number. `DamageableComponent.TotalDamage` and `Damage` are access-restricted to `DamageableSystem`.

Pathfinder hit points are the source of truth. Write the SS14 bar with `DamageableSystem.SetDamage` (one `Blunt` value derived from CF HP and mob thresholds). Do not read the current total and add a delta.

## Do not dispose UI controls

`Control.Dispose()` and `Control.Dispose(bool)` are obsolete (`CS0618`). Overriding `Dispose(bool)` also warns `CS0672`.

Remove a control from the UI tree. Do not add an empty `Dispose` override that only calls `base.Dispose`. The lobby editor overlay (`HumanoidProfileEditor` under `_CF`) follows this.

## Check lengths in a NetMessage

A `NetMessage` that reads a length must call `buffer.AssertAtLeastSize` before it allocates or loops, or a client can claim a huge length and make the server allocate it. Use the sizes in `NetMessageExt`, such as `count * NetMessageExt.MinSizeOfString`. This is not a debug assert: a size that is too large disconnects a real client, so use the true minimum. Stock messages were fixed with `RobustToolbox` 294.0.2.

CF has no `NetMessage` of its own. Prefer `[Serializable, NetSerializable]` events: NetSerializer checks those lengths itself, up to 64 MiB per call. Anything a client sends still needs checking in its handler, as `CFShopSystem.Purchase` does with `MaxItems`.

## Event subscriptions

One directed component + event pair. A second subscriber throws `Duplicate Subscriptions` at startup.

- `MobStateComponent` + `MobStateChangedEvent` is already taken by `SharedStunSystem`. Subscribe to the broadcast, not the component.
- `MobStateChangedEvent` has no `[ByRefEvent]`. Existing handlers are by-value. A `ref` handler throws `Attempted to subscribe by-ref and by-value to the same broadcast event`.
- `ProjectileComponent` + `BeforeProjectileHitEvent` is already handled by `CFStrikeSystem`. Do not add another subscriber. Party blocks and strike resolution belong in that handler.
- Melee friendly-fire cancels `GettingAttackedAttemptEvent` (raised from `CanAttack`) before the strike runs.
- `StandUpAttemptEvent` runs for "could it stand?" checks and several times per stand. Never charge a cost there. A finished stand is `TryStandDoAfterEvent`.
- `CanPull` and `BeingPulledAttemptEvent` run for every verb menu. Anything with a cost or a roll goes on `PullAttemptEvent`, which only `TryStartPull` raises.

## Stock behaviour that surprises

- Mobs do not collide. `MobLayer` is not in `MobMask`, so they overlap and push through `MobCollisionComponent`. `PreventCollideEvent` changes nothing between two mobs. Cancel `AttemptMobCollideEvent` and `AttemptMobTargetCollideEvent` instead. Neither event says who the other mob is.
- A click in combat mode is a swing, even with a non-weapon in hand. `AfterInteractEvent` does not run. Use `UseInHandEvent` for anything that has to work in a fight.
- `MeleeHitEvent.Direction` is null for a click and set for a wide swing.
- The flanking sensor is a fixture, so `GetEntitiesIntersectingBody`, `GetAABBNoContainer` and `GetWorldAABB` all see a creature as 2.5 tiles wide. That silently ended every climb near a wall (`ClimbingComponent.TransitionCollisionMask` is 0 on combatants for this) and gave "no room to stand" beside any table (marked fix in `SharedStunSystem.Knockdown`). `LookupFlags.Sensors` lookups find a creature that far out too. `InRangeUnobstructed` is not affected: it skips non-hard fixtures on a hard body.
- `InRangeUnobstructed` between two entities measures surface to surface. NPC melee measures center to center, so a creature can pass the first and still be out of swing range.
- A thin ray fits through a gap the body does not. A creature sent straight at a one-tile gap catches its corner and circles there, so `CFGOAPSteeringSystem.CanWalkStraight` casts down each side as well.
- A smashed `CFWindowWooden` leaves a broken frame, which is a half wall to climb and can trip whoever is beside it. A smashed door leaves a door frame, which still blocks a body although it is not `Impassable`.
- A bow's `ContainerAmmoProvider` reports its capacity as `int.MaxValue`, so `GunAmmoPrecondition` sees one arrow as about 0%. `RangedCombatCompound` drops the bow as empty, picks it up and repeats. Use `CFArcherCompound`.
- `GunCombatCompound`'s `JukeOperator` strafes for the whole plan, not once. Its `MoveToOperator` uses `shutdownState: PlanFinished`, which leaves steering registered, and an NPC that has arrived then keeps its last move input forever. Strafing hides that, so take both out together.
- A bow holds one arrow, and nothing in stock AI reloads a gun. `CFArrowSupply` nocks the next one.
- With two parents, the first parent's copy of a component wins. `parent: [ CFMobSomething, CFGOAPBrain ]` keeps the mob's own `HTN`, which is why `CFGOAPSystem.OnBrainInit` sets the root task.
- A thrown item without `LandAtCursor` ends its flight at about 80% of the way and slides the rest, so it never hits what the cursor is on. Every thrown weapon needs it, and `CFThrownWeaponTest` checks that.
- `BeforeThrowEvent.Direction` is read-only and already clamped to `HandsComponent.ThrowRange`. Range has to change in `HandsSystem.ThrowHeldItem`.
- Stock times a flight as if air did not slow the item, so a long throw lands short. `CFStrikeSystem.OnThrown` stretches `LandTime` for thrown weapons.
- `EmbeddableProjectile` embeds on any touch of a thrown item, with no roll, and `EmbedAttach` is private. Set `embedOnThrow: false` and raise `ProjectileHitEvent` on the item to stick it.
- `OpenStorage` empties a closet before it raises `StorageAfterOpenEvent`, so the contents are already gone. Watch `EntGotRemovedFromContainerMessage` on the creature.
- `PlayEmoteMessage` is only sent by the emote menu. `EmoteEvent` also fires for typed emotes and for automatic ones such as pain screams.
- Alt+Z runs the first `AlternativeVerb` on the held item. Set a high `Priority` to come first.
- An `Entity<T>` handler must take the event by `ref`, whether or not the event is `[ByRefEvent]`. A broadcast handler for a plain class event is by value.
- In test output, `Skipped` with `Test was dirty-disposed` is a failed test. An assert inside `Server.WaitAssertion` runs on the server thread, whose NUnit context belongs to whichever test started that pair. On a reused pair the failure is not recorded on the current test, and the dirty-dispose warning becomes its only result. A short duration proves nothing: hundreds of ticks run in under a second. See `ai-testing.md`.
- A test with no game (plain math) goes in a plain class, not a `GameTest`. Mixed in, it can fail the tests around it with no message.
- `NPCSteeringSystem.Register` with new coordinates cancels the path, so call it once. `new EntityCoordinates(target, Vector2.Zero)` follows a moving target.
- Stock gives up on a path for good after three failures, and `SteeringStatus.NoPath` then stops all steering until the NPC is unregistered. HTN's move operator gets replanned. Code that moves an NPC itself has to retry.
- `FilterObstacleEntities` returns at once when an NPC has no stock path, so `CollisionAvoidance` then treats everything nearby as a blocker, cables and wall lights included. Overlapping one is distance 0 at full weight, which beats normal interest and freezes the mob.
- Lava and chasms are not hard, so stock paths cross them. `IntersectRay` also ignores non-hard fixtures: a ray that must touch them needs `RayCastSystem.CastRayClosest` with `QueryFlags.Sensors`. `LookupFlags.Static` does not find them either. Add `LookupFlags.Sensors`.
- `SharedTransformSystem.ToCoordinates(mapCoordinates)` parents to the map, and pathfinding only works on grids, so the path fails. Pass the grid: `ToCoordinates(grid, mapCoordinates)`. A path to something in line of sight can hide this.
- `IsTileBlocked` with `Impassable` misses tables (`MidImpassable`) and every unanchored body. Stock crates are not `Impassable` either, because `MachineLayer` has no such bit. To know what stops a mob, compare the other body's hard layer with the mob's mask.
- A stock `Window` stands on a separate `Grille`, so one tile can hold two obstacles and smashing the window leaves the grille. Anything that excuses one entity on a tile still trips over the other.
- A grid's `GridAtmosphere` saves air per tile as an index into `uniqueMixes`, and that is what rooms have. `MapAtmosphere` only fills space outside the grid, so a map can have both and its rooms still be vacuum. Mobs show it as Critical after about 35 seconds. `fixgridatmos` only changes the running game: to keep it, save the map or edit the mix.
- `GetTileMixture` returns null for a tile under a wall, window, grille or closed door. That is not vacuum.
- A steering safety radius must stay under 0.5 tiles. The middle of a one-tile doorway is 0.5 from each side, so anything larger overrides the way forward and holds the NPC there.
- `Vector2.Normalize(Vector2.Zero)` is NaN. In `NPCSteeringComponent.Interest` or `Danger`, stock's blend keeps a NaN forever and the NPC stands still with no error. Check before normalizing.
- The path graph takes a few seconds to build after a map loads. An NPC test that needs a real path should run ticks first.
- A map with no `MapAtmosphere` kills low-HP mobs in seconds, with full CF HP: a kobold's bar is only 7. Every map mobs stand on needs the block in `meteor-arena.yml`.
- An occasional test failure in `SetUp` with `Value cannot be null. (Parameter 'key')` from `PrototypeManager.Freeze` is an engine race. Rerun the test.
- Prototype kinds go in Shared, even when only the server reads them. The client also reads every prototype file and throws on a kind it does not know. A bare `[Prototype]` names the kind from the class: `CFGOAPProfilePrototype` would be `cFGOAPProfile`, so name it.

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

## A wall is a component, not a tag

Stock replaced the `Wall` tag with `WallComponent`. CP14 code still asks for the tag, so check for the component when porting. Windows only have the `Window` tag. The linter catches a `ProtoId<TagPrototype>` naming a tag that is gone, but only if the error is not lost among others.

## A LocId field holds a key, never the text

`[DataField] public LocId Name` wants a key that is in an `.ftl`. English written straight into the YAML still shows up in game, because `Loc.GetString` hands back an unknown key as it is, so nothing looks wrong. But the YAML linter reports `No localization message found`, the client logs `Unknown messageId` every time it is read, and nothing can translate it. That was 2,567 errors on feats and backgrounds.

Do not compare the result of a `LocId` in code. `CFPetMath` used to search a feat's name for "Animal Companion", which stops working once the name is a key. Match on the id, or better, on a field made for it.

Fluent treats `{` and `}` as code, and a value starting with `.`, `[` or `*` as syntax, so write those as `{"{"}`. One bad line can lose the rest of the file.

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

- `MagicMirror` needs an `organs` list. Stock barber scissors use `Head`.
- `IconSmooth.key` is required. A missing one drops the entity on the client only.
- `Crayon` needs `selectedState`. `Injector` needs `activeModeProtoId` and `allowedModes`. `Occluder` takes `polygon`, not `boundingBox`.
- `ToggleableLightVisuals` is `ToggleableVisuals`. `FlammableSystem` sets its data, so burning items need nothing else.
- `Sharp` is gone with no replacement: butchering goes through the kitchen spike. `Tool` with `Slicing` stays.
- CP14 files can start with a BOM. Read them as `utf-8-sig`, or the first prototype is lost with no error.

A prototype that fails to read is logged once and dropped: `Reading ...EntityPrototype(id) threw the following exception`. The first symptom is often an unrelated "invalid id" error from something that refers to it. Neither the linter nor `SpawnAndDirtyAllEntities` reports it: `CFScissors` sat broken behind a clean lint until a recipe asked for it. To check, run any test with `--logger "console;verbosity=detailed"` and grep for `threw the following`.

The linter also skips abstract entities, so a bad field there only shows up in the spawn test.

To catch this, spawn the entity in a focused test for that system. `EntityTest.SpawnAndDirtyAllEntities` spawns every prototype on server and client, but it takes far too long: do not run it, and do not filter on `EntityTest`, which includes it.

## VV cannot add to a list

The VV menu edits strings, numbers, booleans and enums, but it has no way to add an item to a `List` or `HashSet`. A field a DM must extend live has to be plain text, such as comma-separated names. Prototype data is copied per entity on spawn, so editing one entity never changes another.

`EntityQueryEnumerator` skips paused entities, and a map just loaded is paused. Use `AllEntityQueryEnumerator` in tests that read one.

## Limb sprites need their own state

Stock limb organs set `VisualOrgan.data.state` to `l_arm` and so on. A body whose RSI has only sexed states (`l_arm_m`, `l_arm_f`), or one full-body state, logs `State 'l_arm' not found` even with `sexStateOverrides`. Give each limb organ its own `data` with `sprite` and `state`. See `sprites-ce.yml`.

## Fixtures added in code

An entity spawned with an empty `Fixtures` component has `CanCollide` turned off. Creating fixtures later with `FixtureSystem.TryCreateFixture` does not turn it back on, so sensors never report contacts. After adding fixtures, call `SharedPhysicsSystem.SetCanCollide(uid, true)` (see `CFHazardSystem`).

Bodies that stand still go to sleep and make no new contacts. Integration tests that spawn a creature beside a sensor must `WakeBody` it.

## Do not name a folder Debug

`.gitignore` has `[Dd]ebug/`, which matches at any depth, so a folder called `Debug` or `debug` is never committed and git says nothing. The files stay on that machine and the build breaks on the next one. That lost the first GOAP decision log. `Release/` and `bin/` are worth the same care. When in doubt, run `git check-ignore -v <path>`.

## Build

- Never build `Content.Shared` alone. Build `Content.Server` and/or `Content.Client`.
- `MSB3027` means the client or server is locking DLLs. Close them, then rebuild.

## Leave these alone

- `NU1903` on ImageSharp and `CS1668` for a missing `C:\sqlite\lib` path are environment / package warnings, not CF code.
- Warnings whose source file is under `RobustToolbox/` stay upstream.

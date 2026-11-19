# Z-levels

How levels stack and how things move between them. The code is CrystallEdge's z-level system, ported on 17 November 2026 with permission from eshhhed. That permission covers the z-level code alone, and every ported file carries a header crediting eshhhed and CrystallEdge. The source is `Content.*/_CE/ZLevels` in https://github.com/crystallpunk-14/crystall-edge (sublicensed MIT), and the integration guide is https://crystallpunk-14.github.io/crystallpunk-docs/tech/z-levels/integration-guide/. Pull requests that change it carry the `zLevels` label there. The CP14 version it replaced is in `roadmapold.md`.

## The idea

Each level is a whole map. A `CFZMapNetwork` entity in nullspace holds them by integer depth: 0 is the station's own map, negative is below, positive above. Each map in it gets `CFZMapComponent` (`Depth`, `MapAbove`, `MapBelow`, `NetworkUid`). The client draws the levels in view one over another with a fixed screen offset per level (`CFSharedZLevelsSystem.ZLevelOffset`), so height is faked. Levels below are blurred, dimmed and fogged (`CFZLevelBlurOverlay`, `CFZLevelFogOverlay`, shaders `CFZBlur` and `CFZFog`).

A station gets its stack from `CFStationZLevels` (`mapsBelow`, `mapsAbove`, `zLevelsComponentOverrides`), built on `StationPostInitEvent` by the server `CFZLevelsSystem`. A level that is a planet map (map entity with a grid) is added to the station as a grid. `Resources/Prototypes/_CF/ZLevels/test_map.yml` is the example.

## Where things are

Everything is under `_CF/ZLevels/` on each side.

| Folder | What it does |
|---|---|
| `Core/` | The network, traversal (`TryMapUp`, `TryMapDown`, `TryMapOffset`, `TryMove`), vertical physics (`CFZPhysics`), the viewer and its PVS eyes, the opening cache, high ground (`CFZLevelHighGround`: stairs and wall tops with a height curve), `CFFilter.ZPvs` for sounds and effects seen from other levels |
| `Damage/` | `CFZLevelDamageSystem` (ours, see below), `CFSoftSurface` (something soft to land on), the soft landing status effect |
| `Chasm/` | `CFZLevelChasmComponent` on a map: falling out of the bottom of the stack is the stock chasm fall |
| `Climbing/` | A body in the air passes over climbable things |
| `Flight/` | `CFZFlyer` and `CFControllableFlight`: toggle flight, then go up or down a level |
| `Ghost/` | `CFZLevelGhostMover`: the Move up and Move down actions, on the observer |
| `Ladder/` | `CFLadder`: a wall-mounted ladder whose sensor the climber walks into to climb (`CFWoodenLadder`, `Resources/Prototypes/_CF/ZLevels/ladders.yml`). Ours beside it: `CFLadderCheck` and the server's `CFLadderCheckSystem`, the Player Core Climb check |
| `Pulling/` | A pulled thing follows its puller to the new level |
| `Roof/` | A tile on a level above roofs the tiles below (`RoofComponent`) and shields them from weather (`CFZLevelRoof`). Server-side |
| `Throwing/` | A thrown item or a shot projectile is left alone by vertical physics until it lands |
| `Tiles/` | A tool with `CFZLevelToolTile` takes apart the ceiling while looking up |
| `Weather/`, `DayCycle/` | One weather and one time of day for the whole stack (`cf_znetwork_weather`, `cf_znetwork_time_set`), and `CFStationWeather`, a weather graph for the station |
| `Mapping/` (server) | The `cf_znetwork_*` admin commands and the `zMap` prototype, for building a stack in the editor |
| `Chat/` (server) | Speech is whispered to the level above and below, as "(from above)" |
| `Audio/`, `NavMap/`, `TileVolume/` (client) | Sounds from other levels are quieter, a nav map control that stacks levels, and a lip drawn under floor edges that border an opening |

## Vertical physics

`CFZPhysicsComponent` is on every mob (`BaseMob`) and every item (`BaseItem`). `LocalPosition` is the height within the level, 0 to 1, and `Velocity` the vertical speed. Gravity is `ZGravityForce` (9.8 levels per second squared). An entity over an empty or `transparent` tile with no high ground under it falls: when `LocalPosition` goes below 0 it moves to the level below, and when it goes above 1 with nothing overhead it moves to the level above. Landing faster than `ImpactVelocityLimit` (4.1) raises `CFZLevelHitEvent` and a stock `LandEvent`. Bodies sleep when they rest, so a mob standing still costs nothing. A ghost has `velocityGravity: false` and `autoStep: false`.

`ContentTileDefinition.Transparent` is what makes a tile an opening: `Space` and `Lattice` have it. `BlocksWeather` (default true) is separate, so glass can be see-through and still keep rain off. `VolumeSprite` is the lip texture.

**Furniture falls too.** `CFZPhysics` is on the stock `BaseStructureDynamic` (crates, chests, barrels, closets, the bookshelf) and `SeatBase` (chairs, stools, benches), and on `CFTableBase`, `CFCabinetBase`, `CFWoodenBed`, `CFCraneBarrel` and `CFFurnace`. Anything anchored sleeps, so nothing happens until it is loose: crates, chests, barrels, chairs and stools start unanchored and can be pushed or pulled over an edge today. Beds, cabinets, closets, the bookshelf and the furnace need a wrench first, and no CF item has the `Anchoring` quality yet. Tables and benches cannot be unanchored at all, so they only have the component for when that changes. A dropped piece lands with a thud and hurts nothing: only creatures take fall damage, and nothing yet hurts what a crate lands on. Test: `CFZLevelTest.LooseFurnitureFallsThroughOpenAir`.

## Fall damage (ours)

CrystallEdge deals stock damage from speed squared. Stock damage never reaches CF hit points (`ss14rules.md`), so `CFZLevelDamageSystem` was rewritten:

- A creature with `CFCombatant` that lands hard takes Player Core fall damage: half the distance fallen in bludgeoning, and it lands prone. The distance comes from the landing speed (`CFFallRules.FeetFromImpact`), one level being `CFFallRules.LevelFeet` (10 feet), rounded to 5 feet. One level is about 4.4 speed, 20 feet, 10 damage.
- `CFSafeFall` from a grant still applies: `FallsSafely` (nothing, as a leshy with a free hand) and `FallFeet` (half the distance). Tests: `CFHeritageTest.SafeFallsAndTheFreeHandTheyMayNeed`, `CFHeritageEffectsTest.KeenerSensesFallsAndClimbs`.
- A Leap stops where it lands (`CFLeaping`, cleared by `CFActivitySystem.OnLeapEnded`), so a creature that leaps onto an opening falls into it instead of sliding across at the speed of the throw. Test: `CFZLevelTest.ALeapClearsAHoleUnlessItLandsInIt`.
- Landing on a creature: it makes a Reflex save against DC 15, basic, against half the faller's damage. A critical failure also knocks it prone.
- CrystallEdge's events still fire, so `CFSoftSurface` and the soft landing effect lower the damage.
- Items take nothing. Only the dust puff (`CFDustEffect`) is predicted.

## Ways between levels

Besides falling:

- **Stairs** are CrystallEdge's ramps (`Resources/Prototypes/_CF/Entities/Structures/Stairs/`, since 19 November 2026): an entity with `CFZLevelHighGround` (`heightCurve`, `stick`, `corner`) that a creature walks up and down, in wood or stone (`stairs.yml`), or as a red, green or blue roof ramp (`roofs.yml`, the slopes of a pitched roof, laid from the matching `CFFloorTileRoof*` tile, which also lays the flat `CFRoof*` roof tiles and is made from stone at a workbench), straight, corner or inner corner, each built from a frame at the construction menu (`Recipes/Construction/stairs.yml`). The dirt cliff (`dirt_cliff.yml`) is the same ramp as a bank of earth for mappers, not built, as in CrystallEdge. The stairs stand on a floor tile of the lower level, climbing towards north with no rotation, and the tile straight above their top on the level above is left empty: at the top of the ramp the creature is above the ceiling and `TryMoveUp` takes it through that opening, and from above the opening is solid ground, because the ramp below holds it (`ComputeGroundHeightInternal` looks one level down). `stick` keeps a walker on the ramp on the way down, so it is not a fall. The top edge is a thin wall, so nobody walks off it at ground level; the sides are open, and a creature that steps onto the opening from the side drops onto the ramp. The step slows to 0.3 of Speed (`SpeedModifierContacts`), as CrystallEdge has it. The component works on any anchored entity. CrystallEdge's `CEAutoTilePlacement`, which laid a plank floor under a placed staircase, is outside the permission and was not ported: build them on a floor tile. Tests: `CFZLevelTest.StairsCarryYouUpToTheLevelAbove`, `StairsCarryYouDownWithoutAFall`.
- **Ladders** are CrystallEdge's wall ladders (`CFWoodenLadder`, built from the construction menu facing a wall). The ladder hangs on the wall its rotation points at, north with none, and the tile straight above it on the level above is left open. A creature whose body touches the wall side of the ladder's tile takes hold (`CFLadderClimber`): walking into the wall climbs at half speed, standing still hangs, running slides down, and stepping into the opening from above takes hold on the way down. Climbing out, the ladder below keeps holding the climber until it steps off the tile. Ours on top: `CFLadderCheck`, the Player Core Climb. With a `dc`, taking hold from the ground is an Athletics check (`CFGrantClimb`: `bonus`, `successIsCritical`, `climbSpeed` skips it) that costs an action and needs both hands. A failure makes no progress, a critical failure leaves the climber prone, or lets go on the way down, and the verdict holds for `retrySeconds` so a body jittering against the ladder does not roll again. A `dc` of 0, the default, is a trivial climb: free, as the rules allow. `CFLadderSystem.OnStartCollide` asks with `CFLadderGripAttemptEvent` (a marked edit), and the server's `CFLadderCheckSystem` answers. Stock mobs climb as CrystallEdge lets them. Tests: `CFZLevelTest.WalkingIntoALadderTakesHold`, `AHardClimbIsAnAthleticsCheckWithFreeHands`, `CriticalFailureOnTheWayDownLetsGo`.
- **Holes** are empty tiles, as in CrystallEdge: take the floor out and whatever stands there falls. The CP14 pieces that did these jobs, the teleport stairs (`CFStairs*AutoLink`), the click-to-Climb ladder (`CFLadder*AutoLink`, `CFZLevelClimb`), the hole entity (`CFZLevelHole`), `CFZLevelAutoPortal` and `CFZLevelPortalSystem`, were removed on 19 November 2026 with their `_CP14` sprites.
- **Look up.** Every player gets `CFZLevelViewer` and the `CFActionToggleLookUp` action: the level above is drawn where its floor is transparent or missing.
- **Ghosts** have `CFZLevelGhostMover` (`CFActionZLevelUp`, `CFActionZLevelDown`).
- **Flight** is `CFZFlyer` plus `CFControllableFlight` on a creature. Roadmap 2.7 (flying over open sky) can be built on it.

## Building a stack in the editor, and making it the round map

- `cf_znetwork_gamemap_mapping <gameMap id>` opens every level of a `gameMap` that has `CFStationZLevels` in the editor. `cf_znetwork_mapping <zMap id>` does the same from a `zMap` prototype, and `cf_znetwork_add_above` / `cf_znetwork_add_below` add a level to an open network.
- `cf_znetwork_save <network entity, from the aghost VV or entity list> <name>` writes one file per level to `ZNetworkSaves/<name>/<name><depth>.yml` under the user data folder: `<name>0.yml` is the ground, `<name>1.yml` the level above it, `<name>-1.yml` the one below. Give it a bare name: `DragonDungeon`, not `DragonDungeon.yml`, or the files come out as `DragonDungeon.yml0.yml`.
- Copy the folder under `Resources/Maps/_CF/`, then write a `gameMap` whose `mapPath` is the depth 0 file and whose station has `CFStationZLevels` with `mapsAbove` (depth 1, 2, ... in order) and `mapsBelow` (deepest first). `Resources/Prototypes/_CF/Maps/dragondungeon.yml` is the example.
- The depth 0 grid needs `BecomesStation` with the station key, or stock builds no station and nobody can join. The save command does not write one.
- Every level needs its own `MapAtmosphere`, or anyone on it suffocates in seconds with full CF HP. The editor only puts one on the level that was made as a planet. Levels share one time of day through `cf_znetwork_time_set`, but only among maps that have `LightCycle`.
- A level saved from the editor is paused (`mapPaused: True`) and pre-init, which is what a round wants. `NoSavedPostMapInitTest` fails on one saved from a running round.

## What was changed in the port

Each is marked `CarpFinder` in the code.

- **Bodies parented to the map fall too** (`CFSharedZLevelsSystem.IsLoose`). CrystallEdge's levels are planet maps, where the map is the grid, so it only simulated bodies parented to their grid. Our islands are grids inside a map, and a creature that steps off one is parented to the map. Without this nothing ever fell off an island edge.
- **A spawned body learns its ground at once** (`OnMapInit` calls `RequestCacheMovement`). The first move event comes before the component can hear it, so a creature spawned over open air stood on nothing.
- **Weather respects the levels above.** `SharedWeatherSystem.CanWeatherAffect` asks `CFSharedZLevelsRoofSystem.IsWeatherBlocked` after the stock roof, so a glass floor above keeps rain off though it casts no shadow, and an empty tile is checked like any other instead of always counting as open sky. `GridStencilSystem.GetTileStencil` took an `ignoreEmpty` parameter so the weather stencil can mask empty tiles. CrystallEdge also shifts each masked tile for its isometric walls, which we do not need.
- The fall damage, as above. The PVS override component became `PvsOverrideSystem.AddForceSend`. `Subs.CVar` became `OnValueChanged`. The tool system is asked for each quality, because `ToolComponent.Qualities` is read-only here.
- Names: `CF` for `CE`, `Content.*.CF.ZLevels.*` namespaces, `cf_znetwork_*` commands, `cf-` locale keys, `cf.zlevels.*` cvars in `CFCVars`, and CrystallEdge's `CESafeFalling` is `CFSoftSurface`, because `CFSafeFall` was already our grant.
- One file keeps a stock namespace on purpose: `Content.Client/_CF/ZLevels/Core/ScalingViewport.CFZLevels.cs` is a partial of the stock `ScalingViewport`, so it is `Content.Client.Viewport`. The stock file itself only gained the one marked call to `RenderZLevels`.

## Licenses

The code is MIT, sublicensed by CrystallEdge, and the header of every ported file says so. eshhhed confirmed on 17 November 2026 that CrystallEdge textures may be used under CC-BY-SA-4.0 as long as each stays in its `_CE` folder with its license file, and that `misc.rsi` is included. TheShuEd in their `meta.json` files is eshhhed. The assets here: the look up, flight and ghost move icons (`Actions/zlevels.rsi`), the stair sprites (`Structures/Architecture/Stairs`), the ladder (`Structures/Furniture/ladder.rsi`, by Max Gab), the dust puff (`Effects/dust.rsi`, which carries CrystallEdge's own CC-BY-SA-3.0 tag), the lip texture (`Tiles/Volume`, the same) and the two shaders (`Shaders/attributions.yml`, CC-BY-SA-4.0).

## What was left out, and why

- `TryGetEffectiveDistance`: it read CrystallEdge's murk cvar, and murk is not z-level code.
- The public stamina animation key: nothing here uses it.
- `CEAutoTilePlacement`, and `CEPlaceable`, which lets CrystallEdge mount its ladder from a folded ladder item and hang it over an edge: neither is z-level code. The stone and wooden stairs and the wooden ladder came on 19 November 2026, from the construction menu.
- The PVS override component: the network entity uses stock `PvsOverrideSystem.AddForceSend`.
- The fall damage cvars: Player Core's rule replaced them.

## Known gaps

- The chat relay speaks as an entity with no `CFLanguages`, so a line heard from another level comes through in the common tongue whatever the speaker spoke.
- GOAP knows nothing of levels: a path never goes up or down stairs (roadmap 6e). Stock pathfinding sees a staircase as a tile with a thin wall at its top and a slowdown on it.
- `CFChasmEdge` (slip, grace, Grab an Edge) is still on stock `Chasm` entities only. Open sky at the bottom of a stack has no grace: give the map `CFZLevelChasm` for a lethal drop, or leave it off and the creature stands a little below floor height over the void.
- Not played yet: anything beyond the build and the two fall tests. The two-level test map should be the first thing tried.

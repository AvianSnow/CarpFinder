# GOAP

Advanced NPCs: mini bosses and special encounters. Ordinary mobs stay on stock HTN.

This is our own design, under the repo's MIT licence. **Do not copy from `crystall-edge`.** Its `_CE` code is all rights reserved, and using it needs TheShuEd's written consent. A port of its GOAP was thrown away before it was ever committed, for that reason.

To test it, read `ai-testing.md`. For where mappers put hints, read `investigation-nodes.md`. For what the weights mean and how mobs will choose spells, read `goap-weights.md`. Open items are under Step 5 in `roadmap.md`, and the history is under Phase 6 in `roadmapold.md`.

## Layout

```
Content.Shared/_CF/GOAP/
  CFGOAPPrototypes.cs   Facts, steps, and the cfGoapGoal, cfGoapAction, cfGoapProfile prototypes
  CFGOAPTacticsPrototype.cs   The goapTactics weights
  CFGOAPEncounterPrototype.cs, CFDangerousTileComponent.cs
Content.Server/_CF/GOAP/
  CFGOAPSystem.*        Decides: think, choose a goal, run the plan. Reflexes and node choice are partials
  CFGOAPPlanner         Backward A* over the facts. Plain code, so it tests without a game
  CFGOAPComponent       The brain
  CFGOAPOperator        The bridge from HTN
  Sensors/              Say what is true, one system per subject
  Steps/                Do the work, one system per CFGOAPStep
  Tactics/              Puts numbers on a fight: target scores, and what an action is worth
  Steering/             Feelers, flow fields, doors and glass, on top of stock steering
  DecisionLog/          DEBUG, to be removed: the decision log
  Encounters/           The spawner and groups
  Influence/            Scent and noise maps
  Nodes/                Investigation nodes
Resources/Prototypes/_CF/GOAP/
  goap.yml  tactics.yml  encounters.yml
```

## One think

`CFGOAPOperator` calls `Tick` every tick, and `Tick` calls `Think` twice a second.

1. `Think` raises `CFGOAPSenseEvent`. Every sensor system handles it and adds facts. `CFGOAPSightSensorSystem` goes first, because it decides who the creature is after and the others read that.
2. Reflexes can force a goal.
3. `ChooseGoal` takes the highest priority goal the planner can reach.
4. If the goal or a fact changed, the plan changes. A plan that starts with the step already running keeps it running, so a fact flipping does not restart a walk.

Then `RunStep` looks the current action's `step` up in a table of step systems and runs it. Walking is a step like any other: `Close`, `GoToLkp` and `PickUp` walk the creature themselves, through `CFGOAPSteeringSystem.Navigate`.

## How to add to it

| To add | Do this |
|---|---|
| A fact | A value in `CFGOAPFact`, and a sensor that sets it. A fact only a goal wants needs no sensor |
| A sensor | A handler for `CFGOAPSenseEvent` in the system for that subject, or a new system in `Sensors/`. Use `after: [typeof(CFGOAPSightSensorSystem)]` if it reads the target |
| A goal | A `cfGoapGoal` in `goap.yml`, then add it to the default list on `CFGOAPComponent` or to an `encounter` |
| An action | A `cfGoapAction` in `goap.yml`, using a step that already exists if it can. Add it the same way |
| A step | A value in `CFGOAPStep` and a `CFGOAPStepSystem` in `Steps/`. It adds itself to the table. `EveryKindOfStepHasASystem` fails if one is missing |
| A personality | A `cfGoapProfile` in `goap.yml`. It only changes numbers |
| A way of fighting | A `goapTactics` in `tactics.yml`, with `parent: Default` and only what differs |
| An action worth weighing | A `CFGOAPValue`, a case in `CFGOAPTacticsSystem.Value`, and `valuedBy` on the action. Measure it against a clean Strike, which is 1 |

## Rules

- It never cheats. Sensors use sight, the influence maps, the last known position, and nodes. Hiders are found by Seek, never known.
- An undetected creature is not seen. Pain gives the attacker's position, not sight of them.
- A step system serves every creature, so what one creature is doing goes on its components.
- Costs go below 1, so the planner's heuristic is scaled by the cheapest option. Leave that in, or a plain Strike can beat a cheaper flank.
- `AtLkp` stays true once reached, until a new sighting. Otherwise walking to a node flips it and sends the creature back, and it never gets there.
- A sensor runs for every brain on every think, so it must stay cheap. `Cornered` is a time the `Flee` step leaves behind, not a search.

## Tactics

A creature has a personality and tactics, and the two combine. The personality (`cfGoapProfile`) says when it runs and how it moves. Tactics (`goapTactics`) say who it goes for and what an action is worth, so `Cowardly` and `Tactician` are not the same thing as `Cowardly` and `Beast`.

**Who it goes for.** `CFGOAPSightSensorSystem` takes the seen enemy with the highest score from `CFGOAPTacticsSystem.ScoreTarget`. A group still agrees on one target after that.

**What an action is worth.** An action with `valuedBy` has its plan cost divided by what `CFGOAPTacticsSystem.Value` says it is worth, measured against a clean Strike, which is 1. Zero leaves it out of the plan. Everything is read from `CFStrikeSystem.TryPreviewMelee` and the `TryGet...Check` methods on `CFConditionSystem`, which use the numbers the real roll uses, so a rule change reaches the AI on its own. `CFGOAPOdds` runs them through the real degree-of-success rules.

**Multiple Attack Penalty does most of the work.** A swing costs an action, and only a full pool clears the penalty, so a creature that never stops swinging stays at -10 for the whole fight. With a +8 attack against AC 18, the third swing is worth 1/6 of the first, which is under the usual `discipline` of 0.25. So it Strikes twice, then does something else: Demoralize or Feint if they are worth it, or `HoldBack` while the pool refills. `Attacked` and `AttackedTwice` are facts so that it replans as the penalty changes.

An agile weapon or an easy target keeps the third swing worthwhile, with no special case. Tactics with `discipline: 0` swing regardless, as beasts do.

**Insight is the one place the AI reads a hidden number.** At 1 it uses the target's real AC and DCs, which stands for sizing up armor and stance. At 0 it assumes an even fight. This does not break the never-cheat rule, which is about knowing where someone is, but keep it to judging a fight it can already see.

## Decision log (DEBUG, to be removed)

Every brain gets a `CFGOAPDecisionLogComponent`: the last 64 goal changes, plans, steps, facts by name, targets, reflexes, obstacles, routes and giving up. In a real game, open `CFGOAPDecisionLog` on a creature in VV and read `Entries`. In tests, `CFGOAPLoggedTest` prints it after each test, and the runner shows that for tests that fail.

The folder must not be called `Debug`: `.gitignore` drops any folder with that name, which lost the first copy of this log.

`CFGOAPDecisionLogSystem` watches brains from outside once a tick, so the AI code has no logging in it. Two changes in one tick show as their result, and a one-action step can finish before it is seen. `ActionsLeft` is left out, because it flips with every swing.

To remove it:

1. Delete `Content.Server/_CF/GOAP/DecisionLog/` and `Content.IntegrationTests/Tests/_CF/GOAP/DecisionLog/`.
2. In `CFGOAPTest`, `CFGOAPEncounterTest`, `CFGOAPObstacleTest` and `CFGOAPMazeTest`, change `CFGOAPLoggedTest` back to `GameTest` and drop its `using`. Delete `TheDecisionLogKeepsWhatItChose`.
3. Delete this section and its line in the layout above, and put the first step of "When a live scenario fails" in `ai-testing.md` back.

## Getting past things

`CFGOAPSteeringSystem.Classify` says what anything on a tile means to this creature, and `TileCost` turns that into a flow field. Both look at every hard body on the tile, anchored or not, because `IsTileBlocked` with `Impassable` misses tables and crates.

| It is | Means | In a field |
|---|---|---|
| Does not stop its body: a rug, a chair, an item | `None` | Open floor |
| A door it can open | `Door` | Slow |
| Climbable, and it can climb: a table, a crate, a broken frame | `Climb` | Slow. It goes round if that is shorter |
| Destructible, and its plain hit beats the Hardness | `Smash` | Slower, and a locked door more so |
| Anything else: a wall, or a grille it cannot hurt | `Solid` | Blocked, so it goes round |

Only `Navigate` deals with these, so a step that moves any other way cannot get through a door. `FollowScent` is one. A mob working on something is left alone by the safety circle, except by lava and other mobs, or the next table along would throw it off the one it is climbing.

## Long trips

A flow field reaches 20 tiles. For a known place further off, `CFGOAPSteeringSystem.Travel` plans the whole way with `CFGOAPFlowFieldBuilder.TryFindRoute`, a tile A* on the same `TileCost` and step rules as the arrows, then walks it on one short field after another. So doors, climbing and giving up work as usual. Stock paths are not used: they cross lava, run async, and need the path graph.

`Travel` takes a place, never a creature, so it cannot find anyone. `GoToLkp`, `CheckNode` and `GoHome` use it. Chasing a creature still uses `Navigate`.

- A place within 14 tiles gets a field first, because it may be close but a long walk round. A field that was built and has no arrow under the creature falls back to a route.
- A field with no arrows at all means the place is sealed. No route then sets `NoWayUntil`, which is how the creature gives up. Telling "built and empty" from "not built yet" matters: mixing them up left a creature standing still for good.
- `Navigate` keeps the steering it has while the line is clear. `HeadingElsewhere` is what starts a walk nothing else started, and moves it on to the next stop of a route.

**Home.** `CFGOAPComponent.Home` is set the first time a brain starts, relative to the grid. With nothing to do it may wander up to `HomeRange` (8 tiles). Past that the `GoHome` goal walks it right back, to within `HomeArrival`, on its own field so a group's shared one is left alone. Going home runs the whole way, so a creature does not stop at the edge of its range.

## Shooting

`Shoot` is an action like `Strike`, and the step hands the work to stock NPC shooting, which CF turns into Strikes. It needs `HasShot`: `CFGOAPShotSensorSystem` sets that for a loaded gun or bow in hand with the same line check stock shooting makes, so glass it can see through does not count. Its cost is 0.9, under a clean Strike, so an archer stays where it is even with an enemy beside it. Out of arrows, or with no line, it plans `Close` and `Strike` as anything else does. It keeps the empty bow in hand and uses its claws.

`Shoot` has no `valuedBy`, so it does not weigh Multiple Attack Penalty. The action pool still paces its shots. `CFStrikeSystem.TryPreviewMelee` has no ranged version yet.

## Fire

`PutOutFire` has the highest priority of any goal, so a burning creature stops what it is doing, even running from a rout. The step calls `FlammableSystem.Resist`, which is what clicking the on fire alert does: 2 seconds paralyzed, which drops what it holds, while the fire dies 15 times as fast. A big fire takes several rolls. `ArmSelf` picks the weapon back up afterwards.

The `OnFire` fact is left off while it stands on a dangerous tile, because a roll on lava only lights it again. It carries on with its plan until it is off.

What burning costs in hit points is not AI. See `CFStockHeatSystem`.

## Fleeing

`CFGOAPFlowFieldBuilder.BuildFlee` makes a Dijkstra safety map, a technique from roguelikes: distance from the threat, flipped and scaled by 1.2 so the far ends of the map are low ground, then spread again so dead ends stop looking attractive. Following it downhill goes round corners toward real space. It uses the same tile costs as any field, with glass and locks as walls, so a runner goes through doors it can open.

A routed group shares one flee field, built by its navigator. A creature fleeing alone uses its own, because its pack may still be chasing on theirs.

A zero arrow means nowhere is better than here. With the threat within 3 tiles, that sets `CorneredUntil`, the `Cornered` fact takes `Flee` out of the plan, and `FightBack` becomes the way to reach `Safe`.

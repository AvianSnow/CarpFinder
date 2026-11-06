# Testing the GOAP AI

How to test advanced NPCs without burning runs. This was written after the door and window work on 5 November 2026, which took roughly 140 test launches and 25 builds. Most of that was avoidable. The numbers below were measured then.

## The main rule

Game ticks are almost free. Starting a test is not.

| Step | Cost |
|---|---|
| Build after a C# change | 10 to 60 seconds |
| Launch `dotnet test`, up to the first result | about 12 seconds |
| One live scenario, about 20 game seconds | about 0.4 seconds |
| The whole CF suite, 297 tests | under 1 minute |

So 40 repeats of one test take 8 minutes as a shell loop and about 16 seconds inside one process. Never loop `dotnet test` over a single test. Run the whole suite by default, because it costs little more than one test.

## Test at the cheapest level that can show the problem

| Level | What belongs here | Examples |
|---|---|---|
| 1. Plain logic | Planner, flow field, steering math. No game, so no dice and no timing. A plain class, not a `GameTest` | `CFGOAPPlannerTest`, `CFGOAPSteeringMathTest` |
| 2. One decision | Set the scene, call `Goap.Think` once, read `Facts`, `Goal` and `Plan`. Nothing moves | `ItSeesThroughGlass` |
| 3. Live scenario | A real NPC with physics, for what only shows up in motion. One per behaviour, not one per case | `CFGOAPObstacleTest` |
| 4. Real client | Feel, sprites, input, anything with a player. See "Checks in a real client" | |

Push logic down a level whenever you can. If a rule can be a pure function, test it as one.

## Commands

```bash
# Default: everything CF.
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Tools --no-build --filter "FullyQualifiedName~IntegrationTests.Tests.CF&FullyQualifiedName!~EntityTest"

# AI only.
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Tools --no-build --filter "FullyQualifiedName~CF.GOAP"
```

Close the game first, or the build cannot replace its DLLs. Check that the build succeeded before reading test results: a failed build leaves the old binary, and the tests run anyway.

## Reading a result

| You see | It means | Do this |
|---|---|---|
| `Failed` with a message | A failed test | Read it |
| `Skipped`, with `Test was dirty-disposed` | **Also a failed test**, with its message lost | Treat it as a failure |
| Many tests fail within seconds, with `Parameter 'key'` or a null in `CFDevMap.Load` | The engine startup race, not a result | Rerun once |
| `Passed`, once, for a live scenario | Very little | See "Repeating a live scenario" |

Why `Skipped` happens, confirmed by experiment: an assert inside `Server.WaitAssertion` runs on the server thread, whose NUnit context belongs to whichever test started that pair. On a reused pair the failure is not recorded on the current test, so the dirty-dispose warning becomes its only result. The same assert on the test thread gives a normal `Failed` with its message. The first test on a pair always shows a normal `Failed`, which is why a test run alone never shows `Skipped`.

A short duration proves nothing. Hundreds of ticks run in under a second.

## Repeating a live scenario

Only do this when a live scenario is new or looks unreliable. Repeat it inside one process with a throwaway test, and delete the test afterwards:

```csharp
[Test]
public async Task Sweep([Range(1, 40)] int run)
{
    // Set up the scenario, then run it in half-second steps up to a generous limit.
    // Stop as soon as it succeeds. Log the time it took, or the timeline if it never did.
}
```

- Forty runs catch a 1 in 20 problem about 87% of the time.
- Tests run in parallel, so put a `lock` around anything that writes to a shared file.
- Use `FormattableString.Invariant` for numbers in logs. This machine prints `5,5`, which breaks a script reading them.
- Sample the timeline every 2 game seconds, but only print it for runs that failed.
- **A seed does not replay a run.** `IRobustRandom.SetSeed` at the start of a test gave different results each time, across 3 seeds run 3 times each. Do not chase one failing seed. Compare rates.
- **Rates are noisy.** The same locked door code stalled 4, 8 and 4 times in 40. A change has to move the rate clearly before it counts as a fix. One sweep cannot tell 4 from 7.

## Writing a live scenario

- Pass `avoidStructures: true`. Without it `CFDevMap` quietly settles for floor with structures on it, and the test fails for reasons that have nothing to do with the AI.
- Keep the footprint to 7 by 3. The arena has nothing bigger that is clear, and a 7 by 5 fails. Tiles touching lava count as impassable in a flow field, so there is only one safe row, and a live detour cannot be tested there.
- Take the dice out. Set an extreme ability, or `PrintedAttack` and `PrintedDamage`. In one run a kobold took 13 seconds to do 1 damage to a window, so a test on real numbers tests luck.
- Set `RegenSeconds` very high if you read stamina or MAP after a delay. A full pool clears MAP.
- Make the time limit several times what the scenario usually takes. Ticks are cheap. `ANpcGoesRoundAWallToReachItsTarget` looked unreliable for a long time because its window was 10 seconds, and it takes 6 on average and up to 29.
- Read values in `WaitPost` and assert after it, on the test thread, so a failure keeps its message.
- A creature that loses sight needs a node or scent to hunt. Test heroes have no scent emitter, and players do.
- Switch `NPCEnabled` back off at the end.

## When a live scenario fails

1. Read the decision log first: the test output has it, or VV on `CFGOAPDebugLog` in a real game. For what it leaves out, get a timeline before forming a theory: position, speed, goal, step, obstacle, facts, steering kinds, prone, held items. Guessing from one snapshot cost more runs than anything else.
2. Read facts by name. `(CFGOAPFact) comp.Facts` prints them.
3. To see what is on a tile, use the anchored entities or default lookup flags. `LookupFlags.All` includes sensors, so it reports creatures up to 1.25 tiles away. That produced one wrong conclusion here.
4. An empty goal, no steering and empty hands mean the brain stopped. The creature is probably down, not stuck.
5. Stop after three failed theories and write down what is known. Fixes for one cause can hide another.

Stock behaviour that caught this work out is in `ss14rules.md`, under "Stock behaviour that surprises".

## Current state

Final code, 5 November 2026: the CF suite passed 297 of 297 in one run. That does not mean every test is reliable, so these are the rates from 40-run sweeps:

| Scenario | Result |
|---|---|
| Break a window and climb through | 40 of 40 |
| Open a closed door | Not swept, passed in the later batch runs |
| Smash a locked door and reach the target | 33 of 40 |
| Go round a wall | 40 of 40, 3 of them took 10 seconds or more |
| `ARealSwingLandsAsAPathfinderStrike` (not AI) | Fails about 1 run in 12 |

`ItSmashesALockedDoor` will fail now and then until the causes below are dealt with.

### What `npc_test_map.yml` showed (6 November 2026)

`Resources/Maps/Test/npc_test_map.yml` is a maze of one-tile corridors, doors, windows on grilles and tables, which the arena has none of. Load it for navigation problems. A monitored run found these, and they are in the roadmap under 6k:

- **Corners in one-tile corridors.** Arrows give a direction and nothing centres the body. Danger from one wall nudges it off the middle, its body catches the next corner, the safety circle throws it back, and it repeats. Measured tick by tick at the mouth of the alcove at (0-1, 6). This is very likely the "stalls in front of the gap" cause below.
- Single runs are noisy. The same fight dealt 127, 140 and 168 damage in three runs, so read trends, not numbers.

### Why the locked door scenario fails: suspected causes

None of these is proven. They are read from the timelines of failing runs. Whether they matter in play is for a real client to show.

| Suspected cause | What the timeline shows | Confidence | In real play? |
|---|---|---|---|
| **It loses sight and gives up.** Pushed off the line of a one-tile gap, it cannot see the target. With no node or scent, `HuntTarget` has no plan, because `GoToLkp` only exists as the step before `CheckNode`. It goes `Idle` and wanders off. The `Investigate` goal was added for this on 6 November 2026, so this needs checking again | `TargetVisible` drops, goal becomes `Idle`, it walks away | High | Less often, since players leave scent. A map with no nodes and a target without scent would show it |
| **It stalls in front of the gap.** The door is gone and the line is clear, but it stands still or jitters a tile short. Feelers and the safety circle may be fighting near the corners, and the gap rule only applies when both side rays touch | `Pursuit` or `FlowField`, speed 0 to 3, x 1.6 to 2.0, for many seconds | Medium | Yes, at any one-tile doorway |
| **It dies beside the lava.** The arena's lava is one tile from the test row. Stock fire kills through the damage bar while CF HP stays full | Goal empty, steering gone, hands empty, partway through the run. Mob state was never read, so this is inferred | Medium | Only near lava. Mostly a fault of the test map |
| **The door frame.** A smashed door leaves `CFWoodenDoorFrame`, a second thing to break, with its own patience clock | The frame was still standing at the end of several failing runs | Low on its own | Yes |
| **The safety circle moves it while it works.** `Hold` does not stop the circle pushing it | It ended up out of reach while `Working`. It now walks back, so this should only cost time | Low now | Yes |
| **It slips on broken glass.** It falls, drops its weapon, and goes for `ArmSelf` | Seen in window runs, which recover | Low | Yes, and it is arguably correct |

### Other things found and not fixed

- The boar test: one `AttemptLightAttack` counted 2 or 3 attacks in 22 of 60 runs, and damage went past the most one Strike can do. The cause is unknown. It could be the test, or one swing landing twice, which would be a real combat bug.
- `CFGOAPSteeringSystem.Panic` measures to `GetWorldAABB`, which is 2.5 tiles wide for a creature because of the flanking sensor. Allies probably keep about 1.7 tiles apart as a result. A kobold was seen holding that gap in a corridor.

## Checks in a real client

- Kobolds and a closed door: they should open it.
- Kobolds and a locked door: they swing at it, and after 20 seconds they give up. With no other way in, they leave it for 60 seconds.
- Kobolds and a window: they see you, try to break it, and climb through the frame. They are weak against glass, so they may run out of patience.
- As a player, get knocked down next to a table and stand up. Then climb a table beside a wall. Both were blocked by the flanking sensor and are fixed in code, but no player has tried them.
- Throw a spear: it flies point first, sticks in what it hits, skids past on a miss, and costs 1 action.

## Proposed changes

Number 2 and number 6 are built. The others are not. 1 to 3 would have saved most of the runs.

| # | Change | What it solves | Size |
|---|---|---|---|
| 1 | **An AI test yard map.** `Resources/Maps/Test/cf_ai_yard.yml`: about 30 by 20 of plain floor with `MapAtmosphere`, no structures, and a lava strip along one edge. `CFAiYard.Load(Pair, layout)` stamps walls, doors and windows from text, so the test shows its own layout | The false failures, the missing room, live detours, and lava deaths | Small to medium |
| 2 | **A decision log. Built, as `CFGOAPDebugLogComponent`, marked DEBUG. See `goap.md`.** A short list of goal changes, steps, obstacles and giving up, with facts by name. Printed when a test fails, readable in VV | About 7 rounds of adding and removing trace code. It also shows why a mob did something in a real game | Small |
| 3 | **Failures keep their message.** A `CFGameTest` base class records the failure before the pair is disposed | `Skipped` hiding failures, and the runs spent working out what it meant | Small |
| 4 | **A sweep helper and `RunUntil`.** Repeats a scenario in one process, reports the rate, and stops each run as soon as it succeeds | The shell loops, and windows that are too short | Small |
| 5 | **Move the flanking sensor off the body**, onto a child entity or a lookup | The root of the climb and stand bugs, the misleading lookups, and pack spacing. Today it is patched in two places | Medium. It touches flanking |
| 6 | **Hunt without a node.** Let a creature go to the last known position and look around | The first suspected cause above. It needs a design decision on when it stops looking | Small |

The yard keeps the rule against building grids by hand: the grid comes from a map, and only structures are stamped, as tests already do with walls. A layout would look like this:

```
#######
B..G.H#      B boss, H hero, G glass, D door, L locked door, # wall, ~ lava
#######
```

The same stamps could back an admin command, so a scenario can be watched in a real client.

# Tribes: a small society on GOAP

A tribe is an encounter group that has a life when nobody is fighting it: its members get hungry and thirsty, farm, store food, share it, talk to each other, and ask a stranger for food before they attack. Built on 16 November 2026. Tests: `CFGOAPSocietyTest`, `CFSocietyPromptTest`. The test map is `Resources/Maps/_CF/KoboldSocietyTest2.yml`.

Read `goap.md` first. A tribe adds facts, goals, actions and steps to the same brain, and nothing here plans on its own.

## Layout

```
Content.Server/_CF/GOAP/Society/
  CFGOAPTribeComponents.cs      CFGOAPTribe (on the spawner), CFGOAPSocial (on each member), the records they keep
  CFGOAPTribeSystem.cs          What the tribe knows: nest, leader, stores, plots, water, strangers, memory, speech
  CFGOAPSocietySensorSystem.cs  Sets the tribe facts, and holds the lookups the steps use too
  CFSocietyAdvisorSystem.cs     Asks a language model, and turns its answer into something GOAP can act on
  CFSocietyPrompt.cs            The question, the grammar, reading the answer, and the built-in rules. No game in it
  Steps/                        One system per errand
Resources/Prototypes/_CF/GOAP/society.yml   Goals, actions, the KoboldTribe encounter and its spawner
Resources/Locale/en-US/_CF/society.ftl      What members remember and say
```

## Making a tribe

Give an `encounter` `tribe: true`. When it spawns, `CFEncounterSpawnedEvent` reaches `CFGOAPTribeSystem`, which puts `CFGOAPTribe` on the spawner and `CFGOAPSocial` on every member, and adds the tribe's goals and actions to every brain, a leader with `keepOwnLists` included.

`CFEncounterSpawnerKoboldTribe` is a spawner already set to `KoboldTribe`: five warriors and a trapper. It has `immediate: true`, so it spawns as the map starts, and the tribe's brains have `NeverSleeps`, so they live on with no player near. That costs CPU all round, so turn `AlwaysAwake` off on a tribe that is only scenery.

- **Nest.** The fire nearest the spawner (`CFFireplace`, or stock's bonfire), or the spawner itself. Each member's `Home` is a spot of its own two tiles from it.
- **What it has.** Every 10 seconds `Survey` finds, within `NestTiles` (16) of the nest: stores (anything with `Storage` that is not an item, a body or a fireplace), plots (`PlantTray`), water (a drinkable structure, such as `CFStoneWell`) and fires. A member knows these from living there, so they are not found by sight.
- **Leader.** The living member of the highest level, and the eldest of those. A new one is chosen when it dies. A dragon in the group would lead for the same reason.
- Every number is a field on `CFGOAPTribe` and can be changed in VV: `Appetite`, `NeedBelow`, `FullAbove`, `LowStock`, `DryBelow`, `PoorBelow`, `InspectEvery`, `ParleyPatience`, `PeaceTime`, `ChatMin`, `ChatMax`.

## Hunger and thirst

These are stock `Satiation`, which every `SimpleMobBase` body has. Stock takes about three hours to starve, so `Appetite` (6) speeds it up for a tribe's members. Appetite stops at 12% of full, so a tribe with no food is hungry, not dead.

`Hungry` is set below `NeedBelow` (40%) and stays set up to `FullAbove` (80%), so a member eats its fill in one go. `Thirsty` works the same, and is only set while the tribe has water, because a need nothing can be done about is not worth a plan.

## What a member does

Goals, highest first. All of them are under the fighting goals, and none counts with an enemy in sight except `Parley`.

| Goal | When | Plan |
|---|---|---|
| `Parley` (5.5) | The tribe is talking to its target | `Parley`: stand 3.5 tiles off. The leader makes the demand every 9 seconds |
| `Drink` (2.4) | `Thirsty` | Walk to the nearest water and drink there |
| `Eat` (2.3) | `Hungry` | `PickUpFood` or `TakeStoredFood`, `CookFood` if it is raw and a fire burns, `Eat` |
| `ShareFood` (2.2) | It holds food it does not need, and a hungry tribemate with none is in sight | `GiveFood`: hand it over |
| `StockFood` (2.0) | Not hungry | `PickUpFood`, `CookFood`, `StoreFood`: into the nearest store with room |
| `TendCrops` (1.9) | Always | One of `Harvest`, `WaterSoil`, `PlantSeed`, `FeedSoil`, `InspectSoil`, cheapest first |
| `Socialize` (1.7) | A tribemate is in sight and it has not talked for 40 to 90 seconds | `Chat` |

Only a hungry member takes from the stores (`TakeStoredFood` needs `Hungry`). Without that, `StockFood` would take food out to put it back.

**Errands.** Most steps are a `CFGOAPErrandStepSystem`: pick a target, fetch a tool if one is needed (a bucket, a seed, fertilizer), take a detour if one is needed (the well, with an empty bucket), walk up, wait a moment, do it. The target and the tool are claimed on the tribe for the walk, so two members do not go for the same plot or the same bread. A new errand is usually one small subclass.

**Plots.** These are stock botany: `HydroponicsSoil` and `FungalSoil` with stock seeds. CF's own `CFSeed` needs a seedbed tile and is not used.

- What state a plot is in is only known by going to look. `InspectSoil` writes a `CFSoilRecord` (water, nutrients, planted, ready), trusted for `InspectEvery` (90 seconds). Every soil fact reads the record, never the plot.
- `WaterSoil` picks up a bucket, fills it at the tribe's water, pours it on the plot and puts the bucket down. It marks the plot wet at once, because the soil only takes the water in over the next while.
- `FeedSoil` works one item with `CFItemUse` `Fertilizer` into a poor plot, for 40 of its 100 nutrients. Ash (`CFAsh1`) is the only fertilizer so far.
- `PlantSeed` uses the seed on the plot, and `Harvest` uses a hand on the plant, the same as a player. What is picked falls at the member's feet, and becomes loose food for `StockFood` or `Eat`.

**Cooking.** Food with stock's `Raw` tag is held over the tribe's fire for 4 seconds and heated the way Prestidigitation does, so its own recipe cooks it. A fire that has to be lit counts only while it burns. With no fire, raw food is eaten as it is.

## Strangers

A tribe makes one decision about each stranger, kept in `CFGOAPTribe.Strangers`.

| Stance | Means |
|---|---|
| `Parley` | `Parleying` is set, `KillTarget` does not count, and the tribe stands off and demands food |
| `Hostile` | It fights as any monster does |
| `Peace` | The sight sensor does not see them as an enemy at all |

- **Meeting.** The first member to see a stranger calls `Meet`, and `CFSocietyRules.Greet` decides at once: `Hostile` for someone armed, inside half the nest's range, when the tribe is neither hungry nor outnumbered. Anyone else gets `Parley`.
- **Patience.** After `ParleyPatience` (30 seconds) with no food, the stance becomes `Hostile` and is settled.
- **Tribute.** Food dropped by a stranger the tribe has met, or within earshot of a member, makes peace for `PeaceTime` (10 minutes), even in the middle of a fight. The food is then loose food like any other.
- **Blood.** Anyone who hurts a member is `Hostile` and settled. A settled stance is only changed by tribute.
- Kobolds speak Draconic and Sakvroth, so a party needs someone who knows one of those to understand the demand.

## Memory and talk

Each member remembers its last 3 things (`CFGOAPSocial.Memory`): a stranger seen, an attack, tribute, dry or poor soil, a ripe crop, low stores, a new leader, and anything a tribemate said within 10 tiles.

`Chat` walks up to a tribemate and tells them the newest thing they do not know and did not tell it, so news goes round the tribe once and is never told back. With nothing new it is small talk. `CFGOAPTribeSystem.Say` speaks a line through chat, in the speaker's language, and puts it in the memory of every member in earshot.

## The language model

Off by default. With it off, or with the model out of reach, everything above works on the built-in rules.

**The split.** The model only picks one of five goals, a creature, and a line to say. GOAP plans and does everything. The model never sees an action, and anything it sends that is not a known goal, or a creature it was told about, is dropped.

| The model says | What happens |
|---|---|
| `DefendNest`, `ParleyForFood` | Only from the leader, and only for a stranger whose stance is not settled: the tribe's stance changes |
| `ShareResource` | Forces `ShareFood` for 20 seconds, toward the tribemate named |
| `Socialize` | Forces `Socialize`, toward the tribemate named, and it may chat at once |
| `GatherFood` | Forces `StockFood` if it has or sees food, otherwise `TendCrops`. The line is said at once, so tribemates hear the plan |

A forced goal with no plan is simply skipped, and one of these is dropped the moment an enemy is in sight. The line is said by the step that fits it (`Parley`, `Chat`, `GiveFood`), or dropped after 30 seconds.

**When it is asked.** On events and a slow cooldown, never every tick: at once when a stranger is met (the leader asks), and otherwise the member who has waited longest asks, each at most once per `cf.society_llm_cooldown` (15 seconds), and only with no enemy in sight. One question is out at a time, because the model runs on a CPU. The rules have already decided before the answer comes, so a slow answer delays nothing.

**The question** is the system text in `CFSocietyPrompt.System` and a world state of about 100 tokens: name, role, hunger and thirst out of 100, what it holds, whether the stores are low, the creatures near it under short names (`Adventurer_1`, `Kobold_Grik`) with notes, and its 3 memories. The request carries a GBNF grammar, built per question, that only allows the five goals and those names.

**Running it.** The server speaks to an OpenAI-style chat endpoint. llama.cpp's server is the one it is written for, because it takes the `grammar` field:

```bash
llama-server -m Llama-3.2-3B-Instruct-Q4_K_M.gguf --port 8080 -c 2048 -t 8
```

| CVar | Default | |
|---|---|---|
| `cf.society_llm` | `false` | Turns asking on |
| `cf.society_llm_url` | `http://127.0.0.1:8080/v1/chat/completions` | |
| `cf.society_llm_timeout` | `20` | Seconds before an answer is given up on |
| `cf.society_llm_cooldown` | `15` | Seconds between two questions from one creature |

Another server that ignores `grammar`, such as Ollama, still works: the answer is checked the same way, it is just more often dropped. A failed request logs one warning a minute.

**Not tried yet:** a real model. `TheLeaderTakesTheModelsAnswerOverItsOwnRules` runs the whole path against a stand-in server that answers in llama.cpp's shape.

## Rules

- A tribe never cheats either. Food, seeds, buckets and tribemates are found by sight. A plot's state comes from the last look. What the tribe has round its nest it knows from living there.
- The model advises and GOAP decides. Do not let an answer do anything directly: map it to a stance or a forced goal.
- `CFGOAPFact` is a `ulong` and only bit 63 is free. A new tribe fact has to replace one, or fold into a sensor the way `Thirsty` folds in "there is water".
- A step system serves every member, so what one is in the middle of goes on `CFGOAPSocial`.

## Left open

- Nobody lights the fire, or puts fuel on it. `CFBonfire` has to be lit by hand before the tribe can cook. Stock's `Bonfire` always burns.
- Nothing on the test map is raw, so cooking only shows with raw meat about. The cooking bench is not used.
- Ash is the only fertilizer, and nothing in the tribe makes it. `FungalSoil` starts with no nutrients.
- A member does not drink from a bucket or from open water, only from a structure such as the well.
- Tribute is food dropped from a hand. Food thrown, or handed straight to a kobold, does not count.
- Hunting, and eating what was killed, is not built. Neither is sleeping in the beds.
- Roles are only names in the question. A scout does not scout.
- Weeds and pests on a plot are not dealt with.
- The whole tribe stands off during a parley, the trapper included, but its `KeepDistance` still backs it away from anyone who walks up.
- 21 other `_CF` prototype files still use `SolutionContainerManager`, which left the well and the bucket holding nothing until they were fixed here. Each is likely empty in the same way.

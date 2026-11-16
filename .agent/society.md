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
  Steps/                        One system per errand: take, eat, drink, prepare (grind, mix, cook, slice), store, give, soil, tidy, chat, parley
Content.Shared/_CF/Cooking/     The mortar, mixing flour into water, and swapping a thing for its next stage in place
Content.Shared/_CF/Temperature/CFTemperatureTransformationSystem.cs   Dough into bread when it is hot
Resources/Prototypes/_CF/GOAP/society.yml   Goals, actions, the KoboldTribe encounter, its supplies and its spawner
Resources/Prototypes/_CF/Entities/Objects/Tools/cooking.yml   The mortar and pestle, and flour
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
| `StockFood` (2.0) | Not hungry | `PickUpFood`, `PrepareFood` until it is ready, `StoreFood`: into the nearest store with room |
| `TendCrops` (1.9) | Always | One of `Harvest`, `WaterSoil`, `PlantSeed`, `FeedSoil`, `InspectSoil`, cheapest first |
| `KeepNest` (1.8) | `NestUntended` | `KeepNest`: the fire fed, rubbish out, a puddle wiped, a valuable, fuel or something useless in its hand into a store |
| `Socialize` (1.7) | A tribemate is in sight and it has not talked for 40 to 90 seconds | `Chat` |

Only a hungry member takes from the stores (`TakeStoredFood` needs `Hungry`). Without that, `StockFood` would take food out to put it back.

**What counts as food.** `CFGOAPTribeSystem.Stage` says how far along a thing is: `Grind` (wheat, by the `Wheat` or `CFWheat` tag), `Mix` (`CFMixInto`: flour), `Cook` (stock's `Raw` tag, or a `CFTemperatureTransformation` that heat changes: dough, raw meat), `Slice` (an edible `ToolRefinable` that wants `Slicing`: a loaf), `Ready` (eaten as it is), or `None`. `IsFood` is anything but `None`. A member wants food that can be eaten as it is (`IsEdible`: a loaf, raw meat, a slice) or that the tribe can take a stage further (`CFGOAPSocietySensorSystem.IsWanted`): wheat with no mortar about is not picked up, not counted, and not taken from a store, which is what stopped the loop where a kobold carried stock wheat it cannot digest in and out of the crate. A loaf with no knife about, or raw meat with no fire burning, is still food: it is eaten as it is, as it was before bread came in. For one day on 16 November 2026 it was not, and a tribe sat hungry beside a full crate. `CanEat` asks the eater's stomach, so the `Eat` step gives up at once on something it cannot digest instead of chewing for good.

**Bread** (`PrepareFood`, step `Prepare`, `CFGOAPPrepareStepSystem`). One action per stage, each what a player does with the same things, so the same recipes run: wheat is used on the mortar (`CFMortarAndPestle`, `CFMortarSystem`, a 2 second do-after, the flour appears in the same hand), flour is used on the well or a bucket with 10 units of water (`CFMixIntoSystem`, dough at once), dough is held over the fire for 4 seconds (`SetCookedTemperature` to 420, and `CFTemperatureTransformationSystem` swaps it for the loaf in hand), and the loaf is put down, cut with the knife (stock `ToolRefinable`, 4 slices) and one slice picked up; the rest lie there as loose food. After each stage the errand ends, thinking sees what it holds now and plans the next, so a loaf is four plans. `FoodRaw` means "not ready, and the tribe has the station for the next stage": `TryFindStation` finds the nearest mortar or knife from the survey, water from `Waters` or a loose bucket with enough in it, or the burning fire. Mortars and knives are claimed while one member works, the fire and the well are not. The stations come with the tribe: the `KoboldTribe` encounter has `supplies` (a mortar, a bucket, a kitchen knife, clippers), which the spawner lays down between its members.

**Seeds** (`PlantSeed`). With a plot empty and no seed in sight, a member takes the clippers (stock `BotanySampleTaker`) to a plot whose plant was grown enough at the last look (`CFSoilRecord.Sampleable`: alive, past the first growth stage, not already sampled), clips it as a player does, drops the clippers, picks up the seed packet that fell at its feet and plants it. Stock damages the plant a little each time and marks it sampled three times in ten, so one plant gives a few seeds. A plant too young refuses, and the errand ends with nothing planted.

**Stores as memory** (`CFGOAPTribe.Stock`). The survey counts what each store holds into a `CFStoreRecord` (`Food` that is ready, `Ingredients`, `Valuables`), and `NoteStored` and `NoteTaken` keep it right between surveys, so a member knows what is in a crate from living there and never opens one to look. `TakeStoredFood` picks the store from the record and opens only that one, and what it finds there is what the tribe knows from then on (`LookInto`, which also stamps `At`). A hungry member that knows of no food goes and looks in the nearest store nobody has looked into for `InspectEvery`, in case a player put something there, so the sensor sets `StoredFood` for that too. `FoodStock` counts shared stores only: what a member has in its own pack is its own, and its record survives the survey.

**Packs.** Every kobold wears a `CFKoboldBackpack` (the `CFKobold` inventory template gives kobolds a pocket, a belt and a back slot). It is not one of the tribe's stores, because the survey skips anything that is an item, but `TryFindStore` falls back on the member's own pack when no store has room, so a plank, a coin or a loaf it is carrying goes into the pack rather than onto the ground, and a hungry member takes food out of its own pack first. The pack has a `Stock` record like any store, so the count still holds.

**Keeping the nest** (`KeepNest`, `CFGOAPKeepNestStepSystem`). `NestUntended` is set by `TryFindChore`, the fire first and then the nearest: a fire wanting fuel; rubbish (stock's `Trash` tag) or a puddle within `NestTiles` of the nest; a valuable (a coin, a potion, or `fullResale` on `CFItemPrice`) lying there with a store that has room; or something in the member's own hand that is rubbish, a valuable, fuel, or food nobody can work on. Rubbish is picked up and thrown straight away from the fire to 3 tiles past the nest's edge (`ThrowingSystem.TryThrow` with friction compensated), where it is no longer the tribe's business. A puddle takes 2 seconds and leaves stock's `PuddleSparkle`, as Prestidigitation does. A valuable is carried to the nearest store and the record follows; with no store it is put down.

**The fire** (`CFChoreKind.FeedFire`). A CF fireplace (`CFFireplace`: the bonfire, the furnace) burns `Fuel` down a point a second and then eats the next piece of fuel waiting in its container; with none it goes out. `CFFireplaceSystem.NeedsFuel` says when the tribe steps in: the fire is out, or it has less than `LowFuel` (30) left and nothing waiting. It is also stoked while burning when a plot is poor and no ash lies about, because each piece of fuel burnt now leaves `Ash` (`CFAsh1`, with `AshChance`) on a tile beside the fire, and ash is the tribe's fertilizer (`FeedSoil` picks it up by sight). The member takes fuel from its hand, from the ground (`TryFindFuel`) or out of a store whose record says `Fuel` (a detour, as the clippers are for a seed), splits one piece off a stack and puts it in with `TryFeed`, which also lights a fire that is out. What is left of the stack stays in hand and goes to a store as the next chore. Stock's `Bonfire` has no `CFFireplace`, always burns, and is never fed. The kobold tribe brings ten planks in its `supplies`.

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

- **Game.** An animal is nobody to talk to. `CFGOAPTribeSystem.IsGame` is a living creature that speaks no language (no `CFLanguages`), has no humanoid body (no `HumanoidProfile`, so a stock human who happens to be mute is still a stranger), is nobody's character (no `CFPvp`, no player), is not a tribemate, and can be butchered (`CFHarvestable`, which every stock mob that stock could butcher gets at map init): a pig, a chicken, a boar. `Meet` never greets one. A tribe that wants food (a member hungry, or its stock low) marks it `Hostile`, so the ordinary fighting goals hunt it, and remembers "Good eating"; a tribe that wants nothing marks it `Peace` for a minute and decides again after. No demand is spoken, and the language model is not asked. An animal that can be butchered dies at 0 Hit Points like a monster (`CFCombatBridgeSystem` sets `diesAtZeroHp` on such commoners), so the hunt ends in a carcass, not a body lying dying. A carcass is loose food to the sensor when the tribe has a survival knife (`CFHarvestTool`, surveyed into `Butchers`): `PickUpFood` fetches the knife, butchers the carcass as a player does (`CFHarvestSystem.TryStartHarvest` with training not required, since a tribe knows its own game), puts the knife down and picks up a piece of what came out. The meat is raw, so it goes to the fire, and the rest lies there for the others to stock. The kobold tribe brings a survival knife in its `supplies`.
- **Meeting.** The first member to see a stranger calls `Meet`, and `CFSocietyRules.Greet` decides at once: `Hostile` for someone armed, inside half the nest's range, when the tribe is neither hungry nor outnumbered. Anyone else gets `Parley`.
- **Patience.** After `ParleyPatience` (30 seconds) with no food, the stance becomes `Hostile` and is settled.
- **Tribute.** Food dropped by a stranger the tribe has met, or within earshot of a member, makes peace for `PeaceTime` (10 minutes), even in the middle of a fight. The food is then loose food like any other.
- **Blood.** Anyone who hurts a member is `Hostile` and settled. A settled stance is only changed by tribute.
- Kobolds speak Draconic and Sakvroth, so a party needs someone who knows one of those to understand the demand.

## Memory and talk

Each member remembers its last 3 things (`CFGOAPSocial.Memory`): a stranger seen, an attack, tribute, dry or poor soil, a ripe crop, low stores, a new leader, and anything a tribemate said within 10 tiles.

A stranger's words count too. `CFGOAPTribeSystem.OnSpoke` hears every `EntitySpokeEvent` from a creature outside the tribe: each member within 10 tiles (2 for a whisper) remembers `Adventurer_1 said to us: '...'` if it knows the language the stranger speaks, and `Adventurer_1 spoke to us in a tongue we do not know.` if not, so talking to kobolds in Draconic means something and Common does not. Hearing someone is meeting them. Each stranger keeps the label it got at the first meeting (`CFTribeStranger.Label`), so the question to the model names the speaker the same way the memory does, and the system text tells the model to answer what was said. The last thing said is also put on its own `Heard:` line at the end of the question for a minute (`CFGOAPSocial.Heard`), the system text says the utterance is a reply to it in the kobold's own words, and while it is there the grammar makes the line at least 3 characters: with the words only in memory, the 3B model left a greeting unanswered or echoed the memory back. The leader asks the model at once when it hears a stranger. If the tribe's mind about that stranger is already made up, at peace or at war, the leader still says the answer's line, so a kobold talks back either way.

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

**The question** is the system text in `CFSocietyPrompt.System` and a world state of about 100 tokens: name, role, hunger and thirst out of 100, what it holds, whether the stores are low, the creatures near it under short names (`Adventurer_1`, `Kobold_Grik`) with notes, and its 3 memories. The request carries a GBNF grammar, built per question, that only allows the five goals, each with a target of its own kind: `DefendNest` and `ParleyForFood` name a stranger, `ShareResource` and `Socialize` a tribemate, `GatherFood` nobody. A goal with nobody of its kind near is left out, so alone the only answer is `GatherFood`. Before the grammar did that, the 3B model parleyed with its own tribemates, which the game drops. The system text also says what the hunger scale means, because the model read 90/100 as nearly full.

Two things about GBNF: a real newline inside a character class, which a C# `"\n"` gives, makes the server refuse the request with `failed to parse grammar`, so the class is written `[^"\\\n]` with the escape spelled out; and `{0,120}` on the line keeps a cut-off answer rare within `max_tokens` 64.

**Running it.** The server speaks to an OpenAI-style chat endpoint. llama.cpp's server is the one it is written for, because it takes the `grammar` field. `runllm.sh` starts it: it expects a llama.cpp release unzipped in `~/llama.cpp/current` (on Windows the CUDA build and its `cudart` zip from the same release, both into that folder) and the model in `~/llama.cpp/models`:

```bash
# https://github.com/ggml-org/llama.cpp/releases  (b12438: llama-*-bin-win-cuda-13.5-x64.zip + cudart-*-win-cuda-13.5-x64.zip)
# https://huggingface.co/bartowski/Llama-3.2-3B-Instruct-GGUF  (Llama-3.2-3B-Instruct-Q4_K_M.gguf, 2 GB)
./runllm.sh
```

On an RTX 3080 the model loads in 3 seconds and answers a question in 0.1 to 0.5 seconds. On a CPU it is a few seconds; `LLAMA_THREADS` sets the threads.

| CVar | Default | |
|---|---|---|
| `cf.society_llm` | `false` | Turns asking on |
| `cf.society_llm_url` | `http://127.0.0.1:8080/v1/chat/completions` | |
| `cf.society_llm_timeout` | `20` | Seconds before an answer is given up on |
| `cf.society_llm_cooldown` | `15` | Seconds between two questions from one creature |

Another server that ignores `grammar`, such as Ollama, still works: the answer is checked the same way, it is just more often dropped. A failed request logs one warning a minute.

**Trying it in a round.** The cvars are server-only, and a cvar is set with the `cvar` command, so a bare `cf.society_llm true` in the console is an unknown command:

1. `./runllm.sh` in its own terminal, then start the server and client as usual.
2. In the server's terminal: `cvar cf.society_llm true`. Or from the client console as an admin: `sudo cvar cf.society_llm true`.
3. Join, `aghost`, and either `spawn CFEncounterSpawnerKoboldTribe` near a lit fire (the tribe lives round the nearest one), or load the test map beside the round: `loadmap 20 /Maps/_CF/KoboldSocietyTest2.yml` and `tp 0 0 20`. The map has no spawn points, so it is not a round map.
4. Walk a character up to the kobolds. The server's terminal prints `Society model: <goal> <target> "<line>"` for each answer, and a line when an answer could not be used.

**Tests.** `TheLeaderTakesTheModelsAnswerOverItsOwnRules` runs the whole path against a stand-in server that answers in llama.cpp's shape, so it runs anywhere. `ARealModelAnswersTheLeader` is `[Explicit]` and runs against the real server started with `runllm.sh`; run it after a change to the prompt, the grammar or the request, because the stand-in accepts anything. A `--filter` alone skips an explicit test, so name it to NUnit as well:

```bash
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj -c Tools --no-build --filter "Name=ARealModelAnswersTheLeader" -- NUnit.Where="test==Content.IntegrationTests.Tests.CF.GOAP.CFGOAPSocietyTest.ARealModelAnswersTheLeader"
```

Tried on 16 November 2026 with Llama 3.2 3B Q4_K_M: a hungry leader facing an armed stranger with bread parleys three times in four, a fed one in its nest defends, a warrior with meat beside a starving tribemate shares, and anyone alone gathers.

## Rules

- A tribe never cheats either. Food, seeds, buckets and tribemates are found by sight. A plot's state comes from the last look. What the tribe has round its nest it knows from living there.
- The model advises and GOAP decides. Do not let an answer do anything directly: map it to a stance or a forced goal.
- `CFGOAPFact` is a `ulong` and every bit is taken (`MessNearby` took 63). A new tribe fact has to replace one, or fold into a sensor the way `Thirsty` folds in "there is water" and `FoodRaw` folds in every stage of bread.
- A step system serves every member, so what one is in the middle of goes on `CFGOAPSocial`.

## Left open

- The fire is lit by feeding it: a member putting fuel into a dead `CFBonfire` lights it, with no torch or flint. Nobody builds a new fire where there is none.
- Ash is the only fertilizer and the fire is the only thing that makes it. A tribe with no fuel and poor soil does nothing about either.
- The cooking bench is not used: bread is made by hand at the mortar, the water and the fire. A loaf is the only thing sliced, and only when a knife lies about; with none it is eaten whole.
- A puddle is wiped by hand, in 2 seconds, with no mop. Rubbish is anything with stock's `Trash` tag and nothing else, so CP14 junk without the tag is left where it lies.
- Thrown rubbish that hits a wall falls back inside the nest and is thrown again next time.
- A valuable is a coin, a potion or anything with `fullResale`. Gear a stranger dropped is not picked up.
- Clipping a seed hurts the plant, as stock has it, and a young plant refuses. Seeds are only clipped when a plot is empty and no seed lies about, so a tribe with a full crate of seeds never clips.
- The mortar, knives and clippers are found by the survey within `NestTiles`; one carried off farther than that is lost to the tribe until it comes back. A survival knife put down beside a kill far from the nest stays there.
- Hunting is whatever the fighting goals do to a hostile target: no stalking, no driving game toward the spears, no choosing the weakest. A tribe that wants food hunts any animal it sees, a wolf included, so a pack of them is a fight it may lose.
- Only members on their way past notice a carcass. Nobody is told where the kill lies.
- `FungalSoil` starts with no nutrients, so a fresh plot wants ash before anything grows.
- A member drinks from the well or from open water (`FloorWaterEntity`, which CF gives `Edible` and `DrawableSolution` in a marked edit), and fills its bucket at either. It does not drink from a bucket.
- Tribute is food dropped from a hand. Food thrown, or handed straight to a kobold, does not count.
- Hunting, and eating what was killed, is not built. Neither is sleeping in the beds.
- Roles are only names in the question. A scout does not scout.
- Weeds and pests on a plot are not dealt with.
- The whole tribe stands off during a parley, the trapper included, but its `KeepDistance` still backs it away from anyone who walks up.
- 21 other `_CF` prototype files still use `SolutionContainerManager`, which left the well and the bucket holding nothing until they were fixed here. Each is likely empty in the same way.

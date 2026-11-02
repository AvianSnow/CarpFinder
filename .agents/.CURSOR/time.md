# CarpFinder time

`CFTimeComponent` on the `CFWorldClock` entity (`Resources/Prototypes/_CF/Time/time.yml`) is the rate card. The round clock is not running yet: nothing spawns `CFWorldClock` or ends the round at 6 hours.

Four scales. Do not convert one into another unless the method for that scale says so.

| Scale      | What a second means                                            | Use it for                                                  |
| ---------- | -------------------------------------------------------------- | ----------------------------------------------------------- |
| Real       | One wall-clock second                                          | Engine waits. Action regen, popup cooldowns.                |
| Carpfinder | One tabletop second. One round is 6.                           | The book duration. Not a timer.                             |
| Adjusted   | One wall-clock second, chosen per feature                      | Spells, conditions, and class features in real-time combat. |
| CarpFinder | 0.25 wall-clock seconds (4 world seconds pass per real second) | Day, night, and the round.                                  |

`TryGetRealSeconds` accepts Real, Adjusted, and CarpFinder. It refuses Carpfinder. A feature that should last "1 minute" in the book needs its own Adjusted wall-clock duration.

With the YAML rates:

- 1 real second = 4 world seconds. 1 world second = 0.25 real seconds.
- 1 real hour = 4 world hours. 6 real hours = 24 world hours.
- Day is the first 14 world hours (3 hours 30 minutes real). Night is the next 10 (2 hours 30 minutes real). The round starts at dawn.
- `IsDay` takes wall-clock time since dawn.

Spell `durationSeconds` and condition `TimeSpan.FromSeconds` values already in code are wall-clock seconds. They are not tagged, and they are not CarpFinder time. Tag a new duration with the scale it actually is.

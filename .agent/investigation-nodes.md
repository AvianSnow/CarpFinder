# Placing investigation nodes

`CFInvestigationNode` is a hidden marker for the AI. It is only visible in mapping mode. When an advanced NPC loses its target, it goes to the last known position, then checks nearby nodes. Without nodes it can only follow scent.

Only GOAP NPCs use these, so a node only matters where a mini boss or special encounter can reach. Simple mobs ignore them.

## Where to put them

Place one wherever a player would actually hide or slip away:

- Beside closets and lockers. Tag these `Closet`: the NPC opens containers there.
- At corners a player could duck around (`Corner`).
- In doorways and side-room entrances (`Doorway`).
- Partway down long corridors (`Corridor`).
- By stairs, ladders and holes (`Stairs`).
- In dark spots (`Shadow`). Stealthy NPCs prefer these.

An NPC looks 60 feet (12 tiles) around the last known position, so a room needs only a few nodes. Do not carpet a map: with too many, it spends its search on nodes that mean nothing.

## Fields

| Field | Meaning |
|---|---|
| `weight` | How likely this spot is. 1 is normal. Use 2 or 3 for an obvious hiding place. |
| `tags` | One of the tags above. `Tags` is a flag set. |
| `linked` | Nodes worth checking next. |

## How one gets picked

Score = weight × distance falloff × match with the target's last heading × scent bonus × "not checked recently" × the personality's tag multiplier. The pick is weighted random, so it is not predictable. Pack mates take different nodes. An NPC gives up after `nodesToCheck` nodes (4 by default).

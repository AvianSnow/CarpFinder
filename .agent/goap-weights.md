# GOAP weights reference

What our AI weighs, what it should weigh once mobs cast spells, and where the ideas came from.

## The source

[AI Archetypes Mastery: The Complete AI Weights Reference](https://mod.io/g/baldursgate3/r/ai-archetypes-mastery-the-complete-ai-weights-reference) documents about 227 weights from Baldur's Gate 3. Keep it as a reference for what is worth weighing.

- Read it for ideas. Do not paste its values, developer notes or weight names into this repo. The guide states no licence, the values come from Larian's game files, and they are tuned for 5e anyway. Our names and numbers come from PF2e math.
- The page needs JavaScript. To read it from a shell:
  `google-chrome --headless=new --no-sandbox --virtual-time-budget=20000 --dump-dom <url>`
- Larian's own overview of the method is on the [Divinity Engine wiki](https://docs.larian.game/Combat_AI).

The method is the part worth keeping. For each thing it could do, the AI simulates the effect on each target it would touch, turns that into a score, takes off the cost, and does the best one. Weights say how much each kind of effect matters to this kind of creature.

## How the guide's families map to ours

| Guide family | What it is for | Here |
|---|---|---|
| Target priorities | Who to go for | **Built.** `goapTactics` target weights |
| Kill bonuses | Finishing blows | **Built.** `finish` |
| Hit chance and resistance "stupidity" | How well it judges odds | **Built.** `insight` covers AC and DCs. Resistances wait for spells |
| Damage on enemies | Worth of a Strike | **Built.** `strike`, as a share of a clean Strike |
| Action resource cost | Is this worth an action | **Built, differently.** BG3 has no Multiple Attack Penalty, so `discipline` and `CFGOAPOdds.StrikeShare` replace it |
| Fear, control on enemies | Worth of a debuff | **Built for Demoralize and Feint.** The rest waits for spells |
| Retreat and fallback | Where to run | **Built, differently.** The safety map in `CFGOAPFlowFieldBuilder.BuildFlee`. It does not lean toward allies yet |
| Weapon pickup | Rearming | Already there: `ArmSelf` and `PickUpWeapon` |
| Seeking hidden targets | Searching | Already there: last known position, scent and nodes |
| End position, height, darkness | Where to stand | **Not built.** Needs a position score. Darkness waits for the `Light` layer |
| Damage, heal, DoT, HoT, control, boost, each on self, ally, enemy, neutral | **Spell choice** | **Planned.** Most of the 227 are here |
| Status remove, overwrite, fail | **Spell choice** | **Planned** |
| Resource replenish, cooldown, free action | **Spell choice** | **Planned**, as slots, focus and cantrips |
| Boost and roll bonus weights | **Spell choice** | **Planned**, through `CFBonusEffect` |
| Concentration | Breaking and guarding it | PF2e uses Sustain, so this needs rethinking |
| Auras, surfaces, summons, reflect damage | Special cases | Later |

## Plan: mobs that cast spells

Our spells are already data, which is what the method needs: `CFTargeting`, `CFAreaEffect`, `CFCheck` and `CFDegreeEffects` say what a spell does and to whom. So one scorer can read any spell, with no code per spell, and a new spell needs no AI work.

1. **A spellbook on the mob**, and a way to cast with no player. Check this against `CFCastRequest`, which carries bolts and modes on the request.
2. **`CFGOAPValue.Spell`.** Value it for each known spell and its best target or area, as for Demoralize and Feint.
3. **Walk the effects, not the spell.** For each creature it would touch, take the chance of each degree from `CFCheck`, using `CFGOAPOdds`, and add up what the `CFDegreeEffects` lists would do:

   | Effect | Worth |
   |---|---|
   | `CFDamageEffect` | Average damage against what a Strike would do, after resistance. Basic saves scale it, as they do in play |
   | `CFHealEffect` | Hit points restored, rising as the target gets lower, and capped at what is missing |
   | `CFApplyConditionEffect` | A table per condition. Paralyzed is worth far more than dazzled |
   | `CFBonusEffect` | What the bonus adds to the Strikes it will affect, as with `BonusShare` |
   | `CFResistanceEffect` | Only if something is dealing that damage |

4. **Sign by who it lands on.** Add for enemies, subtract for allies, using `CFFactionSystem`. This is the self, ally, enemy and neutral grid in the guide, and most of its weights collapse into one weight per effect kind times this sign. An area spell sums everyone in `CFAreaGrid.Collect`, so friendly fire takes care of itself.
5. **Charge for the resource.** A cantrip is free. A slot costs more the higher its rank, and more early in a fight than when the creature is losing. Focus points sit in between.
6. **Count actions.** Most spells take 2 actions, so divide by that. Save spells ignore Multiple Attack Penalty, which makes them the natural use of actions after two Strikes, and the scoring will find that on its own.
7. **New `goapTactics` weights**, one per effect kind, plus how freely it spends slots. A `Caster` set would raise them. A `Healer` set would raise healing and lower damage.

The odds, the planner and the shape of the scoring carry over as they are. Only the walk over the effects and the table of conditions are new.

using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Network;

namespace Content.Shared.CF.Spells;

/// <summary>What an Animist's feats do as it casts: Channeled Protection.</summary>
public sealed partial class CFAnimistSystem : EntitySystem
{
    /// <summary>Adjacent: within a tile and a half, centre to centre.</summary>
    private const float AdjacentTiles = 1.6f;

    [Dependency] private INetManager _net = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFStanceSystem _stances = default!;

    /// <summary>
    /// A spell has been paid for. Channeled Protection: cast from a slot in Channeler's Stance, it gives the caster and the
    /// allies beside it +1 status to AC and Reflex for 6 seconds.
    /// </summary>
    public void NoteCast(EntityUid caster, CFSpellComponent spell)
    {
        if (!_net.IsServer
            || spell.Focus
            || spell.Rank <= 0
            || CompOrNull<CFCombatOptionsComponent>(caster) is not { ChanneledProtection: true }
            || _stances.Current(caster) is not { Channeler: true })
        {
            return;
        }

        var bonus = spell.ResolvedRank >= 7 ? 2 : 1;
        Protect(caster, bonus);
        foreach (var (ally, _) in _lookup.GetEntitiesInRange<CFCombatantComponent>(Transform(caster).Coordinates, AdjacentTiles))
        {
            if (ally != caster && _mobState.IsAlive(ally) && !_factions.CanHarm(caster, ally))
                Protect(ally, bonus);
        }
    }

    private void Protect(EntityUid uid, int bonus)
    {
        _bonuses.AddBonus(uid, CFBonusStat.ArmorClass, CFBonusType.Status, bonus, 6f, source: "channeled-protection");
        _bonuses.AddBonus(uid, CFBonusStat.Reflex, CFBonusType.Status, bonus, 6f, source: "channeled-protection");
    }
}

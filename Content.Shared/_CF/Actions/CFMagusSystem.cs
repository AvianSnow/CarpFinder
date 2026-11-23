using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;

namespace Content.Shared.CF.Actions;

/// <summary>
/// What a Magus's hybrid study and feats add to entering Arcane Cascade. Bonuses go under the stance's own source, so
/// they end with the stance.
/// </summary>
public sealed partial class CFMagusSystem : EntitySystem
{
    private const string ArcaneCascade = "CFActArcaneCascade";

    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFTemporaryHpSystem _tempHp = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    [SubscribeLocalEvent]
    private void OnStanceEntered(Entity<CFCombatOptionsComponent> ent, ref CFStanceEnteredEvent args)
    {
        if (args.Stance != ArcaneCascade)
            return;

        var options = ent.Comp;
        var level = CompOrNull<CFCombatantComponent>(ent)?.Level ?? 1;

        // Inexorable Iron: temporary Hit Points with a melee weapon held in both hands.
        if (options.InexorableIron
            && _hands.TryGetActiveItem(ent.Owner, out var held)
            && HasComp<CFMeleeWeaponComponent>(held.Value)
            && CompOrNull<CFWeaponComponent>(held.Value)?.Hands == CFWeaponHands.Two)
        {
            _tempHp.Grant(ent, Math.Max(1, level / 2), 60f, "inexorable-iron");
        }

        // Laughing Shadow: faster, and faster still with no armor.
        if (options.LaughingShadow)
        {
            var feet = _armor.TryGetWornArmor(ent, out _) ? 5 : 10;
            _bonuses.AddPermanentBonus(ent, CFBonusStat.Speed, CFBonusType.Status, feet, source: CFStanceSystem.BonusSource);
        }

        // Burning Cascade: the stance's extra damage to every enemy within 5 feet.
        if (options.BurningCascade)
        {
            foreach (var other in _lookup.GetEntitiesInRange<CFCombatantComponent>(_transform.GetMapCoordinates(ent), 1.5f))
            {
                if (other.Owner != ent.Owner && _mobState.IsAlive(other) && _factions.CanHarm(ent, other))
                    _effects.DealDamage(ent, other, 1, "Force", critical: false);
            }
        }
    }
}

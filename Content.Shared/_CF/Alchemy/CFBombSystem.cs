using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared.CF.Alchemy;

/// <summary>
/// Breaks a <see cref="CFBombComponent"/> on its thrown Strike and splashes the creatures round the target.
/// The Strike itself is <c>CFStrikeSystem</c>'s: the attack roll, the multiple attack penalty and the hit's damage.
/// </summary>
public sealed partial class CFBombSystem : EntitySystem
{
    /// <summary>Splash reaches 5 feet: the next tile over, with a little slack for a body on its edge.</summary>
    private const float SplashRange = 1.2f;

    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private Content.Shared.CF.Factions.CFFactionSystem _factions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    [SubscribeLocalEvent]
    private void OnResolved(Entity<CFBombComponent> bomb, ref CFStrikeResolvedEvent args)
    {
        if (!_net.IsServer || args.Melee || bomb.Comp.Broken || !TryComp(bomb, out CFWeaponComponent? weapon))
            return;

        var type = weapon.DamageType;
        if (args.Degree.IsSuccess())
        {
            if (bomb.Comp.Persistent > 0)
                _bonuses.AddPersistent(args.Target, bomb.Comp.PersistentType ?? type, 0, 0, bomb.Comp.Persistent, args.User);

            if (bomb.Comp.OffGuardSeconds > 0f)
                _conditions.SetOffGuardAll(args.Target, bomb.Comp.OffGuardSeconds);
        }

        if (args.Degree != CFDegree.CriticalFailure && bomb.Comp.Splash > 0)
            Splash(bomb, args.User, args.Target, type.Id);

        Break(bomb);
    }

    /// <summary>A throw that reached nobody. The bomb is lost all the same.</summary>
    [SubscribeLocalEvent]
    private void OnLand(Entity<CFBombComponent> bomb, ref LandEvent args)
    {
        if (_net.IsServer && !bomb.Comp.Broken)
            Break(bomb);
    }

    private void Splash(Entity<CFBombComponent> bomb, EntityUid user, EntityUid target, string damageType)
    {
        var splashed = new HashSet<Entity<MobStateComponent>>();
        _lookup.GetEntitiesInRange(Transform(target).Coordinates, SplashRange, splashed);

        // A Bomber keeps the splash off its allies, and from level 5 makes it at least its Intelligence modifier.
        var bomber = CompOrNull<CFCombatOptionsComponent>(user)?.BomberField == true;
        var amount = bomb.Comp.Splash;
        if (bomber && (CompOrNull<CFCombatantComponent>(user)?.Level ?? 1) >= 5)
            amount = Math.Max(amount, CompOrNull<Content.Shared.CF.Character.CFAbilityScoreComponent>(user)?.GetModifier(Content.Shared.CF.Preferences.CFAbility.Intelligence) ?? 0);

        foreach (var creature in splashed)
        {
            if (_mobState.IsDead(creature, creature.Comp))
                continue;

            if (bomber && creature.Owner != target && (creature.Owner == user || !_factions.CanHarm(user, creature)))
                continue;

            _effects.DealDamage(user, creature, amount, damageType, critical: false);
        }
    }

    private void Break(Entity<CFBombComponent> bomb)
    {
        bomb.Comp.Broken = true;
        _audio.PlayPvs(bomb.Comp.BreakSound, Transform(bomb).Coordinates);
        QueueDel(bomb);
    }
}

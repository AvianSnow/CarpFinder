using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Factions;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;

namespace Content.Shared.CF.Cover;

/// <summary>How much stands between an attacker and a creature.</summary>
public enum CFCover : byte
{
    None,

    /// <summary>Another creature is in the way: +1 to AC and nothing else.</summary>
    Lesser,

    /// <summary>A corner or the like: +2 to AC, to Reflex against areas and to Stealth.</summary>
    Standard,

    /// <summary>All but a sliver is behind something: +4 to the same.</summary>
    Greater,
}

/// <summary>A creature that used Take Cover, and where. Leaving the spot or attacking ends it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFTookCoverComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityCoordinates Position;
}

/// <summary>
/// Cover, for Strikes, spell attacks, Reflex saves against areas, and Stealth. Three sight lines are drawn to the
/// creature's centre and sides, so a corner gives cover and a whole wall gives greater cover. The rules count grid
/// corners instead.
/// </summary>
public sealed partial class CFCoverSystem : EntitySystem
{
    /// <summary>How far to each side of a creature the outer sight lines reach. Mobs have a 0.35 radius.</summary>
    private const float Spread = 0.3f;

    /// <summary>How far a creature may shift and still be where it took cover.</summary>
    private const float TakeCoverSlack = 0.5f;

    /// <summary>How far away an enemy can be and still count for whether there is cover to take: 60 feet.</summary>
    private const float AwarenessTiles = 12f;

    /// <summary>What stops a sight line. Mobs are on none of these.</summary>
    private const int WallMask = (int) (CollisionGroup.Impassable | CollisionGroup.HighImpassable | CollisionGroup.MidImpassable);

    /// <summary>What a creature's body is on. Walls are too, but they are looked for first.</summary>
    private const int BodyMask = (int) CollisionGroup.BulletImpassable;

    private static readonly float[] Offsets = { 0f, 1f, -1f };

    [Dependency] private INetManager _net = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFShieldSystem _shields = default!;

    /// <summary>The cover <paramref name="target"/> has from <paramref name="attacker"/>.</summary>
    /// <param name="ranged">A prone creature that took cover only has it against ranged attacks.</param>
    /// <param name="creatures">Whether a creature in the way counts, as lesser cover.</param>
    public CFCover Cover(EntityUid target, EntityUid attacker, bool ranged = true, bool creatures = true)
    {
        return Cover(target, _transform.GetMapCoordinates(attacker), attacker, ranged, creatures);
    }

    /// <summary>The cover <paramref name="target"/> has from a point, such as where an area starts.</summary>
    public CFCover Cover(EntityUid target, MapCoordinates from, EntityUid? attacker = null, bool ranged = true, bool creatures = true)
    {
        var to = _transform.GetMapCoordinates(target);
        if (from.MapId != to.MapId)
            return CFCover.Greater;

        var cover = Walls(from, to.Position, target, attacker);
        if (TookCover(target))
        {
            if (cover == CFCover.Standard)
                cover = CFCover.Greater;
            else if (cover == CFCover.None && ranged && _conditions.IsProne(target))
                cover = CFCover.Greater;
        }

        if (cover != CFCover.None || !creatures)
            return cover;

        return CreatureBetween(from, to.Position, target, attacker) ? CFCover.Lesser : CFCover.None;
    }

    /// <summary>
    /// The circumstance bonus to AC cover gives against this attacker, beyond what a raised shield or another
    /// circumstance bonus already gives: they do not add up.
    /// </summary>
    public int AcBonus(EntityUid target, EntityUid attacker, bool ranged, bool ignoreLesser = false)
    {
        if (target == attacker)
            return 0;

        var cover = Cover(target, attacker, ranged, creatures: !ignoreLesser);
        return Beyond(Bonus(cover), Math.Max(_shields.RaisedBonus(target), _bonuses.TotalOfType(target, CFBonusStat.ArmorClass, CFBonusType.Circumstance)));
    }

    /// <summary>The circumstance bonus to a Reflex save against an area that starts at <paramref name="origin"/>.</summary>
    public int ReflexBonus(EntityUid target, MapCoordinates origin)
    {
        var cover = Cover(target, origin, creatures: false);
        return Beyond(Bonus(cover), _bonuses.TotalOfType(target, CFBonusStat.Reflex, CFBonusType.Circumstance));
    }

    /// <summary>The circumstance bonus to Stealth against this observer. Above zero is what Hide and Sneak need.</summary>
    public int StealthBonus(EntityUid creature, EntityUid observer)
    {
        return Bonus(Cover(creature, observer, ranged: false, creatures: false));
    }

    public static int Bonus(CFCover cover)
    {
        return cover switch
        {
            CFCover.Lesser => 1,
            CFCover.Standard => 2,
            CFCover.Greater => 4,
            _ => 0,
        };
    }

    private static int Beyond(int bonus, int already)
    {
        return Math.Max(0, bonus - Math.Max(0, already));
    }

    #region Take Cover

    /// <summary>Whether the creature took cover and is still where it did.</summary>
    public bool TookCover(EntityUid uid)
    {
        return TryComp(uid, out CFTookCoverComponent? took)
               && took.Position.TryDistance(EntityManager, Transform(uid).Coordinates, out var distance)
               && distance < TakeCoverSlack;
    }

    /// <summary>Take Cover would do something: the creature is prone, or has cover from an enemy near it.</summary>
    public bool CanTakeCover(EntityUid user)
    {
        if (TookCover(user))
            return false;

        if (_conditions.IsProne(user))
            return true;

        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(_transform.GetMapCoordinates(user), AwarenessTiles))
        {
            if (mob.Owner != user
                && _mobState.IsAlive(mob)
                && _factions.CanHarm(mob, user)
                && Cover(user, mob.Owner, creatures: false) >= CFCover.Standard)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One action. Cover from walls counts as greater cover, and a prone creature has greater cover against ranged
    /// attacks, until it leaves the spot or makes an attack roll. The rules end it on any move.
    /// </summary>
    public bool TryTakeCover(EntityUid user)
    {
        if (!_net.IsServer)
            return false;

        if (!CanTakeCover(user))
        {
            _popup.PopupEntity(Loc.GetString("cf-cover-nothing"), user, user);
            return false;
        }

        if (!_actions.TrySpendActions(user, 1))
            return false;

        var took = EnsureComp<CFTookCoverComponent>(user);
        took.Position = Transform(user).Coordinates;
        Dirty(user, took);
        _popup.PopupEntity(Loc.GetString("cf-cover-taken"), user, user);
        return true;
    }

    /// <summary>Any attack roll ends Take Cover.</summary>
    public void EndCover(EntityUid user)
    {
        if (_net.IsServer && HasComp<CFTookCoverComponent>(user))
            RemComp<CFTookCoverComponent>(user);
    }

    #endregion

    #region Sight lines

    private CFCover Walls(MapCoordinates from, Vector2 to, EntityUid target, EntityUid? attacker)
    {
        var line = to - from.Position;
        var length = line.Length();
        if (length <= 0f)
            return CFCover.None;

        var side = new Vector2(-line.Y, line.X) / length * Spread;
        var blocked = 0;
        foreach (var offset in Offsets)
        {
            if (LineBlocked(from, to + side * offset, target, attacker))
                blocked++;
        }

        return blocked == 0 ? CFCover.None : blocked == Offsets.Length ? CFCover.Greater : CFCover.Standard;
    }

    private bool LineBlocked(MapCoordinates from, Vector2 to, EntityUid target, EntityUid? attacker)
    {
        var line = to - from.Position;
        var length = line.Length();
        if (length <= 0f)
            return false;

        var ray = new CollisionRay(from.Position, line / length, WallMask);
        foreach (var _ in _physics.IntersectRayWithPredicate(from.MapId, ray, length, uid => uid == target || uid == attacker))
            return true;

        return false;
    }

    /// <summary>A living creature, other than the two, stands on the line between their centres.</summary>
    private bool CreatureBetween(MapCoordinates from, Vector2 to, EntityUid target, EntityUid? attacker)
    {
        var line = to - from.Position;
        var length = line.Length();
        if (length <= 0f)
            return false;

        var ray = new CollisionRay(from.Position, line / length, BodyMask);
        foreach (var hit in _physics.IntersectRayWithPredicate(from.MapId, ray, length, uid => uid == target || uid == attacker, returnOnFirstHit: false))
        {
            if (TryComp(hit.HitEntity, out MobStateComponent? state) && _mobState.IsAlive(hit.HitEntity, state))
                return true;
        }

        return false;
    }

    #endregion
}

using System.Numerics;
using Content.Server.CF.Temperature;
using Content.Shared.CF.GOAP;
using Content.Shared.Fluids.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CF.GOAP.Society.Steps;

/// <summary>
/// Keeps the nest: the fire is fed and relit, rubbish is picked up and thrown out past the nest's edge, a puddle is
/// wiped up, and a coin, a potion, fuel or anything in its own hand that nobody can use goes into a store. The
/// tribe's count of what each store holds follows, so nobody opens a crate to find out.
/// </summary>
public sealed partial class CFGOAPKeepNestStepSystem : CFGOAPErrandStepSystem
{
    private static readonly EntProtoId Sparkle = "PuddleSparkle";

    /// <summary>How far past the nest's edge rubbish is thrown, in tiles.</summary>
    private const float ThrowBeyond = 3f;

    private const float ThrowSpeed = 8f;

    private static readonly TimeSpan WipeTime = TimeSpan.FromSeconds(2);

    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedStackSystem _stacks = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private CFFireplaceSystem _fireplaces = default!;

    protected override CFGOAPStep Kind => CFGOAPStep.KeepNest;

    protected override float Reach => 1.4f;

    protected override TimeSpan Dwell => TimeSpan.FromSeconds(0.5);

    protected override bool TryPick(CFErrand errand, CFGOAPActionPrototype action, out EntityUid target)
    {
        var found = Society.TryFindChore(errand.Uid, errand.Tribe.Comp, Xform.GetMapCoordinates(errand.Uid), out target, out var kind);
        errand.Social.Chore = found ? kind : CFChoreKind.None;
        return found;
    }

    public override void Begin(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        if (TryGetErrand(ent, out var before))
        {
            before.Social.Working = null;
            before.Social.Chore = CFChoreKind.None;
        }

        base.Begin(ent, action);

        // The fire serves everyone at once.
        if (TryGetErrand(ent, out var errand) && errand.Social.Chore == CFChoreKind.FeedFire && errand.Social.Errand is { } fire)
            Tribes.Release(errand.Tribe.Comp, fire, ent);
    }

    /// <summary>Feeding the fire wants fuel in hand: a piece in its hand or lying about, or one out of a store.</summary>
    protected override EntityUid? PickTool(CFErrand errand, EntityUid target)
    {
        if (errand.Social.Chore != CFChoreKind.FeedFire)
            return null;

        var here = Xform.GetMapCoordinates(errand.Uid);
        if (Society.TryFindFuel(errand.Uid, errand.Tribe.Comp, here, out var fuel))
            return fuel;

        if (Society.TryFindStoreWith(errand.Tribe.Comp, here, record => record.Fuel > 0, out var store))
            errand.Social.Working = store;

        return null;
    }

    protected override EntityUid? Detour(CFErrand errand, EntityUid target)
    {
        // The store the fuel is in, until a piece is in hand.
        var social = errand.Social;
        return social.Chore == CFChoreKind.FeedFire && social.Tool == null && social.Working is { } store ? store : null;
    }

    protected override void AtDetour(CFErrand errand, EntityUid detour)
    {
        var uid = errand.Uid;
        var social = errand.Social;
        var tribe = errand.Tribe.Comp;
        social.Working = null;
        if (TryComp(detour, out StorageComponent? storage))
        {
            foreach (var item in storage.Container.ContainedEntities)
            {
                if (!Tribes.IsFuel(item) || !Hands.TryPickupAnyHand(uid, item))
                    continue;

                Tribes.NoteTaken(tribe, detour, item);
                social.Tool = item;
                Tribes.Claim(tribe, item, uid);
                return;
            }
        }

        // The tribe thought there was fuel here. Now it knows better.
        if (tribe.Stock.TryGetValue(detour, out var record))
            record.Fuel = 0;

        social.Errand = null;
    }

    protected override bool Finish(CFErrand errand, EntityUid target, CFGOAPActionPrototype action)
    {
        var uid = errand.Uid;
        var social = errand.Social;
        var tribe = errand.Tribe.Comp;
        switch (social.Chore)
        {
            case CFChoreKind.FeedFire:
                if (social.Tool is not { } fuel || !Hands.IsHolding(uid, fuel) || !TryComp(target, out CFFireplaceComponent? fireplace))
                    return true;

                // One piece off a stack. What is left stays in hand, and goes to a store as the next chore.
                var piece = fuel;
                if (TryComp(fuel, out StackComponent? stack) && stack.Count > 1 && _stacks.Split((fuel, stack), 1, Xform.GetMoverCoordinates(uid), uid) is { } split)
                    piece = split;

                _fireplaces.TryFeed((target, fireplace), piece, uid);
                return true;

            case CFChoreKind.Puddle:
                if (Timing.CurTime < (social.ArrivedAt ?? Timing.CurTime) + WipeTime)
                    return false;

                if (HasComp<PuddleComponent>(target))
                {
                    Spawn(Sparkle, Transform(target).Coordinates);
                    QueueDel(target);
                }

                return true;

            case CFChoreKind.Rubbish:
                if (!Hands.IsHolding(uid, target) && (Containers.IsEntityInContainer(target) || !Hands.TryPickupAnyHand(uid, target)))
                    return true;

                ThrowOut(errand, target);
                return true;

            case CFChoreKind.Valuable:
                // At the store, with the thing in hand.
                if (HasComp<StorageComponent>(target))
                {
                    if (social.Working is { } item && Hands.IsHolding(uid, item) && _storage.Insert(target, item, out _, user: uid))
                        Tribes.NoteStored(tribe, target, item);

                    return true;
                }

                if (!Hands.IsHolding(uid, target) && (Containers.IsEntityInContainer(target) || !Hands.TryPickupAnyHand(uid, target)))
                    return true;

                if (!Tribes.TryFindStore(tribe, target, Xform.GetMapCoordinates(uid), out var store, uid))
                {
                    // Nowhere to keep it, not even its own pack. Down it goes, so its hands are free.
                    Hands.TryDrop(uid, target);
                    return true;
                }

                // Its own pack is right here: stash it and be done.
                if (Tribes.TryGetPack(uid, out var pack) && store == pack)
                {
                    if (_storage.Insert(pack, target, out _, user: uid))
                        Tribes.NoteStored(tribe, pack, target);

                    return true;
                }

                // Now the errand is to the store. Run walks there and calls this again.
                Tribes.Release(tribe, target, uid);
                social.Working = target;
                social.Errand = store;
                social.ArrivedAt = null;
                social.WalkTarget = null;
                return false;

            default:
                return true;
        }
    }

    /// <summary>Throws it out of the nest, straight away from the fire. Past the nest's edge it is no longer a mess.</summary>
    private void ThrowOut(CFErrand errand, EntityUid rubbish)
    {
        var uid = errand.Uid;
        var tribe = errand.Tribe.Comp;
        var here = Xform.GetMapCoordinates(uid);
        var nest = tribe.Nest is { } coords ? Xform.ToMapCoordinates(coords) : here;
        var away = here.Position - nest.Position;
        if (away.LengthSquared() < 0.01f)
            away = _random.NextAngle().ToVec();

        away = Vector2.Normalize(away);
        var destination = nest.Position + away * (tribe.NestTiles + ThrowBeyond);
        Hands.TryDrop(uid, rubbish);
        _throwing.TryThrow(rubbish, destination - here.Position, ThrowSpeed, uid, compensateFriction: true);
    }

    public override void End(Entity<CFGOAPComponent> ent, CFGOAPActionPrototype action)
    {
        base.End(ent, action);
        if (TryGetErrand(ent, out var errand))
        {
            errand.Social.Working = null;
            errand.Social.Chore = CFChoreKind.None;
        }
    }
}

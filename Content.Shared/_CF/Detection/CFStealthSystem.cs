using System.Numerics;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects.Areas;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Hazards;
using Content.Shared.CF.Preferences;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.CombatMode;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Detection;

/// <summary>
/// Remaster Hide, Sneak and Seek. Each creature keeps its own view of the sneaker in
/// <see cref="CFStealthComponent.Detection"/>, which the AI reads the same way players do.
/// </summary>
public sealed partial class CFStealthSystem : EntitySystem
{
    /// <summary>How far a creature can be and still get a roll: 60 feet.</summary>
    private const float AwarenessTiles = 12f;

    /// <summary>Seek's area, around the spot pointed at.</summary>
    private const float SeekFeet = 30f;

    /// <summary>Added to Perception DCs against another diversion from the same creature.</summary>
    private const int DiversionDcBonus = 4;

    private const int CoverBonusStandard = 2;
    private const int CoverBonusGreater = 4;

    /// <summary>How far to each side of a creature the outer sight lines reach. Mobs have a 0.35 radius.</summary>
    private const float CoverSpread = 0.3f;

    /// <summary>What stops a sight line. Mobs are on none of these, so they give no cover.</summary>
    private const int CoverMask = (int) (CollisionGroup.Impassable | CollisionGroup.HighImpassable | CollisionGroup.MidImpassable);

    private static readonly TimeSpan ShotRevealDelay = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan DiversionMemory = TimeSpan.FromMinutes(1);

    /// <summary>A diversion lasts until the end of the creature's next turn, unless it Hides or Sneaks.</summary>
    private static readonly TimeSpan DiversionTime = TimeSpan.FromSeconds(6);

    private static readonly float[] CoverOffsets = { 0f, 1f, -1f };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedCombatModeSystem _combatMode = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFHazardSystem _hazards = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFStealthComponent, InputMoverComponent, CFActionPoolComponent>();
        while (query.MoveNext(out var uid, out var stealth, out var mover, out var pool))
        {
            if (!stealth.Active)
                continue;

            if (stealth.RevealAt is { } revealAt && now >= revealAt)
            {
                Reveal(uid);
                continue;
            }

            if (!mover.HasDirectionalMovement)
                continue;

            // Running is a Stride, which gives a creature away.
            if (mover.Sprinting)
            {
                Reveal(uid);
                continue;
            }

            stealth.SneakProgress += frameTime;
            if (stealth.SneakProgress < _actions.RegenSeconds(uid, pool))
                continue;

            stealth.SneakProgress = 0f;
            TrySneak(uid);
        }
    }

    /// <summary>How <paramref name="observer"/> sees this creature.</summary>
    public CFDetection Detection(EntityUid creature, EntityUid observer)
    {
        if (!TryComp(creature, out CFStealthComponent? stealth) || !stealth.Active)
            return CFDetection.Observed;

        var detection = stealth.Detection.GetValueOrDefault(observer);
        return detection == CFDetection.Undetected && SensesNearby(observer, creature)
            ? CFDetection.Hidden
            : detection;
    }

    /// <summary>An imprecise sense, such as scent, finds the square of a creature it cannot see.</summary>
    private bool SensesNearby(EntityUid observer, EntityUid creature)
    {
        if (!TryComp(observer, out CFSensesComponent? senses) || senses.ImpreciseFeet <= 0)
            return false;

        var from = _transform.GetMapCoordinates(observer);
        var to = _transform.GetMapCoordinates(creature);
        return from.MapId == to.MapId
               && (from.Position - to.Position).Length() * CFAreaGrid.FeetPerTile <= senses.ImpreciseFeet;
    }

    public bool IsUnseenBy(EntityUid creature, EntityUid observer)
    {
        return Detection(creature, observer) != CFDetection.Observed;
    }

    /// <summary>
    /// How much a sight line from the observer is blocked: none, cover, or greater cover. Three lines are drawn
    /// to the creature's center and sides, so a corner gives cover and a whole wall gives greater cover.
    /// </summary>
    public int CoverBonus(EntityUid creature, EntityUid observer)
    {
        var from = _transform.GetMapCoordinates(observer);
        var to = _transform.GetMapCoordinates(creature);
        if (from.MapId != to.MapId)
            return CoverBonusGreater;

        var line = to.Position - from.Position;
        var length = line.Length();
        if (length <= 0f)
            return 0;

        var side = new Vector2(-line.Y, line.X) / length * CoverSpread;
        var blocked = 0;
        foreach (var offset in CoverOffsets)
        {
            if (LineBlocked(from, to.Position + side * offset, creature, observer))
                blocked++;
        }

        return blocked == 0 ? 0 : blocked == CoverOffsets.Length ? CoverBonusGreater : CoverBonusStandard;
    }

    private bool LineBlocked(MapCoordinates from, Vector2 to, EntityUid creature, EntityUid observer)
    {
        var line = to - from.Position;
        var length = line.Length();
        if (length <= 0f)
            return false;

        var ray = new CollisionRay(from.Position, line / length, CoverMask);
        foreach (var _ in _physics.IntersectRayWithPredicate(from.MapId, ray, length, uid => uid == creature || uid == observer))
            return true;

        return false;
    }

    /// <summary>
    /// One action. Every enemy this creature has cover or concealment from rolls Perception against its Stealth,
    /// and those it beats lose sight of it. A closet passes <paramref name="fixedCover"/> and costs nothing,
    /// because getting in is the action.
    /// </summary>
    public bool TryHide(EntityUid user, int? fixedCover = null)
    {
        if (!_net.IsServer || !HasComp<CFCombatantComponent>(user))
            return false;

        if (fixedCover == null && !_actions.TrySpendActions(user, 1, stealthy: true))
            return false;

        var stealth = EnsureComp<CFStealthComponent>(user);
        var enemies = 0;
        var hiddenFrom = 0;
        foreach (var enemy in Enemies(user))
        {
            enemies++;
            if (stealth.Detection.GetValueOrDefault(enemy) != CFDetection.Observed)
            {
                hiddenFrom++;
                continue;
            }

            var cover = fixedCover ?? CoverBonus(user, enemy);
            var beaten = (cover > 0 || IsConcealed(user)) && RollStealth(user, enemy, cover).IsSuccess();
            stealth.Detection[enemy] = beaten ? CFDetection.Hidden : CFDetection.Observed;
            if (beaten)
                hiddenFrom++;
        }

        stealth.Active = enemies == 0 || hiddenFrom > 0;
        stealth.RevealAt = null;
        Dirty(user, stealth);
        _popup.PopupEntity(Loc.GetString(stealth.Active ? "cf-stealth-hidden" : "cf-stealth-seen"), user, user);
        return stealth.Active;
    }

    /// <summary>
    /// One stretch of walking, for one action. The result stays secret, as printed.
    /// With no action to pay, it is a plain Stride and everyone sees it.
    /// </summary>
    public bool TrySneak(EntityUid user)
    {
        if (!_net.IsServer || !TryComp(user, out CFStealthComponent? stealth) || !stealth.Active)
            return false;

        if (!_actions.TrySpendActions(user, 1, stealthy: true))
        {
            Reveal(user);
            return false;
        }

        stealth.RevealAt = null;
        foreach (var enemy in Enemies(user))
        {
            // Only those it was hidden from or undetected by are rolled against. A newcomer starts as hidden.
            if (!stealth.Detection.TryGetValue(enemy, out var before))
                before = CFDetection.Hidden;

            if (before == CFDetection.Observed)
                continue;

            var cover = CoverBonus(user, enemy);
            stealth.Detection[enemy] = cover > 0 || IsConcealed(user)
                ? AfterSneak(RollStealth(user, enemy, cover), before, IsInvisible(user))
                : CFDetection.Observed;
        }

        Dirty(user, stealth);
        return true;
    }

    private static CFDetection AfterSneak(CFDegree degree, CFDetection before, bool invisible)
    {
        return degree switch
        {
            CFDegree.CriticalSuccess or CFDegree.Success => CFDetection.Undetected,
            CFDegree.Failure => CFDetection.Hidden,
            _ => invisible && before == CFDetection.Hidden ? CFDetection.Hidden : CFDetection.Observed,
        };
    }

    /// <summary>
    /// One action. A single Deception check against each nearby enemy's Perception DC, which is 4 higher for a
    /// minute after any attempt on it. Those it beats lose sight of the creature, and need no cover to do so.
    /// </summary>
    public bool TryDiversion(EntityUid user)
    {
        if (!_net.IsServer || !_actions.TrySpendActions(user, 1, stealthy: true))
            return false;

        _conditions.RollSkillAgainst(user, "Deception", CFAbility.Charisma, 0, out var total);
        var now = _timing.CurTime;
        var stealth = EnsureComp<CFStealthComponent>(user);
        var fooled = 0;
        foreach (var enemy in Enemies(user))
        {
            var wary = stealth.DivertedUntil.GetValueOrDefault(enemy) > now;
            stealth.DivertedUntil[enemy] = now + DiversionMemory;
            if (total < _conditions.PerceptionDc(enemy) + (wary ? DiversionDcBonus : 0))
            {
                stealth.Detection.TryAdd(enemy, CFDetection.Observed);
                continue;
            }

            stealth.Detection[enemy] = CFDetection.Hidden;
            fooled++;
        }

        if (fooled > 0)
        {
            stealth.Active = true;
            stealth.RevealAt = now + DiversionTime;
        }

        Dirty(user, stealth);
        _popup.PopupEntity(Loc.GetString(fooled > 0 ? "cf-stealth-diversion" : "cf-stealth-diversion-fail"), user, user);
        return fooled > 0;
    }

    /// <summary>Everyone sees this creature again. Doing anything but Hide, Sneak, or Step does this.</summary>
    public void Reveal(EntityUid uid)
    {
        if (!_net.IsServer || !TryComp(uid, out CFStealthComponent? stealth) || !stealth.Active)
            return;

        stealth.Active = false;
        stealth.RevealAt = null;
        stealth.SneakProgress = 0f;
        stealth.Detection.Clear();
        Dirty(uid, stealth);
    }

    /// <summary>A shot gives the shooter away once it has landed.</summary>
    public void RevealAfterShot(EntityUid uid)
    {
        if (_net.IsServer && TryComp(uid, out CFStealthComponent? stealth) && stealth.Active)
            stealth.RevealAt ??= _timing.CurTime + ShotRevealDelay;
    }

    /// <summary>Speaking or a loud noise gives a position away, but not sight of the creature.</summary>
    public void MakeNoise(EntityUid uid)
    {
        if (!_net.IsServer || !TryComp(uid, out CFStealthComponent? stealth) || !stealth.Active)
            return;

        foreach (var (observer, detection) in stealth.Detection)
        {
            if (detection == CFDetection.Undetected)
                stealth.Detection[observer] = CFDetection.Hidden;
        }

        Dirty(uid, stealth);
    }

    /// <summary>
    /// One action, a secret Perception check against the Stealth DC of each creature hiding from the seeker.
    /// A success turns undetected into hidden and hidden into observed. A critical success sees either.
    /// Hazards in the area get the usual notice check.
    /// </summary>
    public bool TrySeek(EntityUid user, MapCoordinates spot)
    {
        if (!_net.IsServer || !_actions.TrySpendActions(user, 1))
            return false;

        var tiles = SeekFeet / CFAreaGrid.FeetPerTile;
        foreach (var hider in _lookup.GetEntitiesInRange<CFStealthComponent>(spot, tiles))
        {
            var before = Detection(hider, user);
            if (hider.Owner == user || before == CFDetection.Observed)
                continue;

            var degree = CFDefense.Roll(_random, _conditions.PerceptionModifier(user), _conditions.StealthDc(hider));
            if (!degree.IsSuccess())
                continue;

            var seen = degree == CFDegree.CriticalSuccess || before == CFDetection.Hidden;
            hider.Comp.Detection[user] = seen ? CFDetection.Observed : CFDetection.Hidden;
            Dirty(hider);
        }

        foreach (var hazard in _lookup.GetEntitiesInRange<CFHazardComponent>(spot, tiles))
        {
            _hazards.TryNotice(hazard, user);
        }

        _popup.PopupEntity(Loc.GetString("cf-stealth-seek"), user, user);
        return true;
    }

    /// <summary>Whether something can be hidden behind: a closet, or anything fixed and solid.</summary>
    public bool IsCover(EntityUid target)
    {
        if (HasComp<EntityStorageComponent>(target))
            return true;

        return Transform(target).Anchored && TryComp(target, out PhysicsComponent? body) && body.CanCollide && body.Hard;
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<CFCombatantComponent>(args.User) || !IsCover(args.Target))
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("cf-stealth-hide"),
            Act = () => TryHide(user),
        });
    }

    [SubscribeLocalEvent]
    private void OnPointedAtGround(Entity<CFCombatOptionsComponent> ent, ref CFPointedAtGroundEvent args)
    {
        if (_combatMode.IsInCombatMode(ent))
            TrySeek(ent, args.Coordinates);
    }

    [SubscribeLocalEvent]
    private void OnSpoke(Entity<CFStealthComponent> ent, ref EntitySpokeEvent args)
    {
        MakeNoise(ent);
    }

    [SubscribeLocalEvent]
    private void OnEmote(Entity<CFStealthComponent> ent, ref EmoteEvent args)
    {
        if (args.Emote.Category.HasFlag(EmoteCategory.Vocal))
            MakeNoise(ent);
    }

    /// <summary>A shut closet is greater cover from everyone.</summary>
    [SubscribeLocalEvent]
    private void OnStorageClosed(Entity<EntityStorageComponent> ent, ref StorageAfterCloseEvent args)
    {
        foreach (var inside in ent.Comp.Contents.ContainedEntities)
        {
            if (HasComp<MobStateComponent>(inside))
                TryHide(inside, CoverBonusGreater);
        }
    }

    /// <summary>
    /// Forgets a creature that is going away. <see cref="CFStealthComponent.Detection"/> is networked, and a deleted
    /// key turns into an invalid entity when the state is sent. Two of those in one dictionary collide, and the state
    /// for every player who can see the hider fails each tick from then on.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTerminating(ref EntityTerminatingEvent args)
    {
        var gone = args.Entity.Owner;
        var query = AllEntityQuery<CFStealthComponent>();
        while (query.MoveNext(out var uid, out var stealth))
        {
            stealth.DivertedUntil.Remove(gone);
            if (stealth.Detection.Remove(gone))
                Dirty(uid, stealth);
        }
    }

    /// <summary>Stock empties a closet before saying it opened, so this watches the creature leave instead.</summary>
    [SubscribeLocalEvent]
    private void OnLeftContainer(Entity<CFStealthComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        Reveal(ent);
    }

    private IEnumerable<EntityUid> Enemies(EntityUid user)
    {
        foreach (var mob in _lookup.GetEntitiesInRange<MobStateComponent>(_transform.GetMapCoordinates(user), AwarenessTiles))
        {
            if (mob.Owner != user && _mobState.IsAlive(mob) && _factions.CanHarm(mob, user))
                yield return mob;
        }
    }

    private CFDegree RollStealth(EntityUid user, EntityUid observer, int cover)
    {
        return _conditions.RollSkillAgainst(
            user,
            "Stealth",
            CFAbility.Dexterity,
            _conditions.PerceptionDc(observer),
            out _,
            itemBonus: cover);
    }

    private bool IsConcealed(EntityUid uid)
    {
        return TryComp(uid, out CFConditionComponent? cond) && cond.Concealed;
    }

    private bool IsInvisible(EntityUid uid)
    {
        return TryComp(uid, out CFConditionComponent? cond) && cond.Concealed && cond.FlatCheckDc >= CFConditionSystem.HiddenFlatCheck;
    }
}

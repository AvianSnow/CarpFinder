using Content.Shared.Bed.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.CF.Character;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Spells;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server.CF.Character;

/// <summary>
/// Falling asleep in a bed is a long rest: hit points and spell slots come back at once. A character cannot get into one
/// with an enemy close by, and has to wait <see cref="Cooldown"/> between them. PF2e's 8 hours and once a day would not
/// fit a game this quick. Sleeping anywhere else restores nothing, so none of this applies there.
/// </summary>
public sealed partial class CFLongRestSystem : EntitySystem
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    /// <summary>How close an enemy has to be to stop a rest, in tiles: 50 feet.</summary>
    public const float EnemyRange = 10f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private CFFactionSystem _factions = default!;
    [Dependency] private CFDyingSystem _dying = default!;
    [Dependency] private CFReactionSystem _reactions = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private readonly HashSet<Entity<MobStateComponent>> _nearby = new();

    [SubscribeLocalEvent]
    private void OnTryingToSleep(Entity<CFCombatantComponent> ent, ref TryingToSleepEvent args)
    {
        if (!InBed(ent))
            return;

        if (EnemyNear(ent))
        {
            args.Cancelled = true;
            _popup.PopupEntity(Loc.GetString("cf-long-rest-enemies"), ent, ent);
        }
        else if (MinutesLeft(ent) is var minutes and > 0)
        {
            args.Cancelled = true;
            _popup.PopupEntity(Loc.GetString("cf-long-rest-cooldown", ("minutes", minutes)), ent, ent);
        }
    }

    [SubscribeLocalEvent]
    private void OnSleepStateChanged(Entity<CFCombatantComponent> ent, ref SleepStateChangedEvent args)
    {
        // Something that forces sleep skips the attempt above, so this has to ask too.
        if (args.FellAsleep && InBed(ent) && _mobState.IsAlive(ent) && !EnemyNear(ent) && MinutesLeft(ent) <= 0)
            LongRest(ent);
    }

    /// <summary>Whether a living enemy is close and nothing solid is in the way. A dungeon is full of rooms, so walls count.</summary>
    public bool EnemyNear(EntityUid mob)
    {
        _nearby.Clear();
        _lookup.GetEntitiesInRange(Transform(mob).Coordinates, EnemyRange, _nearby);
        foreach (var other in _nearby)
        {
            if (other.Owner != mob
                && _mobState.IsAlive(other, other.Comp)
                && _factions.GetAlliance(mob, other) == CFAlliance.Opposition
                && _interaction.InRangeUnobstructed(mob, other.Owner, EnemyRange))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whole minutes until the next long rest, rounded up. Zero or less means it can be taken now.</summary>
    public int MinutesLeft(EntityUid mob)
    {
        if (!TryComp(mob, out CFRestCooldownComponent? cooldown))
            return 0;

        return (int) Math.Ceiling((cooldown.ReadyAt - _timing.CurTime).TotalMinutes);
    }

    /// <summary>A bed is whatever stock lets you sleep in: a strap with <see cref="HealOnBuckleComponent"/>.</summary>
    private bool InBed(EntityUid mob)
    {
        return TryComp(mob, out BuckleComponent? buckle)
               && buckle.BuckledTo is { } strap
               && HasComp<HealOnBuckleComponent>(strap);
    }

    public void LongRest(EntityUid mob)
    {
        EnsureComp<CFRestCooldownComponent>(mob).ReadyAt = _timing.CurTime + Cooldown;
        _dying.RestoreFully(mob);
        _reactions.RestoreDaily(mob);

        if (TryComp(mob, out CFSpellbookComponent? book))
        {
            for (var rank = 0; rank < book.Expended.Count; rank++)
            {
                book.Expended[rank] = 0;
            }

            for (var rank = 0; rank < book.ApparitionExpended.Count; rank++)
            {
                book.ApparitionExpended[rank] = 0;
            }

            CFPreparedSpells.Refresh(book);
            book.FocusSpent = 0;
            Dirty(mob, book);
        }

        _popup.PopupEntity(Loc.GetString("cf-long-rest"), mob, mob);
    }
}

using Content.Shared.CF.Character;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.Companions;
using Content.Shared.CF.Crafting;
using Content.Shared.CF.Factions;
using Content.Shared.CF.Preferences;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Tools.Components;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.CF.Harvest;

/// <summary>
/// Harvesting a dead monster for parts, after Battlezoo Bestiary's monster parts. It needs a survival knife and
/// training in Survival. The Survival check against the monster's level DC sets what the parts are worth. It replaces
/// stock butchering: the body goes, what it carried drops, and its parts land around it. There are no organs.
/// A summoned creature leaves no body, and a player character is never harvested.
/// </summary>
public sealed partial class CFHarvestSystem : EntitySystem
{
    private const string Skill = "Survival";

    [Dependency] private INetManager _net = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private CFConditionSystem _conditions = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;

    /// <summary>How hard parts are scattered from the body.</summary>
    private const float PartImpulse = 3f;

    /// <summary>
    /// Takes over stock butchering, by knife or on the kitchen spike, so the body can only be harvested with the
    /// check. Its butchering list becomes the parts, unless the monster lists its own.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStartup(Entity<CFHarvestableComponent> ent, ref ComponentStartup args)
    {
        if (_net.IsClient)
            return;

        if (TryComp(ent, out ToolRefinableComponent? knife))
        {
            if (ent.Comp.Parts.Count == 0)
                ent.Comp.Parts.AddRange(knife.RefineResult);

            RemCompDeferred<ToolRefinableComponent>(ent);
        }

        if (TryComp(ent, out ButcherableComponent? spike))
        {
            if (ent.Comp.Parts.Count == 0)
                ent.Comp.Parts.AddRange(spike.SpawnedEntities);

            RemCompDeferred<ButcherableComponent>(ent);
        }
    }

    /// <summary>A stock mob that could be butchered is harvested instead.</summary>
    [SubscribeLocalEvent]
    private void OnRefinableMapInit(Entity<ToolRefinableComponent> ent, ref MapInitEvent args)
    {
        if (HasComp<MobStateComponent>(ent))
            EnsureComp<CFHarvestableComponent>(ent);
    }

    [SubscribeLocalEvent]
    private void OnButcherableMapInit(Entity<ButcherableComponent> ent, ref MapInitEvent args)
    {
        if (HasComp<MobStateComponent>(ent))
            EnsureComp<CFHarvestableComponent>(ent);
    }

    /// <summary>
    /// True when this body is there to be harvested: dead, not yet harvested, not summoned, and not a player
    /// character. Only player characters have <see cref="CFPvpComponent"/>.
    /// </summary>
    public bool IsHarvestable(Entity<CFHarvestableComponent> ent)
    {
        return !ent.Comp.Harvested && _mobState.IsDead(ent)
               && !HasComp<CFSummonedComponent>(ent) && !HasComp<CFPvpComponent>(ent);
    }

    [SubscribeLocalEvent]
    private void OnInteractUsing(Entity<CFHarvestableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<CFHarvestToolComponent>(args.Used) || !IsHarvestable(ent))
            return;

        args.Handled = true;
        TryStartHarvest(args.User, ent, args.Used);
    }

    [SubscribeLocalEvent]
    private void OnGetVerbs(Entity<CFHarvestableComponent> ent, ref GetVerbsEvent<UtilityVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } knife
            || !HasComp<CFHarvestToolComponent>(knife) || !IsHarvestable(ent))
            return;

        var user = args.User;
        args.Verbs.Add(new UtilityVerb
        {
            Text = Loc.GetString("cf-harvest-verb"),
            Act = () => TryStartHarvest(user, ent, knife),
        });
    }

    [SubscribeLocalEvent]
    private void OnExamined(Entity<CFHarvestableComponent> ent, ref ExaminedEvent args)
    {
        if (IsHarvestable(ent))
            args.PushMarkup(Loc.GetString("cf-harvest-examine"));
    }

    /// <summary>True when the creature can harvest parts at all: trained in Survival.</summary>
    public bool CanHarvest(EntityUid user)
    {
        return TryComp(user, out CFCombatantComponent? combat)
               && CFDefense.SkillRank(_prototypes, combat, Skill) >= CFProficiencyRank.Trained;
    }

    /// <summary>Starts harvesting a dead monster. False, with a popup, when the user cannot.</summary>
    public bool TryStartHarvest(EntityUid user, Entity<CFHarvestableComponent> target, EntityUid knife)
    {
        if (!IsHarvestable(target))
            return false;

        if (!CanHarvest(user))
        {
            _popup.PopupEntity(Loc.GetString("cf-harvest-untrained"), target, user);
            return false;
        }

        var args = new DoAfterArgs(EntityManager, user, target.Comp.Delay, new CFHarvestDoAfterEvent(), target, target, knife)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        return _doAfter.TryStartDoAfter(args);
    }

    [SubscribeLocalEvent]
    private void OnHarvestDone(Entity<CFHarvestableComponent> ent, ref CFHarvestDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Harvested)
            return;

        args.Handled = true;
        if (_net.IsClient)
            return;

        var degree = RollHarvest(args.User, ent);
        Harvest(args.User, ent, degree);
    }

    /// <summary>The Survival check against the DC for the monster's level.</summary>
    public CFDegree RollHarvest(EntityUid user, EntityUid target)
    {
        if (!TryComp(user, out CFCombatantComponent? combat) || !TryComp(user, out CFAbilityScoreComponent? abilities))
            return CFDegree.CriticalFailure;

        var modifier = CFDefense.SkillModifier(_prototypes, combat, abilities, Skill, CFAbility.Wisdom,
            _conditions.SkillBonus(user, Skill));
        return CFDefense.Roll(_random, modifier, CFCraftingRules.LevelDc(Level(target)));
    }

    private int Level(EntityUid target)
    {
        return CompOrNull<CFCombatantComponent>(target)?.Level ?? 0;
    }

    /// <summary>
    /// Butchers the body for its parts at <paramref name="degree"/>: none on a critical failure, otherwise generic
    /// monster parts plus the meat, bone and leather the monster lists, sharing its value at the degree's share. They
    /// are scattered where it lay. What it wore or held drops, and the body goes. Returns the parts. Server only.
    /// </summary>
    public List<EntityUid> Harvest(EntityUid user, Entity<CFHarvestableComponent> target, CFDegree degree)
    {
        var spawned = new List<EntityUid>();
        target.Comp.Harvested = true;
        Dirty(target);

        var value = CFHarvestRules.Yield(Level(target), degree);
        if (value <= 0)
        {
            _popup.PopupEntity(Loc.GetString("cf-harvest-ruined"), target, user, PopupType.MediumCaution);
            RemoveBody(target);
            return spawned;
        }

        var coords = _transform.GetMoverCoordinates(target);
        var generic = SpawnAtPosition(target.Comp.FallbackPart, coords);
        var genericUnits = Units(generic);

        var others = new List<EntityUid>();
        var otherUnits = 0;
        foreach (var id in EntitySpawnCollection.GetSpawns(target.Comp.Parts, _random))
        {
            var part = SpawnAtPosition(id, coords);
            others.Add(part);
            otherUnits += Units(part);
        }

        // Every part gets an even share, and the generic monster parts keep what rounding leaves, so the total is
        // exactly the value.
        var perUnit = value / (genericUnits + otherUnits);
        foreach (var part in others)
        {
            SetWorth(part, perUnit);
        }

        SetWorth(generic, (value - perUnit * otherUnits) / genericUnits);
        spawned.Add(generic);
        spawned.AddRange(others);

        var message = degree switch
        {
            CFDegree.CriticalSuccess => "cf-harvest-critical",
            CFDegree.Failure => "cf-harvest-poor",
            _ => "cf-harvest-done",
        };
        _popup.PopupEntity(Loc.GetString(message), target, user);
        RemoveBody(target);
        return spawned;
    }

    private int Units(EntityUid part)
    {
        return Math.Max(1, CompOrNull<StackComponent>(part)?.Count ?? 1);
    }

    private void SetWorth(EntityUid part, int copperPerUnit)
    {
        var worth = EnsureComp<CFPartsValueComponent>(part);
        worth.CopperPerUnit = Math.Max(0, copperPerUnit);
        Dirty(part, worth);
        _physics.ApplyLinearImpulse(part, _random.NextAngle().ToVec() * PartImpulse);
    }

    /// <summary>Drops what the body wore and held, then deletes it. No organs: only the parts are left.</summary>
    private void RemoveBody(EntityUid body)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities((body, null, null)))
        {
            if (!HasComp<AttachedClothingComponent>(item))
                _transform.DropNextTo(item, body);
        }

        QueueDel(body);
    }
}

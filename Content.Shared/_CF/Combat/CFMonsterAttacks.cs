using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Traits;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Combat;

/// <summary>What a monster does, for an action, after a Strike that lists it lands (Monster Core's Grab, Knockdown, Push).</summary>
[Serializable, NetSerializable]
public enum CFFollowUp : byte
{
    None,

    /// <summary>Athletics against Fortitude: grabbed for 6 seconds, restrained on a critical success.</summary>
    Grab,

    /// <summary>A Trip.</summary>
    Knockdown,

    /// <summary>A Shove.</summary>
    Push,
}

/// <summary>One melee Strike of a monster: jaws, claw, tail.</summary>
[DataDefinition]
public sealed partial class CFMonsterAttack
{
    /// <summary>What the hand status and the switch verb call it: "jaws".</summary>
    [DataField(required: true)]
    public string Name = string.Empty;

    /// <summary>The printed attack bonus, the +11 in "jaws +11".</summary>
    [DataField(required: true)]
    public int Attack;

    [DataField]
    public int Dice = 1;

    [DataField(required: true)]
    public int Die;

    /// <summary>The printed flat damage, the +4 in "2d8+4".</summary>
    [DataField]
    public int Bonus;

    [DataField(required: true)]
    public string DamageType = string.Empty;

    /// <summary>Weapon traits by <c>cfTrait</c> id: Agile, Finesse, Reach10, Deadly and so on.</summary>
    [DataField]
    public List<ProtoId<CFTraitPrototype>> Traits = new();

    [DataField]
    public CFFollowUp FollowUp;

    /// <summary>The improved version: the follow-up costs no action.</summary>
    [DataField]
    public bool FollowUpFree;

    /// <summary>Run on a creature this attack damages: venom, extra acid, a condition.</summary>
    [DataField]
    public List<CFEffect> OnHit = new();
}

/// <summary>
/// A monster with more than one melee Strike, or with a Strike that grabs, knocks down or pushes. Each attack has its
/// own printed numbers, which replace <c>printedAttack</c> and <c>printedDamage</c>. With no player in the body,
/// <see cref="CFMonsterAttackSystem"/> picks the attack before each Strike. A creature with one plain attack can keep
/// the <c>natural*</c> fields on <c>CFCombatOptions</c> instead.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFMonsterAttacksComponent : Component
{
    [DataField(required: true)]
    public List<CFMonsterAttack> Attacks = new();

    /// <summary>The attack the next Strike is made with.</summary>
    [DataField, AutoNetworkedField]
    public int Current;
}

public sealed partial class CFMonsterAttackSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;
    [Dependency] private CFEffectSystem _effects = default!;
    [Dependency] private CFManeuverSystem _maneuvers = default!;

    /// <summary>The attack the creature's next unarmed Strike uses, as weapon stats. False without the component.</summary>
    public bool TryStats(EntityUid user, string proficiency, out CFStrikeWeaponStats stats)
    {
        stats = default;
        if (!TryCurrent(user, out var attack))
            return false;

        var traits = CFWeaponTraits.From(_prototypes, attack.Traits) with { Unarmed = true };
        stats = new CFStrikeWeaponStats(Math.Max(1, attack.Dice), attack.Die, attack.DamageType, proficiency, traits)
        {
            PrintedAttack = attack.Attack,
            PrintedDamage = attack.Bonus,
        };
        return true;
    }

    public bool TryCurrent(EntityUid user, out CFMonsterAttack attack)
    {
        attack = default!;
        if (!TryComp(user, out CFMonsterAttacksComponent? comp) || comp.Attacks.Count == 0)
            return false;

        attack = comp.Attacks[Math.Clamp(comp.Current, 0, comp.Attacks.Count - 1)];
        return true;
    }

    /// <summary>
    /// The attack with the best bonus once the multiple attack penalty is counted, the first listed on a tie. That is
    /// the main attack with no penalty, and an agile one after it.
    /// </summary>
    public int Best(Entity<CFMonsterAttacksComponent> ent)
    {
        var best = 0;
        var bestBonus = int.MinValue;
        for (var i = 0; i < ent.Comp.Attacks.Count; i++)
        {
            var attack = ent.Comp.Attacks[i];
            var agile = attack.Traits.Contains("Agile");
            var bonus = attack.Attack + _actions.MapPenalty(ent, agile);
            if (bonus <= bestBonus)
                continue;

            best = i;
            bestBonus = bonus;
        }

        return best;
    }

    public void Select(Entity<CFMonsterAttacksComponent> ent, int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, ent.Comp.Attacks.Count - 1));
        if (ent.Comp.Current == index)
            return;

        ent.Comp.Current = index;
        Dirty(ent);
    }

    /// <summary>With no player in the body, the creature takes its best attack for the next Strike.</summary>
    private void Repick(Entity<CFMonsterAttacksComponent> ent)
    {
        if (_net.IsServer && !HasComp<ActorComponent>(ent))
            Select(ent, Best(ent));
    }

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<CFMonsterAttacksComponent> ent, ref MapInitEvent args)
    {
        Repick(ent);
    }

    [SubscribeLocalEvent]
    private void OnTurn(Entity<CFMonsterAttacksComponent> ent, ref CFTurnRefilledEvent args)
    {
        Repick(ent);
    }

    /// <summary>
    /// A natural Strike is raised on the creature itself. On a hit it runs the attack's own effects and its Grab,
    /// Knockdown or Push, then picks the attack for the next Strike.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnStrike(Entity<CFMonsterAttacksComponent> ent, ref CFStrikeResolvedEvent args)
    {
        if (!_net.IsServer || args.User != ent.Owner || !args.Melee)
            return;

        if (TryCurrent(ent, out var attack) && args.Degree.IsSuccess() && _mobState.IsAlive(args.Target))
        {
            if (args.Damage > 0 && attack.OnHit.Count > 0)
            {
                _effects.Run(attack.OnHit, new CFEffectArgs
                {
                    User = ent,
                    Source = ent,
                    Target = args.Target,
                    Position = Transform(args.Target).Coordinates,
                });
            }

            // A player in the body grabs, trips and shoves with its own inputs.
            if (attack.FollowUp != CFFollowUp.None && !HasComp<ActorComponent>(ent))
                _maneuvers.FollowUp(ent, args.Target, attack.FollowUp, attack.FollowUpFree);
        }

        Repick(ent);
    }

    /// <summary>A player in the body chooses its attack from its own right-click menu.</summary>
    [SubscribeLocalEvent]
    private void OnVerbs(Entity<CFMonsterAttacksComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (args.User != ent.Owner || ent.Comp.Attacks.Count < 2)
            return;

        for (var i = 0; i < ent.Comp.Attacks.Count; i++)
        {
            if (i == ent.Comp.Current)
                continue;

            var index = i;
            var name = ent.Comp.Attacks[i].Name;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("cf-monster-attack-switch", ("attack", name)),
                Priority = -i,
                Act = () =>
                {
                    Select(ent, index);
                    _popup.PopupClient(Loc.GetString("cf-monster-attack-now", ("attack", name)), ent, ent);
                },
            });
        }
    }
}

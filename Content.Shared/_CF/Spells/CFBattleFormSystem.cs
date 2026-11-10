using Content.Shared.CF.Combat;
using Content.Shared.CF.Effects;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Spells;

/// <summary>A creature in a battle form, such as Animal Form, and what to put back when it ends.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CFBattleFormComponent : Component
{
    [DataField, AutoNetworkedField]
    public LocId Name;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Until;

    /// <summary>The form's AC. The creature keeps its own if that is higher.</summary>
    [DataField, AutoNetworkedField]
    public int ArmorClass;

    /// <summary>The form's attack modifier for its unarmed attacks. The creature keeps its own if that is higher.</summary>
    [DataField, AutoNetworkedField]
    public int AttackModifier;

    [DataField]
    public int OldArmorClass;

    [DataField]
    public int OldDice;

    [DataField]
    public int OldDie;

    [DataField]
    public string OldType = "";

    [DataField]
    public bool OldFinesse;

    [DataField]
    public bool OldAgile;
}

/// <summary>A spell that turns the caster into one of several battle forms, picked from its mode menu.</summary>
[RegisterComponent]
public sealed partial class CFBattleFormSpellComponent : Component
{
    [DataField(required: true)]
    public List<CFBattleFormOption> Forms = new();

    /// <summary>The form's AC is this plus the caster's level.</summary>
    [DataField]
    public int BaseArmorClass = 16;

    [DataField]
    public int AttackModifier = 9;

    [DataField]
    public int DamageBonus = 1;

    [DataField]
    public int TemporaryHp = 5;

    [DataField]
    public float DurationSeconds = 60f;
}

[DataDefinition]
public sealed partial class CFBattleFormOption
{
    /// <summary>The <c>CFModeChoice</c> value that picks it.</summary>
    [DataField(required: true)]
    public byte Mode;

    [DataField(required: true)]
    public LocId Name;

    [DataField]
    public int Dice = 2;

    [DataField]
    public int Die = 8;

    [DataField]
    public string DamageType = "Piercing";

    [DataField]
    public bool Agile;

    [DataField]
    public int SpeedFeet = 30;
}

/// <summary>
/// Battle forms. The form's unarmed attack replaces the fist, with the form's attack modifier and AC unless the creature's
/// own are higher. It cannot cast spells or use a weapon in the form, which lasts until it runs out or the creature drops.
/// </summary>
public sealed partial class CFBattleFormSystem : EntitySystem
{
    private const string BonusSource = "battle-form";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFSpellSystem _spells = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFArmorSystem _armor = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CFBattleFormComponent>();
        while (query.MoveNext(out var uid, out var form))
        {
            if (now >= form.Until)
                End(uid);
        }
    }

    [SubscribeLocalEvent]
    private void OnPicked(Entity<CFBattleFormSpellComponent> ent, ref CFModeImmediateCastEvent args)
    {
        var mode = args.Mode;
        if (!_net.IsServer
            || ent.Comp.Forms.Find(form => form.Mode == mode) is not { } option
            || !TryComp(ent, out CFSpellComponent? spell)
            || !_spells.TrySpend(args.User, spell))
        {
            return;
        }

        Apply(args.User, ent.Comp, option);
        args.Success = true;
    }

    /// <summary>Puts <paramref name="user"/> into the form, replacing any form it was already in.</summary>
    public void Apply(EntityUid user, CFBattleFormSpellComponent spell, CFBattleFormOption option)
    {
        if (HasComp<CFBattleFormComponent>(user))
            End(user);

        var combat = EnsureComp<CFCombatantComponent>(user);
        var options = EnsureComp<CFCombatOptionsComponent>(user);
        var form = EnsureComp<CFBattleFormComponent>(user);
        form.Name = option.Name;
        form.Until = _timing.CurTime + TimeSpan.FromSeconds(spell.DurationSeconds);
        form.ArmorClass = spell.BaseArmorClass + combat.Level;
        form.AttackModifier = spell.AttackModifier;
        form.OldArmorClass = combat.ArmorClass;
        form.OldDice = options.NaturalDice;
        form.OldDie = options.NaturalDie;
        form.OldType = options.NaturalType;
        form.OldFinesse = options.NaturalFinesse;
        form.OldAgile = options.NaturalAgile;
        Dirty(user, form);

        options.NaturalDice = option.Dice;
        options.NaturalDie = option.Die;
        options.NaturalType = option.DamageType;
        options.NaturalFinesse = false;
        options.NaturalAgile = option.Agile;
        Dirty(user, options);

        combat.ArmorClass = Math.Max(combat.ArmorClass, form.ArmorClass);
        combat.TemporaryHp = Math.Max(combat.TemporaryHp, spell.TemporaryHp);
        Dirty(user, combat);

        _bonuses.AddPermanentBonus(user, CFBonusStat.MeleeDamage, CFBonusType.Untyped, spell.DamageBonus, source: BonusSource);
        if (option.SpeedFeet != combat.SpeedFeet)
            _bonuses.AddPermanentBonus(user, CFBonusStat.Speed, CFBonusType.Untyped, option.SpeedFeet - combat.SpeedFeet, source: BonusSource);

        _popup.PopupEntity(Loc.GetString("cf-battle-form-start", ("form", Loc.GetString(option.Name))), user);
    }

    public void End(EntityUid user)
    {
        if (!TryComp(user, out CFBattleFormComponent? form))
            return;

        if (TryComp(user, out CFCombatOptionsComponent? options))
        {
            options.NaturalDice = form.OldDice;
            options.NaturalDie = form.OldDie;
            options.NaturalType = form.OldType;
            options.NaturalFinesse = form.OldFinesse;
            options.NaturalAgile = form.OldAgile;
            Dirty(user, options);
        }

        _bonuses.RemoveBonusesFrom(user, BonusSource);
        RemComp<CFBattleFormComponent>(user);

        if (TryComp(user, out CFCombatantComponent? combat))
        {
            if (combat.ArmorFromEquipment)
            {
                _armor.RefreshArmorClass(user);
            }
            else
            {
                combat.ArmorClass = form.OldArmorClass;
                Dirty(user, combat);
            }
        }

        _popup.PopupEntity(Loc.GetString("cf-battle-form-end"), user);
    }

    /// <summary>Its attacks are its own: a held weapon cannot be swung in a battle form.</summary>
    [SubscribeLocalEvent]
    private void OnAttackAttempt(Entity<CFBattleFormComponent> ent, ref AttackAttemptEvent args)
    {
        if (args.Disarm || args.Weapon is not { } weapon || weapon.Owner == ent.Owner)
            return;

        args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnMobState(MobStateChangedEvent args)
    {
        if (_net.IsServer && args.NewMobState != MobState.Alive && HasComp<CFBattleFormComponent>(args.Target))
            End(args.Target);
    }
}

using Content.Shared.Actions;
using Content.Shared.CF.Actions;
using Content.Shared.CF.Checks;
using Content.Shared.CF.Combat;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Effects;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Interaction.Events;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared.CF.Spells;

/// <summary>A creature in a battle form, such as Animal Form, and what to put back when it ends.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class CFBattleFormComponent : Component
{
    [DataField, AutoNetworkedField]
    public LocId Name;

    /// <summary>The spell that gave the form, as the HUD shows it: "Animal Form: Cat".</summary>
    [DataField, AutoNetworkedField]
    public string SpellName = "";

    /// <summary>What the creature looks like in the form. The client draws it in place of the body.</summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier.Rsi? Sprite;

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

    /// <summary>
    /// A status bonus to the creature's own attack modifier, when that is what it uses. With Untamed Form the creature
    /// attacks with the better of the form's modifier and its own plus this.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int OwnAttackBonus;

    /// <summary>The form has no attacks, as a pest's.</summary>
    [DataField, AutoNetworkedField]
    public bool NoStrikes;

    /// <summary>The Dismiss action the form came with.</summary>
    [DataField]
    public EntityUid? DismissAction;
}

/// <summary>On the Dismiss action a battle form gives.</summary>
[RegisterComponent]
public sealed partial class CFDismissFormActionComponent : Component
{
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

    /// <summary>What the numbers above become when the spell is cast at a higher rank. The highest one reached counts.</summary>
    [DataField]
    public List<CFBattleFormTier> Tiers = new();

    /// <summary>See <see cref="CFBattleFormComponent.OwnAttackBonus"/>.</summary>
    [DataField]
    public int OwnAttackBonus;

    /// <summary>Form Control works on this spell: 2 ranks lower, at least 1, and it lasts an hour.</summary>
    [DataField]
    public bool FormControl;
}

[DataDefinition]
public sealed partial class CFBattleFormTier
{
    [DataField(required: true)]
    public int Rank;

    [DataField(required: true)]
    public int BaseArmorClass;

    [DataField(required: true)]
    public int AttackModifier;

    [DataField(required: true)]
    public int DamageBonus;

    [DataField(required: true)]
    public int TemporaryHp;
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

    /// <summary>What the creature looks like in this form. Every form needs one, so the caster is seen to change.</summary>
    [DataField]
    public SpriteSpecifier.Rsi? Sprite;

    /// <summary>The lowest rank of the spell that offers this form.</summary>
    [DataField]
    public int MinRank;

    /// <summary>
    /// A form with no attacks, as Pest Form's. It gets none of the spell's temporary Hit Points or damage, and uses its own
    /// <see cref="BaseArmorClass"/>.
    /// </summary>
    [DataField]
    public bool NoStrikes;

    /// <summary>With <see cref="NoStrikes"/>, the form's AC is this plus the caster's level.</summary>
    [DataField]
    public int BaseArmorClass = 15;

    /// <summary>How long this form lasts, when that is not the spell's own time.</summary>
    [DataField]
    public float DurationSeconds;

    /// <summary>A weakness to bludgeoning, piercing and slashing damage while in the form.</summary>
    [DataField]
    public int PhysicalWeakness;
}

/// <summary>
/// Battle forms. The form's unarmed attack replaces the fist, with the form's attack modifier and AC unless the creature's
/// own are higher. It cannot cast spells or use a weapon in the form, which lasts until it runs out or the creature drops.
/// </summary>
public sealed partial class CFBattleFormSystem : EntitySystem
{
    private const string BonusSource = "battle-form";
    private const string DismissAction = "CFActDismissForm";

    /// <summary>What Form Control does: 2 ranks lower, and an hour.</summary>
    private const int FormControlRanks = 2;
    private const float FormControlSeconds = 3600f;

    private static readonly ProtoId<CFDamageTypePrototype>[] Physical = { "Bludgeoning", "Piercing", "Slashing" };

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFSpellSystem _spells = default!;
    [Dependency] private CFBonusSystem _bonuses = default!;
    [Dependency] private CFArmorSystem _armor = default!;
    [Dependency] private CFTemporaryHpSystem _tempHp = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CFSpellshapeSystem _spellshapes = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

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
            || !TryComp(ent, out CFSpellComponent? spell))
        {
            return;
        }

        // A focus spell is cast at the cantrip rank, so its rank is known before it is paid for.
        var controlled = ent.Comp.FormControl && _spellshapes.IsFormControl(args.User);
        var level = CompOrNull<CFCombatantComponent>(args.User)?.Level ?? 1;
        var rank = spell.Focus ? CFCasting.CantripRank(level) : Math.Max(1, spell.Rank);
        if (controlled)
            rank = Math.Max(1, rank - FormControlRanks);

        if (rank < option.MinRank)
        {
            _popup.PopupEntity(Loc.GetString("cf-battle-form-too-weak", ("rank", option.MinRank)), args.User, args.User);
            return;
        }

        if (!_spells.TrySpend(args.User, spell))
            return;

        if (!spell.Focus)
            rank = Math.Max(rank, spell.ResolvedRank);

        if (controlled)
            _spellshapes.Consume(args.User);

        var name = _prototypes.TryIndex(spell.Spell, out var proto) ? proto.Name : string.Empty;
        Apply(args.User, ent.Comp, option, rank, controlled ? FormControlSeconds : 0f, name);
        args.Success = true;
    }

    /// <summary>
    /// Puts <paramref name="user"/> into the form, replacing any form it was already in. <paramref name="rank"/> picks the
    /// spell's tier, and <paramref name="atLeastSeconds"/> stretches a form that would end sooner.
    /// </summary>
    public void Apply(
        EntityUid user,
        CFBattleFormSpellComponent spell,
        CFBattleFormOption option,
        int rank = 0,
        float atLeastSeconds = 0f,
        string spellName = "")
    {
        if (HasComp<CFBattleFormComponent>(user))
            End(user);

        var baseArmorClass = spell.BaseArmorClass;
        var attackModifier = spell.AttackModifier;
        var damageBonus = spell.DamageBonus;
        var temporaryHp = spell.TemporaryHp;
        var reached = 0;
        foreach (var tier in spell.Tiers)
        {
            if (tier.Rank > rank || tier.Rank <= reached)
                continue;

            reached = tier.Rank;
            baseArmorClass = tier.BaseArmorClass;
            attackModifier = tier.AttackModifier;
            damageBonus = tier.DamageBonus;
            temporaryHp = tier.TemporaryHp;
        }

        if (option.NoStrikes)
        {
            baseArmorClass = option.BaseArmorClass;
            attackModifier = 0;
            damageBonus = 0;
            temporaryHp = 0;
        }

        var seconds = Math.Max(option.DurationSeconds > 0f ? option.DurationSeconds : spell.DurationSeconds, atLeastSeconds);
        var combat = EnsureComp<CFCombatantComponent>(user);
        var options = EnsureComp<CFCombatOptionsComponent>(user);
        var form = EnsureComp<CFBattleFormComponent>(user);
        form.Name = option.Name;
        form.SpellName = spellName;
        form.Sprite = option.Sprite;
        form.Until = _timing.CurTime + TimeSpan.FromSeconds(seconds);
        form.ArmorClass = baseArmorClass + combat.Level;
        form.AttackModifier = attackModifier;
        form.OwnAttackBonus = spell.OwnAttackBonus;
        form.NoStrikes = option.NoStrikes;
        form.OldArmorClass = combat.ArmorClass;
        form.OldDice = options.NaturalDice;
        form.OldDie = options.NaturalDie;
        form.OldType = options.NaturalType;
        form.OldFinesse = options.NaturalFinesse;
        form.OldAgile = options.NaturalAgile;
        form.DismissAction = _actions.AddAction(user, DismissAction);
        Dirty(user, form);

        if (!option.NoStrikes)
        {
            options.NaturalDice = option.Dice;
            options.NaturalDie = option.Die;
            options.NaturalType = option.DamageType;
            options.NaturalFinesse = false;
            options.NaturalAgile = option.Agile;
            Dirty(user, options);
        }

        combat.ArmorClass = Math.Max(combat.ArmorClass, form.ArmorClass);
        Dirty(user, combat);

        if (temporaryHp > 0)
            _tempHp.Grant(user, temporaryHp, seconds, BonusSource);

        if (damageBonus != 0)
            _bonuses.AddPermanentBonus(user, CFBonusStat.MeleeDamage, CFBonusType.Untyped, damageBonus, source: BonusSource);

        if (option.SpeedFeet != combat.SpeedFeet)
            _bonuses.AddPermanentBonus(user, CFBonusStat.Speed, CFBonusType.Untyped, option.SpeedFeet - combat.SpeedFeet, source: BonusSource);

        if (option.PhysicalWeakness > 0)
        {
            foreach (var type in Physical)
            {
                _bonuses.AddResistance(user, type, option.PhysicalWeakness, weakness: true, seconds);
            }
        }

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
        _tempHp.Clear(user, BonusSource);
        foreach (var type in Physical)
        {
            _bonuses.EndTimedWeakness(user, type);
        }

        if (form.DismissAction is { } dismiss)
            _actions.RemoveAction(user, dismiss);

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

    /// <summary>Its attacks are its own: a held weapon cannot be swung in a battle form. A pest has no attacks at all.</summary>
    [SubscribeLocalEvent]
    private void OnAttackAttempt(Entity<CFBattleFormComponent> ent, ref AttackAttemptEvent args)
    {
        if (ent.Comp.NoStrikes)
        {
            args.Cancel();
            return;
        }

        if (args.Disarm || args.Weapon is not { } weapon || weapon.Owner == ent.Owner)
            return;

        args.Cancel();
    }

    [SubscribeLocalEvent]
    private void OnDismiss(Entity<CFDismissFormActionComponent> ent, ref CFActivityInstantEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (_net.IsServer)
            End(args.Performer);
    }

    [SubscribeLocalEvent]
    private void OnMobState(MobStateChangedEvent args)
    {
        if (_net.IsServer && args.NewMobState != MobState.Alive && HasComp<CFBattleFormComponent>(args.Target))
            End(args.Target);
    }
}

using Content.Shared.CF.Actions;
using Content.Shared.CF.DamageTypes;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Combat;

/// <summary>
/// A weapon whose damage type can be switched, as the modular trait allows. Use it in hand to go to the next type, like a
/// multitool, and the hand's status text shows which is set. It replaces the type on <see cref="CFWeaponComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CFDamageModeComponent : Component
{
    [DataField(required: true)]
    public List<ProtoId<CFDamageTypePrototype>> Types = new();

    [DataField, AutoNetworkedField]
    public int Index;

    [DataField]
    public SoundSpecifier? SwitchSound = new SoundPathSpecifier("/Audio/Items/change_jaws.ogg");

    /// <summary>The type set right now, or null if the weapon lists none.</summary>
    public ProtoId<CFDamageTypePrototype>? Current => Types.Count > 0 ? (ProtoId<CFDamageTypePrototype>?) Types[Math.Clamp(Index, 0, Types.Count - 1)] : null;
}

/// <summary>Switches a weapon's damage type. See <see cref="CFDamageModeComponent"/>.</summary>
public sealed partial class CFDamageModeSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private CFActionPoolSystem _actions = default!;

    /// <summary>The name of the type set right now, with its color, as markup.</summary>
    public string Markup(CFDamageModeComponent comp)
    {
        if (comp.Current is not { } current || !_prototypes.TryIndex(current, out var type))
            return string.Empty;

        return Loc.GetString("cf-damage-mode-status", ("color", type.Color.ToHex()), ("type", Loc.GetString(type.Name)));
    }

    [SubscribeLocalEvent]
    private void OnUseInHand(Entity<CFDamageModeComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.Types.Count < 2)
            return;

        args.Handled = true;
        if (!_actions.TrySpendActions(args.User, 1))
            return;

        ent.Comp.Index = (ent.Comp.Index + 1) % ent.Comp.Types.Count;
        Dirty(ent);
        _audio.PlayPredicted(ent.Comp.SwitchSound, ent, args.User);
        if (ent.Comp.Current is { } current && _prototypes.TryIndex(current, out var type))
            _popup.PopupEntity(Loc.GetString("cf-damage-mode-changed", ("type", Loc.GetString(type.Name))), args.User, args.User);
    }

    [SubscribeLocalEvent]
    private void OnExamine(Entity<CFDamageModeComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Current is { } current && _prototypes.TryIndex(current, out var type))
            args.PushMarkup(Loc.GetString("cf-damage-mode-examine", ("type", Loc.GetString(type.Name))));
    }
}

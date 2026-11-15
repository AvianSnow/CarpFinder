using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.CF.Alchemy;

/// <summary>
/// A potion that breaks when thrown. Against a living creature it works on that creature as if drunk. Against anything
/// else, or the floor, it leaves a puddle that still holds its healing (<see cref="CFHealingPuddleComponent"/>).
/// Goes beside <see cref="CFHealConsumableComponent"/>. <see cref="CFSmashPotionSystem"/>.
/// </summary>
[RegisterComponent]
public sealed partial class CFSmashPotionComponent : Component
{
    /// <summary>What the puddle is made of. Only for looks: the healing is on the puddle.</summary>
    [DataField]
    public ProtoId<ReagentPrototype> Reagent = "CFHealingDraught";

    [DataField]
    public float Units = 15f;

    [DataField]
    public SoundSpecifier BreakSound = new SoundCollectionSpecifier("GlassBreak");

    /// <summary>Already broken this tick: a throw can both hit and land.</summary>
    public bool Broken;
}

/// <summary>
/// A spilled healing potion. A creature that lies prone in it, or drinks it, is healed and the puddle is gone. Mopping
/// it up loses it. Server only.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CFHealingPuddleComponent : Component
{
    [DataField]
    public int HealDice = 1;

    [DataField]
    public int HealDie = 8;

    [DataField]
    public int HealBonus;

    /// <summary>The potion spoils on the floor after this.</summary>
    [DataField, AutoPausedField]
    public TimeSpan Expires;

    [DataField, AutoPausedField]
    public TimeSpan NextCheck;
}

[Serializable, NetSerializable]
public sealed partial class CFDrinkPuddleDoAfterEvent : SimpleDoAfterEvent;

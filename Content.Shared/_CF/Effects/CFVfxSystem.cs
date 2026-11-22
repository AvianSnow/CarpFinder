using System.Numerics;
using Content.Shared.CF.DamageTypes;
using Content.Shared.CF.Traits;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Effects;

/// <summary>A short-lived magic effect. The client tints its sprite with <see cref="Color"/>.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class CFVfxComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Color = Color.White;
}

/// <summary>
/// Sparkles on casters, on those targeted by a save spell, and on anyone healed. Each effect is an entity attached
/// to the creature, so it follows them, and it removes itself when its animation ends.
/// </summary>
public sealed partial class CFVfxSystem : EntitySystem
{
    private static readonly EntProtoId CastEffect = "CFVfxCast";
    private static readonly EntProtoId TargetedEffect = "CFVfxTargeted";
    private static readonly EntProtoId HealingEffect = "CFVfxHealing";

    public static readonly Color HealingColor = Color.FromHex("#70F0A0");
    public static readonly Color ArcaneColor = Color.FromHex("#70C0FF");

    /// <summary>For spells that deal no damage. The first trait a spell has from this list sets its colour.</summary>
    private static readonly (ProtoId<CFTraitPrototype> Trait, Color Color)[] TraitColors =
    {
        ("Healing", HealingColor),
        ("Vitality", HealingColor),
        ("Fear", Color.FromHex("#C050E0")),
        ("Mental", Color.FromHex("#B080FF")),
        ("Emotion", Color.FromHex("#F080C0")),
        ("Sleep", Color.FromHex("#8090F0")),
        ("Illusion", Color.FromHex("#E0A0FF")),
        ("Poison", Color.FromHex("#90D040")),
        ("Curse", Color.FromHex("#905090")),
        ("Void", Color.FromHex("#7050A0")),
        ("Darkness", Color.FromHex("#6050A0")),
        ("Unholy", Color.FromHex("#C03040")),
        ("Holy", Color.FromHex("#FFE080")),
        ("Light", Color.FromHex("#FFF0B0")),
    };

    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private CFEffectSystem _effects = default!;

    /// <summary>Shows others that this creature has cast the spell.</summary>
    public void Cast(EntityUid caster, EntityUid spell)
    {
        Play(caster, CastEffect, SpellColor(spell));
    }

    /// <summary>A save spell has been aimed at this creature.</summary>
    public void Targeted(EntityUid target, EntityUid spell)
    {
        Play(target, TargetedEffect, SpellColor(spell));
    }

    public void Healing(EntityUid creature)
    {
        Play(creature, HealingEffect, HealingColor);
    }

    /// <summary>The colour of a spell: its damage type if it deals damage, otherwise what kind of magic it is.</summary>
    public Color SpellColor(EntityUid spell)
    {
        if (TryComp(spell, out CFDegreeEffectsComponent? lists)
            && (TryDamageColor(lists.Effects, out var color)
                || TryDamageColor(lists.Landed, out color)
                || TryDamageColor(lists.Failure, out color)
                || TryDamageColor(lists.CriticalFailure, out color)
                || TryDamageColor(lists.Success, out color)
                || TryDamageColor(lists.CriticalSuccess, out color)))
        {
            return color;
        }

        var traits = _effects.SourceTraits(spell);
        foreach (var (trait, traitColor) in TraitColors)
        {
            if (traits.Contains(trait))
                return traitColor;
        }

        return ArcaneColor;
    }

    private bool TryDamageColor(IEnumerable<CFEffect> effects, out Color color)
    {
        foreach (var effect in effects)
        {
            if (effect is CFDamageEffect damage && _prototypes.TryIndex<CFDamageTypePrototype>(damage.DamageType, out var type))
            {
                color = type.Color;
                return true;
            }
        }

        color = default;
        return false;
    }

    private void Play(EntityUid creature, EntProtoId effect, Color color)
    {
        if (!_net.IsServer || TerminatingOrDeleted(creature))
            return;

        var spawned = SpawnAttachedTo(effect, new EntityCoordinates(creature, Vector2.Zero));
        var vfx = EnsureComp<CFVfxComponent>(spawned);
        vfx.Color = color;
        Dirty(spawned, vfx);
    }
}

namespace Content.Shared.CF.Effects;

/// <summary>
/// How a source reaches its target: range, delivery, extra targets, and who it may affect.
/// Spells, wands, thrown items, and item activations use it.
/// </summary>
[RegisterComponent]
public sealed partial class CFTargetingComponent : Component
{
    [DataField]
    public CFDelivery Delivery = CFDelivery.Creature;

    /// <summary>0 means no range check, as for a spell on yourself.</summary>
    [DataField]
    public int RangeFeet;

    /// <summary>Reaches the next tile in every direction, diagonals too. Reach Spell makes it 30 feet.</summary>
    [DataField]
    public bool Touch;

    /// <summary>Further creatures in range that are also hit, such as electric arc's second target.</summary>
    [DataField]
    public int ExtraTargets;

    /// <summary>Who an explicitly chosen target may be.</summary>
    [DataField]
    public CFTargetFilter Filter = CFTargetFilter.Enemies;

    /// <summary>Walls and closed doors block the effect. Remaster spells need line of effect.</summary>
    [DataField]
    public bool LineOfEffect;
}

public enum CFDelivery : byte
{
    /// <summary>Affects the user, or an emanation around them.</summary>
    Self,

    /// <summary>Affects the clicked creature directly.</summary>
    Creature,

    /// <summary>Aimed at a point. Needs <see cref="CFAreaEffectComponent"/>.</summary>
    Point,

    /// <summary>Fires bolts from <see cref="CFProjectileDeliveryComponent"/> at the clicked creature.</summary>
    Projectile,
}

public enum CFTargetFilter : byte
{
    /// <summary>Creatures the user can harm (not in their party).</summary>
    Enemies,

    /// <summary>The user and creatures they cannot harm.</summary>
    Allies,

    /// <summary>Every living creature.</summary>
    All,
}

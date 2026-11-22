using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared.CF.GOAP;

/// <summary>
/// How a creature fights. A personality says when it runs and how it moves. This says who it goes for and what it
/// thinks an action is worth, so a cowardly beast and a cowardly tactician are different. Set <c>parent</c> to start
/// from another set and change only what differs.
/// </summary>
[Prototype("goapTactics")]
public sealed partial class CFGOAPTacticsPrototype : IPrototype, IInheritingPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<CFGOAPTacticsPrototype>))]
    public string[]? Parents { get; private set; }

    [AbstractDataField]
    public bool Abstract { get; private set; }

    // Who it goes for. Each of these adds to a target's score, and the highest score wins.

    /// <summary>Score lost per tile of walking.</summary>
    [DataField]
    public float Distance = 0.1f;

    /// <summary>Score at no hit points, scaled down to nothing at full.</summary>
    [DataField]
    public float Wounded = 0.5f;

    /// <summary>Score for a target one plain hit could drop.</summary>
    [DataField]
    public float Finish = 1f;

    /// <summary>Score for whoever hurt it last.</summary>
    [DataField]
    public float Payback = 0.5f;

    /// <summary>Score for the target it already has, so it does not keep changing its mind.</summary>
    [DataField]
    public float Commitment = 0.5f;

    /// <summary>Score for a creature that casts spells.</summary>
    [DataField]
    public float Casters;

    /// <summary>Score for a companion. Below zero, it would rather go for the one giving the orders.</summary>
    [DataField]
    public float Companions = -0.3f;

    /// <summary>
    /// How well it sizes a target up, from 0 to 1. At 0 it assumes every fight is an even one, as a beast does. At 1 it
    /// reads armor and stance as well as a veteran.
    /// </summary>
    [DataField]
    public float Insight = 0.5f;

    // What an action is worth. Every one is measured against a clean Strike, which is worth 1.

    /// <summary>Scales what a Strike is worth.</summary>
    [DataField]
    public float Strike = 1f;

    /// <summary>Scales what Demoralize is worth. 0 never tries it.</summary>
    [DataField]
    public float Frighten = 1f;

    /// <summary>Scales what Feint is worth. 0 never tries it.</summary>
    [DataField]
    public float Feint = 1f;

    /// <summary>
    /// It will not make a Strike worth less than this share of a clean one. Swinging all the time keeps the pool from
    /// filling, and only a full pool clears Multiple Attack Penalty, so this is what makes it stop and wait.
    /// 0 swings regardless.
    /// </summary>
    [DataField]
    public float Discipline = 0.25f;
}

using Content.Shared.CF.Character.Grants;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Character;

/// <summary>
/// A domain of a deity's power. A character with Domain Initiate, Deity's Domain or the like picks one of their deity's
/// domains, and gets what <see cref="Grants"/> gives: its domain spell. A spell cast as a reaction is a Focus Point and a
/// <c>CFGrantReaction</c> instead of an action.
/// </summary>
[Prototype("cfDomain")]
public sealed partial class CFDomainPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>What the domain spell does, in a line.</summary>
    [DataField(required: true)]
    public LocId Description;

    [DataField]
    public List<CFGrant> Grants = new();
}

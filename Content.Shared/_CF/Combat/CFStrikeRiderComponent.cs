using Content.Shared.CF.Effects;

namespace Content.Shared.CF.Combat;

/// <summary>
/// What a creature's Strikes do besides damage, run on each creature a Strike of its damages: a spider's venom
/// (<c>!type:CFExposeAfflictionEffect</c>), a rat's plague. A character's feats use a <c>CFStrikeAction</c> instead.
/// </summary>
[RegisterComponent]
public sealed partial class CFStrikeRiderComponent : Component
{
    [DataField(required: true)]
    public List<CFEffect> OnHit = new();

    /// <summary>Only melee Strikes carry it, as jaws and fangs do.</summary>
    [DataField]
    public bool MeleeOnly = true;
}

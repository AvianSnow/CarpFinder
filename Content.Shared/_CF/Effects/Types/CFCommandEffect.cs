using Content.Shared.CF.Spells;

namespace Content.Shared.CF.Effects;

/// <summary>
/// Gives the order picked from the source's <see cref="CFModeChoiceComponent"/> menu, through
/// <see cref="CFCommandSystem.Give"/>. The mode values are <see cref="CFCommandOrder"/>.
/// </summary>
public sealed partial class CFCommandEffect : CFEffect
{
    /// <summary>How long a movement order lasts. Drop and Fall happen at once.</summary>
    [DataField]
    public float Seconds = 6f;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target
            || !entities.TryGetComponent(args.Source, out CFModeChoiceComponent? mode))
            return;

        entities.System<CFCommandSystem>().Give(args.User, target, (CFCommandOrder) mode.Chosen, Seconds, args.Dc);
    }
}

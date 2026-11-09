namespace Content.Shared.CF.Effects;

/// <summary>Raises the user's shield and starts Shields of the Spirit in their aura.</summary>
public sealed partial class CFRaiseSpiritShieldsEffect : CFEffect
{
    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        entities.System<CFAuraSystem>().RaiseSpiritShields(args.User);
    }
}

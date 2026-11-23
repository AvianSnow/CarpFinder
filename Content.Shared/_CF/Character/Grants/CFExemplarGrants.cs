using Content.Shared.CF.Combat;

namespace Content.Shared.CF.Character.Grants;

/// <summary>Energized Spark: the Exemplar's spirit damage is dealt as the energy picked (a <c>DamageType</c> choice).</summary>
public sealed partial class CFGrantSparkType : CFGrant
{
    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        var features = entities.System<CFCharacterFeatureSystem>();
        if (features.Pick(args.Mob, args.Source) is not { } type || type.Length == 0)
            return;

        entities.EnsureComponent<CFExemplarComponent>(args.Mob).SparkType = type;
    }
}

/// <summary>A flag on the Exemplar for a feat that changes what the ikons do. See <see cref="CFExemplarOption"/>.</summary>
public sealed partial class CFGrantExemplarOption : CFGrant
{
    [DataField(required: true)]
    public CFExemplarOption Option;

    public override void Apply(IEntityManager entities, CFGrantArgs args)
    {
        entities.System<CFIkonSystem>().SetOption(args.Mob, Option);
    }
}

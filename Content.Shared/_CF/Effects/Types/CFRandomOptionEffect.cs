using Content.Shared.CF.Checks;
using Content.Shared.Popups;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.CF.Effects;

/// <summary>One outcome of <see cref="CFRandomOptionEffect"/>, by the target's degree of success.</summary>
[DataDefinition]
public sealed partial class CFRandomOption
{
    [DataField]
    public List<CFEffect> Failure = new();

    [DataField]
    public List<CFEffect> CriticalFailure = new();
}

/// <summary>
/// One of several outcomes, rolled once per cast so every creature in the area gets the same one: Creative Splash's colour.
/// The roll is the same for every target the source resolves on one tick.
/// </summary>
public sealed partial class CFRandomOptionEffect : CFEffect
{
    [DataField(required: true)]
    public List<CFRandomOption> Options = new();

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (Options.Count == 0)
            return;

        var tick = IoCManager.Resolve<IGameTiming>().CurTick.Value;
        var seed = HashCode.Combine(args.Source.Id, args.User.Id, tick);
        var option = Options[(int) ((uint) seed % (uint) Options.Count)];
        var list = args.Degree switch
        {
            CFDegree.Failure => option.Failure,
            CFDegree.CriticalFailure => option.CriticalFailure,
            _ => null,
        };

        if (list != null)
            entities.System<CFEffectSystem>().Run(list, args);
    }
}

/// <summary>
/// Read Fate: the user learns one enigmatic word about the target's near future, or "inconclusive" on a failed DC 6 flat
/// check. Only the user sees it.
/// </summary>
public sealed partial class CFReadFateEffect : CFEffect
{
    private const int Words = 12;

    public override void Apply(IEntityManager entities, CFEffectArgs args)
    {
        if (args.Target is not { } target)
            return;

        var random = IoCManager.Resolve<IRobustRandom>();
        var key = random.Next(1, 21) < 6 ? "cf-read-fate-inconclusive" : $"cf-read-fate-word-{random.Next(1, Words + 1)}";
        entities.System<SharedPopupSystem>().PopupEntity(Loc.GetString("cf-read-fate", ("word", Loc.GetString(key))), target, args.User, PopupType.Medium);
    }
}

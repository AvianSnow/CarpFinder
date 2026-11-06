using Content.Shared.CF.Checks;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Afflictions;

/// <summary>
/// A staged affliction (poison, disease, drug): a save on exposure, an onset, then stages with a save after each.
/// Times are world minutes, the same clock as the temperature rules.
/// </summary>
[Prototype("cfAffliction")]
public sealed partial class CFAfflictionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public CFDefenseType Defense = CFDefenseType.Fortitude;

    [DataField(required: true)]
    public int Dc;

    /// <summary>What a save against this resists, such as Poison or Disease.</summary>
    [DataField]
    public HashSet<ProtoId<CFTraitPrototype>> Traits = new();

    [DataField]
    public float OnsetWorldMinutes;

    /// <summary>
    /// Failing a save after a stage raises the stage. Alcohol turns this off: only drinking more does that.
    /// </summary>
    [DataField]
    public bool FailureRaisesStage = true;

    /// <summary>
    /// Real seconds before another exposure counts. A reagent ticks many times per drink, and each drink is one exposure.
    /// </summary>
    [DataField]
    public float ExposureCooldownSeconds = 30f;

    [DataField(required: true)]
    public List<CFAfflictionStage> Stages = new();
}

[DataDefinition]
public sealed partial class CFAfflictionStage
{
    [DataField]
    public float WorldMinutes = 10f;

    /// <summary>Run when the creature enters the stage. Durations on these are real seconds.</summary>
    [DataField]
    public List<CFEffect> Effects = new();
}

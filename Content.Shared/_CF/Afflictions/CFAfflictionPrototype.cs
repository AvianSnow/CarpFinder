using Content.Shared.CF.Checks;
using Content.Shared.CF.Effects;
using Content.Shared.CF.Traits;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Afflictions;

/// <summary>
/// A staged affliction (poison, disease, drug): a save on exposure, an onset, then stages with a save after each.
/// Times are world minutes, the same clock as the temperature rules, or real seconds for one that works in rounds:
/// a round is 6 seconds. A <c>Poison</c> or <c>Disease</c> trait is what immunities, saves and the HUD go by.
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

    /// <summary>An onset in real seconds instead, for one printed in rounds.</summary>
    [DataField]
    public float OnsetSeconds;

    /// <summary>The printed maximum duration, in real seconds: 6 rounds is 36. Zero is none.</summary>
    [DataField]
    public float MaxSeconds;

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

    /// <summary>How long the stage lasts in real seconds instead, for one printed in rounds. Zero uses <see cref="WorldMinutes"/>.</summary>
    [DataField]
    public float Seconds;

    /// <summary>Run when the creature enters the stage. Durations on these are real seconds.</summary>
    [DataField]
    public List<CFEffect> Effects = new();
}

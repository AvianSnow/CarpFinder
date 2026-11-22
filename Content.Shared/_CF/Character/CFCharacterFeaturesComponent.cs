namespace Content.Shared.CF.Character;

/// <summary>Which character options have run their grants, so none runs twice. Server-only.</summary>
[RegisterComponent, Access(typeof(CFCharacterFeatureSystem))]
public sealed partial class CFCharacterFeaturesComponent : Component
{
    [DataField]
    public HashSet<string> Applied = new();
}

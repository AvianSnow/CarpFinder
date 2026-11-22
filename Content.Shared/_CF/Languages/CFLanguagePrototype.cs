using Content.Shared.CF.Character;
using Robust.Shared.Prototypes;

namespace Content.Shared.CF.Languages;

/// <summary>A language a creature can speak. Listeners who do not know it hear <see cref="Syllables"/> instead.</summary>
[Prototype("language")]
public sealed partial class CFLanguagePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>Only common languages can be picked with Intelligence. Others come from an ancestry or a feat.</summary>
    [DataField]
    public CFRarity Rarity = CFRarity.Common;

    [DataField(required: true)]
    public List<string> Syllables = new();
}
